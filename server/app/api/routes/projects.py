from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import select
from sqlalchemy.orm import Session

from app.api.deps import get_current_user, require_roles
from app.db.session import get_db
from app.models.project import Project, ProjectAssignment, ProjectMembership
from app.models.user import User
from app.schemas.project import (
    ProjectAssignmentListResponse,
    ProjectAssignmentRequest,
    ProjectAssignmentResponse,
    ProjectCreateRequest,
    ProjectListResponse,
    ProjectMembershipListResponse,
    ProjectMembershipRequest,
    ProjectMembershipResponse,
    ProjectResponse,
    ProjectUpdateRequest,
)

router = APIRouter()


@router.get("", response_model=ProjectListResponse)
def list_projects(
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> ProjectListResponse:
    if current_user.role == "admin":
        projects = db.scalars(select(Project).order_by(Project.name)).all()
    else:
        projects = db.scalars(
            select(Project)
            .join(ProjectMembership)
            .where(ProjectMembership.user_id == current_user.id)
            .where(ProjectMembership.status == "active")
            .order_by(Project.name)
        ).all()
    return ProjectListResponse(projects=[to_project_response(project) for project in projects])


@router.post("", response_model=ProjectResponse, status_code=status.HTTP_201_CREATED)
def create_project(
    request: ProjectCreateRequest,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> ProjectResponse:
    project = Project(name=request.name.strip())
    db.add(project)
    db.commit()
    db.refresh(project)
    return to_project_response(project)


@router.patch("/{project_id}", response_model=ProjectResponse)
def update_project(
    project_id: str,
    request: ProjectUpdateRequest,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> ProjectResponse:
    project = get_project_or_404(db, project_id)
    if request.name is not None:
        project.name = request.name.strip()
    if request.status is not None:
        project.status = request.status
    db.commit()
    db.refresh(project)
    return to_project_response(project)


@router.get("/{project_id}/memberships", response_model=ProjectMembershipListResponse)
def list_project_memberships(
    project_id: str,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> ProjectMembershipListResponse:
    get_project_or_404(db, project_id)
    memberships = db.scalars(
        select(ProjectMembership).where(ProjectMembership.project_id == project_id).order_by(ProjectMembership.created_at_utc)
    ).all()
    return ProjectMembershipListResponse(memberships=[to_membership_response(membership) for membership in memberships])


@router.post("/{project_id}/memberships", response_model=ProjectMembershipResponse, status_code=status.HTTP_201_CREATED)
def upsert_project_membership(
    project_id: str,
    request: ProjectMembershipRequest,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> ProjectMembershipResponse:
    get_project_or_404(db, project_id)
    if db.get(User, request.user_id) is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found")

    membership = db.scalar(
        select(ProjectMembership)
        .where(ProjectMembership.project_id == project_id)
        .where(ProjectMembership.user_id == request.user_id)
    )
    if membership is None:
        membership = ProjectMembership(project_id=project_id, user_id=request.user_id, role=request.role)
        db.add(membership)
    else:
        membership.role = request.role
        membership.status = "active"
    db.commit()
    db.refresh(membership)
    return to_membership_response(membership)


@router.get("/{project_id}/assignments", response_model=ProjectAssignmentListResponse)
def list_project_assignments(
    project_id: str,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> ProjectAssignmentListResponse:
    get_project_or_404(db, project_id)
    assignments = db.scalars(
        select(ProjectAssignment).where(ProjectAssignment.project_id == project_id).order_by(ProjectAssignment.created_at_utc)
    ).all()
    return ProjectAssignmentListResponse(assignments=[to_assignment_response(assignment) for assignment in assignments])


@router.post("/{project_id}/assignments", response_model=ProjectAssignmentResponse, status_code=status.HTTP_201_CREATED)
def create_project_assignment(
    project_id: str,
    request: ProjectAssignmentRequest,
    _admin: User = Depends(require_roles("admin")),
    db: Session = Depends(get_db),
) -> ProjectAssignmentResponse:
    get_project_or_404(db, project_id)
    if db.get(User, request.user_id) is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found")

    assignment = ProjectAssignment(
        project_id=project_id,
        user_id=request.user_id,
        pattern_kind=request.pattern_kind,
        pattern=request.pattern.strip(),
    )
    db.add(assignment)
    db.commit()
    db.refresh(assignment)
    return to_assignment_response(assignment)


def get_project_or_404(db: Session, project_id: str) -> Project:
    project = db.get(Project, project_id)
    if project is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Project not found")
    return project


def to_project_response(project: Project) -> ProjectResponse:
    return ProjectResponse(
        id=project.id,
        name=project.name,
        status=project.status,
        current_scan_revision_id=project.current_scan_revision_id,
        created_at_utc=project.created_at_utc,
        updated_at_utc=project.updated_at_utc,
    )


def to_membership_response(membership: ProjectMembership) -> ProjectMembershipResponse:
    return ProjectMembershipResponse(
        id=membership.id,
        project_id=membership.project_id,
        user_id=membership.user_id,
        role=membership.role,
        status=membership.status,
        created_at_utc=membership.created_at_utc,
        updated_at_utc=membership.updated_at_utc,
    )


def to_assignment_response(assignment: ProjectAssignment) -> ProjectAssignmentResponse:
    return ProjectAssignmentResponse(
        id=assignment.id,
        project_id=assignment.project_id,
        user_id=assignment.user_id,
        pattern_kind=assignment.pattern_kind,
        pattern=assignment.pattern,
        status=assignment.status,
        created_at_utc=assignment.created_at_utc,
        updated_at_utc=assignment.updated_at_utc,
    )
