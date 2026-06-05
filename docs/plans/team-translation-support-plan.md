# 팀 작업 지원 TODO

## 목표

- [ ] 기존 WPF 클라이언트를 유지한 채 팀 단위 협업을 지원한다.
- [ ] 협업 서버는 별도 소스로 분리한다.
- [ ] 서버는 Windows/Linux 모두에서 실행 가능해야 한다.
- [ ] 서버는 여러 프로젝트를 동시에 관리할 수 있어야 한다.
- [ ] 서버 관리 UI는 웹으로 제공한다.

## 서버 아키텍처

- [ ] 서버 루트를 앱 코드와 분리된 별도 디렉터리로 추가한다.
- [ ] 서버 스택을 `Python 3.12+ + FastAPI`로 고정한다.
- [ ] 서버 내부를 `api / models / schemas / services / repositories / web / templates / static / tests` 구조로 나눈다.
- [ ] 클라이언트-서버 계약은 공용 C# 라이브러리 대신 OpenAPI + 명시 DTO 문서 기준으로 관리한다.
- [ ] 서버는 sync API와 관리 UI를 같은 서비스 안에서 제공한다.
- [ ] 관리 UI는 내부 관리도구 성격에 맞게 서버 렌더링 웹 화면으로 구현한다.
- [ ] v1에서는 P2P/grid 구조를 소스 오브 트루스로 사용하지 않는다.

## 서버 DB

- [ ] 운영 DB 기본값을 `PostgreSQL`로 확정한다.
- [ ] 개발/테스트 전용 대체 DB가 필요하면 별도 검토하되, 기준 구현은 PostgreSQL 기준으로 설계한다.
- [ ] Alembic 기반 마이그레이션 체계를 추가한다.
- [ ] 프로젝트, 작업자, 할당, 일반 항목, 공통 참조키, 제출 이력, 충돌, 충돌 해소 이력을 분리된 테이블로 설계한다.
- [ ] 충돌 payload, 제출 원본, 비교 스냅샷 저장에 필요한 구조화 필드를 포함한다.

## 프로젝트 / 작업자 관리

- [ ] 서버는 `ProjectId` 단위로 여러 프로젝트를 동시에 관리한다.
- [ ] 프로젝트별로 현재 기준 원문 스냅샷 버전 `ScanRevisionId`를 유지한다.
- [ ] 클라이언트는 config에 고정 `ClientId`를 저장하고 서버 식별자로 사용한다.
- [ ] 같은 `ClientId`가 여러 프로젝트에 참가할 수 있게 한다.
- [ ] sync/submit은 항상 단일 `ProjectId` 기준으로만 수행한다.
- [ ] 관리자 UI에 프로젝트 목록/생성/수정/비활성화 화면을 추가한다.
- [ ] 관리자 UI에 작업자 목록, 최근 접속, 프로젝트 참여 현황 화면을 추가한다.

## 할당 범위

- [ ] 작업 할당은 프로젝트별로 관리한다.
- [ ] v1 기본 할당 단위는 `RelativePath` prefix 또는 glob 패턴으로 한다.
- [ ] 할당 밖 항목은 클라이언트에서 읽기 전용으로 표시한다.
- [ ] 할당 밖 수정 제출은 서버에서 `AssignmentConflict` 또는 reject로 처리한다.
- [ ] 관리자 UI에서 프로젝트별 작업자 할당을 편집할 수 있게 한다.

## 공통 키 관리

- [ ] 공통 관리 대상은 특정 CSV 파일 목록이 아니라 `참조키(reference-bearing key)` 전체로 정의한다.
- [ ] 기준 판정은 클라이언트 추출 결과의 `IsReferenceBearingKey`, `SymbolNamespace`, `OriginalSymbolKey`를 사용한다.
- [ ] built-in namespace와 custom CSV/ERD namespace 모두 동일 규칙으로 처리한다.
- [ ] 공통 키 식별자는 `ProjectId + SymbolNamespace + OriginalSymbolKey`로 고정한다.
- [ ] 공통 키는 프로젝트별 서버 마스터 데이터로 유지한다.
- [ ] 클라이언트 sync 시 공통 키 snapshot을 일반 항목보다 먼저 받도록 한다.
- [ ] 클라이언트는 로컬 참조키 항목에만 서버 공통 키 값을 매핑한다.
- [ ] 관리자 UI에 namespace/key 기준 공통 키 검색, 수정, 이력 화면을 추가한다.

## 버전 관리

- [ ] 프로젝트별 현재 기준 원문 버전 `CurrentScanRevisionId`를 유지한다.
- [ ] 일반 항목은 `ProjectId + SegmentId + OriginalText` 기준으로 매칭한다.
- [ ] 일반 항목에는 서버 내부 `ItemId`와 `ItemRevision` 정수 버전을 둔다.
- [ ] 공통 참조키에는 `SharedRevision` 정수 버전을 둔다.
- [ ] 클라이언트 제출 항목마다 `BaseRevision`을 포함한다.
- [ ] `BaseRevision == CurrentRevision`일 때만 자동 반영한다.
- [ ] 제출 번역이 현재 서버 번역과 완전히 같으면 stale 상태여도 `NoOp`로 처리한다.
- [ ] 같은 제출 재전송은 idempotent하게 처리한다.

