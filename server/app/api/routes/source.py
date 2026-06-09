from fastapi import APIRouter, Depends, File, HTTPException, Query, UploadFile, status
from fastapi.responses import FileResponse
from sqlalchemy import select
from sqlalchemy.orm import Session

from app.api.deps import get_current_user, require_roles
from app.core.config import get_settings
from app.db.session import get_db
from app.models.project import Project, ProjectMembership
from app.models.source_snapshot import SourceSnapshot
from app.models.user import User
from app.schemas.source_snapshot import OrphanArchiveCleanupResponse, SourceSnapshotDeleteResponse, SourceSnapshotListResponse, SourceSnapshotResponse
from app.services.source_archive import (
    ArchiveValidationError,
    cleanup_orphan_archives,
    delete_stored_archive,
    new_scan_revision_id,
    resolve_archive_path,
    store_uploaded_source_archive,
)

router = APIRouter()


@router.get("/{project_id}/source", response_model=SourceSnapshotListResponse)
def list_source_snapshots(
    project_id: str,
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> SourceSnapshotListResponse:
    project = get_project_for_user_or_404(db, project_id, current_user)
    snapshots = db.scalars(
        select(SourceSnapshot).where(SourceSnapshot.project_id == project_id).order_by(SourceSnapshot.created_at_utc.desc())
    ).all()
    return SourceSnapshotListResponse(
        current_scan_revision_id=project.current_scan_revision_id,
        snapshots=[to_source_snapshot_response(snapshot, project) for snapshot in snapshots],
    )


@router.post("/{project_id}/source", response_model=SourceSnapshotResponse, status_code=status.HTTP_201_CREATED)
async def upload_source_snapshot(
    project_id: str,
    file: UploadFile = File(...),
    admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> SourceSnapshotResponse:
    project = get_project_or_404(db, project_id)
    if file.filename and not file.filename.lower().endswith(".zip"):
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Source archive must be a zip file")

    scan_revision_id = new_scan_revision_id()
    try:
        stored_archive = await store_uploaded_source_archive(file, project_id, scan_revision_id)
    except ArchiveValidationError as exc:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(exc)) from exc

    snapshot = SourceSnapshot(
        project_id=project_id,
        scan_revision_id=scan_revision_id,
        archive_path=stored_archive.relative_path,
        archive_sha256=stored_archive.sha256,
        archive_size_bytes=stored_archive.size_bytes,
        archive_file_count=stored_archive.file_count,
        uploaded_by_user_id=admin.id,
    )
    db.add(snapshot)
    db.commit()
    db.refresh(snapshot)
    prune_source_snapshots(db, project)
    return to_source_snapshot_response(snapshot, project)


@router.get("/{project_id}/source/download")
def download_source_snapshot(
    project_id: str,
    scan_revision_id: str | None = Query(default=None),
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> FileResponse:
    project = get_project_for_user_or_404(db, project_id, current_user)
    target_revision_id = scan_revision_id or project.current_scan_revision_id
    if not target_revision_id:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="No active source snapshot")

    snapshot = get_source_snapshot_or_404(db, project_id, target_revision_id)
    archive_path = resolve_archive_path(snapshot.archive_path)
    if not archive_path.is_file():
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Source archive file is missing")
    return FileResponse(path=archive_path, media_type="application/zip", filename=f"{snapshot.scan_revision_id}.zip")


@router.post("/{project_id}/source/orphans/cleanup", response_model=OrphanArchiveCleanupResponse)
def cleanup_orphan_source_archives(
    project_id: str,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> OrphanArchiveCleanupResponse:
    get_project_or_404(db, project_id)
    referenced = set(db.scalars(select(SourceSnapshot.archive_path)).all())
    deleted_paths = cleanup_orphan_archives(referenced)
    return OrphanArchiveCleanupResponse(deleted_paths=deleted_paths)


@router.delete("/{project_id}/source/{scan_revision_id}", response_model=SourceSnapshotDeleteResponse)
def delete_source_snapshot(
    project_id: str,
    scan_revision_id: str,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> SourceSnapshotDeleteResponse:
    project = get_project_or_404(db, project_id)
    snapshot = get_source_snapshot_or_404(db, project_id, scan_revision_id)
    if project.current_scan_revision_id == snapshot.scan_revision_id or snapshot.status == "active":
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Active source snapshot cannot be deleted")

    delete_stored_archive(snapshot.archive_path)
    db.delete(snapshot)
    db.commit()
    return SourceSnapshotDeleteResponse(deleted=True, scan_revision_id=scan_revision_id)


@router.post("/{project_id}/source/{scan_revision_id}/activate", response_model=SourceSnapshotResponse)
def activate_source_snapshot(
    project_id: str,
    scan_revision_id: str,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> SourceSnapshotResponse:
    project = get_project_or_404(db, project_id)
    snapshot = get_source_snapshot_or_404(db, project_id, scan_revision_id)
    if not snapshot.has_scan_manifest:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Source snapshot cannot be activated until its scan manifest is uploaded",
        )

    project.current_scan_revision_id = snapshot.scan_revision_id
    snapshot.status = "active"
    db.commit()
    db.refresh(project)
    db.refresh(snapshot)
    return to_source_snapshot_response(snapshot, project)


def get_project_or_404(db: Session, project_id: str) -> Project:
    project = db.get(Project, project_id)
    if project is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Project not found")
    return project


def get_project_for_user_or_404(db: Session, project_id: str, user: User) -> Project:
    project = get_project_or_404(db, project_id)
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


def get_source_snapshot_or_404(db: Session, project_id: str, scan_revision_id: str) -> SourceSnapshot:
    snapshot = db.scalar(
        select(SourceSnapshot)
        .where(SourceSnapshot.project_id == project_id)
        .where(SourceSnapshot.scan_revision_id == scan_revision_id)
    )
    if snapshot is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Source snapshot not found")
    return snapshot


def to_source_snapshot_response(snapshot: SourceSnapshot, project: Project) -> SourceSnapshotResponse:
    return SourceSnapshotResponse(
        id=snapshot.id,
        project_id=snapshot.project_id,
        scan_revision_id=snapshot.scan_revision_id,
        archive_sha256=snapshot.archive_sha256,
        archive_size_bytes=snapshot.archive_size_bytes,
        archive_file_count=snapshot.archive_file_count,
        uploaded_by_user_id=snapshot.uploaded_by_user_id,
        has_scan_manifest=snapshot.has_scan_manifest,
        status=snapshot.status,
        is_current=snapshot.scan_revision_id == project.current_scan_revision_id,
        created_at_utc=snapshot.created_at_utc,
    )


def prune_source_snapshots(db: Session, project: Project) -> None:
    retention_count = max(1, get_settings().source_snapshot_retention_count)
    snapshots = db.scalars(
        select(SourceSnapshot).where(SourceSnapshot.project_id == project.id).order_by(SourceSnapshot.created_at_utc.desc())
    ).all()
    retained_ids = {snapshot.id for snapshot in snapshots[:retention_count]}
    for snapshot in snapshots[retention_count:]:
        if snapshot.id in retained_ids or snapshot.scan_revision_id == project.current_scan_revision_id or snapshot.status == "active":
            continue
        delete_stored_archive(snapshot.archive_path)
        db.delete(snapshot)
    db.commit()
