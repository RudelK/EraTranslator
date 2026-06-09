# 팀 작업 서버 상세 계획

## Summary

- 기존 WPF 클라이언트는 유지하고, 협업 서버는 별도 소스로 분리한다.
- 클라이언트는 `로컬 단독 작업 모드`와 `팀 협업 모드`를 모두 지원한다.
- 팀 모드에서는 로컬 원본 파일이 없어도 서버에서 프로젝트 작업본을 받아 바로 추출/번역/저장을 시작할 수 있어야 한다.
- 서버는 `Python + FastAPI`로 구현하고, 관리 UI는 웹으로 제공한다.
- 서버는 여러 프로젝트를 동시에 관리하며, 프로젝트별로 작업자, 할당, 원본 스냅샷, 공통 참조키, 버전, 충돌을 분리한다.
- 공통 키 범위는 특정 CSV 파일 목록이 아니라 `참조키(reference-bearing key)` 전체로 정의한다.
- 서버 DB 기본값은 `PostgreSQL`로 하고, 실제 프로젝트 작업본 archive는 파일 스토리지에 둔다.
- 서버의 work item 생성 기준은 서버 자체 파서가 아니라 **클라이언트가 생성한 scan manifest 업로드**로 한다.
- v1 권한은 최소 `admin`, `reviewer`, `translator` 3단계로 둔다.

## Key Changes

### 클라이언트 모드

- `ProjectMode = Local | Team` 개념을 추가한다.
- 로컬 모드는 현재 동작을 유지한다.
  - 사용자가 게임 폴더와 출력 폴더를 직접 지정
  - 로컬 파일을 스캔
  - 로컬 `.era-translator/state.db`에 진행 상태 저장
  - 서버 연결 없이 독립 작업 가능
- 팀 모드는 선택형 워크플로로 추가한다.
  - 서버 URL, 프로젝트, 작업 공간 루트를 선택
  - 서버에서 현재 source snapshot을 받아 로컬 workspace 생성
  - 팀 sync/submit, 공통 참조키, 충돌, 할당 범위를 사용
- 로컬 프로젝트와 팀 프로젝트는 같은 앱 안에서 공존하지만, 상태/경로/캐시는 서로 섞이지 않게 분리한다.

### 원본 파일 동기화

- 서버는 프로젝트별 현재 작업 기준이 되는 `source snapshot`을 보관한다.
- snapshot 식별자는 `ProjectId + ScanRevisionId`로 둔다.
- snapshot 저장 형식은 기본적으로 zip archive 하나로 한다.
- 팀 모드 클라이언트는 startup 또는 프로젝트 접속 시 서버의 현재 `ScanRevisionId`와 로컬 source revision을 비교한다.
- 로컬 source가 없거나 revision이 다르면 현재 snapshot archive를 내려받아 압축 해제한다.
- 팀 모드 기본 로컬 구조:
  - `TeamWorkspaceRoot/<ProjectId>/source/`
  - `TeamWorkspaceRoot/<ProjectId>/output/`
  - `TeamWorkspaceRoot/<ProjectId>/.era-translator/`
- 팀 모드에서 `GameDirectory`는 `source/`, `OutputDirectory`는 `output/`를 가리키게 한다.
- source snapshot이 갱신되면 새 `ScanRevisionId`를 발급하고, 기존 클라이언트 제출은 기본적으로 재동기화 전까지 차단한다.
- archive 생성 시 `.era-translator`, `.era-translator-backup`, output 폴더, 로그/캐시 폴더는 제외한다.
- archive 다운로드 후 압축 해제 시 zip slip 방지, 최대 크기, 최대 파일 수, 경로 정규화 검사를 수행한다.

### Scan Manifest / Work Item 생성