## 충돌 정책

- [ ] 기본 정책은 자동 덮어쓰기 금지로 유지한다.
- [ ] 충돌 유형을 최소한 아래처럼 나눈다.
- [ ] `StaleRevisionConflict`
- [ ] `SourceChangedConflict`
- [ ] `SharedNamespaceConflict`
- [ ] `AssignmentConflict`
- [ ] `ProjectScopeConflict`
- [ ] `DuplicateSubmissionConflict`
- [ ] 서버는 충돌 발생 시 현재 승인값을 유지하고 충돌 레코드를 생성한다.
- [ ] 충돌 레코드에는 서버값, 제출값, 서버 revision, client base revision, client id를 저장한다.
- [ ] 클라이언트와 관리자 UI 모두 `검수 필요 / 충돌` 상태를 볼 수 있게 한다.
- [ ] 충돌 해소 방식은 `KeepServer`, `AcceptIncoming`, `ManualMerge` 3가지로 고정한다.
- [ ] 어떤 방식으로 해소하더라도 최종 채택값 기준으로 revision을 증가시킨다.

## 클라이언트 연동

- [ ] WPF 앱에 팀 서버 설정 `TeamServerUrl`, `TeamProjectId`, `TeamDisplayName`, `ClientId`를 추가한다.
- [ ] 팀 모드에서 startup 또는 명시 sync 시 서버 snapshot을 내려받도록 한다.
- [ ] 로컬 `state.db`에는 기존 진행 상태와 함께 서버 메타데이터를 저장한다.
- [ ] 최소 저장 메타데이터:
- [ ] `LastSyncedScanRevisionId`
- [ ] 일반 항목별 `ServerItemId`, `ServerRevision`
- [ ] 공통 키별 `ServerSharedRevision`
- [ ] 충돌이 연결된 경우 `ServerConflictId`
- [ ] 기존 동일 원문 교정, symbol rewrite, identifier rewrite는 로컬 기능으로 유지한다.
- [ ] submit 시에는 로컬 최종 상태만 서버 change 집합으로 전송한다.

## 서버 API

- [ ] `POST /api/clients/register`
- [ ] `GET /api/projects`
- [ ] `GET /api/projects/{project_id}/sync`
- [ ] `POST /api/projects/{project_id}/submit`
- [ ] `GET /api/projects/{project_id}/conflicts`
- [ ] `POST /api/projects/{project_id}/conflicts/{conflict_id}/resolve`
- [ ] `GET /api/projects/{project_id}/shared-keys`
- [ ] `POST /api/projects/{project_id}/shared-keys/{entry_id}`

## 관리자 웹 UI

- [ ] 프로젝트 목록/생성/수정/비활성화 화면
- [ ] 프로젝트 상세 대시보드
- [ ] 현재 `ScanRevisionId`, 문서 수, 항목 수, 공통 키 수, 작업자 수, 미해결 충돌 수 표시
- [ ] 작업자 목록/상세 화면
- [ ] 프로젝트별 멤버십/할당 관리 화면
- [ ] 공통 참조키 검색/수정/이력 화면
- [ ] 충돌 목록/필터/비교/해소 화면
- [ ] 제출 이력 조회 화면

## 테스트

- [ ] 같은 `SegmentId + OriginalText`라도 프로젝트가 다르면 서로 분리되는지 확인
- [ ] 같은 `Namespace + Key`라도 프로젝트가 다르면 공통 키가 섞이지 않는지 확인
- [ ] 일반 항목 동시 수정 시 `StaleRevisionConflict`가 발생하는지 확인
- [ ] 공통 키 동시 수정 시 `SharedNamespaceConflict`가 발생하는지 확인
- [ ] `ScanRevisionId` 변경 뒤 제출하면 `SourceChangedConflict`가 발생하는지 확인
- [ ] 할당 밖 제출이 차단되거나 충돌로 집계되는지 확인
- [ ] 동일값 재제출이 `NoOp`로 처리되는지 확인
- [ ] `KeepServer`, `AcceptIncoming`, `ManualMerge` 해소 결과가 모두 올바르게 반영되는지 확인
- [ ] Windows/Linux에서 FastAPI 서버 실행이 가능한지 확인

## 메모

- [ ] v1은 실시간 공동 편집이 아니라 sync/submit 기반 협업으로 본다.
- [ ] 팀 패키지 export/import 계획은 폐기하지 않고 오프라인 보조 기능 후보로 남겨둘 수 있다.
- [ ] 서버 기준 진실 원본은 중앙 서버이며, 로컬 `state.db`는 개인 작업 캐시로 유지한다.
