import hashlib
import os
import shutil
import tempfile
import uuid
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

from fastapi import UploadFile

from app.core.config import get_settings

FORBIDDEN_PATH_SEGMENTS = {
    ".era-translator",
    ".era-translator-backup",
    ".git",
    "__pycache__",
    "cache",
    "logs",
    "output",
}


class ArchiveValidationError(ValueError):
    pass


@dataclass(frozen=True)
class StoredSourceArchive:
    relative_path: str
    sha256: str
    size_bytes: int
    file_count: int


async def store_uploaded_source_archive(upload: UploadFile, project_id: str, scan_revision_id: str) -> StoredSourceArchive:
    settings = get_settings()
    archive_root = settings.archive_root.resolve()
    archive_root.mkdir(parents=True, exist_ok=True)
    temp_fd, temp_name = tempfile.mkstemp(prefix="upload-", suffix=".zip", dir=archive_root)
    os.close(temp_fd)
    temp_path = Path(temp_name)

    try:
        sha256 = hashlib.sha256()
        size_bytes = 0
        with temp_path.open("wb") as target:
            while chunk := await upload.read(1024 * 1024):
                size_bytes += len(chunk)
                if size_bytes > settings.max_archive_bytes:
                    raise ArchiveValidationError("Archive exceeds configured byte limit")
                sha256.update(chunk)
                target.write(chunk)

        validation = validate_source_archive(temp_path)
        target_path = archive_root / project_id / scan_revision_id / f"{sha256.hexdigest()[:16]}.zip"
        target_path.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(temp_path), target_path)
        return StoredSourceArchive(
            relative_path=target_path.relative_to(archive_root).as_posix(),
            sha256=sha256.hexdigest(),
            size_bytes=size_bytes,
            file_count=validation.file_count,
        )
    except Exception:
        if temp_path.exists():
            temp_path.unlink()
        raise
    finally:
        await upload.close()


def resolve_archive_path(relative_path: str) -> Path:
    archive_root = get_settings().archive_root.resolve()
    archive_path = (archive_root / relative_path).resolve()
    if not archive_path.is_relative_to(archive_root):
        raise ArchiveValidationError("Stored archive path escapes archive root")
    return archive_path


def delete_stored_archive(relative_path: str) -> bool:
    archive_path = resolve_archive_path(relative_path)
    if archive_path.is_file():
        archive_path.unlink()
        remove_empty_parents(archive_path.parent)
        return True
    return False


def cleanup_orphan_archives(referenced_relative_paths: set[str]) -> list[str]:
    archive_root = get_settings().archive_root.resolve()
    if not archive_root.exists():
        return []

    deleted: list[str] = []
    normalized_references = {Path(path).as_posix() for path in referenced_relative_paths}
    for archive_path in archive_root.rglob("*.zip"):
        relative_path = archive_path.relative_to(archive_root).as_posix()
        if relative_path not in normalized_references:
            archive_path.unlink()
            deleted.append(relative_path)
            remove_empty_parents(archive_path.parent)
    return deleted


def remove_empty_parents(start: Path) -> None:
    archive_root = get_settings().archive_root.resolve()
    current = start.resolve()
    while current != archive_root and archive_root in current.parents:
        try:
            current.rmdir()
        except OSError:
            return
        current = current.parent


@dataclass(frozen=True)
class ArchiveValidationResult:
    file_count: int
    uncompressed_bytes: int


def validate_source_archive(path: Path) -> ArchiveValidationResult:
    settings = get_settings()
    seen_paths: set[str] = set()
    file_count = 0
    uncompressed_bytes = 0

    try:
        with zipfile.ZipFile(path) as archive:
            for entry in archive.infolist():
                normalized_path = normalize_zip_entry_name(entry.filename)
                if normalized_path is None:
                    continue
                if normalized_path in seen_paths:
                    raise ArchiveValidationError(f"Duplicate archive path: {normalized_path}")
                if is_zip_symlink(entry):
                    raise ArchiveValidationError(f"Symbolic links are not allowed: {normalized_path}")

                seen_paths.add(normalized_path)
                file_count += 1
                uncompressed_bytes += entry.file_size
                if file_count > settings.max_archive_files:
                    raise ArchiveValidationError("Archive exceeds configured file count limit")
                if uncompressed_bytes > settings.max_archive_bytes:
                    raise ArchiveValidationError("Archive contents exceed configured byte limit")
    except zipfile.BadZipFile as exc:
        raise ArchiveValidationError("Uploaded source archive must be a valid zip file") from exc

    if file_count == 0:
        raise ArchiveValidationError("Source archive must contain at least one file")
    return ArchiveValidationResult(file_count=file_count, uncompressed_bytes=uncompressed_bytes)


def normalize_zip_entry_name(name: str) -> str | None:
    normalized = name.replace("\\", "/")
    if normalized.endswith("/"):
        return None
    if normalized.startswith("/") or normalized.startswith("../"):
        raise ArchiveValidationError(f"Unsafe archive path: {name}")
    if len(normalized) >= 2 and normalized[1] == ":":
        raise ArchiveValidationError(f"Absolute archive path is not allowed: {name}")

    path = PurePosixPath(normalized)
    parts = path.parts
    if any(part in {"", ".", ".."} for part in parts):
        raise ArchiveValidationError(f"Unsafe archive path: {name}")
    if any(part.lower() in FORBIDDEN_PATH_SEGMENTS for part in parts):
        raise ArchiveValidationError(f"Excluded archive path is not allowed: {name}")
    return path.as_posix()


def is_zip_symlink(entry: zipfile.ZipInfo) -> bool:
    unix_mode = entry.external_attr >> 16
    return (unix_mode & 0o170000) == 0o120000


def new_scan_revision_id() -> str:
    return f"src-{uuid.uuid4().hex}"
