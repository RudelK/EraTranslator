"""add submission payload hash

Revision ID: 20260605_0006
Revises: 20260605_0005
Create Date: 2026-06-05
"""
from collections.abc import Sequence

from alembic import op
import sqlalchemy as sa

revision: str = "20260605_0006"
down_revision: str | None = "20260605_0005"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.add_column("submissions", sa.Column("payload_hash", sa.String(length=64), nullable=True))


def downgrade() -> None:
    op.drop_column("submissions", "payload_hash")
