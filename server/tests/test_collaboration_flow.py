from io import BytesIO
from zipfile import ZipFile

from fastapi.testclient import TestClient

from app.core.config import reset_settings_cache
from app.models.project import Project, ProjectAssignment, ProjectMembership
from app.models.user import User
from app.services.security import hash_password


def test_client_registration_is_idempotent(client, db_session):
    translator = create_user(db_session, "translator", "translator")

    with TestClient(client) as test_client:
        token = login(test_client, translator.username)
        first = test_client.post(
            "/api/clients/register",
            json={"client_id": "client-a", "display_name": "Desk A"},
            headers=auth_header(token),
        )
        second = test_client.post(
            "/api/clients/register",
            json={"client_id": "client-a", "display_name": "Desk A2"},
            headers=auth_header(token),
        )

    assert first.status_code == 200
    assert second.status_code == 200
    assert first.json()["id"] == second.json()["id"]
    assert second.json()["display_name"] == "Desk A2"


def test_manifest_upload_activation_and_sync(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    project = create_project(db_session)
    db_session.add(ProjectMembership(project_id=project.id, user_id=translator.id, role="translator"))
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        upload = upload_source(test_client, project.id, admin_token)
        manifest = test_client.post(
            f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(upload["scan_revision_id"], upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        activate = test_client.post(
            f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/activate",
            headers=auth_header(admin_token),
        )
        translator_token = login(test_client, translator.username)
        sync = test_client.get(f"/api/projects/{project.id}/sync", headers=auth_header(translator_token))

    assert manifest.status_code == 200
    assert manifest.json()["item_count"] == 2
    assert manifest.json()["shared_key_count"] == 1
    assert activate.status_code == 200
    assert sync.status_code == 200
    assert len(sync.json()["work_items"]) == 2
    assert len(sync.json()["shared_keys"]) == 1


def test_submit_applies_changes_and_is_idempotent(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    project = create_project(db_session)
    db_session.add_all(
        [
            ProjectMembership(project_id=project.id, user_id=translator.id, role="translator"),
            ProjectAssignment(project_id=project.id, user_id=translator.id, pattern_kind="prefix", pattern="ERB/"),
        ]
    )
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        upload = upload_source(test_client, project.id, admin_token)
        test_client.post(
            f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(upload["scan_revision_id"], upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        translator_token = login(test_client, translator.username)
        test_client.post(
            "/api/clients/register",
            json={"client_id": "client-submit", "display_name": "Submitter"},
            headers=auth_header(translator_token),
        )
        sync = test_client.get(f"/api/projects/{project.id}/sync", headers=auth_header(translator_token)).json()
        target = next(item for item in sync["work_items"] if item["segment_id"] == "seg-1")
        payload = {
            "submission_id": "submission-1",
            "scan_revision_id": sync["scan_revision_id"],
            "client_id": "client-submit",
            "work_items": [{"id": target["id"], "base_revision": target["item_revision"], "translation": "안녕하세요"}],
            "shared_keys": [],
        }
        submit = test_client.post(f"/api/projects/{project.id}/submit", json=payload, headers=auth_header(translator_token))
        repeat = test_client.post(f"/api/projects/{project.id}/submit", json=payload, headers=auth_header(translator_token))

    assert submit.status_code == 200
    assert submit.json()["applied_count"] == 1
    assert repeat.status_code == 200
    assert repeat.json() == submit.json()


def test_stale_submit_creates_conflict_and_reviewer_resolves(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    reviewer = create_user(db_session, "reviewer", "reviewer")
    translator = create_user(db_session, "translator", "translator")
    project = create_project(db_session)
    db_session.add_all(
        [
            ProjectMembership(project_id=project.id, user_id=translator.id, role="translator"),
            ProjectMembership(project_id=project.id, user_id=reviewer.id, role="reviewer"),
            ProjectAssignment(project_id=project.id, user_id=translator.id, pattern_kind="prefix", pattern="ERB/"),
        ]
    )
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        upload = upload_source(test_client, project.id, admin_token)
        test_client.post(
            f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(upload["scan_revision_id"], upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        translator_token = login(test_client, translator.username)
        test_client.post(
            "/api/clients/register",
            json={"client_id": "client-conflict", "display_name": "Conflicter"},
            headers=auth_header(translator_token),
        )
        target = next(
            item
            for item in test_client.get(f"/api/projects/{project.id}/sync", headers=auth_header(translator_token)).json()["work_items"]
            if item["segment_id"] == "seg-1"
        )
        first_payload = {
            "submission_id": "submission-a",
            "scan_revision_id": upload["scan_revision_id"],
            "client_id": "client-conflict",
            "work_items": [{"id": target["id"], "base_revision": target["item_revision"], "translation": "첫번째"}],
            "shared_keys": [],
        }
        second_payload = {
            "submission_id": "submission-b",
            "scan_revision_id": upload["scan_revision_id"],
            "client_id": "client-conflict",
            "work_items": [{"id": target["id"], "base_revision": target["item_revision"], "translation": "두번째"}],
            "shared_keys": [],
        }
        test_client.post(f"/api/projects/{project.id}/submit", json=first_payload, headers=auth_header(translator_token))
        conflict_submit = test_client.post(f"/api/projects/{project.id}/submit", json=second_payload, headers=auth_header(translator_token))
        conflict_id = conflict_submit.json()["results"][0]["conflict_id"]
        reviewer_token = login(test_client, reviewer.username)
        resolved = test_client.post(
            f"/api/projects/{project.id}/conflicts/{conflict_id}/resolve",
            json={"resolution_kind": "AcceptIncoming"},
            headers=auth_header(reviewer_token),
        )

    assert conflict_submit.json()["conflict_count"] == 1
    assert resolved.status_code == 200
    assert resolved.json()["conflict"]["status"] == "resolved"
    assert resolved.json()["conflict"]["resolved_value"] == "두번째"


def test_shared_key_stale_submit_creates_shared_namespace_conflict(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    project = create_project(db_session)
    db_session.add(ProjectMembership(project_id=project.id, user_id=translator.id, role="translator"))
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        upload = upload_source(test_client, project.id, admin_token)
        test_client.post(
            f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(upload["scan_revision_id"], upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        translator_token = login(test_client, translator.username)
        test_client.post(
            "/api/clients/register",
            json={"client_id": "client-shared", "display_name": "Shared"},
            headers=auth_header(translator_token),
        )
        shared_key = test_client.get(f"/api/projects/{project.id}/sync", headers=auth_header(translator_token)).json()["shared_keys"][0]
        first_payload = {
            "submission_id": "shared-a",
            "scan_revision_id": upload["scan_revision_id"],
            "client_id": "client-shared",
            "work_items": [],
            "shared_keys": [{"id": shared_key["id"], "base_revision": shared_key["shared_revision"], "translation": "용감"}],
        }
        second_payload = {
            "submission_id": "shared-b",
            "scan_revision_id": upload["scan_revision_id"],
            "client_id": "client-shared",
            "work_items": [],
            "shared_keys": [{"id": shared_key["id"], "base_revision": shared_key["shared_revision"], "translation": "대담"}],
        }
        test_client.post(f"/api/projects/{project.id}/submit", json=first_payload, headers=auth_header(translator_token))
        conflict_submit = test_client.post(f"/api/projects/{project.id}/submit", json=second_payload, headers=auth_header(translator_token))
        conflicts = test_client.get(f"/api/projects/{project.id}/conflicts", headers=auth_header(translator_token)).json()["conflicts"]

    assert conflict_submit.json()["conflict_count"] == 1
    assert conflicts[0]["conflict_type"] == "SharedNamespaceConflict"


def test_submit_after_source_revision_change_creates_source_changed_conflict(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    project = create_project(db_session)
    db_session.add_all(
        [
            ProjectMembership(project_id=project.id, user_id=translator.id, role="translator"),
            ProjectAssignment(project_id=project.id, user_id=translator.id, pattern_kind="prefix", pattern="ERB/"),
        ]
    )
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        first_upload = upload_source(test_client, project.id, admin_token)
        test_client.post(
            f"/api/projects/{project.id}/source/{first_upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(first_upload["scan_revision_id"], first_upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(f"/api/projects/{project.id}/source/{first_upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        translator_token = login(test_client, translator.username)
        test_client.post(
            "/api/clients/register",
            json={"client_id": "client-source-change", "display_name": "SourceChange"},
            headers=auth_header(translator_token),
        )
        stale_target = test_client.get(f"/api/projects/{project.id}/sync", headers=auth_header(translator_token)).json()["work_items"][0]

        second_upload = upload_source(test_client, project.id, admin_token)
        test_client.post(
            f"/api/projects/{project.id}/source/{second_upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(second_upload["scan_revision_id"], second_upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(f"/api/projects/{project.id}/source/{second_upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        submit = test_client.post(
            f"/api/projects/{project.id}/submit",
            json={
                "submission_id": "source-changed",
                "scan_revision_id": first_upload["scan_revision_id"],
                "client_id": "client-source-change",
                "work_items": [
                    {"id": stale_target["id"], "base_revision": stale_target["item_revision"], "translation": "늦은 제출"}
                ],
                "shared_keys": [],
            },
            headers=auth_header(translator_token),
        )
        conflicts = test_client.get(f"/api/projects/{project.id}/conflicts", headers=auth_header(translator_token)).json()["conflicts"]

    assert submit.json()["conflict_count"] == 1
    assert conflicts[0]["conflict_type"] == "SourceChangedConflict"


def test_work_items_and_shared_keys_are_isolated_by_project(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    first_project = create_project(db_session)
    second_project = create_project(db_session)

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        first_upload = upload_source(test_client, first_project.id, admin_token)
        second_upload = upload_source(test_client, second_project.id, admin_token)
        test_client.post(
            f"/api/projects/{first_project.id}/source/{first_upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(first_upload["scan_revision_id"], first_upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(
            f"/api/projects/{second_project.id}/source/{second_upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(second_upload["scan_revision_id"], second_upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(f"/api/projects/{first_project.id}/source/{first_upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        test_client.post(f"/api/projects/{second_project.id}/source/{second_upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        first_sync = test_client.get(f"/api/projects/{first_project.id}/sync", headers=auth_header(admin_token)).json()
        second_sync = test_client.get(f"/api/projects/{second_project.id}/sync", headers=auth_header(admin_token)).json()

    assert first_sync["project_id"] != second_sync["project_id"]
    assert first_sync["work_items"][0]["id"] != second_sync["work_items"][0]["id"]
    assert first_sync["shared_keys"][0]["id"] != second_sync["shared_keys"][0]["id"]
    assert first_sync["shared_keys"][0]["namespace"] == second_sync["shared_keys"][0]["namespace"]
    assert first_sync["shared_keys"][0]["key"] == second_sync["shared_keys"][0]["key"]


def test_duplicate_submission_id_with_different_payload_creates_conflict(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    project = create_project(db_session)
    db_session.add_all(
        [
            ProjectMembership(project_id=project.id, user_id=translator.id, role="translator"),
            ProjectAssignment(project_id=project.id, user_id=translator.id, pattern_kind="prefix", pattern="ERB/"),
        ]
    )
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        upload = upload_source(test_client, project.id, admin_token)
        test_client.post(
            f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(upload["scan_revision_id"], upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(f"/api/projects/{project.id}/source/{upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        translator_token = login(test_client, translator.username)
        test_client.post(
            "/api/clients/register",
            json={"client_id": "client-duplicate", "display_name": "Duplicate"},
            headers=auth_header(translator_token),
        )
        target = next(
            item
            for item in test_client.get(f"/api/projects/{project.id}/sync", headers=auth_header(translator_token)).json()["work_items"]
            if item["segment_id"] == "seg-1"
        )
        first_payload = {
            "submission_id": "same-id",
            "scan_revision_id": upload["scan_revision_id"],
            "client_id": "client-duplicate",
            "work_items": [{"id": target["id"], "base_revision": target["item_revision"], "translation": "첫 제출"}],
            "shared_keys": [],
        }
        second_payload = {
            "submission_id": "same-id",
            "scan_revision_id": upload["scan_revision_id"],
            "client_id": "client-duplicate",
            "work_items": [{"id": target["id"], "base_revision": target["item_revision"], "translation": "다른 제출"}],
            "shared_keys": [],
        }
        test_client.post(f"/api/projects/{project.id}/submit", json=first_payload, headers=auth_header(translator_token))
        duplicate = test_client.post(f"/api/projects/{project.id}/submit", json=second_payload, headers=auth_header(translator_token))
        conflicts = test_client.get(f"/api/projects/{project.id}/conflicts", headers=auth_header(translator_token)).json()["conflicts"]

    assert duplicate.json()["status"] == "duplicate_conflict"
    assert duplicate.json()["conflict_count"] == 1
    assert conflicts[0]["conflict_type"] == "DuplicateSubmissionConflict"


def test_cross_project_submit_creates_project_scope_conflict(client, db_session, monkeypatch, tmp_path):
    monkeypatch.setenv("ERATRANSLATOR_ARCHIVE_ROOT", str(tmp_path / "archives"))
    reset_settings_cache()
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    first_project = create_project(db_session)
    second_project = create_project(db_session)
    db_session.add_all(
        [
            ProjectMembership(project_id=first_project.id, user_id=translator.id, role="translator"),
            ProjectAssignment(project_id=first_project.id, user_id=translator.id, pattern_kind="prefix", pattern="ERB/"),
        ]
    )
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        first_upload = upload_source(test_client, first_project.id, admin_token)
        second_upload = upload_source(test_client, second_project.id, admin_token)
        test_client.post(
            f"/api/projects/{first_project.id}/source/{first_upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(first_upload["scan_revision_id"], first_upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(
            f"/api/projects/{second_project.id}/source/{second_upload['scan_revision_id']}/scan-manifest",
            json=manifest_payload(second_upload["scan_revision_id"], second_upload["archive_sha256"]),
            headers=auth_header(admin_token),
        )
        test_client.post(f"/api/projects/{first_project.id}/source/{first_upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        test_client.post(f"/api/projects/{second_project.id}/source/{second_upload['scan_revision_id']}/activate", headers=auth_header(admin_token))
        second_target = test_client.get(f"/api/projects/{second_project.id}/sync", headers=auth_header(admin_token)).json()["work_items"][0]
        translator_token = login(test_client, translator.username)
        test_client.post(
            "/api/clients/register",
            json={"client_id": "client-scope", "display_name": "Scope"},
            headers=auth_header(translator_token),
        )
        submit = test_client.post(
            f"/api/projects/{first_project.id}/submit",
            json={
                "submission_id": "scope-conflict",
                "scan_revision_id": first_upload["scan_revision_id"],
                "client_id": "client-scope",
                "work_items": [
                    {"id": second_target["id"], "base_revision": second_target["item_revision"], "translation": "교차 제출"}
                ],
                "shared_keys": [],
            },
            headers=auth_header(translator_token),
        )
        conflicts = test_client.get(f"/api/projects/{first_project.id}/conflicts", headers=auth_header(translator_token)).json()["conflicts"]

    assert submit.json()["conflict_count"] == 1
    assert conflicts[0]["conflict_type"] == "ProjectScopeConflict"


def upload_source(test_client: TestClient, project_id: str, token: str) -> dict:
    response = test_client.post(
        f"/api/projects/{project_id}/source",
        files={"file": ("source.zip", make_zip({"ERB/MAIN.ERB": "PRINTL hello"}), "application/zip")},
        headers=auth_header(token),
    )
    assert response.status_code == 201
    return response.json()


def manifest_payload(scan_revision_id: str, archive_sha256: str) -> dict:
    return {
        "scan_revision_id": scan_revision_id,
        "source_archive_sha256": archive_sha256,
        "documents": [{"relative_path": "ERB/MAIN.ERB", "file_type": "ERB", "encoding": "UTF-8"}],
        "items": [
            {
                "segment_id": "seg-1",
                "relative_path": "ERB/MAIN.ERB",
                "line_number": 1,
                "file_type": "ERB",
                "segment_type": "print-tail",
                "original_text": "こんにちは",
                "source_key": "ERB/MAIN.ERB:1",
                "is_reference_bearing_key": False,
            },
            {
                "segment_id": "seg-2",
                "relative_path": "CSV/TALENT.csv",
                "line_number": 1,
                "file_type": "CSV",
                "segment_type": "csv-reference-key",
                "original_text": "勇敢",
                "source_key": "TALENT:勇敢",
                "symbol_namespace": "TALENT",
                "original_symbol_key": "勇敢",
                "is_reference_bearing_key": True,
            },
        ],
    }


def make_zip(entries: dict[str, str]) -> BytesIO:
    buffer = BytesIO()
    with ZipFile(buffer, "w") as archive:
        for path, content in entries.items():
            archive.writestr(path, content)
    buffer.seek(0)
    return buffer


def create_project(db_session) -> Project:
    project = Project(name="Collab Project")
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
