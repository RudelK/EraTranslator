from app.core.config import Settings


def test_settings_default_to_postgresql():
    settings = Settings()

    assert settings.database_url.startswith("postgresql+psycopg://")
    assert settings.database_schema == "eratranslator"
    assert settings.max_archive_bytes > 0
    assert settings.max_archive_files > 0
