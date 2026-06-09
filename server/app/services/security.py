import base64
import hashlib
import hmac
import secrets
from datetime import UTC, datetime, timedelta

from app.core.config import get_settings


_PASSWORD_SCHEME = "pbkdf2_sha256"


def hash_password(password: str) -> str:
    settings = get_settings()
    salt = secrets.token_bytes(16)
    digest = hashlib.pbkdf2_hmac(
        "sha256",
        password.encode("utf-8"),
        salt,
        settings.password_hash_iterations,
    )
    return "$".join(
        [
            _PASSWORD_SCHEME,
            str(settings.password_hash_iterations),
            base64.b64encode(salt).decode("ascii"),
            base64.b64encode(digest).decode("ascii"),
        ]
    )


def verify_password(password: str, stored_hash: str) -> bool:
    try:
        scheme, iterations_text, salt_text, digest_text = stored_hash.split("$", 3)
        if scheme != _PASSWORD_SCHEME:
            return False
        iterations = int(iterations_text)
        salt = base64.b64decode(salt_text.encode("ascii"))
        expected = base64.b64decode(digest_text.encode("ascii"))
    except (ValueError, TypeError):
        return False

    actual = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, iterations)
    return hmac.compare_digest(actual, expected)


def generate_access_token() -> str:
    return secrets.token_urlsafe(48)


def hash_access_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def access_token_expires_at() -> datetime:
    settings = get_settings()
    return datetime.now(UTC) + timedelta(hours=settings.access_token_expire_hours)

