from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field


ProjectStatus = Literal["active", "inactive"]
ProjectRole = Literal["admin", "reviewer", "translator"]
AssignmentPatternKind = Literal["prefix", "glob"]


class ProjectCreateRequest(BaseModel):
    name: str = Field(min_length=1, max_length=200)


class ProjectUpdateRequest(BaseModel):
    name: str | None = Field(default=None, min_length=1, max_length=200)
    status: ProjectStatus | None = None


class ProjectResponse(BaseModel):
    id: str
    name: str
    status: str
    current_scan_revision_id: str | None
    created_at_utc: datetime
    updated_at_utc: datetime


class ProjectListResponse(BaseModel):
    projects: list[ProjectResponse]


class ProjectMembershipRequest(BaseModel):
    user_id: str = Field(min_length=1, max_length=36)
    role: ProjectRole


class ProjectMembershipResponse(BaseModel):
    id: str
    project_id: str
    user_id: str
    role: str
    status: str
    created_at_utc: datetime
    updated_at_utc: datetime


class ProjectMembershipListResponse(BaseModel):
    memberships: list[ProjectMembershipResponse]


class ProjectAssignmentRequest(BaseModel):
    user_id: str = Field(min_length=1, max_length=36)
    pattern: str = Field(min_length=1, max_length=500)
    pattern_kind: AssignmentPatternKind = "prefix"


class ProjectAssignmentResponse(BaseModel):
    id: str
    project_id: str
    user_id: str
    pattern_kind: str
    pattern: str
    status: str
    created_at_utc: datetime
    updated_at_utc: datetime


class ProjectAssignmentListResponse(BaseModel):
    assignments: list[ProjectAssignmentResponse]
