# 팀 작업 지원 TODO

상세 설계는 [team-translation-server-plan.md](team-translation-server-plan.md)를 기준으로 한다.

## 공통 결정 사항

- [x] 기존 WPF 클라이언트는 로컬 단독 작업과 팀 협업 작업을 모두 지원한다.
- [x] 팀 협업 서버는 앱 코드와 분리된 별도 소스로 구현한다.
- [x] 서버는 `Python 3.12+ + FastAPI`로 구현하고 Windows/Linux 실행을 지원한다.
- [x] 서버 DB 기본값은 `PostgreSQL`로 한다.
- [x] 서버 관리 UI는 웹으로 제공한다.
- [x] 서버는 여러 프로젝트를 동시에 관리한다.
- [x] source snapshot 동기화 단위는 v1에서 전체 프로젝트 작업본으로 한다.
- [x] 서버 work item은 클라이언트가 생성한 scan manifest 업로드로 만든다.
- [x] 공통 관리 대상은 특정 CSV 목록이 아니라 `참조키(reference-bearing key)` 전체로 한다.
- [x] v1은 실시간 공동 편집이 아니라 sync/submit 기반 협업으로 한다.
- [x] P2P/grid 구조는 v1 소스 오브 트루스로 사용하지 않는다.

## 서버 구현 TODO

### 서버 프로젝트 / 인프라

- [x] 서버 루트를 앱 코드와 분리된 별도 디렉터리로 추가한다.
- [x] FastAPI 서버 구조를 `api / models / schemas / services / repositories / web / templates / static / tests`로 나눈다.
- [x] SQLAlchemy 2.x, Alembic, Pydantic v2 기반을 구성한다.
- [x] PostgreSQL 연결 설정과 마이그레이션 실행 흐름을 추가한다.
- [x] OpenAPI 문서를 클라이언트-서버 계약 기준으로 관리한다.
- [x] 서버 설정 파일 또는 환경변수로 DB 연결, archive 저장 위치, 업로드 제한값을 관리한다.

### 서버 인증 / 권한

- [x] v1 권한을 `admin`, `reviewer`, `translator` 3단계로 정의한다.
- [x] API 인증 토큰을 추가한다.
- [x] `ClientId`는 장치/작업자 식별용으로만 쓰고 인증 토큰을 대체하지 않게 한다.
- [x] 관리 UI 로그인 화면을 추가한다.
- [x] `admin`은 프로젝트, source snapshot, scan manifest, 멤버십, 할당, 충돌 해소를 관리할 수 있게 한다.
- [x] `reviewer`는 공통 참조키 승인/수정과 허용된 충돌 해소를 수행할 수 있게 한다.
- [x] `translator`는 할당 범위 내 일반 항목과 공통 참조키 제안을 제출할 수 있게 한다.
- [x] 관리 UI는 로그인 후 역할에 따라 메뉴와 작업 버튼을 제한한다.

### 서버 데이터 모델

- [x] 프로젝트, 작업자, 멤버십, 할당 테이블을 설계한다.
- [x] source snapshot 메타데이터 테이블을 설계한다.
- [x] 일반 work item과 공통 참조키 테이블을 설계한다.
- [x] 제출 이력과 제출 변경 항목 테이블을 설계한다.
- [x] 충돌과 충돌 해소 이력 테이블을 설계한다.
- [x] 충돌 payload, 제출 원본, 비교 스냅샷 저장에 필요한 구조화 필드를 포함한다.
- [x] 모든 핵심 데이터는 `ProjectId` 기준으로 격리한다.

### 서버 프로젝트 / 작업자 관리

- [x] 프로젝트 생성/수정/비활성화 API를 추가한다.
- [x] 프로젝트별 현재 `CurrentScanRevisionId`를 유지한다.
- [x] `ClientId` 등록 API를 추가한다.
- [x] 같은 `ClientId`가 여러 프로젝트에 참가할 수 있게 한다.
- [x] 프로젝트별 멤버십과 역할을 관리한다.
- [x] 프로젝트별 작업 할당을 `RelativePath` prefix 또는 glob 패턴으로 관리한다.
- [x] 할당 밖 제출은 `AssignmentConflict` 또는 reject로 처리한다.

### 서버 Source Snapshot

- [x] source snapshot을 `ProjectId + ScanRevisionId` 단위로 저장한다.
- [x] snapshot archive 형식은 zip으로 한다.
- [x] archive 메타데이터에 경로, sha256, 크기, 파일 수, 업로더, 업로드 시각, 활성 여부를 저장한다.
- [x] source archive에는 `.era-translator`, `.era-translator-backup`, output 폴더, 로그/캐시 폴더를 포함하지 않는다.
- [x] 업로드 시 archive 크기, 파일 수, 허용 경로, 제외 경로를 검증한다.
- [x] active source snapshot archive는 삭제할 수 없게 한다.
- [x] snapshot 보존 기본값은 프로젝트별 최근 3개로 한다.
- [x] orphan archive 정리 기능은 관리자 승인 작업으로 둔다.