- 서버는 FastAPI에서 ERB/CSV 추출 로직을 재구현하지 않는다.
- 기준 scan manifest는 WPF 클라이언트의 기존 스캐너가 생성한다.
- 프로젝트 생성 또는 source snapshot 갱신 흐름은 다음 순서로 고정한다.
  - 관리자가 source archive를 서버에 업로드한다.
  - 권한 있는 클라이언트 또는 관리용 스캔 작업자가 source snapshot을 내려받는다.
  - 클라이언트가 기존 `FileScanner`로 추출해 scan manifest를 만든다.
  - 클라이언트가 `ScanRevisionId`와 함께 scan manifest를 서버에 업로드한다.
  - 서버는 manifest에서 `work_items`와 `shared_namespace_entries`를 생성하거나 갱신한다.
- scan manifest에는 최소한 다음 필드를 포함한다.
  - `scan_revision_id`
  - `documents`
  - `items`
  - `segment_id`
  - `relative_path`
  - `line_number`
  - `file_type`
  - `segment_type`
  - `original_text`
  - `source_key`
  - `symbol_namespace`
  - `original_symbol_key`
  - `is_reference_bearing_key`
  - `identifier_occurrences`
  - `symbol_references`
- 서버는 manifest 업로드 시 같은 `ScanRevisionId`의 source archive 해시와 연결해 저장한다.
- manifest가 없는 source snapshot은 다운로드 가능하지만 팀 sync/submit 대상으로 활성화할 수 없다.

### 서버 협업 구조

- 서버는 `ProjectId` 단위로 여러 프로젝트를 동시에 관리한다.
- 서버가 관리하는 핵심 데이터:
  - 프로젝트 메타데이터
  - source snapshot 메타데이터
  - 작업자와 멤버십
  - 할당 범위
  - 일반 항목 상태와 revision
  - 공통 참조키 상태와 revision
  - 제출 이력
  - 충돌과 충돌 해소 이력
- 공통 참조키 식별자는 `ProjectId + SymbolNamespace + OriginalSymbolKey`
- 일반 항목 매칭 기준은 `ProjectId + SegmentId + OriginalText`

### 인증 / 권한

- v1은 간단한 서버 로그인과 역할 기반 권한으로 시작한다.
- 역할은 `admin`, `reviewer`, `translator`로 고정한다.
- `ClientId`는 장치/작업자 식별용이며 인증 토큰을 대체하지 않는다.
- `admin` 권한:
  - 프로젝트 생성/수정/비활성화
  - source snapshot 업로드/활성화
  - scan manifest 업로드/재생성 승인
  - 멤버십과 할당 수정
  - 모든 충돌 해소
- `reviewer` 권한:
  - 할당 범위 내 또는 프로젝트 설정상 허용된 충돌 해소
  - 공통 참조키 승인/수정
- `translator` 권한:
  - 할당 범위 내 일반 항목 제출
  - 할당 범위 내 공통 참조키 제안

### 버전 / 충돌 정책

- 프로젝트별 활성 source snapshot 버전 `CurrentScanRevisionId`를 유지한다.
- 일반 항목은 `ItemRevision`, 공통 참조키는 `SharedRevision` 정수 버전을 사용한다.
- 클라이언트 제출에는 항목별 `BaseRevision`과 전체 `ScanRevisionId`를 포함한다.
- `BaseRevision == CurrentRevision`이고 `ScanRevisionId`가 현재 source snapshot과 같을 때만 자동 반영한다.
- 제출값이 현재 서버값과 같으면 stale 상태여도 `NoOp`로 처리한다.
- 모든 제출은 client-generated `SubmissionId`를 포함해 재시도 시 idempotent하게 처리한다.
- 충돌 유형:
  - `StaleRevisionConflict`
  - `SourceChangedConflict`
  - `SharedNamespaceConflict`
  - `AssignmentConflict`
  - `ProjectScopeConflict`
  - `DuplicateSubmissionConflict`
- 충돌 해소 방식:
  - `KeepServer`
  - `AcceptIncoming`
  - `ManualMerge`
