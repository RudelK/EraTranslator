from fastapi.testclient import TestClient
from datetime import UTC, datetime, timedelta

from app.models.project import Project, ProjectAssignment, ProjectMembership
from app.models.user import ApiToken, User
from app.services.security import hash_password


def test_admin_login_page_loads(client):
    with TestClient(client) as test_client:
        response = test_client.get("/admin/login")

    assert response.status_code == 200
    assert "초기 서버 설정" in response.text


def test_admin_login_page_loads_after_setup(client, db_session):
    admin = User(
        username="admin",
        display_name="Admin",
        role="admin",
        password_hash=hash_password("password123"),
    )
    db_session.add(admin)
    db_session.commit()

    with TestClient(client) as test_client:
        response = test_client.get("/admin/login")

    assert response.status_code == 200
    assert "관리 UI 로그인" in response.text


def test_initial_setup_creates_admin_and_saves_env(client, db_session, monkeypatch, tmp_path):
    env_path = tmp_path / ".env"
    monkeypatch.setenv("ERATRANSLATOR_ENV_FILE", str(env_path))

    with TestClient(client) as test_client:
        response = test_client.post(
            "/admin/setup",
            data={
                "username": "admin",
                "display_name": "Admin",
                "password": "password123",
                "password_confirm": "password123",
                "bootstrap_token": "bootstrap-secret",
                "database_url": "postgresql+psycopg://user:pass@localhost:5432/db",
                "database_schema": "team_schema",
                "archive_root": "data/source-archives",
            },
            follow_redirects=False,
        )

    assert response.status_code == 303
    assert response.headers["location"] == "/admin/projects"
    assert db_session.query(User).filter(User.username == "admin").count() == 1
    env_text = env_path.read_text(encoding="utf-8")
    assert 'ERATRANSLATOR_BOOTSTRAP_ADMIN_TOKEN="bootstrap-secret"' in env_text
    assert 'ERATRANSLATOR_DATABASE_SCHEMA="team_schema"' in env_text


def test_admin_login_cookie_allows_project_list(client, db_session):
    admin = User(
        username="admin",
        display_name="Admin",
        role="admin",
        password_hash=hash_password("password123"),
    )
    project = Project(name="Web Project")
    db_session.add_all([admin, project])
    db_session.commit()

    with TestClient(client) as test_client:
        login = test_client.post(
            "/admin/login",
            data={"username": "admin", "password": "password123"},
            follow_redirects=False,
        )
        projects = test_client.get("/admin/projects")

    assert login.status_code == 303
    assert projects.status_code == 200
    assert "Web Project" in projects.text


def test_admin_users_page_lists_workers_and_clients(client, db_session):
    admin = User(
        username="admin",
        display_name="Admin",
        role="admin",
        password_hash=hash_password("password123"),
    )
    db_session.add(admin)
    db_session.commit()

    with TestClient(client) as test_client:
        test_client.post("/admin/login", data={"username": "admin", "password": "password123"}, follow_redirects=False)
        response = test_client.get("/admin/users")

    assert response.status_code == 200
    assert "Workers" in response.text
    assert "admin" in response.text
    assert "Create Worker" in response.text


def test_admin_can_create_worker_from_users_page(client, db_session):
    admin = User(
        username="admin",
        display_name="Admin",
        role="admin",
        password_hash=hash_password("password123"),
    )
    db_session.add(admin)
    db_session.commit()

    with TestClient(client) as test_client:
        test_client.post("/admin/login", data={"username": "admin", "password": "password123"}, follow_redirects=False)
        response = test_client.post(
            "/admin/users",
            data={
                "username": "translator",
                "display_name": "Translator",
                "role": "translator",
                "password": "password123",
                "password_confirm": "password123",
            },
            follow_redirects=False,
        )

    assert response.status_code == 303
    worker = db_session.query(User).filter(User.username == "translator").one()
    assert worker.display_name == "Translator"
    assert worker.role == "translator"
    assert worker.status == "active"


def test_admin_can_reset_password_and_deactivate_worker(client, db_session):
    admin = User(
        username="admin",
        display_name="Admin",
        role="admin",
        password_hash=hash_password("password123"),
    )
    worker = User(
        username="translator",
        display_name="Translator",
        role="translator",
        password_hash=hash_password("oldpassword"),
    )
    db_session.add_all([admin, worker])
    db_session.commit()

    with TestClient(client) as test_client:
        test_client.post("/admin/login", data={"username": "admin", "password": "password123"}, follow_redirects=False)
        reset_response = test_client.post(
            f"/admin/users/{worker.id}/password",
            data={"password": "newpassword", "password_confirm": "newpassword"},
            follow_redirects=False,
        )
        token = ApiToken(user_id=worker.id, token_hash="abc", expires_at_utc=datetime.now(UTC) + timedelta(hours=1))
        db_session.add(token)
        db_session.commit()
        status_response = test_client.post(
            f"/admin/users/{worker.id}/status",
            data={"status_value": "inactive"},
            follow_redirects=False,
        )

    db_session.refresh(worker)
    db_session.refresh(token)
    assert reset_response.status_code == 303
    assert status_response.status_code == 303
    assert worker.status == "inactive"
    assert token.revoked_at_utc is not None


def test_admin_project_detail_can_add_membership_and_assignment(client, db_session):
    admin = User(
        username="admin",
        display_name="Admin",
        role="admin",
        password_hash=hash_password("password123"),
    )
    translator = User(
        username="translator",
        display_name="Translator",
        role="translator",
        password_hash=hash_password("password123"),
    )
    project = Project(name="Managed Project")
    db_session.add_all([admin, translator, project])
    db_session.commit()

    with TestClient(client) as test_client:
        test_client.post("/admin/login", data={"username": "admin", "password": "password123"}, follow_redirects=False)
        membership_response = test_client.post(
            f"/admin/projects/{project.id}/memberships",
            data={"user_id": translator.id, "role": "translator"},
            follow_redirects=False,
        )
        assignment_response = test_client.post(
            f"/admin/projects/{project.id}/assignments",
            data={"user_id": translator.id, "pattern_kind": "prefix", "pattern": "ERB/"},
            follow_redirects=False,
        )

    assert membership_response.status_code == 303
    assert assignment_response.status_code == 303
    assert db_session.query(ProjectMembership).filter(ProjectMembership.project_id == project.id).count() == 1
    assert db_session.query(ProjectAssignment).filter(ProjectAssignment.project_id == project.id).count() == 1
