from datetime import UTC, datetime

from fastapi import APIRouter, Depends
from sqlalchemy import select
from sqlalchemy.orm import Session

from app.api.deps import get_current_user
from app.db.session import get_db
from app.models.collaboration import ClientDevice
from app.models.user import User
from app.schemas.collaboration import ClientRegisterRequest, ClientRegisterResponse

router = APIRouter()


@router.post("/register", response_model=ClientRegisterResponse)
def register_client(
    request: ClientRegisterRequest,
    current_user: User = Depends(get_current_user),
    db: Session = Depends(get_db),
) -> ClientRegisterResponse:
    now = datetime.now(UTC)
    client = db.scalar(select(ClientDevice).where(ClientDevice.client_id == request.client_id))
    if client is None:
        client = ClientDevice(
            client_id=request.client_id,
            display_name=request.display_name.strip(),
            registered_by_user_id=current_user.id,
            last_seen_at_utc=now,
        )
        db.add(client)
    else:
        client.display_name = request.display_name.strip()
        client.last_seen_at_utc = now
        client.status = "active"
    db.commit()
    db.refresh(client)
    return ClientRegisterResponse(
        id=client.id,
        client_id=client.client_id,
        display_name=client.display_name,
        status=client.status,
        created_at_utc=client.created_at_utc,
        last_seen_at_utc=client.last_seen_at_utc,
    )
