# EraTranslator

WPF 기반 Emuera 게임 번역기입니다.

## 현재 지원

- 게임 디렉토리 선택
- `ERB/CSV/CVS` 하위의 `.erb`, `.era`, `.csv`, `.cvs` 스캔
- `Shift-JIS`, `EUC-JP`, `UTF-8`, `UTF-8 BOM`, `UTF-16 LE` 인코딩 감지
- `Shift-JIS` / `EUC-JP` 파일의 `UTF-8 BOM` 변환
- `ERB`의 `PRINT*`, 문자열 리터럴, `CALL ... "..."`, `MESSAGE_BOX`, 일부 inline conditional 텍스트 추출
- `PRINTDATA`, `DATA/DATALIST`, `PRINT_TAG`, `HTML_PRINT` 안의 텍스트 추출
  - HTML 문자열은 태그 내부 표시 텍스트와 `title` 속성 값을 우선 추출
- CSV 스키마 인식 추출
  - `키,값`
  - `ID,이름,...`
  - 캐릭터 CSV의 `呼び名`, `CSTR,*` 같은 표시용 값
  - 숫자 시작 다중 열 테이블의 이름/설명 열
- `%...%`, `{...}`, `<...>`, `[ 0 ]`, `\n`, `\d`, `\@`, `\%`, `\/`, `\\` 보호
- OpenAI API, LM Studio, DeepL API Free, DeepL API Pro, Papago API 선택
- 번역 설정 모달 창
  - 공급자, URL, 인증 정보, 언어 설정 분리
  - OpenAI / LM Studio는 `모델 불러오기`로 `/models` 목록 조회 후 선택 가능
  - API Key는 공급자별로 따로 보관
  - 동시 번역 줄 수(batch size) 설정 가능
  - OpenAI / LM Studio용 `Thinking 끄기` 옵션 제공
  - 프롬프트 탭에서 기본/재시도 프롬프트 수정 가능
  - 기타 탭에서 요청/응답 실행 로그 기록 여부 제어
- 사용자 사전
  - 전역 사전과 현재 프로젝트 사전 분리
  - 같은 원문이 있으면 프로젝트 사전이 전역 사전을 덮어씀
  - 사전 치환은 기존 placeholder 보호 체인에 합류해서 공급자 공통으로 적용
- 공급자 오류 분류
  - 시간 초과
  - HTTP 상태 코드
  - JSON 파싱 오류
  - 응답 누락
- DeepL / Papago 전용 placeholder 마커 복원
- 저장 모드
  - 별도 출력 폴더 저장
  - 원본 덮어쓰기 + 자동 백업
- 작업 중 UI
  - 실행 버튼 잠금 / 취소 버튼 활성화
  - 상태 바에 현재 처리 파일 표시
  - 저장 모드에 따라 출력 폴더 입력 자동 노출/숨김
- 필터링
  - 파일 타입
  - 상태
  - 경고만 보기
  - 텍스트 검색

## 현재 제약

- `EzTransXP`는 UI에 표시되지만 실제 번역 연동은 아직 미구현입니다.
- Emuera 문법 전체를 완전 파싱하는 수준은 아니며, 드문 매크로 조합은 검토가 필요합니다.
- HTML 문자열 추출은 현재 표시 텍스트와 `title` 속성 중심입니다.
- DeepL은 XML ignore tag 기반, Papago는 고정 안전 마커 기반으로 placeholder를 보존합니다.
- 요청/응답 실행 로그는 기본값이 꺼져 있으며, 켜면 실행파일과 같은 폴더의 `EraTranslator.request-response.log` 에 기록합니다.

## 실행

```powershell
cd D:\Work\EraTranslator\EraTranslator
dotnet run
```

## 테스트

```powershell
cd D:\Work\EraTranslator
dotnet test .\EraTranslator.Tests\EraTranslator.Tests.csproj
```

샘플 게임 폴더는 기본적으로 `D:\Work\EraTranslator\sample\era魔界牧場1.050` 를 자동 탐색하도록 되어 있습니다.
