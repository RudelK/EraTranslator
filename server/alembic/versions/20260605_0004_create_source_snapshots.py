"""create source snapshots

Revision ID: 20260605_0004
Revises: 20260605_0003
Create Date: 2026-06-05
"""
from collections.abc import Sequence

from alembic import op
import sqlalchemy as sa

revision: str = "20260605_0004"
down_revision: str | None = "20260605_0003"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "source_snapshots",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column("project_id", sa.String(length=36), sa.ForeignKey("projects.id", ondelete="CASCADE"), nullable=False),
        sa.Column("scan_revision_id", sa.String(length=64), nullable=False),
        sa.Column("archive_path", sa.String(length=1000), nullable=False),
        sa.Column("archive_sha256", sa.String(length=64), nullable=False),
        sa.Column("archive_size_bytes", sa.BigInteger(), nullable=False),
        sa.Column("archive_file_count", sa.BigInteger(), nullable=False),
        sa.Column("uploaded_by_user_id", sa.String(length=36), sa.ForeignKey("users.id", ondelete="RESTRICT"), nullable=False),
        sa.Column("has_scan_manifest", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("status", sa.String(length=32), nullable=False, server_default="uploaded"),
        sa.Column("created_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("project_id", "scan_revision_id", name="uq_source_snapshots_project_revision"),
    )
    op.create_index("ix_source_snapshots_project_id", "source_snapshots", ["project_id"])
    op.create_index("ix_source_snapshots_uploaded_by_user_id", "source_snapshots", ["uploaded_by_user_id"])


def downgrade() -> None:
    op.drop_index("ix_source_snapshots_uploaded_by_user_id", table_name="source_snapshots")
    op.drop_index("ix_source_snapshots_project_id", table_name="source_snapshots")
    op.drop_table("source_snapshots")
