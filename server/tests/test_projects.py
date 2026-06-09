from fastapi.testclient import TestClient

from app.models.project import Project
from app.models.user import User
from app.services.security import hash_password


def test_admin_can_create_and_update_project(client, db_session):
    admin = create_user(db_session, "admin", "admin")

    with TestClient(client) as test_client:
        token = login(test_client, admin.username)
        create_response = test_client.post(
            "/api/projects",
            json={"name": "Team Project"},
            headers=auth_header(token),
        )
        project_id = create_response.json()["id"]
        update_response = test_client.patch(
            f"/api/projects/{project_id}",
            json={"name": "Renamed Project", "status": "inactive"},
            headers=auth_header(token),
        )

    assert create_response.status_code == 201
    assert create_response.json()["name"] == "Team Project"
    assert update_response.status_code == 200
    assert update_response.json()["name"] == "Renamed Project"
    assert update_response.json()["status"] == "inactive"


def test_translator_cannot_create_project(client, db_session):
    translator = create_user(db_session, "translator", "translator")

    with TestClient(client) as test_client:
        token = login(test_client, translator.username)
        response = test_client.post(
            "/api/projects",
            json={"name": "Forbidden Project"},
            headers=auth_header(token),
        )

    assert response.status_code == 403


def test_non_admin_lists_only_membership_projects(client, db_session):
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    visible = Project(name="Visible")
    hidden = Project(name="Hidden")
    db_session.add_all([visible, hidden])
    db_session.commit()

    with TestClient(client) as test_client:
        admin_token = login(test_client, admin.username)
        member_response = test_client.post(
            f"/api/projects/{visible.id}/memberships",
            json={"user_id": translator.id, "role": "translator"},
            headers=auth_header(admin_token),
        )
        translator_token = login(test_client, translator.username)
        list_response = test_client.get("/api/projects", headers=auth_header(translator_token))

    assert member_response.status_code == 201
    assert [project["name"] for project in list_response.json()["projects"]] == ["Visible"]


def test_admin_can_create_assignment(client, db_session):
    admin = create_user(db_session, "admin", "admin")
    translator = create_user(db_session, "translator", "translator")
    project = Project(name="Assigned Project")
    db_session.add(project)
    db_session.commit()

    with TestClient(client) as test_client:
        token = login(test_client, admin.username)
        create_response = test_client.post(
            f"/api/projects/{project.id}/assignments",
            json={"user_id": translator.id, "pattern_kind": "prefix", "pattern": "ERB/口上"},
            headers=auth_header(token),
        )
        list_response = test_client.get(
            f"/api/projects/{project.id}/assignments",
            headers=auth_header(token),
        )

    assert create_response.status_code == 201
    assert create_response.json()["pattern"] == "ERB/口上"
    assert list_response.json()["assignments"][0]["user_id"] == translator.id


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
