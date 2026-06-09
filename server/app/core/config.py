import os
from functools import lru_cache
from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="ERATRANSLATOR_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    app_name: str = "EraTranslator Team Server"
    app_version: str = "0.1.0"
    database_url: str = Field(
        default="postgresql+psycopg://eratranslator:eratranslator@localhost:5432/eratranslator",
        description="SQLAlchemy database URL. PostgreSQL is the production default.",
    )
    database_schema: str = Field(
        default="eratranslator",
        description="PostgreSQL schema used by the server. Ignored for SQLite tests.",
    )
    archive_root: Path = Field(
        default=Path("data/source-archives"),
        description="Directory used to store uploaded source snapshot archives.",
    )
    max_archive_bytes: int = 2 * 1024 * 1024 * 1024
    max_archive_files: int = 200_000
    source_snapshot_retention_count: int = 3
    bootstrap_admin_token: str = Field(
        default="",
        description="Optional one-time bootstrap token used to create the first admin account.",
    )
    access_token_expire_hours: int = 24
    password_hash_iterations: int = 210_000


def get_env_file_path() -> Path:
    return Path(os.environ.get("ERATRANSLATOR_ENV_FILE", ".env"))


@lru_cache
def get_settings() -> Settings:
    return Settings(_env_file=get_env_file_path())


def reset_settings_cache() -> None:
    get_settings.cache_clear()
