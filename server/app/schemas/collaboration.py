from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field


class ClientRegisterRequest(BaseModel):
    client_id: str = Field(min_length=1, max_length=100)
    display_name: str = Field(min_length=1, max_length=200)


class ClientRegisterResponse(BaseModel):
    id: str
    client_id: str
    display_name: str
    status: str
    created_at_utc: datetime
    last_seen_at_utc: datetime


class ManifestDocument(BaseModel):
    relative_path: str = Field(min_length=1, max_length=1000)
    file_type: str = Field(min_length=1, max_length=32)
    encoding: str | None = Field(default=None, max_length=100)


class ManifestItem(BaseModel):
    segment_id: str = Field(min_length=1, max_length=200)
    relative_path: str = Field(min_length=1, max_length=1000)
    line_number: int | None = None
    file_type: str = Field(min_length=1, max_length=32)
    segment_type: str = Field(min_length=1, max_length=100)
    original_text: str
    source_key: str | None = Field(default=None, max_length=500)
    symbol_namespace: str | None = Field(default=None, max_length=200)
    original_symbol_key: str | None = Field(default=None, max_length=500)
    is_reference_bearing_key: bool = False


class ScanManifestUploadRequest(BaseModel):
    scan_revision_id: str = Field(min_length=1, max_length=64)
    source_archive_sha256: str = Field(min_length=64, max_length=64)
    documents: list[ManifestDocument] = Field(default_factory=list)
    items: list[ManifestItem] = Field(default_factory=list)


class ScanManifestValidationResponse(BaseModel):
    scan_revision_id: str
    validation_status: str
    validation_messages: list[dict]
    document_count: int
    item_count: int
    shared_key_count: int


class WorkItemResponse(BaseModel):
    id: str
    scan_revision_id: str
    segment_id: str
    relative_path: str
    line_number: int | None
    file_type: str
    segment_type: str
    original_text: str
    source_key: str | None
    symbol_namespace: str | None
    original_symbol_key: str | None
    is_reference_bearing_key: bool
    translation: str | None
    status: str
    item_revision: int
    carryover_state: str


class SharedKeyResponse(BaseModel):
    id: str
    namespace: str
    key: str
    original_text: str
    translation: str | None
    status: str
    shared_revision: int


class SyncResponse(BaseModel):
    project_id: str
    scan_revision_id: str
    source_archive_sha256: str
    work_items: list[WorkItemResponse]
    shared_keys: list[SharedKeyResponse]


class SubmitWorkItemChange(BaseModel):
    id: str
    base_revision: int
    translation: str | None = None


class SubmitSharedKeyChange(BaseModel):
    id: str
    base_revision: int
    translation: str | None = None


class SubmitRequest(BaseModel):
    submission_id: str = Field(min_length=1, max_length=100)
    scan_revision_id: str = Field(min_length=1, max_length=64)
    client_id: str = Field(min_length=1, max_length=100)
    work_items: list[SubmitWorkItemChange] = Field(default_factory=list)
    shared_keys: list[SubmitSharedKeyChange] = Field(default_factory=list)


class SubmitChangeResult(BaseModel):
    target_kind: Literal["work_item", "shared_key", "submission"]
    target_id: str
    result: str
    conflict_id: str | None = None


class SubmitResponse(BaseModel):
    submission_id: str
    status: str
    applied_count: int
    noop_count: int
    conflict_count: int
    rejected_count: int
    results: list[SubmitChangeResult]


class SharedKeyUpdateRequest(BaseModel):
    base_revision: int
    translation: str | None = None
    status: str = "approved"


class ConflictResponse(BaseModel):
    id: str
    conflict_type: str
    target_kind: str
    target_id: str
    scan_revision_id: str | None
    server_revision: int
    client_base_revision: int
    server_value: str | None
    incoming_value: str | None
    status: str
    resolution_kind: str | None
    resolved_value: str | None
    created_at_utc: datetime
    resolved_at_utc: datetime | None


class ConflictListResponse(BaseModel):
    conflicts: list[ConflictResponse]


class ConflictResolveRequest(BaseModel):
    resolution_kind: Literal["KeepServer", "AcceptIncoming", "ManualMerge"]
    resolved_value: str | None = None


class ConflictResolveResponse(BaseModel):
    conflict: ConflictResponse
