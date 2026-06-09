from datetime import datetime

from pydantic import BaseModel


class SourceSnapshotResponse(BaseModel):
    id: str
    project_id: str
    scan_revision_id: str
    archive_sha256: str
    archive_size_bytes: int
    archive_file_count: int
    uploaded_by_user_id: str
    has_scan_manifest: bool
    status: str
    is_current: bool
    created_at_utc: datetime


class SourceSnapshotListResponse(BaseModel):
    current_scan_revision_id: str | None
    snapshots: list[SourceSnapshotResponse]


class SourceSnapshotDeleteResponse(BaseModel):
    deleted: bool
    scan_revision_id: str


class OrphanArchiveCleanupResponse(BaseModel):
    deleted_paths: list[str]
