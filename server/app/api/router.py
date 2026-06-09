from fastapi import APIRouter

from app.api.routes import auth, clients, collaboration, health, projects, source

api_router = APIRouter()
api_router.include_router(auth.router, prefix="/auth", tags=["auth"])
api_router.include_router(clients.router, prefix="/clients", tags=["clients"])
api_router.include_router(collaboration.router, prefix="/projects", tags=["collaboration"])
api_router.include_router(health.router, tags=["health"])
api_router.include_router(projects.router, prefix="/projects", tags=["projects"])
api_router.include_router(source.router, prefix="/projects", tags=["source"])