- source snapshot이 변경되면 서버는 이전 `ScanRevisionId`의 번역 상태를 새 manifest에 carryover한다.
- carryover 우선순위는 기존 클라이언트 로직과 맞춰 `SegmentId + OriginalText`, strong key, occurrence, same-original 순서로 둔다.
- carryover된 항목은 기본적으로 `검수 필요` 상태로 표시한다.

### Submit / Offline Queue

- 팀 모드 클라이언트는 서버 연결 실패 시 dirty change를 로컬 큐에 보관한다.
- 큐 항목은 `SubmissionId`, `ProjectId`, `ScanRevisionId`, 대상 revision, 변경 payload를 포함한다.
- 다음 sync 성공 시 사용자가 명시 제출하거나 자동 제출 설정이 켜진 경우에만 서버로 보낸다.
- source revision이 바뀐 상태에서는 큐를 자동 제출하지 않고 재동기화 필요 상태로 표시한다.
- 같은 `SubmissionId` 재전송은 서버에서 기존 결과를 반환한다.

### 서버 / UI / 저장소

- 서버 스택:
  - Python 3.12+
  - FastAPI
  - SQLAlchemy 2.x
  - Alembic
  - Pydantic v2
- 관리 UI는 서버 렌더링 웹 화면으로 구현한다.
- source snapshot archive는 파일 스토리지에 저장하고, PostgreSQL에는 경로/해시/크기/업로더/활성 여부만 저장한다.
- active snapshot archive는 삭제할 수 없다.
- 기본 보존 정책은 프로젝트별 최근 3개 snapshot 보존으로 한다.
- orphan archive는 관리 UI의 정리 작업에서 검출하고, 관리자 승인 후 삭제한다.
- 관리 UI 최소 기능:
  - 프로젝트 목록/생성/수정/비활성화
  - 작업자 목록/최근 접속
  - 프로젝트별 할당 관리
  - source snapshot 업로드/활성화/다운로드
  - scan manifest 업로드/검증/활성화
  - 공통 참조키 검색/수정/이력
  - 충돌 목록/비교/해소
  - 제출 이력 조회

### 로컬 상태 / 사전 격리

- 로컬 모드의 프로젝트 사전은 현재처럼 사용자가 지정한 게임 폴더 기준으로 유지한다.
- 팀 모드의 프로젝트 사전은 `TeamWorkspaceRoot/<ProjectId>/.era-translator/dictionaries/` 아래에 둔다.
- 팀 모드의 로컬 `state.db`에는 서버 sync 메타데이터와 offline queue 상태를 함께 저장한다.
- 로컬 모드 state와 팀 모드 state는 서로 carryover하지 않는다.
- 단, 사용자가 명시적으로 텍스트 export/import를 수행하는 오프라인 보조 흐름은 유지할 수 있다.
- 팀 모드에서 서버 shared key sync가 완료되면 로컬 참조키 항목에 즉시 반영하고, 저장 시에는 기존 symbol rewrite가 그 값을 기준으로 동작한다.

## Public APIs / Types

- 클라이언트 설정 / 상태
  - `ProjectMode`
  - `ProjectContext`
  - `LocalProjectContext`
  - `TeamProjectContext`
  - `TeamServerUrl`
  - `TeamProjectId`
  - `TeamDisplayName`
  - `ClientId`
  - `TeamWorkspaceRoot`
  - `LastSyncedScanRevisionId`
  - `LocalSourceScanRevisionId`
  - `OfflineSubmissionQueue`
  - `TeamProjectDictionaryPath`
- 클라이언트 서비스
  - `ProjectContextService`
  - `TeamProjectSyncService`
  - `TeamSourceSyncService`
  - `TeamWorkspaceService`
  - `TeamConflictService`
- 서버 모델
  - `ProjectSourceSnapshot`
  - `TeamProject`
  - `TeamClient`
  - `TeamAssignment`
  - `TeamWorkItem`
  - `SharedNamespaceEntry`
  - `TeamSubmission`
  - `TeamConflictRecord`
