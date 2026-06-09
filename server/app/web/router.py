import json
from datetime import UTC, datetime
from pathlib import Path

from fastapi import APIRouter, Cookie, Depends, File, Form, HTTPException, Request, UploadFile, status
from fastapi.responses import FileResponse, HTMLResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.core.config import get_settings
from app.db.session import get_db
from app.models.collaboration import ClientDevice, Conflict, ScanManifest, SharedNamespaceEntry, Submission, WorkItem
from app.models.project import Project, ProjectAssignment, ProjectMembership
from app.models.source_snapshot import SourceSnapshot
from app.models.user import ApiToken, User
from app.schemas.collaboration import ScanManifestUploadRequest
from app.services.env_file import save_env_values
from app.services.source_archive import ArchiveValidationError, new_scan_revision_id, resolve_archive_path, store_uploaded_source_archive
from app.services.security import access_token_expires_at, generate_access_token, hash_access_token, hash_password, verify_password

router = APIRouter()
templates = Jinja2Templates(directory=str(Path(__file__).parent / "templates"))
COOKIE_NAME = "eratran_admin_token"


@router.get("/", response_class=HTMLResponse)
def root() -> RedirectResponse:
    return RedirectResponse(url="/admin/projects")


@router.get("/login", response_class=HTMLResponse)
def login_page(request: Request, db: Session = Depends(get_db)):
    if is_first_run(db):
        return RedirectResponse(url="/admin/setup", status_code=status.HTTP_303_SEE_OTHER)
    return templates.TemplateResponse(request, "login.html", {"error": None})


@router.get("/setup", response_class=HTMLResponse)
def setup_page(request: Request, db: Session = Depends(get_db)):
    if not is_first_run(db):
        return RedirectResponse(url="/admin/login", status_code=status.HTTP_303_SEE_OTHER)
    settings = get_settings()
    return templates.TemplateResponse(
        request,
        "setup.html",
        {
            "error": None,
            "database_url": settings.database_url,
            "database_schema": settings.database_schema,
            "archive_root": str(settings.archive_root),
            "bootstrap_token": settings.bootstrap_admin_token,
        },
    )


@router.post("/setup", response_model=None)
def setup(
    request: Request,
    username: str = Form(...),
    display_name: str = Form(...),
    password: str = Form(...),
    password_confirm: str = Form(...),
    bootstrap_token: str = Form(""),
    database_url: str = Form(""),
    database_schema: str = Form(""),
    archive_root: str = Form(""),
    db: Session = Depends(get_db),
):
    if not is_first_run(db):
        return RedirectResponse(url="/admin/login", status_code=status.HTTP_303_SEE_OTHER)
    if password != password_confirm:
        return setup_error(request, "Password confirmation does not match", database_url, database_schema, archive_root, bootstrap_token)
    if len(password) < 8:
        return setup_error(request, "Password must be at least 8 characters", database_url, database_schema, archive_root, bootstrap_token)

    try:
        env_values = {
            "ERATRANSLATOR_BOOTSTRAP_ADMIN_TOKEN": bootstrap_token.strip(),
        }
        if database_url.strip():
            env_values["ERATRANSLATOR_DATABASE_URL"] = database_url.strip()
        if database_schema.strip():
            env_values["ERATRANSLATOR_DATABASE_SCHEMA"] = database_schema.strip()
        if archive_root.strip():
            env_values["ERATRANSLATOR_ARCHIVE_ROOT"] = archive_root.strip()
        save_env_values(env_values)

        user = User(
            username=username.strip(),
            display_name=display_name.strip() or username.strip(),
            role="admin",
            password_hash=hash_password(password),
        )
        db.add(user)
        db.commit()
        db.refresh(user)
        return login_web_user(user, db)
    except SQLAlchemyError as exc:
        db.rollback()
        return setup_error(
            request,
            f"Database setup failed: {exc.__class__.__name__}. Run migrations and check PostgreSQL connection/schema permissions.",
            database_url,
            database_schema,
            archive_root,
            bootstrap_token,
        )
    except OSError as exc:
        db.rollback()
        return setup_error(
            request,
            f"Could not write server .env file: {exc}",
            database_url,
            database_schema,
            archive_root,
            bootstrap_token,
        )


