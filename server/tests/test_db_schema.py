import pytest

from app.db.schema import normalize_schema_name, quote_identifier


def test_normalize_schema_name_accepts_simple_identifier():
    assert normalize_schema_name(" eratranslator_1 ") == "eratranslator_1"


def test_normalize_schema_name_rejects_unsafe_identifier():
    with pytest.raises(ValueError):
        normalize_schema_name("public; drop schema public")


def test_quote_identifier_wraps_schema_name():
    assert quote_identifier("eratranslator") == '"eratranslator"'
