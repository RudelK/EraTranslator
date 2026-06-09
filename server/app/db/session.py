from collections.abc import Generator

from sqlalchemy import create_engine
from sqlalchemy import event
from sqlalchemy import text
from sqlalchemy.orm import Session, sessionmaker

from app.core.config import get_settings
from app.db.schema import normalize_schema_name, quote_identifier


def create_database_engine():
    settings = get_settings()
    connect_args = {"check_same_thread": False} if settings.database_url.startswith("sqlite") else {}
    created_engine = create_engine(settings.database_url, pool_pre_ping=True, connect_args=connect_args)
    schema = normalize_schema_name(settings.database_schema)
    if schema and settings.database_url.startswith("postgresql"):
        quoted_schema = quote_identifier(schema)

        @event.listens_for(created_engine, "connect")
        def set_search_path(dbapi_connection, _connection_record):
            with dbapi_connection.cursor() as cursor:
                cursor.execute(f"SET search_path TO {quoted_schema}")

    return created_engine


engine = create_database_engine()
SessionLocal = sessionmaker(bind=engine, autoflush=False, autocommit=False)


def get_db() -> Generator[Session, None, None]:
    db = SessionLocal()
    try:
        settings = get_settings()
        schema = normalize_schema_name(settings.database_schema)
        if schema and settings.database_url.startswith("postgresql"):
            db.execute(text(f"SET search_path TO {quote_identifier(schema)}"))
        yield db
    finally:
        db.close()
