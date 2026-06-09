from io import BytesIO
from zipfile import ZipFile

from fastapi.testclient import TestClient

from app.core.config import reset_settings_cache
from app.models.project import Project, ProjectMembership
from app.models.source_snapshot import SourceSnapshot
from app.models.user import User
from app.services.security import hash_password


def test_admin_uploads_source_snapshot(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    project = create_project(db_session)

    with TestClient(client) as test_client:
        token = login(test_client, admin.username)
        response = test_client.post(
            f"/api/projects/{project.id}/source",
            files={"file": ("source.zip", make_zip({"ERB/SYSTEM.ERB": "PRINTL test"}), "application/zip")},
            headers=auth_header(token),
        )

    assert response.status_code == 201
    body = response.json()
    assert body["project_id"] == project.id
    assert body["archive_file_count"] == 1
    assert body["has_scan_manifest"] is False
    assert body["is_current"] is False


def test_upload_rejects_zip_slip_path(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    project = create_project(db_session)

    with TestClient(client) as test_client:
        token = login(test_client, admin.username)
        response = test_client.post(
            f"/api/projects/{project.id}/source",
            files={"file": ("source.zip", make_zip({"../escape.ERB": "bad"}), "application/zip")},
            headers=auth_header(token),
        )

    assert response.status_code == 400
    assert "Unsafe archive path" in response.json()["detail"]
    assert db_session.query(SourceSnapshot).count() == 0


def test_source_snapshot_without_manifest_cannot_be_activated(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    project = create_project(db_session)

    with TestClient(client) as test_client:
        token = login(test_client, admin.username)
        upload_response = test_client.post(
            f"/api/projects/{project.id}/source",
            files={"file": ("source.zip", make_zip({"CSV/TALENT.csv": "1,勇敢"}), "application/zip")},
            headers=auth_header(token),
        )
        scan_revision_id = upload_response.json()["scan_revision_id"]
        activate_response = test_client.post(
            f"/api/projects/{project.id}/source/{scan_revision_id}/activate",
            headers=auth_header(token),
        )

    assert activate_response.status_code == 409


def test_project_member_can_download_specific_source_snapshot(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    project = create_project(db_session)
    db_session.add(ProjectMembership(project_id=project.id, user_id=translator.id, role="translator"))
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        upload_response = test_client.post(
            f"/api/projects/{project.id}/source",
            files={"file": ("source.zip", make_zip({"ERB/MAIN.ERB": "PRINTL ok"}), "application/zip")},
            headers=auth_header(admin_token),
        )
        scan_revision_id = upload_response.json()["scan_revision_id"]
        translator_token = login(test_client, translator.username)
        download_response = test_client.get(
            f"/api/projects/{project.id}/source/download",
            params={"scan_revision_id": scan_revision_id},
            headers=auth_header(translator_token),
        )

    assert download_response.status_code == 200
    assert download_response.headers["content-type"] == "application/zip"
    assert len(download_response.content) > 0


def test_source_snapshot_retention_keeps_recent_uploads(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    monkeypatch.setenv("ERATRANSLATOR_SOURCE_SNAPSHOT_RETENTION_COUNT", "2")
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    project = create_project(db_session)

    with TestClient(client) as test_client:
        token = login(test_client, admin.username)
        for index in range(3):
            response = test_client.post(
                f"/api/projects/{project.id}/source",
                files={"file": ("source.zip", make_zip({f"ERB/{index}.ERB": "PRINTL test"}), "application/zip")},
                headers=auth_header(token),
            )
            assert response.status_code == 201

    assert db_session.query(SourceSnapshot).filter(SourceSnapshot.project_id == project.id).count() == 2


def test_active_source_snapshot_cannot_be_deleted(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    project = create_project(db_session)

    with TestClient(client) as test_client:
        token = login(test_client, admin.username)
        upload_response = test_client.post(
            f"/api/projects/{project.id}/source",
            files={"file": ("source.zip", make_zip({"ERB/MAIN.ERB": "PRINTL test"}), "application/zip")},
            headers=auth_header(token),
        )
        scan_revision_id = upload_response.json()["scan_revision_id"]
        snapshot = db_session.query(SourceSnapshot).filter(SourceSnapshot.scan_revision_id == scan_revision_id).one()
        snapshot.has_scan_manifest = True
        db_session.commit()
        test_client.post(f"/api/projects/{project.id}/source/{scan_revision_id}/activate", headers=auth_header(token))
        delete_response = test_client.delete(f"/api/projects/{project.id}/source/{scan_revision_id}", headers=auth_header(token))

    assert delete_response.status_code == 409
    assert db_session.query(SourceSnapshot).filter(SourceSnapshot.scan_revision_id == scan_revision_id).count() == 1


def test_orphan_archive_cleanup_deletes_unreferenced_zip(client, db_session, monkeypatch, tmp_path):
    archive_root = tmp_path / "archives"
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(archive_root))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    project = create_project(db_session)
    orphan_path = archive_root / project.id / "orphan" / "orphan.zip"
    orphan_path.parent.mkdir(parents=True)
    orphan_path.write_bytes(make_zip({"ERB/ORPHAN.ERB": "PRINTL orphan"}).getvalue())

    with TestClient(client) as test_client:
        token = login(test_client, admin.username)
        response = test_client.post(f"/api/projects/{project.id}/source/orphans/cleanup", headers=auth_header(token))

    assert response.status_code == 200
    assert response.json()["deleted_paths"] == [f"{project.id}/orphan/orphan.zip"]
    assert not orphan_path.exists()


def make_zip(entries: dict[str, str]) -> BytesIO:
    buffer = BytesIO()
    with ZipFile(buffer, "w") as archive:
        for path, content in entries.items():
            archive.writestr(path, content)
    buffer.seek(0)
    return buffer


def create_project(db_session) -> Project:
    project = Project(name="Source Project")
    db_session.add(project)
    db_session.commit()
    return project


def create_user(db_session, username: str, role: str) -> User:
    user = User(
        username=username,
        display_name=username.title(),
        role=role,
        password_hash=hash_password("password123"),
    )
    db_session.add(user)
    db_session.commit()
    return user


def login(test_client: TestClient, username: str) -> str:
    response = test_client.post("/api/auth/login", json={"username": username, "password": "password123"})
    assert response.status_code == 200
    return response.json()["access_token"]


def auth_header(token: str) -> dict[str, str]:
    return {"Authorization": f"Bearer {token}"}