- 서버 API
  - `POST /api/clients/register`
  - `GET /api/projects`
  - `GET /api/projects/{project_id}/sync`
  - `GET /api/projects/{project_id}/source`
  - `GET /api/projects/{project_id}/source/download`
  - `POST /api/projects/{project_id}/source`
  - `POST /api/projects/{project_id}/source/{scan_revision_id}/activate`
  - `POST /api/projects/{project_id}/source/{scan_revision_id}/scan-manifest`
  - `GET /api/projects/{project_id}/source/{scan_revision_id}/scan-manifest/validation`
  - `POST /api/projects/{project_id}/submit`
  - `GET /api/projects/{project_id}/conflicts`
  - `POST /api/projects/{project_id}/conflicts/{conflict_id}/resolve`
  - `GET /api/projects/{project_id}/shared-keys`
  - `POST /api/projects/{project_id}/shared-keys/{entry_id}`

## Test Plan

- 로컬 모드에서 서버 설정 없이 기존 추출/번역/저장이 그대로 동작하는지 확인한다.
- 팀 모드에서 로컬 원본 파일 없이 source snapshot 다운로드 후 바로 추출 가능한지 확인한다.
- 로컬 모드와 팀 모드의 `state.db`, 경로, 최근 상태가 서로 섞이지 않는지 확인한다.
- source revision이 같으면 재다운로드 없이 기존 작업본을 재사용하는지 확인한다.
- source revision이 바뀌면 재다운로드와 재추출이 요구되는지 확인한다.
- scan manifest가 없는 source snapshot은 sync 대상으로 활성화되지 않는지 확인한다.
- scan manifest 업로드 후 work item과 shared key가 생성되는지 확인한다.
- source archive에 zip slip 경로가 포함되면 압축 해제가 차단되는지 확인한다.
- 팀 모드 프로젝트 사전이 로컬 모드 프로젝트 사전과 분리되는지 확인한다.
- 네트워크 실패 후 같은 `SubmissionId`를 재전송하면 중복 반영 없이 기존 결과가 반환되는지 확인한다.
- source revision 변경 후 offline queue가 자동 제출되지 않는지 확인한다.
- 같은 `SegmentId + OriginalText`라도 프로젝트가 다르면 일반 항목이 분리되는지 확인한다.
- 같은 `Namespace + Key`라도 프로젝트가 다르면 공통 참조키가 분리되는지 확인한다.
- 일반 항목 동시 수정 시 `StaleRevisionConflict`가 발생하는지 확인한다.
- 공통 참조키 동시 수정 시 `SharedNamespaceConflict`가 발생하는지 확인한다.
- `ScanRevisionId`가 다른 상태에서 제출하면 `SourceChangedConflict` 또는 source mismatch reject가 발생하는지 확인한다.
- 할당 밖 제출이 차단되거나 충돌로 집계되는지 확인한다.
- 동일값 재제출이 `NoOp`로 처리되는지 확인한다.
- `KeepServer`, `AcceptIncoming`, `ManualMerge` 해소 결과가 모두 올바르게 반영되는지 확인한다.
- Windows/Linux에서 FastAPI 서버 실행과 source archive 저장/다운로드가 가능한지 확인한다.

## Assumptions

- 앱은 팀 협업 전용으로 바뀌지 않고, 로컬 단독 작업이 계속 1급 시나리오로 유지된다.
- 기본 프로젝트 생성/열기 흐름의 기본값은 `Local`이다.
- 팀 모드에서만 서버 source snapshot 다운로드와 sync/submit을 사용한다.
- source snapshot 동기화 단위는 v1에서 전체 프로젝트 작업본이다.
- 서버 운영 DB는 PostgreSQL을 기본으로 하고, archive는 파일 스토리지에 저장한다.
- 서버 work item은 클라이언트 scan manifest 업로드로 생성한다.
- 팀 모드 프로젝트 사전은 팀 workspace 내부에 저장한다.
- snapshot 보존 기본값은 프로젝트별 최근 3개다.
- 실시간 공동 편집이나 P2P/grid 배포는 v1 범위에 넣지 않는다.
