# EraTranslator Team Server

FastAPI 기반 팀 번역 협업 서버입니다.

## Development

```powershell
cd server
uv sync
uv run pytest
uv run uvicorn app.main:app --reload
```

## Run scripts

Windows PowerShell:

```powershell
cd D:\Work\EraTranslator\server
.\scripts\run-server.ps1 -Reload
```

Linux foreground:

```bash
cd /path/to/EraTranslator/server
chmod +x scripts/run-server.sh
./scripts/run-server.sh foreground
```

Linux daemon:

```bash
cd /path/to/EraTranslator/server
chmod +x scripts/run-server.sh
./scripts/run-server.sh start
./scripts/run-server.sh status
./scripts/run-server.sh stop
```

Linux daemon 로그와 pid는 각각 `server/logs/eratranslator-team-server.log`, `server/run/eratranslator-team-server.pid`에 저장됩니다.

공통 환경변수:

```bash
export ERATRANSLATOR_DATABASE_URL="postgresql+psycopg://user:password@localhost:5432/eratranslator"
export ERATRANSLATOR_HOST="0.0.0.0"
export ERATRANSLATOR_PORT="8000"
export ERATRANSLATOR_SKIP_MIGRATION="1"
```

기본 운영 DB는 PostgreSQL입니다. 개발 초기에는 `ERATRANSLATOR_DATABASE_URL` 환경변수로 DB URL을 지정합니다.

```powershell
$env:ERATRANSLATOR_DATABASE_URL = "postgresql+psycopg://user:password@localhost:5432/eratranslator"
```

기본 스키마는 `eratranslator`이며, 서버 계정에는 해당 DB에서 schema 생성 권한이 필요합니다. 다른 스키마를 쓰려면 `ERATRANSLATOR_DATABASE_SCHEMA`를 지정하세요.

## Database migration

```powershell
$env:ERATRANSLATOR_DATABASE_URL = "postgresql+psycopg://user:password@localhost:5432/eratranslator"
uv run alembic upgrade head
```

## First admin

최초 관리자 계정은 웹 초기 설정 화면 또는 bootstrap API로 생성합니다. 사용자가 하나도 없으면 `/admin/login` 접속 시 `/admin/setup`으로 이동합니다.

웹 초기 설정 화면에서는 bootstrap token, DB URL, DB schema, archive root를 `.env`에 저장하고 최초 관리자 계정을 생성합니다. DB URL 변경은 다음 서버 재시작부터 적용됩니다.

```powershell
$env:ERATRANSLATOR_BOOTSTRAP_ADMIN_TOKEN = "change-me"
uv run uvicorn app.main:app --reload
```

`POST /api/auth/bootstrap-admin` 호출 시 `X-Bootstrap-Token` 헤더에 같은 값을 넣어야 합니다.

## Source snapshot flow

관리자 토큰으로 프로젝트 원본 zip을 업로드합니다.

```powershell
curl.exe -X POST `
  -H "Authorization: Bearer <token>" `
  -F "file=@source.zip" `
  http://localhost:8000/api/projects/<project-id>/source
```

업로드된 snapshot은 `ScanRevisionId`를 발급받고 archive sha256, 크기, 파일 수가 DB에 저장됩니다. scan manifest가 아직 없는 snapshot은 다운로드는 가능하지만 활성화할 수 없습니다.

```powershell
curl.exe -L `
  -H "Authorization: Bearer <token>" `
  "http://localhost:8000/api/projects/<project-id>/source/download?scan_revision_id=<scan-revision-id>" `
  -o source.zip
```

기본 보존 개수는 최근 3개입니다. `ERATRANSLATOR_SOURCE_SNAPSHOT_RETENTION_COUNT`로 조정할 수 있고, 활성 snapshot은 삭제되지 않습니다. orphan archive는 관리자 API `POST /api/projects/<project-id>/source/orphans/cleanup`로 명시 정리합니다.

## Collaboration flow

1. 관리자가 source zip을 업로드합니다.
2. 클라이언트가 source zip을 내려받아 기존 스캐너로 scan manifest를 생성합니다.
3. 관리자가 `POST /api/projects/<project-id>/source/<scan-revision-id>/scan-manifest`로 manifest를 업로드합니다.
4. 관리자가 `POST /api/projects/<project-id>/source/<scan-revision-id>/activate`로 source snapshot을 활성화합니다.
5. 작업자는 `POST /api/clients/register`로 `ClientId`를 등록하고 `GET /api/projects/<project-id>/sync`로 work item과 shared key를 받습니다.
6. 작업자는 `POST /api/projects/<project-id>/submit`으로 변경분을 제출합니다.
7. 충돌은 `GET /api/projects/<project-id>/conflicts`와 `POST /api/projects/<project-id>/conflicts/<conflict-id>/resolve`로 조회/해소합니다.

## Admin UI

서버 실행 후 `/admin/login`에서 로그인하면 프로젝트, source snapshot, manifest, shared key, conflict, submission 상태를 조회하고 기본 관리 작업을 수행할 수 있습니다.
