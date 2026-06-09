from fastapi.testclient import TestClient

from app.main import create_app


def test_health_check_returns_server_metadata():
    client = TestClient(create_app())

    response = client.get("/api/health")

    assert response.status_code == 200
    assert response.json()["status"] == "ok"
    assert response.json()["app"] == "EraTranslator Team Server"


def test_projects_endpoint_requires_authentication():
    client = TestClient(create_app())

    response = client.get("/api/projects")

    assert response.status_code == 401
