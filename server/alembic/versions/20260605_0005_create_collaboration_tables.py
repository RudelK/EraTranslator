"""create collaboration tables

Revision ID: 20260605_0005
Revises: 20260605_0004
Create Date: 2026-06-05
"""
from collections.abc import Sequence

from alembic import op
import sqlalchemy as sa

revision: str = "20260605_0005"
down_revision: str | None = "20260605_0004"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "client_devices",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column("client_id", sa.String(length=100), nullable=False),
        sa.Column("display_name", sa.String(length=200), nullable=False),
        sa.Column("registered_by_user_id", sa.String(length=36), sa.ForeignKey("users.id", ondelete="RESTRICT"), nullable=False),
        sa.Column("status", sa.String(length=32), nullable=False, server_default="active"),
        sa.Column("created_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("last_seen_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )
    op.create_index("ix_client_devices_client_id", "client_devices", ["client_id"], unique=True)
    op.create_index("ix_client_devices_registered_by_user_id", "client_devices", ["registered_by_user_id"])

    op.create_table(
        "scan_manifests",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column("project_id", sa.String(length=36), sa.ForeignKey("projects.id", ondelete="CASCADE"), nullable=False),
        sa.Column("source_snapshot_id", sa.String(length=36), sa.ForeignKey("source_snapshots.id", ondelete="CASCADE"), nullable=False),
        sa.Column("scan_revision_id", sa.String(length=64), nullable=False),
        sa.Column("source_archive_sha256", sa.String(length=64), nullable=False),
        sa.Column("document_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("item_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("shared_key_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("validation_status", sa.String(length=32), nullable=False, server_default="valid"),
        sa.Column("validation_messages", sa.JSON(), nullable=False, server_default="[]"),
        sa.Column("uploaded_by_user_id", sa.String(length=36), sa.ForeignKey("users.id", ondelete="RESTRICT"), nullable=False),
        sa.Column("created_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("project_id", "scan_revision_id", name="uq_scan_manifests_project_revision"),
    )
    op.create_index("ix_scan_manifests_project_id", "scan_manifests", ["project_id"])
    op.create_index("ix_scan_manifests_source_snapshot_id", "scan_manifests", ["source_snapshot_id"])
    op.create_index("ix_scan_manifests_uploaded_by_user_id", "scan_manifests", ["uploaded_by_user_id"])

    op.create_table(
        "work_items",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column("project_id", sa.String(length=36), sa.ForeignKey("projects.id", ondelete="CASCADE"), nullable=False),
        sa.Column("scan_revision_id", sa.String(length=64), nullable=False),
        sa.Column("segment_id", sa.String(length=200), nullable=False),
        sa.Column("relative_path", sa.String(length=1000), nullable=False),
        sa.Column("line_number", sa.Integer(), nullable=True),
        sa.Column("file_type", sa.String(length=32), nullable=False),
        sa.Column("segment_type", sa.String(length=100), nullable=False),
        sa.Column("original_text", sa.Text(), nullable=False),
        sa.Column("source_key", sa.String(length=500), nullable=True),
        sa.Column("symbol_namespace", sa.String(length=200), nullable=True),
        sa.Column("original_symbol_key", sa.String(length=500), nullable=True),
        sa.Column("is_reference_bearing_key", sa.Boolean(), nullable=False, server_default=sa.false()),
        sa.Column("translation", sa.Text(), nullable=True),
        sa.Column("status", sa.String(length=32), nullable=False, server_default="pending"),
        sa.Column("item_revision", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("carryover_state", sa.String(length=32), nullable=False, server_default="new"),
        sa.Column("created_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("updated_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("project_id", "scan_revision_id", "segment_id", name="uq_work_items_project_scan_segment"),
    )
    op.create_index("ix_work_items_project_id", "work_items", ["project_id"])
    op.create_index("ix_work_items_scan_revision_id", "work_items", ["scan_revision_id"])

    op.create_table(
        "shared_namespace_entries",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column("project_id", sa.String(length=36), sa.ForeignKey("projects.id", ondelete="CASCADE"), nullable=False),
        sa.Column("namespace", sa.String(length=200), nullable=False),
        sa.Column("key", sa.String(length=500), nullable=False),
        sa.Column("original_text", sa.Text(), nullable=False),
        sa.Column("translation", sa.Text(), nullable=True),
        sa.Column("status", sa.String(length=32), nullable=False, server_default="pending"),
        sa.Column("shared_revision", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("source_work_item_id", sa.String(length=36), sa.ForeignKey("work_items.id", ondelete="SET NULL"), nullable=True),
        sa.Column("created_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("updated_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("project_id", "namespace", "key", name="uq_shared_namespace_entries_project_key"),
    )
    op.create_index("ix_shared_namespace_entries_project_id", "shared_namespace_entries", ["project_id"])

    op.create_table(
        "submissions",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column("submission_id", sa.String(length=100), nullable=False),
        sa.Column("project_id", sa.String(length=36), sa.ForeignKey("projects.id", ondelete="CASCADE"), nullable=False),
        sa.Column("scan_revision_id", sa.String(length=64), nullable=False),
        sa.Column("client_id", sa.String(length=100), nullable=False),
        sa.Column("submitted_by_user_id", sa.String(length=36), sa.ForeignKey("users.id", ondelete="RESTRICT"), nullable=False),
        sa.Column("status", sa.String(length=32), nullable=False, server_default="processed"),
        sa.Column("applied_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("noop_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("conflict_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("rejected_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("created_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("project_id", "submission_id", name="uq_submissions_project_submission"),
    )
    op.create_index("ix_submissions_project_id", "submissions", ["project_id"])
    op.create_index("ix_submissions_submitted_by_user_id", "submissions", ["submitted_by_user_id"])

    op.create_table(
        "submission_changes",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column("submission_id", sa.String(length=36), sa.ForeignKey("submissions.id", ondelete="CASCADE"), nullable=False),
        sa.Column("target_kind", sa.String(length=32), nullable=False),
        sa.Column("target_id", sa.String(length=36), nullable=False),
        sa.Column("base_revision", sa.Integer(), nullable=False),
        sa.Column("incoming_translation", sa.Text(), nullable=True),
        sa.Column("result", sa.String(length=32), nullable=False),
        sa.Column("conflict_id", sa.String(length=36), nullable=True),
    )
    op.create_index("ix_submission_changes_submission_id", "submission_changes", ["submission_id"])

    op.create_table(
        "conflicts",
        sa.Column("id", sa.String(length=36), primary_key=True),
        sa.Column("project_id", sa.String(length=36), sa.ForeignKey("projects.id", ondelete="CASCADE"), nullable=False),
        sa.Column("conflict_type", sa.String(length=64), nullable=False),
        sa.Column("target_kind", sa.String(length=32), nullable=False),
        sa.Column("target_id", sa.String(length=36), nullable=False),
        sa.Column("scan_revision_id", sa.String(length=64), nullable=True),
        sa.Column("server_revision", sa.Integer(), nullable=False),
        sa.Column("client_base_revision", sa.Integer(), nullable=False),
        sa.Column("server_value", sa.Text(), nullable=True),
        sa.Column("incoming_value", sa.Text(), nullable=True),
        sa.Column("status", sa.String(length=32), nullable=False, server_default="open"),
        sa.Column("resolved_by_user_id", sa.String(length=36), sa.ForeignKey("users.id", ondelete="SET NULL"), nullable=True),
        sa.Column("resolution_kind", sa.String(length=32), nullable=True),
        sa.Column("resolved_value", sa.Text(), nullable=True),
        sa.Column("created_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("resolved_at_utc", sa.DateTime(timezone=True), nullable=True),
    )
    op.create_index("ix_conflicts_project_id", "conflicts", ["project_id"])


def downgrade() -> None:
    op.drop_index("ix_conflicts_project_id", table_name="conflicts")
    op.drop_table("conflicts")
    op.drop_index("ix_submission_changes_submission_id", table_name="submission_changes")
    op.drop_table("submission_changes")
    op.drop_index("ix_submissions_submitted_by_user_id", table_name="submissions")
    op.drop_index("ix_submissions_project_id", table_name="submissions")
    op.drop_table("submissions")
    op.drop_index("ix_shared_namespace_entries_project_id", table_name="shared_namespace_entries")
    op.drop_table("shared_namespace_entries")
    op.drop_index("ix_work_items_scan_revision_id", table_name="work_items")
    op.drop_index("ix_work_items_project_id", table_name="work_items")
    op.drop_table("work_items")
    op.drop_index("ix_scan_manifests_uploaded_by_user_id", table_name="scan_manifests")
    op.drop_index("ix_scan_manifests_source_snapshot_id", table_name="scan_manifests")
    op.drop_index("ix_scan_manifests_project_id", table_name="scan_manifests")
    op.drop_table("scan_manifests")
    op.drop_index("ix_client_devices_registered_by_user_id", table_name="client_devices")
    op.drop_index("ix_client_devices_client_id", table_name="client_devices")
    op.drop_table("client_devices")