@router.post("/login", response_model=None)
def login(
    request: Request,
    username: str = Form(...),
    password: str = Form(...),
    db: Session = Depends(get_db),
):
    user = db.scalar(select(User).where(User.username == username))
    if user is None or user.status != "active" or not verify_password(password, user.password_hash):
        return templates.TemplateResponse(request, "login.html", {"error": "Invalid username or password"}, status_code=401)

    raw_token = generate_access_token()
    return login_web_user(user, db, raw_token)


@router.post("/logout")
def logout() -> RedirectResponse:
    response = RedirectResponse(url="/admin/login", status_code=status.HTTP_303_SEE_OTHER)
    response.delete_cookie(COOKIE_NAME)
    return response


def get_web_user(eratran_admin_token: str | None = Cookie(default=None), db: Session = Depends(get_db)) -> User:
    if is_first_run(db):
        raise redirect_to_setup()
    if not eratran_admin_token:
        raise redirect_to_login()
    api_token = db.scalar(
        select(ApiToken)
        .where(ApiToken.token_hash == hash_access_token(eratran_admin_token))
        .where(ApiToken.revoked_at_utc.is_(None))
    )
    if api_token is None or as_utc(api_token.expires_at_utc) <= datetime.now(UTC):
        raise redirect_to_login()
    user = db.get(User, api_token.user_id)
    if user is None or user.status != "active":
        raise redirect_to_login()
    return user


