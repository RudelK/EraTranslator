from fastapi import FastAPI

from app.api.router import api_router
from app.core.config import get_settings
from app.web.router import router as web_router


def create_app() -> FastAPI:
    settings = get_settings()
    app = FastAPI(
        title=settings.app_name,
        version=settings.app_version,
        docs_url="/api/docs",
        redoc_url="/api/redoc",
        openapi_url="/api/openapi.json",
    )
    app.include_router(api_router, prefix="/api")
    app.include_router(web_router, prefix="/admin", tags=["admin"])
    return app


app = create_app()