### 서버 Scan Manifest / Work Item 생성

- [x] scan manifest 업로드 API를 추가한다.
- [x] manifest가 없는 source snapshot은 다운로드는 가능하지만 팀 sync/submit 대상으로 활성화할 수 없게 한다.
- [x] manifest 업로드 시 source archive 해시와 `ScanRevisionId`를 검증한다.
- [x] manifest에서 일반 work item을 생성/갱신한다.
- [x] manifest에서 `IsReferenceBearingKey` 항목을 공통 참조키로 생성/갱신한다.
- [x] source snapshot 변경 시 이전 `ScanRevisionId`의 번역 상태를 새 manifest에 carryover한다.
- [x] carryover 우선순위는 `SegmentId + OriginalText`, strong key, occurrence, same-original 순서로 둔다.
- [x] carryover된 항목은 기본적으로 `검수 필요` 상태로 표시한다.
- [x] scan manifest validation 결과를 조회하는 API를 추가한다.

### 서버 버전 / 제출 / 충돌

- [x] 일반 항목은 `ItemRevision`, 공통 참조키는 `SharedRevision` 정수 버전을 사용한다.
- [x] 제출 API는 `SubmissionId`, `ProjectId`, `ScanRevisionId`, 항목별 `BaseRevision`을 필수로 받는다.
- [x] `BaseRevision == CurrentRevision`이고 `ScanRevisionId`가 현재 source snapshot과 같을 때만 자동 반영한다.
- [x] 제출값이 현재 서버값과 같으면 stale 상태여도 `NoOp`로 처리한다.
- [x] 같은 `SubmissionId` 재전송 시 기존 처리 결과를 반환한다.
- [x] 충돌 유형을 `StaleRevisionConflict`, `SourceChangedConflict`, `SharedNamespaceConflict`, `AssignmentConflict`, `ProjectScopeConflict`, `DuplicateSubmissionConflict`로 나눈다.
- [x] v1 submit 경로에서 `StaleRevisionConflict`, `SourceChangedConflict`, `SharedNamespaceConflict`, `AssignmentConflict`, `ProjectScopeConflict`, `DuplicateSubmissionConflict`를 생성한다.
- [x] 충돌 발생 시 서버 현재 승인값을 유지하고 충돌 레코드를 생성한다.
- [x] 충돌 해소 방식은 `KeepServer`, `AcceptIncoming`, `ManualMerge` 3가지로 구현한다.
- [x] 충돌 해소 후 최종 채택값 기준으로 revision을 증가시킨다.

### 서버 API

- [x] `POST /api/auth/bootstrap-admin`
- [x] `POST /api/auth/login`
- [x] `GET /api/auth/me`
- [x] `GET /api/projects`
- [x] `POST /api/projects`
- [x] `PATCH /api/projects/{project_id}`
- [x] `GET /api/projects/{project_id}/memberships`
- [x] `POST /api/projects/{project_id}/memberships`
- [x] `GET /api/projects/{project_id}/assignments`
- [x] `POST /api/projects/{project_id}/assignments`
- [x] `GET /api/projects/{project_id}/source`
- [x] `GET /api/projects/{project_id}/source/download`
- [x] `POST /api/projects/{project_id}/source`
- [x] `POST /api/projects/{project_id}/source/{scan_revision_id}/activate`
- [x] `POST /api/clients/register`
- [x] `GET /api/projects/{project_id}/sync`
- [x] `POST /api/projects/{project_id}/source/{scan_revision_id}/scan-manifest`
- [x] `GET /api/projects/{project_id}/source/{scan_revision_id}/scan-manifest/validation`
- [x] `POST /api/projects/{project_id}/submit`
- [x] `GET /api/projects/{project_id}/conflicts`
- [x] `POST /api/projects/{project_id}/conflicts/{conflict_id}/resolve`
- [x] `GET /api/projects/{project_id}/shared-keys`
- [x] `POST /api/projects/{project_id}/shared-keys/{entry_id}`

### 서버 관리 UI

