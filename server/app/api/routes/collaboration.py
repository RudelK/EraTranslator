import hashlib
import json
from datetime import UTC, datetime
from fnmatch import fnmatchcase

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import delete, select
from sqlalchemy.orm import Session

from app.api.deps import get_current_user, require_roles
from app.db.session import get_db
from app.models.collaboration import (
    ClientDevice,
    Conflict,
    ScanManifest,
    SharedNamespaceEntry,
    Submission,
    SubmissionChange,
    WorkItem,
)
from app.models.project import Project, ProjectAssignment, ProjectMembership
from app.models.source_snapshot import SourceSnapshot
from app.models.user import User
from app.schemas.collaboration import (
    ConflictListResponse,
    ConflictResolveRequest,
    ConflictResolveResponse,
    ConflictResponse,
    ScanManifestUploadRequest,
    ScanManifestValidationResponse,
    SharedKeyResponse,
    SharedKeyUpdateRequest,
    SubmitChangeResult,
    SubmitRequest,
    SubmitResponse,
    SyncResponse,
    WorkItemResponse,
)

router = APIRouter()


@router.post("/{project_id}/source/{scan_revision_id}/scan-manifest", response_model=ScanManifestValidationResponse)
def upload_scan_manifest(
    project_id: str,
    scan_revision_id: str,
    request: ScanManifestUploadRequest,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> ScanManifestValidationResponse:
    project = get_project_or_404(db, project_id)
    snapshot = get_source_snapshot_or_404(db, project_id, scan_revision_id)
    if request.scan_revision_id != scan_revision_id:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="ScanRevisionId does not match route")
    if request.source_archive_sha256 != snapshot.archive_sha256:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Source archive sha256 does not match snapshot")

    validation_messages = validate_manifest_items(request)
    validation_status = "valid" if not validation_messages else "warning"
    db.execute(delete(WorkItem).where(WorkItem.project_id == project_id).where(WorkItem.scan_revision_id == scan_revision_id))
    db.execute(delete(ScanManifest).where(ScanManifest.project_id == project_id).where(ScanManifest.scan_revision_id == scan_revision_id))
    db.flush()

    shared_key_count = 0
    for item in request.items:
        carryover = find_carryover(db, project_id, scan_revision_id, item.segment_id, item.source_key, item.original_text)
        work_item = WorkItem(
            project_id=project_id,
            scan_revision_id=scan_revision_id,
            segment_id=item.segment_id,
            relative_path=item.relative_path,
            line_number=item.line_number,
            file_type=item.file_type,
            segment_type=item.segment_type,
            original_text=item.original_text,
            source_key=item.source_key,
            symbol_namespace=item.symbol_namespace,
            original_symbol_key=item.original_symbol_key,
            is_reference_bearing_key=item.is_reference_bearing_key,
            translation=carryover.translation if carryover else None,
            status="needs_review" if carryover else "pending",
            carryover_state="carried" if carryover else "new",
        )
        db.add(work_item)
        db.flush()
        if item.is_reference_bearing_key and item.symbol_namespace and item.original_symbol_key:
            shared_key_count += upsert_shared_key(db, project_id, work_item)

    manifest = ScanManifest(
        project_id=project_id,
        source_snapshot_id=snapshot.id,
        scan_revision_id=scan_revision_id,
        source_archive_sha256=request.source_archive_sha256,
        document_count=len(request.documents),
        item_count=len(request.items),
        shared_key_count=shared_key_count,
        validation_status=validation_status,
        validation_messages=validation_messages,
        uploaded_by_user_id=_admin.id,
    )
    snapshot.has_scan_manifest = True
    snapshot.status = "manifest_uploaded"
    db.add(manifest)
    db.commit()
    db.refresh(manifest)
    return to_manifest_validation_response(manifest)


