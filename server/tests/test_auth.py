from datetime import UTC, datetime

from fastapi.testclient import TestClient

from app.core.config import reset_settings_cache
from app.models.user import User
from app.services.security import hash_password


def test_bootstrap_admin_requires_configured_token(client, monkeypatch):
    monkeypatch.setenv("ERATRANSLATOR_BOOTSTRAP_ADMIN_TOKEN", "bootstrap-secret")
    reset_settings_cache()

    with TestClient(client) as test_client:
        response = test_client.post(
            "/api/auth/bootstrap-admin",
            json={"username": "admin", "password": "password123", "display_name": "Admin"},
            headers={"X-Bootstrap-Token": "wrong"},
        )

    assert response.status_code == 404


def test_bootstrap_admin_creates_first_admin(client, monkeypatch):
    monkeypatch.setenv("ERATRANSLATOR_BOOTSTRAP_ADMIN_TOKEN", "bootstrap-secret")
    reset_settings_cache()

    with TestClient(client) as test_client:
        response = test_client.post(
            "/api/auth/bootstrap-admin",
            json={"username": "admin", "password": "password123", "display_name": "Admin"},
            headers={"X-Bootstrap-Token": "bootstrap-secret"},
        )

    assert response.status_code == 201
    assert response.json()["username"] == "admin"
    assert response.json()["role"] == "admin"


def test_login_returns_bearer_token_and_me_uses_it(client, db_session):
    user = User(
        username="translator",
        display_name="Translator",
        role="translator",
        password_hash=hash_password("password123"),
    )
    db_session.add(user)
    db_session.commit()

    with TestClient(client) as test_client:
        login_response = test_client.post(
            "/api/auth/login",
            json={"username": "translator", "password": "password123"},
        )
        token = login_response.json()["access_token"]
        me_response = test_client.get("/api/auth/me", headers={"Authorization": f"Bearer {token}"})

    assert login_response.status_code == 200
    assert login_response.json()["token_type"] == "bearer"
    assert datetime.fromisoformat(login_response.json()["expires_at_utc"]) > datetime.now(UTC)
    assert me_response.status_code == 200
    assert me_response.json()["username"] == "translator"


def test_admin_check_rejects_translator_role(client, db_session):
    user = User(
        username="translator",
        display_name="Translator",
        role="translator",
        password_hash=hash_password("password123"),
    )
    db_session.add(user)
    db_session.commit()

    with TestClient(client) as test_client:
        token = test_client.post(
            "/api/auth/login",
            json={"username": "translator", "password": "password123"},
        ).json()["access_token"]
        response = test_client.get("/api/auth/admin-check", headers={"Authorization": f"Bearer {token}"})

    assert response.status_code == 403