- [x] 프로젝트 목록/생성/수정/비활성화 화면을 추가한다.
- [x] 프로젝트 상세 대시보드에 현재 `ScanRevisionId`, 문서 수, 항목 수, 공통 키 수, 작업자 수, 미해결 충돌 수를 표시한다.
- [x] 작업자 목록/상세 화면을 추가한다.
- [x] 프로젝트별 멤버십/할당 관리 화면을 추가한다.
- [x] source snapshot 업로드/활성화/다운로드/이력 화면을 추가한다.
- [x] scan manifest 업로드/검증/활성화 화면을 추가한다.
- [x] 공통 참조키 검색/수정/이력 화면을 추가한다.
- [x] 충돌 목록/필터/비교/해소 화면을 추가한다.
- [x] 제출 이력 조회 화면을 추가한다.

### 서버 테스트

- [ ] Windows/Linux에서 FastAPI 서버 실행과 source archive 저장/다운로드가 가능한지 확인한다.
- [x] 같은 `SegmentId + OriginalText`라도 프로젝트가 다르면 work item이 분리되는지 확인한다.
- [x] 같은 `Namespace + Key`라도 프로젝트가 다르면 공통 참조키가 섞이지 않는지 확인한다.
- [x] scan manifest가 없는 source snapshot은 sync 대상으로 활성화되지 않는지 확인한다.
- [x] scan manifest 업로드 후 work item과 shared key가 생성되는지 확인한다.
- [x] source archive에 zip slip 경로가 포함되면 업로드 또는 압축 해제가 차단되는지 확인한다.
- [x] 일반 항목 동시 수정 시 `StaleRevisionConflict`가 발생하는지 확인한다.
- [x] 공통 참조키 동시 수정 시 `SharedNamespaceConflict`가 발생하는지 확인한다.
- [x] `ScanRevisionId` 변경 뒤 제출하면 `SourceChangedConflict`가 발생하는지 확인한다.
- [x] 할당 밖 제출이 차단되거나 충돌로 집계되는지 확인한다.
- [x] 동일값 재제출이 `NoOp`로 처리되는지 확인한다.
- [x] 같은 `SubmissionId` 재전송이 중복 반영 없이 기존 결과를 반환하는지 확인한다.
- [x] `KeepServer`, `AcceptIncoming`, `ManualMerge` 해소 결과가 올바르게 반영되는지 확인한다.

## 클라이언트 구현 TODO

### 클라이언트 모드 / 설정

- [x] 프로젝트 단위 `ProjectMode = Local | Team` 개념을 추가한다.
- [x] 로컬 모드는 현재 게임 폴더/출력 폴더 기반 동작을 그대로 유지한다.
- [x] 팀 모드는 서버 URL과 프로젝트를 선택해 workspace를 구성하는 별도 흐름으로 둔다.
- [x] `TeamServerUrl`, `TeamProjectId`, `TeamDisplayName`, `ClientId`, `TeamWorkspaceRoot` 설정을 추가한다.
- [x] `ClientId`는 최초 실행 시 생성하고 이후 config에서 유지한다.
- [x] 프로젝트 시작 UI에 `로컬 프로젝트 열기`와 `팀 프로젝트 열기` 흐름을 모두 제공한다.
- [x] 로컬 프로젝트와 팀 프로젝트의 최근 사용 문맥을 분리한다.

### 클라이언트 Project Context / 상태 격리

- [x] `ProjectContext`, `LocalProjectContext`, `TeamProjectContext` 모델을 추가한다.
- [x] 로컬 모드와 팀 모드의 `state.db`, 경로, 캐시를 서로 섞이지 않게 분리한다.
- [x] 팀 모드 state에 `LastSyncedScanRevisionId`, `LocalSourceScanRevisionId`, `OfflineSubmissionQueue`, `TeamProjectDictionaryPath`를 저장한다.
- [x] 일반 항목별 `ServerItemId`, `ServerRevision`을 저장한다.
- [x] 공통 키별 `ServerSharedRevision`을 저장한다.
- [x] 충돌이 연결된 항목에는 `ServerConflictId`를 저장한다.
- [x] 로컬 모드 프로젝트 사전은 현재 게임 폴더 기준으로 유지한다.
- [x] 팀 모드 프로젝트 사전은 `TeamWorkspaceRoot/<ProjectId>/.era-translator/dictionaries/` 아래에 저장한다.

### 클라이언트 Team Workspace / Source Sync

- [x] 팀 workspace 기본 구조를 `source/`, `output/`, `.era-translator/`로 만든다.
- [x] 팀 모드에서 `GameDirectory`는 `source/`, `OutputDirectory`는 `output/`를 가리키게 한다.
- [x] 프로젝트 접속 시 서버 `CurrentScanRevisionId`와 로컬 source revision을 비교한다.
- [x] 로컬 source가 없거나 revision이 다르면 source snapshot을 다운로드한다.
- [x] source archive 다운로드 후 sha256을 검증한다.
- [x] 압축 해제 시 zip slip 방지와 경로 정규화를 수행한다.
- [x] 압축 해제 시 최대 크기와 최대 파일 수 검사를 수행한다.
- [x] source revision이 같으면 기존 작업본을 재사용한다.
- [x] source revision이 바뀌면 재다운로드, 재추출, 상태 carryover 흐름을 제공한다.