@router.get("/{project_id}/source/{scan_revision_id}/scan-manifest/validation", response_model=ScanManifestValidationResponse)
def get_scan_manifest_validation(
    project_id: str,
    scan_revision_id: str,
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> ScanManifestValidationResponse:
    get_project_for_user_or_404(db, project_id, current_user)
    manifest = get_scan_manifest_or_404(db, project_id, scan_revision_id)
    return to_manifest_validation_response(manifest)


@router.get("/{project_id}/sync", response_model=SyncResponse)
def sync_project(
    project_id: str,
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> SyncResponse:
    project = get_project_for_user_or_404(db, project_id, current_user)
    if not project.current_scan_revision_id:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Project has no active source snapshot")
    snapshot = get_source_snapshot_or_404(db, project_id, project.current_scan_revision_id)
    if not snapshot.has_scan_manifest:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Active source snapshot has no scan manifest")

    work_items = db.scalars(
        select(WorkItem)
        .where(WorkItem.project_id == project_id)
        .where(WorkItem.scan_revision_id == project.current_scan_revision_id)
        .order_by(WorkItem.relative_path, WorkItem.line_number, WorkItem.segment_id)
    ).all()
    shared_keys = db.scalars(
        select(SharedNamespaceEntry).where(SharedNamespaceEntry.project_id == project_id).order_by(
            SharedNamespaceEntry.namespace, SharedNamespaceEntry.key
        )
    ).all()
    return SyncResponse(
        project_id=project_id,
        scan_revision_id=project.current_scan_revision_id,
        source_archive_sha256=snapshot.archive_sha256,
        work_items=[to_work_item_response(item) for item in work_items],
        shared_keys=[to_shared_key_response(entry) for entry in shared_keys],
    )


@router.post("/{project_id}/submit", response_model=SubmitResponse)
def submit_project_changes(
    project_id: str,
    request: SubmitRequest,
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> SubmitResponse:
    project = get_project_for_user_or_404(db, project_id, current_user)
    if db.scalar(select(ClientDevice).where(ClientDevice.client_id == request.client_id).where(ClientDevice.status == "active")) is None:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="ClientId is not registered")

    payload_hash = hash_submit_payload(request)
    existing = db.scalar(select(Submission).where(Submission.project_id == project_id).where(Submission.submission_id == request.submission_id))
    if existing is not None:
        if existing.payload_hash and existing.payload_hash != payload_hash:
            conflict = create_scope_conflict(
                db,
                project_id,
                "DuplicateSubmissionConflict",
                "submission",
                existing.id,
                0,
                existing.payload_hash,
                payload_hash,
                request.scan_revision_id,
            )
            db.commit()
            return SubmitResponse(
                submission_id=request.submission_id,
                status="duplicate_conflict",
                applied_count=0,
                noop_count=0,
                conflict_count=1,
                rejected_count=0,
                results=[
                    SubmitChangeResult(
                        target_kind="submission",
                        target_id=existing.id,
                        result="Conflict",
                        conflict_id=conflict.id,
                    )
                ],
            )
        return response_from_existing_submission(db, existing)

    submission = Submission(
        submission_id=request.submission_id,
        project_id=project_id,
        scan_revision_id=request.scan_revision_id,
        client_id=request.client_id,
        payload_hash=payload_hash,
        submitted_by_user_id=current_user.id,
    )
    db.add(submission)
    db.flush()

    results: list[SubmitChangeResult] = []
    for change in request.work_items:
        result = process_work_item_change(db, project, submission, current_user, change.id, change.base_revision, change.translation)
        results.append(result)
    for change in request.shared_keys:
        result = process_shared_key_change(db, project, submission, change.id, change.base_revision, change.translation)
        results.append(result)

    submission.applied_count = sum(1 for result in results if result.result == "Applied")
    submission.noop_count = sum(1 for result in results if result.result == "NoOp")
    submission.conflict_count = sum(1 for result in results if result.result == "Conflict")
    submission.rejected_count = sum(1 for result in results if result.result == "Rejected")
    submission.status = "processed"
    db.commit()
    return SubmitResponse(
        submission_id=submission.submission_id,
        status=submission.status,
        applied_count=submission.applied_count,
        noop_count=submission.noop_count,
        conflict_count=submission.conflict_count,
        rejected_count=submission.rejected_count,
        results=results,
    )


@router.get("/{project_id}/conflicts", response_model=ConflictListResponse)
def list_conflicts(
    project_id: str,
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> ConflictListResponse:
    get_project_for_user_or_404(db, project_id, current_user)
    conflicts = db.scalars(select(Conflict).where(Conflict.project_id == project_id).order_by(Conflict.created_at_utc.desc())).all()
    return ConflictListResponse(conflicts=[to_conflict_response(conflict) for conflict in conflicts])


@router.post("/{project_id}/conflicts/{conflict_id}/resolve", response_model=ConflictResolveResponse)
def resolve_conflict(
    project_id: str,
    conflict_id: str,
    request: ConflictResolveRequest,
    reviewer: User = Depends(require_roles("admin", "reviewer")),
    db: Session = Depends(get_db),
) -> ConflictResolveResponse:
    get_project_or_404(db, project_id)
    conflict = db.get(Conflict, conflict_id)
    if conflict is None or conflict.project_id != project_id:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Conflict not found")
    if conflict.status != "open":
        return ConflictResolveResponse(conflict=to_conflict_response(conflict))

    resolved_value = choose_resolved_value(conflict, request)
    if conflict.target_kind == "work_item":
        target = db.get(WorkItem, conflict.target_id)
        if target is not None:
            target.translation = resolved_value
            target.status = "translated"
            target.item_revision += 1
    elif conflict.target_kind == "shared_key":
        target = db.get(SharedNamespaceEntry, conflict.target_id)
        if target is not None:
            target.translation = resolved_value
            target.status = "approved"
            target.shared_revision += 1

    conflict.status = "resolved"
    conflict.resolved_by_user_id = reviewer.id
    conflict.resolution_kind = request.resolution_kind
    conflict.resolved_value = resolved_value
    conflict.resolved_at_utc = datetime.now(UTC)
    db.commit()
    db.refresh(conflict)
    return ConflictResolveResponse(conflict=to_conflict_response(conflict))


@router.get("/{project_id}/shared-keys", response_model=list[SharedKeyResponse])
def list_shared_keys(
    project_id: str,
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> list[SharedKeyResponse]:
    get_project_for_user_or_404(db, project_id, current_user)
    entries = db.scalars(
        select(SharedNamespaceEntry)
        .where(SharedNamespaceEntry.project_id == project_id)
        .order_by(SharedNamespaceEntry.namespace, SharedNamespaceEntry.key)
    ).all()
    return [to_shared_key_response(entry) for entry in entries]


@router.post("/{project_id}/shared-keys/{entry_id}", response_model=SharedKeyResponse)
def update_shared_key(
    project_id: str,
    entry_id: str,
    request: SharedKeyUpdateRequest,
    _reviewer: User = Depends(require_roles("admin", "reviewer")),
    db: Session = Depends(get_db),
) -> SharedKeyResponse:
    get_project_or_404(db, project_id)
    entry = db.get(SharedNamespaceEntry, entry_id)
    if entry is None or entry.project_id != project_id:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Shared key not found")
    if entry.shared_revision != request.base_revision:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail="Shared key revision is stale")
    entry.translation = request.translation
    entry.status = request.status
    entry.shared_revision += 1
    db.commit()
    db.refresh(entry)
    return to_shared_key_response(entry)


def process_work_item_change(
    db: Session,
    project: Project,
    submission: Submission,
    user: User,
    target_id: str,
    base_revision: int,
    translation: str | None,
) -> SubmitChangeResult:
    target = db.get(WorkItem, target_id)
    if target is not None and target.project_id != project.id:
        conflict = create_scope_conflict(
            db,
            project.id,
            "ProjectScopeConflict",
            "work_item",
            target_id,
            base_revision,
            target.translation,
            translation,
            submission.scan_revision_id,
        )
        return record_submission_change(db, submission, "work_item", target_id, base_revision, translation, "Conflict", conflict.id)
    if target is None:
        return record_submission_change(db, submission, "work_item", target_id, base_revision, translation, "Rejected", None)
    if project.current_scan_revision_id != submission.scan_revision_id or target.scan_revision_id != submission.scan_revision_id:
        conflict = create_conflict(db, project.id, "SourceChangedConflict", "work_item", target, base_revision, translation)
        return record_submission_change(db, submission, "work_item", target_id, base_revision, translation, "Conflict", conflict.id)
    if user.role != "admin" and not is_assigned_to_user(db, project.id, user.id, target.relative_path):
        conflict = create_conflict(db, project.id, "AssignmentConflict", "work_item", target, base_revision, translation)
        return record_submission_change(db, submission, "work_item", target_id, base_revision, translation, "Conflict", conflict.id)
    if target.item_revision == base_revision:
        if target.translation == translation:
            return record_submission_change(db, submission, "work_item", target_id, base_revision, translation, "NoOp", None)
        target.translation = translation
        target.status = "translated"
        target.item_revision += 1
        return record_submission_change(db, submission, "work_item", target_id, base_revision, translation, "Applied", None)
    if target.translation == translation:
        return record_submission_change(db, submission, "work_item", target_id, base_revision, translation, "NoOp", None)
    conflict = create_conflict(db, project.id, "StaleRevisionConflict", "work_item", target, base_revision, translation)
    return record_submission_change(db, submission, "work_item", target_id, base_revision, translation, "Conflict", conflict.id)


def process_shared_key_change(
    db: Session,
    project: Project,
    submission: Submission,
    target_id: str,
    base_revision: int,
    translation: str | None,
) -> SubmitChangeResult:
    target = db.get(SharedNamespaceEntry, target_id)
    if target is not None and target.project_id != project.id:
        conflict = create_scope_conflict(
            db,
            project.id,
            "ProjectScopeConflict",
            "shared_key",
            target_id,
            base_revision,
            target.translation,
            translation,
            submission.scan_revision_id,
        )
        return record_submission_change(db, submission, "shared_key", target_id, base_revision, translation, "Conflict", conflict.id)
    if target is None:
        return record_submission_change(db, submission, "shared_key", target_id, base_revision, translation, "Rejected", None)
    if project.current_scan_revision_id != submission.scan_revision_id:
        conflict = create_conflict(db, project.id, "SourceChangedConflict", "shared_key", target, base_revision, translation)
        return record_submission_change(db, submission, "shared_key", target_id, base_revision, translation, "Conflict", conflict.id)
    if target.shared_revision == base_revision:
        if target.translation == translation:
            return record_submission_change(db, submission, "shared_key", target_id, base_revision, translation, "NoOp", None)
        target.translation = translation
        target.status = "review_needed"
        target.shared_revision += 1
        return record_submission_change(db, submission, "shared_key", target_id, base_revision, translation, "Applied", None)
    if target.translation == translation:
        return record_submission_change(db, submission, "shared_key", target_id, base_revision, translation, "NoOp", None)
    conflict = create_conflict(db, project.id, "SharedNamespaceConflict", "shared_key", target, base_revision, translation)
    return record_submission_change(db, submission, "shared_key", target_id, base_revision, translation, "Conflict", conflict.id)


def record_submission_change(
    db: Session,
    submission: Submission,
    target_kind: str,
    target_id: str,
    base_revision: int,
    translation: str | None,
    result: str,
    conflict_id: str | None,
) -> SubmitChangeResult:
    db.add(
        SubmissionChange(
            submission_id=submission.id,
            target_kind=target_kind,
            target_id=target_id,
            base_revision=base_revision,
            incoming_translation=translation,
            result=result,
            conflict_id=conflict_id,
        )
    )
    return SubmitChangeResult(target_kind=target_kind, target_id=target_id, result=result, conflict_id=conflict_id)


def create_conflict(
    db: Session,
    project_id: str,
    conflict_type: str,
    target_kind: str,
    target: WorkItem | SharedNamespaceEntry,
    base_revision: int,
    incoming_value: str | None,
) -> Conflict:
    server_revision = target.item_revision if isinstance(target, WorkItem) else target.shared_revision
    server_value = target.translation
    conflict = Conflict(
        project_id=project_id,
        conflict_type=conflict_type,
        target_kind=target_kind,
        target_id=target.id,
        scan_revision_id=target.scan_revision_id if isinstance(target, WorkItem) else None,
        server_revision=server_revision,
        client_base_revision=base_revision,
        server_value=server_value,
        incoming_value=incoming_value,
    )
    db.add(conflict)
    db.flush()
    return conflict


def create_scope_conflict(
    db: Session,
    project_id: str,
    conflict_type: str,
    target_kind: str,
    target_id: str,
    base_revision: int,
    server_value: str | None,
    incoming_value: str | None,
    scan_revision_id: str | None,
) -> Conflict:
    conflict = Conflict(
        project_id=project_id,
        conflict_type=conflict_type,
        target_kind=target_kind,
        target_id=target_id,
        scan_revision_id=scan_revision_id,
        server_revision=0,
        client_base_revision=base_revision,
        server_value=server_value,
        incoming_value=incoming_value,
    )
    db.add(conflict)
    db.flush()
    return conflict


def hash_submit_payload(request: SubmitRequest) -> str:
    payload = request.model_dump(mode="json")
    serialized = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(serialized.encode("utf-8")).hexdigest()


def response_from_existing_submission(db: Session, submission: Submission) -> SubmitResponse:
    changes = db.scalars(select(SubmissionChange).where(SubmissionChange.submission_id == submission.id)).all()
    return SubmitResponse(
        submission_id=submission.submission_id,
        status=submission.status,
        applied_count=submission.applied_count,
        noop_count=submission.noop_count,
        conflict_count=submission.conflict_count,
        rejected_count=submission.rejected_count,
        results=[
            SubmitChangeResult(
                target_kind=change.target_kind,
                target_id=change.target_id,
                result=change.result,
                conflict_id=change.conflict_id,
            )
            for change in changes
        ],
    )


def validate_manifest_items(request: ScanManifestUploadRequest) -> list[dict]:
    messages: list[dict] = []
    seen_segments: set[str] = set()
    for item in request.items:
        if item.segment_id in seen_segments:
            messages.append({"level": "error", "code": "DuplicateSegmentId", "segment_id": item.segment_id})
        seen_segments.add(item.segment_id)
        if item.is_reference_bearing_key and not (item.symbol_namespace and item.original_symbol_key):
            messages.append({"level": "warning", "code": "ReferenceKeyMissingNamespace", "segment_id": item.segment_id})
    return messages


def find_carryover(
    db: Session,
    project_id: str,
    scan_revision_id: str,
    segment_id: str,
    source_key: str | None,
    original_text: str,
) -> WorkItem | None:
    match = db.scalar(
        select(WorkItem)
        .where(WorkItem.project_id == project_id)
        .where(WorkItem.scan_revision_id != scan_revision_id)
        .where(WorkItem.segment_id == segment_id)
        .where(WorkItem.original_text == original_text)
        .where(WorkItem.translation.is_not(None))
        .order_by(WorkItem.updated_at_utc.desc())
    )
    if match is not None:
        return match
    if source_key:
        match = db.scalar(
            select(WorkItem)
            .where(WorkItem.project_id == project_id)
            .where(WorkItem.source_key == source_key)
            .where(WorkItem.original_text == original_text)
            .where(WorkItem.translation.is_not(None))
            .order_by(WorkItem.updated_at_utc.desc())
        )
        if match is not None:
            return match
    return db.scalar(
        select(WorkItem)
        .where(WorkItem.project_id == project_id)
        .where(WorkItem.original_text == original_text)
        .where(WorkItem.translation.is_not(None))
        .order_by(WorkItem.updated_at_utc.desc())
    )


def upsert_shared_key(db: Session, project_id: str, work_item: WorkItem) -> int:
    entry = db.scalar(
        select(SharedNamespaceEntry)
        .where(SharedNamespaceEntry.project_id == project_id)
        .where(SharedNamespaceEntry.namespace == work_item.symbol_namespace)
        .where(SharedNamespaceEntry.key == work_item.original_symbol_key)
    )
    if entry is None:
        db.add(
            SharedNamespaceEntry(
                project_id=project_id,
                namespace=work_item.symbol_namespace or "",
                key=work_item.original_symbol_key or "",
                original_text=work_item.original_text,
                translation=work_item.translation,
                status="needs_review" if work_item.translation else "pending",
                source_work_item_id=work_item.id,
            )
        )
        return 1
    entry.original_text = work_item.original_text
    entry.source_work_item_id = work_item.id
    return 0


def is_assigned_to_user(db: Session, project_id: str, user_id: str, relative_path: str) -> bool:
    assignments = db.scalars(
        select(ProjectAssignment)
        .where(ProjectAssignment.project_id == project_id)
        .where(ProjectAssignment.user_id == user_id)
        .where(ProjectAssignment.status == "active")
    ).all()
    if not assignments:
        return False
    normalized = relative_path.replace("\\", "/")
    for assignment in assignments:
        pattern = assignment.pattern.replace("\\", "/")
        if assignment.pattern_kind == "glob" and fnmatchcase(normalized, pattern):
            return True
        if assignment.pattern_kind == "prefix" and normalized.startswith(pattern):
            return True
    return False


def choose_resolved_value(conflict: Conflict, request: ConflictResolveRequest) -> str | None:
    if request.resolution_kind == "KeepServer":
        return conflict.server_value
    if request.resolution_kind == "AcceptIncoming":
        return conflict.incoming_value
    return request.resolved_value


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


def get_scan_manifest_or_404(db: Session, project_id: str, scan_revision_id: str) -> ScanManifest:
    manifest = db.scalar(
        select(ScanManifest).where(ScanManifest.project_id == project_id).where(ScanManifest.scan_revision_id == scan_revision_id)
    )
    if manifest is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Scan manifest not found")
    return manifest


def to_manifest_validation_response(manifest: ScanManifest) -> ScanManifestValidationResponse:
    return ScanManifestValidationResponse(
        scan_revision_id=manifest.scan_revision_id,
        validation_status=manifest.validation_status,
        validation_messages=manifest.validation_messages,
        document_count=manifest.document_count,
        item_count=manifest.item_count,
        shared_key_count=manifest.shared_key_count,
    )


def to_work_item_response(item: WorkItem) -> WorkItemResponse:
    return WorkItemResponse(
        id=item.id,
        scan_revision_id=item.scan_revision_id,
        segment_id=item.segment_id,
        relative_path=item.relative_path,
        line_number=item.line_number,
        file_type=item.file_type,
        segment_type=item.segment_type,
        original_text=item.original_text,
        source_key=item.source_key,
        symbol_namespace=item.symbol_namespace,
        original_symbol_key=item.original_symbol_key,
        is_reference_bearing_key=item.is_reference_bearing_key,
        translation=item.translation,
        status=item.status,
        item_revision=item.item_revision,
        carryover_state=item.carryover_state,
    )


def to_shared_key_response(entry: SharedNamespaceEntry) -> SharedKeyResponse:
    return SharedKeyResponse(
        id=entry.id,
        namespace=entry.namespace,
        key=entry.key,
        original_text=entry.original_text,
        translation=entry.translation,
        status=entry.status,
        shared_revision=entry.shared_revision,
    )


def to_conflict_response(conflict: Conflict) -> ConflictResponse:
    return ConflictResponse(
        id=conflict.id,
        conflict_type=conflict.conflict_type,
        target_kind=conflict.target_kind,
        target_id=conflict.target_id,
        scan_revision_id=conflict.scan_revision_id,
        server_revision=conflict.server_revision,
        client_base_revision=conflict.client_base_revision,
        server_value=conflict.server_value,
        incoming_value=conflict.incoming_value,
        status=conflict.status,
        resolution_kind=conflict.resolution_kind,
        resolved_value=conflict.resolved_value,
        created_at_utc=conflict.created_at_utc,
        resolved_at_utc=conflict.resolved_at_utc,
    )