@router.get("/projects", response_class=HTMLResponse)
def project_list(
    request: Request,
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> HTMLResponse:
    if user.role == "admin":
        projects = db.scalars(select(Project).order_by(Project.name)).all()
    else:
        projects = db.scalars(
            select(Project)
            .join(ProjectMembership)
            .where(ProjectMembership.user_id == user.id)
            .where(ProjectMembership.status == "active")
            .order_by(Project.name)
        ).all()
    return templates.TemplateResponse(request, "projects.html", {"user": user, "projects": projects})


@router.get("/users", response_class=HTMLResponse)
def user_list(
    request: Request,
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> HTMLResponse:
    require_web_role(user, "admin")
    users = db.scalars(select(User).order_by(User.username)).all()
    clients = db.scalars(select(ClientDevice).order_by(ClientDevice.last_seen_at_utc.desc())).all()
    return templates.TemplateResponse(request, "users.html", {"user": user, "users": users, "clients": clients, "error": None})


@router.post("/users", response_model=None)
def create_user(
    request: Request,
    username: str = Form(...),
    display_name: str = Form(...),
    role: str = Form(...),
    password: str = Form(...),
    password_confirm: str = Form(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
):
    require_web_role(user, "admin")
    username = username.strip()
    display_name = display_name.strip() or username
    if not username:
        return users_error(request, user, db, "Username is required")
    if role not in {"admin", "reviewer", "translator"}:
        return users_error(request, user, db, "Invalid role")
    if password != password_confirm:
        return users_error(request, user, db, "Password confirmation does not match")
    if len(password) < 8:
        return users_error(request, user, db, "Password must be at least 8 characters")
    if db.scalar(select(User).where(User.username == username)) is not None:
        return users_error(request, user, db, "Username already exists")

    db.add(
        User(
            username=username,
            display_name=display_name,
            role=role,
            password_hash=hash_password(password),
        )
    )
    db.commit()
    return RedirectResponse(url="/admin/users", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/users/{target_user_id}/status")
def update_user_status(
    target_user_id: str,
    status_value: str = Form(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin")
    target_user = db.get(User, target_user_id)
    if target_user is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found")
    if status_value not in {"active", "inactive"}:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Invalid user status")
    target_user.status = status_value
    if status_value != "active":
        db.query(ApiToken).filter(ApiToken.user_id == target_user.id).update({"revoked_at_utc": datetime.now(UTC)})
    db.commit()
    return RedirectResponse(url="/admin/users", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/users/{target_user_id}/password", response_model=None)
def reset_user_password(
    request: Request,
    target_user_id: str,
    password: str = Form(...),
    password_confirm: str = Form(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
):
    require_web_role(user, "admin")
    target_user = db.get(User, target_user_id)
    if target_user is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found")
    if password != password_confirm:
        return users_error(request, user, db, "Password confirmation does not match")
    if len(password) < 8:
        return users_error(request, user, db, "Password must be at least 8 characters")

    target_user.password_hash = hash_password(password)
    db.query(ApiToken).filter(ApiToken.user_id == target_user.id).update({"revoked_at_utc": datetime.now(UTC)})
    db.commit()
    return RedirectResponse(url="/admin/users", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/projects")
def create_project(
    name: str = Form(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin")
    project = Project(name=name.strip())
    db.add(project)
    db.commit()
    return RedirectResponse(url=f"/admin/projects/{project.id}", status_code=status.HTTP_303_SEE_OTHER)


@router.get("/projects/{project_id}", response_class=HTMLResponse)
def project_detail(
    project_id: str,
    request: Request,
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> HTMLResponse:
    project = get_project_for_web_user(db, project_id, user)
    stats = {
        "snapshots": count_for(db, SourceSnapshot, project_id),
        "manifests": count_for(db, ScanManifest, project_id),
        "work_items": count_for(db, WorkItem, project_id),
        "shared_keys": count_for(db, SharedNamespaceEntry, project_id),
        "memberships": count_for(db, ProjectMembership, project_id),
        "assignments": count_for(db, ProjectAssignment, project_id),
        "open_conflicts": db.scalar(
            select(func.count(Conflict.id)).where(Conflict.project_id == project_id).where(Conflict.status == "open")
        ),
        "submissions": count_for(db, Submission, project_id),
    }
    snapshots = db.scalars(
        select(SourceSnapshot).where(SourceSnapshot.project_id == project_id).order_by(SourceSnapshot.created_at_utc.desc()).limit(10)
    ).all()
    conflicts = db.scalars(
        select(Conflict).where(Conflict.project_id == project_id).order_by(Conflict.created_at_utc.desc()).limit(10)
    ).all()
    shared_keys = db.scalars(
        select(SharedNamespaceEntry)
        .where(SharedNamespaceEntry.project_id == project_id)
        .order_by(SharedNamespaceEntry.updated_at_utc.desc())
        .limit(10)
    ).all()
    submissions = db.scalars(
        select(Submission).where(Submission.project_id == project_id).order_by(Submission.created_at_utc.desc()).limit(10)
    ).all()
    users = db.scalars(select(User).order_by(User.username)).all()
    memberships = db.scalars(
        select(ProjectMembership).where(ProjectMembership.project_id == project_id).order_by(ProjectMembership.created_at_utc.desc())
    ).all()
    assignments = db.scalars(
        select(ProjectAssignment).where(ProjectAssignment.project_id == project_id).order_by(ProjectAssignment.created_at_utc.desc())
    ).all()
    return templates.TemplateResponse(
        request,
        "project_detail.html",
        {
            "user": user,
            "project": project,
            "stats": stats,
            "snapshots": snapshots,
            "conflicts": conflicts,
            "shared_keys": shared_keys,
            "submissions": submissions,
            "users": users,
            "memberships": memberships,
            "assignments": assignments,
        },
    )


@router.post("/projects/{project_id}")
def update_project(
    project_id: str,
    name: str = Form(...),
    status_value: str = Form(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin")
    project = get_project_for_web_user(db, project_id, user)
    project.name = name.strip()
    project.status = status_value
    db.commit()
    return RedirectResponse(url=f"/admin/projects/{project_id}", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/projects/{project_id}/memberships")
def upsert_membership(
    project_id: str,
    user_id: str = Form(...),
    role: str = Form(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin")
    get_project_for_web_user(db, project_id, user)
    if db.get(User, user_id) is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found")
    membership = db.scalar(
        select(ProjectMembership).where(ProjectMembership.project_id == project_id).where(ProjectMembership.user_id == user_id)
    )
    if membership is None:
        db.add(ProjectMembership(project_id=project_id, user_id=user_id, role=role))
    else:
        membership.role = role
        membership.status = "active"
    db.commit()
    return RedirectResponse(url=f"/admin/projects/{project_id}", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/projects/{project_id}/assignments")
def add_assignment(
    project_id: str,
    user_id: str = Form(...),
    pattern_kind: str = Form(...),
    pattern: str = Form(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin")
    get_project_for_web_user(db, project_id, user)
    if db.get(User, user_id) is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found")
    db.add(ProjectAssignment(project_id=project_id, user_id=user_id, pattern_kind=pattern_kind, pattern=pattern.strip()))
    db.commit()
    return RedirectResponse(url=f"/admin/projects/{project_id}", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/projects/{project_id}/source")
async def upload_source(
    project_id: str,
    file: UploadFile = File(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin")
    get_project_for_web_user(db, project_id, user)
    scan_revision_id = new_scan_revision_id()
    try:
        stored_archive = await store_uploaded_source_archive(file, project_id, scan_revision_id)
    except ArchiveValidationError as exc:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(exc)) from exc
    db.add(
        SourceSnapshot(
            project_id=project_id,
            scan_revision_id=scan_revision_id,
            archive_path=stored_archive.relative_path,
            archive_sha256=stored_archive.sha256,
            archive_size_bytes=stored_archive.size_bytes,
            archive_file_count=stored_archive.file_count,
            uploaded_by_user_id=user.id,
        )
    )
    db.commit()
    from app.api.routes.source import prune_source_snapshots

    project = get_project_for_web_user(db, project_id, user)
    prune_source_snapshots(db, project)
    return RedirectResponse(url=f"/admin/projects/{project_id}", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/projects/{project_id}/source/{scan_revision_id}/activate")
def activate_source(
    project_id: str,
    scan_revision_id: str,
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin")
    project = get_project_for_web_user(db, project_id, user)
    snapshot = db.scalar(
        select(SourceSnapshot).where(SourceSnapshot.project_id == project_id).where(SourceSnapshot.scan_revision_id == scan_revision_id)
    )
    if snapshot is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Source snapshot not found")
    if not snapshot.has_scan_manifest:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Scan manifest is required before activation")
    project.current_scan_revision_id = scan_revision_id
    snapshot.status = "active"
    db.commit()
    return RedirectResponse(url=f"/admin/projects/{project_id}", status_code=status.HTTP_303_SEE_OTHER)


@router.get("/projects/{project_id}/source/{scan_revision_id}/download")
def download_source(
    project_id: str,
    scan_revision_id: str,
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> FileResponse:
    get_project_for_web_user(db, project_id, user)
    snapshot = db.scalar(
        select(SourceSnapshot).where(SourceSnapshot.project_id == project_id).where(SourceSnapshot.scan_revision_id == scan_revision_id)
    )
    if snapshot is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Source snapshot not found")
    archive_path = resolve_archive_path(snapshot.archive_path)
    if not archive_path.is_file():
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Source archive file is missing")
    return FileResponse(path=archive_path, media_type="application/zip", filename=f"{scan_revision_id}.zip")


@router.post("/projects/{project_id}/source/{scan_revision_id}/scan-manifest")
def upload_manifest(
    project_id: str,
    scan_revision_id: str,
    manifest_json: str = Form(...),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin")
    snapshot = db.scalar(
        select(SourceSnapshot).where(SourceSnapshot.project_id == project_id).where(SourceSnapshot.scan_revision_id == scan_revision_id)
    )
    if snapshot is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Source snapshot not found")
    payload = json.loads(manifest_json)
    request = ScanManifestUploadRequest.model_validate(payload)
    if request.scan_revision_id != scan_revision_id or request.source_archive_sha256 != snapshot.archive_sha256:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Manifest does not match source snapshot")
    # Reuse the API route logic through local imports to keep UI and API behavior aligned.
    from app.api.routes.collaboration import upload_scan_manifest

    upload_scan_manifest(project_id, scan_revision_id, request, user, db)
    return RedirectResponse(url=f"/admin/projects/{project_id}", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/projects/{project_id}/shared-keys/{entry_id}")
def update_shared_key(
    project_id: str,
    entry_id: str,
    translation: str = Form(""),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin", "reviewer")
    entry = db.get(SharedNamespaceEntry, entry_id)
    if entry is None or entry.project_id != project_id:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Shared key not found")
    entry.translation = translation
    entry.status = "approved"
    entry.shared_revision += 1
    db.commit()
    return RedirectResponse(url=f"/admin/projects/{project_id}", status_code=status.HTTP_303_SEE_OTHER)


@router.post("/projects/{project_id}/conflicts/{conflict_id}/resolve")
def resolve_conflict(
    project_id: str,
    conflict_id: str,
    resolution_kind: str = Form(...),
    resolved_value: str = Form(""),
    user: User = Depends(get_web_user),
    db: Session = Depends(get_db),
) -> RedirectResponse:
    require_web_role(user, "admin", "reviewer")
    conflict = db.get(Conflict, conflict_id)
    if conflict is None or conflict.project_id != project_id:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Conflict not found")
    if conflict.status == "open":
        if resolution_kind == "KeepServer":
            final_value = conflict.server_value
        elif resolution_kind == "AcceptIncoming":
            final_value = conflict.incoming_value
        else:
            final_value = resolved_value
        if conflict.target_kind == "work_item":
            target = db.get(WorkItem, conflict.target_id)
            if target is not None:
                target.translation = final_value
                target.status = "translated"
                target.item_revision += 1
        elif conflict.target_kind == "shared_key":
            target = db.get(SharedNamespaceEntry, conflict.target_id)
            if target is not None:
                target.translation = final_value
                target.status = "approved"
                target.shared_revision += 1
        conflict.status = "resolved"
        conflict.resolved_by_user_id = user.id
        conflict.resolution_kind = resolution_kind
        conflict.resolved_value = final_value
        conflict.resolved_at_utc = datetime.now(UTC)
        db.commit()
    return RedirectResponse(url=f"/admin/projects/{project_id}", status_code=status.HTTP_303_SEE_OTHER)


def redirect_to_login() -> HTTPException:
    return HTTPException(status_code=status.HTTP_303_SEE_OTHER, headers={"Location": "/admin/login"})


def redirect_to_setup() -> HTTPException:
    return HTTPException(status_code=status.HTTP_303_SEE_OTHER, headers={"Location": "/admin/setup"})


def is_first_run(db: Session) -> bool:
    return (db.scalar(select(func.count(User.id))) or 0) == 0


def setup_error(
    request: Request,
    error: str,
    database_url: str,
    database_schema: str,
    archive_root: str,
    bootstrap_token: str,
) -> HTMLResponse:
    return templates.TemplateResponse(
        request,
        "setup.html",
        {
            "error": error,
            "database_url": database_url,
            "database_schema": database_schema,
            "archive_root": archive_root,
            "bootstrap_token": bootstrap_token,
        },
        status_code=400,
    )


def users_error(request: Request, user: User, db: Session, error: str) -> HTMLResponse:
    users = db.scalars(select(User).order_by(User.username)).all()
    clients = db.scalars(select(ClientDevice).order_by(ClientDevice.last_seen_at_utc.desc())).all()
    return templates.TemplateResponse(
        request,
        "users.html",
        {"user": user, "users": users, "clients": clients, "error": error},
        status_code=400,
    )


def login_web_user(user: User, db: Session, raw_token: str | None = None) -> RedirectResponse:
    token = raw_token or generate_access_token()
    db.add(ApiToken(user_id=user.id, token_hash=hash_access_token(token), expires_at_utc=access_token_expires_at()))
    db.commit()
    response = RedirectResponse(url="/admin/projects", status_code=status.HTTP_303_SEE_OTHER)
    response.set_cookie(COOKIE_NAME, token, httponly=True, samesite="lax")
    return response


def require_web_role(user: User, *roles: str) -> None:
    if user.role not in roles:
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Insufficient role")


def get_project_for_web_user(db: Session, project_id: str, user: User) -> Project:
    project = db.get(Project, project_id)
    if project is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Project not found")
    if user.role == "admin":
        return project
    membership = db.scalar(
        select(ProjectMembership)
        .where(ProjectMembership.project_id == project_id)
        .where(ProjectMembership.user_id == user.id)
        .where(ProjectMembership.status == "active")
    )
    if membership is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Project not found")
    return project


def count_for(db: Session, model, project_id: str) -> int:
    return db.scalar(select(func.count(model.id)).where(model.project_id == project_id)) or 0


def as_utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=UTC)
    return value.astimezone(UTC)