### 클라이언트 Scan Manifest

- [x] 기존 `FileScanner` 결과로 scan manifest를 생성하는 서비스를 추가한다.
- [x] manifest에 `scan_revision_id`, `documents`, `items`, `identifier_occurrences`, `symbol_references`를 포함한다.
- [x] 각 item에 `segment_id`, `relative_path`, `line_number`, `file_type`, `segment_type`, `original_text`, `source_key`, `symbol_namespace`, `original_symbol_key`, `is_reference_bearing_key`를 포함한다.
- [x] scan manifest 업로드와 validation 조회용 팀 서버 client 메서드를 추가한다.
- [x] 권한 있는 팀 사용자에게 scan manifest 업로드 흐름을 제공한다.
- [x] manifest 업로드 결과와 validation 결과를 UI에 표시한다.

### 클라이언트 Sync / Shared Key

- [x] 팀 모드 startup 또는 명시 sync 시 서버 snapshot을 내려받는다.
- [x] sync 응답의 `ScanRevisionId`가 로컬 source와 다르면 source 재동기화 필요 상태로 표시한다.
- [x] 서버 shared key sync가 완료되면 로컬 참조키 항목에 즉시 반영한다.
- [x] 저장 시 기존 symbol rewrite가 서버 shared key 값을 기준으로 동작하게 한다.
- [ ] 팀 모드에서 할당 밖 항목은 읽기 전용으로 표시한다.
- [x] 팀 모드에서 충돌 항목은 `검수 필요 / 충돌` 상태로 표시한다.
- [ ] 서버 sync 응답에 assignment 범위 또는 item별 editable flag를 추가해 할당 밖 읽기 전용 표시를 정확히 구현한다.

### 클라이언트 Submit / Offline Queue

- [x] 팀 모드 dirty change 추적을 추가한다.
- [x] submit payload에 `SubmissionId`, `ProjectId`, `ScanRevisionId`, 대상 revision, 변경 payload를 포함한다.
- [x] 서버 연결 실패 시 dirty change를 local offline queue에 보관한다.
- [x] source revision이 바뀐 상태에서는 queue를 자동 제출하지 않고 재동기화 필요 상태로 표시한다.
- [x] 같은 `SubmissionId` 재전송 시 서버 결과를 기존 제출 결과로 반영한다.
- [x] submit 결과의 `Applied`, `NoOp`, `Conflict`, `SourceMismatch`, `OutOfScope` 집계를 UI에 표시한다.

### 클라이언트 UI

- [x] 팀 프로젝트 열기 화면을 추가한다.
- [x] 팀 서버 URL, 표시 이름, workspace root 설정 화면을 추가한다.
- [x] 팀 프로젝트 목록 선택 UI를 추가한다.
- [x] source snapshot 다운로드/압축 해제 진행 표시를 추가한다.
- [x] 팀 sync/submit 버튼 또는 메뉴를 추가한다.
- [x] 팀 모드 상태 요약에 서버, 프로젝트, source revision, 미제출 변경 수, 충돌 수를 표시한다.
- [x] 로컬 모드에서는 팀 전용 UI를 숨기거나 비활성화한다.

### 클라이언트 테스트

- [x] 로컬 모드에서 서버 설정 없이 기존 추출/번역/저장이 그대로 동작하는지 확인한다.
- [x] 팀 모드에서 로컬 원본 파일 없이 source snapshot 다운로드 후 바로 추출 가능한지 확인한다.
- [x] 로컬 모드와 팀 모드의 `state.db`, 경로, 최근 상태가 서로 섞이지 않는지 확인한다.
- [x] 팀 모드 프로젝트 사전이 로컬 모드 프로젝트 사전과 분리되는지 확인한다.
- [x] source revision이 같으면 재다운로드 없이 기존 작업본을 재사용하는지 확인한다.
- [x] source revision이 바뀌면 재다운로드와 재추출이 요구되는지 확인한다.
- [x] scan manifest 생성 결과가 서버 DTO 요구 필드를 모두 포함하는지 확인한다.
- [x] 서버 shared key sync 후 로컬 참조키 항목에 즉시 반영되는지 확인한다.
- [ ] 할당 밖 항목이 읽기 전용으로 표시되는지 확인한다.
- [x] 네트워크 실패 후 같은 `SubmissionId`를 재전송하면 중복 반영 없이 기존 결과가 반영되는지 확인한다.
- [x] source revision 변경 후 offline queue가 자동 제출되지 않는지 확인한다.
