from fastapi import APIRouter, Depends, Header, HTTPException, status
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.api.deps import get_current_user, require_roles
from app.core.config import get_settings
from app.db.session import get_db
from app.models.user import ApiToken, User
from app.schemas.auth import BootstrapAdminRequest, CurrentUserResponse, LoginRequest, TokenResponse
from app.services.security import (
    access_token_expires_at,
    generate_access_token,
    hash_access_token,
    hash_password,
    verify_password,
)

router = APIRouter()


@router.post("/bootstrap-admin", response_model=CurrentUserResponse, status_code=status.HTTP_201_CREATED)
def bootstrap_admin(
    request: BootstrapAdminRequest,
    x_bootstrap_token: str | None = Header(default=None, alias="X-Bootstrap-Token"),
    db: Session = Depends(get_db),
) -> CurrentUserResponse:
    settings = get_settings()
    if not settings.bootstrap_admin_token or x_bootstrap_token != settings.bootstrap_admin_token:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Not found")

    existing_user_count = db.scalar(select(func.count(User.id)))
    if existing_user_count:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Admin bootstrap is already completed")

    user = User(
        username=request.username,
        display_name=request.display_name,
        role="admin",
        password_hash=hash_password(request.password),
    )
    db.add(user)
    db.commit()
    db.refresh(user)
    return to_current_user_response(user)


@router.post("/login", response_model=TokenResponse)
def login(request: LoginRequest, db: Session = Depends(get_db)) -> TokenResponse:
    user = db.scalar(select(User).where(User.username == request.username))
    if user is None or user.status != "active" or not verify_password(request.password, user.password_hash):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid username or password")

    raw_token = generate_access_token()
    expires_at = access_token_expires_at()
    db.add(
        ApiToken(
            user_id=user.id,
            token_hash=hash_access_token(raw_token),
            expires_at_utc=expires_at,
        )
    )
    db.commit()
    return TokenResponse(access_token=raw_token, expires_at_utc=expires_at.isoformat())


@router.get("/me", response_model=CurrentUserResponse)
def get_me(user: User = Depends(get_current_user)) -> CurrentUserResponse:
    return to_current_user_response(user)


@router.get("/admin-check", response_model=CurrentUserResponse)
def admin_check(user: User = Depends(require_roles("admin"))) -> CurrentUserResponse:
    return to_current_user_response(user)


def to_current_user_response(user: User) -> CurrentUserResponse:
    return CurrentUserResponse(
        id=user.id,
        username=user.username,
        display_name=user.display_name,
        role=user.role,
    )
