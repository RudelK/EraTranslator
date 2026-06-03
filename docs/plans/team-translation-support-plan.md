# 팀 단위 번역 지원 계획

## Summary
- 협업 방식은 "담당 범위 export -> 팀원 번역 -> 리더가 import/merge"로 구현한다.
- 공유 SQLite DB 동시 작업은 지원하지 않고, `state.db`는 각 작업자의 로컬 진행 상태로 유지한다.
- 작업 분할은 파일/폴더 기준을 우선 지원한다.
- 동일 항목에 서로 다른 번역이 들어오면 자동 덮어쓰지 않고 "충돌/검수 필요" 상태로 보류한다.

## Key Changes
- 팀 작업 패키지 export 기능을 추가한다.
- 사용자가 파일/폴더 경로 패턴을 선택하면 해당 범위의 추출 항목만 별도 패키지로 내보낸다.
- 패키지에는 `SegmentId`, `RelativePath`, `LineNumber`, `FileType`, `OriginalText`, `TranslatedText`, `Status`, `SourceKey`, `SymbolNamespace`, `OriginalSymbolKey`를 포함한다.
- 현재 텍스트 export/import 포맷을 확장하거나, 더 안전한 JSON 기반 `EraTranslator Team Package v1` 포맷을 추가한다.
- 팀 작업 패키지 import/merge 기능을 추가한다.
- `SegmentId`와 `OriginalText`가 현재 추출 상태와 모두 일치할 때만 적용한다.
- 기존 번역이 비어 있으면 가져온 번역을 적용한다.
- 기존 번역과 가져온 번역이 같으면 상태만 정상화한다.
- 기존 번역과 가져온 번역이 다르면 덮어쓰지 않고 `검수 필요 / 충돌` 상태로 표시하고, 충돌 내용을 별도 목록 또는 로그에 남긴다.
- 원문이 달라진 항목은 적용하지 않고 `원문 불일치`로 집계한다.
- UI 흐름은 "팀 패키지 내보내기"와 "팀 패키지 가져오기" 중심으로 단순하게 유지한다.
- 결과 요약에는 적용됨, 충돌, 원문 불일치, 대상 없음, 빈 번역 건수를 표시한다.
- 병합으로 적용된 항목은 기존 `SaveTranslationProgressItems` 또는 snapshot 저장 경로를 사용해 현재 프로젝트 DB에 반영한다.
- 팀 패키지는 진행 상태 교환용이며, 스캔 세션 자체나 원본 파일 구조를 바꾸지 않는다.
- 최종 게임 파일 출력은 기존 저장 기능이 담당한다.

## Public APIs / Types
- 추가 모델: `TeamTranslationPackage`, `TeamTranslationPackageEntry`, `TeamTranslationImportResult`, `TeamTranslationConflict`.
- 추가 서비스: `TeamTranslationPackageService.Export(...)`, `TeamTranslationPackageService.Import(...)`, `TeamTranslationMergeService.Merge(...)`.
- `MainWindowViewModel`에 팀 패키지 export/import 명령을 추가한다.
- 기존 `TranslationTextExchangeService`는 유지하되, 팀 기능은 충돌 처리와 메타데이터가 필요한 별도 서비스로 분리한다.

## Test Plan
- 파일/폴더 기준 export가 지정 범위의 항목만 포함하는지 확인한다.
- import 시 `SegmentId + OriginalText`가 일치하는 항목만 적용되는지 확인한다.
- 빈 기존 번역에는 가져온 번역이 적용되는지 확인한다.
- 기존 번역과 같은 번역은 충돌 없이 통과하는지 확인한다.
- 기존 번역과 다른 번역은 덮어쓰지 않고 충돌/검수 필요로 남는지 확인한다.
- 원문 불일치, 대상 없음, 빈 번역 항목이 결과 집계에 정확히 반영되는지 확인한다.
- 병합 후 저장/재시작 시 적용된 번역과 충돌 상태가 복원되는지 확인한다.

## Assumptions
- v1에서는 여러 명이 같은 `state.db`를 동시에 여는 공유 DB 협업은 지원하지 않는다.
- v1 작업 분할은 파일/폴더 기준을 기본으로 한다.
- 충돌 기본 정책은 "자동 덮어쓰기 금지, 검수 필요로 보류"다.
- 담당자 이름, 마감일, 진행률 대시보드 같은 프로젝트 관리 기능은 v1 범위에 넣지 않는다.
