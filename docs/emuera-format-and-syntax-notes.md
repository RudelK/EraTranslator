# Emuera Format And Syntax Notes For EraTranslator

이 문서는 Era Wiki의 Emuera/eramaker 문서를 EraTranslator의 추출, 번역 보호, 심볼 rewrite 구현에 맞게 재정리한 작업 노트입니다.
원문 위키는 일부 영어 포팅이 진행 중인 상태이고, 일본어 원문/소스 문서와 실제 게임별 관용구가 함께 존재하므로 이 문서는 "완전한 언어 명세"가 아니라 구현 기준점과 회귀 테스트 후보 목록으로 사용합니다.

## Source Map

- Main index: https://wiki.eragames.rip/index.php/Emuera
- Eramaker CSV format: https://wiki.eragames.rip/index.php/Emuera/eramacsv
- Eramaker ERB format: https://wiki.eragames.rip/index.php/Emuera/eramaerb
- ERH header files: https://wiki.eragames.rip/index.php/Emuera/ERH
- General Emuera extensions: https://wiki.eragames.rip/index.php/Emuera/exetc
- Operators: https://wiki.eragames.rip/index.php/Emuera/exop
- Constants and variables: https://wiki.eragames.rip/index.php/Emuera/exvar
- User-defined variables: https://wiki.eragames.rip/index.php/Emuera/UserVars
- Instructions: https://wiki.eragames.rip/index.php/Emuera/excom
- Expression functions: https://wiki.eragames.rip/index.php/Emuera/exfunc
- User-defined expression functions: https://wiki.eragames.rip/index.php/Emuera/UserMeth
- HTML_PRINT: https://wiki.eragames.rip/index.php/Emuera/exhtml
- Resource files: https://wiki.eragames.rip/index.php/Emuera/resources
- Emuera.EM+EE reference index: https://evilmask.gitlab.io/emuera.em.doc/en/Reference/index.html
- Emuera.EM+EE summary: https://evilmask.gitlab.io/emuera.em.doc/en/EMEE/EMEE_Summary.html
- Emuera.EM+EE GETNUM: https://evilmask.gitlab.io/emuera.em.doc/en/Reference/GETNUM.html
- Emuera.EM+EE ERDNAME: https://evilmask.gitlab.io/emuera.em.doc/en/Reference/ERDNAME.html
- Emuera.EM+EE VARI/VARS: https://evilmask.gitlab.io/emuera.em.doc/en/Reference/VAR.html
- Emuera.EM+EE STRDATA: https://evilmask.gitlab.io/emuera.em.doc/en/Reference/STRDATA.html

## Emuera.EM+EE Branch Notes

Emuera.EM+EE는 다른 브랜치 계열 문서이므로 기본 Emuera 문법과 동일한 기준으로 단정하지 않습니다.
EraTranslator에서는 "호환성 확장 후보"로 취급하고, 실제 대상 게임이 EM/EE/NET 계열일 때 우선 적용합니다.

- Reference index는 명령/함수 표로 구성되어 있으며, 항목 아이콘으로 eramaker, Emuera, EvilMask EM, Enterprise Edition, Emuera.NET, 기타 contributor 기원을 구분합니다.
- EM+EE summary에는 resource release, audio file support, WebP image support, UTF-8 key macro, dynamic font loading, .NET 7 support 같은 런타임/자산 관련 차이가 정리되어 있습니다.
- Constants & Variables 확장으로 ERH에서 정의한 배열 변수에도 CSV/ERD 파일로 이름을 붙일 수 있습니다. `VariableName.csv`, `VariableName.ERD`, 다차원용 `VariableName@1.ERD` 같은 파일명이 namespace 후보가 됩니다.
- EM+EE는 `DAY.csv`, `TIME.csv`, `MONEY.csv` 같은 파일로도 이름을 부여할 수 있습니다. `DAYNAME`, `TIMENAME`, `MONEYNAME`도 표시/참조 후보로 봐야 합니다.
- Modified commands/functions에는 HTML_PRINT 관련 변경, PRINT 계열과 HTML_PRINT 연동 변경, LOADTEXT/SAVETEXT 파일명 지정, OUTPUTLOG 파일명/확장자 지정, GETNUM의 ERD 지원, GCREATEFROMFILE 상대 경로 지원, EMEE 전용 특수 주석 `;^;` 등이 포함됩니다.
- XML, MAP, DataTable, AWAIT, image processing, sound related 명령이 넓게 추가되어 있습니다. 번역기는 이 계열의 문자열 인자를 기본적으로 식별자, 파일명, XML 경로, key, query, resource id 후보로 보고 보수적으로 보호해야 합니다.

### EM+EE Reference Impact Checklist

- PRINT family: index에는 `PRINT(|V|S|FORM|FORMS)(|K|D)(|L|W|N)`, `PRINTSINGLE`, `PRINTC`, `PRINTDATA`, `PRINTBUTTON`, `PRINTPLAIN`, `DRAWLINE`, `CUSTOMDRAWLINE`, `DRAWLINEFORM`, `REUSELASTLINE`, `PRINT_STATUS`, `PRINT_IMG`, `PRINT_RECT`, `PRINT_SPACE`, `PRINTN` 등이 별도 항목으로 있습니다.
- PRINT extraction policy: `PRINTFORM/FORMS`, `DRAWLINEFORM`, `REUSELASTLINE`, `PRINTPLAINFORM`, `PRINTBUTTON`의 표시 문자열은 번역 후보입니다. `PRINTV`, `PRINTS`의 변수 인자와 `PRINT_IMG`, `PRINT_RECT`, `PRINT_SPACE`의 리소스/숫자 인자는 보호합니다.
- GETNUM: EM+EE reference는 `GETNUM variableName, indexName` command 형식과 expression function 형식을 모두 지원한다고 설명합니다. 따라서 `GETNUM(ABL,"技巧")`뿐 아니라 `GETNUM ABL, "技巧"`도 CSV/ERD key 참조로 추적해야 합니다.
- ERDNAME: `ERDNAME variableName, index(, dimension)`은 ERD 변수 element 이름을 반환합니다. FORM placeholder 안의 `ERDNAME(...)` 결과는 표시 텍스트지만, 호출 인자 `variableName`, `index`, `dimension`은 코드/참조로 보호합니다.
- VARI/VARS: `VARI`는 정수, `VARS`는 문자열 dynamic local variable 정의입니다. `VARS QUESTION = "..."`처럼 문자열 초기값이 자연어일 수 있으므로 변수명은 보호하고 quoted value만 번역 후보로 볼 수 있습니다.
- STRDATA: `STRDATA stringVariable` 블록은 `PRINTDATA`와 같은 `DATA`, `DATAFORM`, `DATALIST`, `ENDLIST`, `ENDDATA` 형식을 사용하지만 화면 출력 대신 문자열 변수에 대입합니다. 블록 내부 텍스트는 번역 후보이고, 첫 줄의 대상 변수는 보호합니다.
- File/resource policy: EM+EE는 sound folder, WebP image, dynamic font folder, GCREATEFROMFILE relative path, LOADTEXT/SAVETEXT/OUTPUTLOG filename extensions를 넓게 다룹니다. 파일명, 확장자, resource id, font name, XML/MAP/DataTable id는 기본 보호 대상입니다.
- Special comments: 기존 Emuera의 `;!;`, `;#;` 외에 EMEE 전용 `;^;`가 존재합니다. 대상 프로젝트가 EMEE 계열이면 일반 주석으로 버리지 말고 별도 코드 라인으로 분류해야 합니다.

## Implementation Goal

EraTranslator가 문법 정보를 활용하는 목표는 원본 Emuera 프로그램의 의미를 보존하면서 번역 가능한 자연어만 안정적으로 분리하는 것입니다.

- 번역 대상: 화면 표시문, 이름, 설명, CSV의 표시용 텍스트 컬럼, HTML_PRINT 내부 표시 텍스트와 tooltip 성격의 속성.
- 보호 대상: 명령어, 변수명, 함수명, 라벨, 숫자, 연산자, 리소스명/파일명, FORM placeholder, CSV 참조키, 제어 문자.
- rewrite 대상: CSV/ERD/ERH에서 번역된 심볼 키가 ERB 코드 안에서 참조될 때 원문 키와 번역 키를 일관되게 맞춰야 하는 항목.
- 검수 대상: 정적 분석으로 안전하게 해석할 수 없는 동적 참조, 게임별 커스텀 함수 인자, 의미는 번역됐지만 코드/참조 안정성이 불확실한 항목.

## File Layout And Load Order

- CSV 계열 파일은 보통 실행 파일 아래 `CSV` 폴더에 둡니다. EraTranslator는 현재 `CSV`, `CVS`, `DATA` 계열 폴더도 스캔 대상으로 삼습니다.
- ERB 파일은 보통 실행 파일 아래 `ERB` 폴더에 둡니다. `.ERB` 확장자는 스크립트 본문입니다.
- ERH 파일은 `ERB` 폴더 안에 둘 수 있으며, ERB보다 먼저 처리해야 하는 `#DIM`, `#DIMS`, `#DEFINE` 중심의 헤더 파일입니다.
- 처리 순서는 대략 CSV, ERH, ERB입니다. 따라서 ERH의 선언은 ERB 분석에는 영향을 주지만 CSV 파싱에는 영향을 주지 않는다고 보는 것이 안전합니다.
- 리소스 파일은 실행 파일 아래 `resources` 폴더에 두며, 이미지 자체나 리소스 CSV의 파일명/리소스명은 번역 대상이 아니라 참조 식별자입니다.
- EM+EE 계열에서는 `sound`, `font` 같은 추가 자산 폴더와 WebP 이미지, GCREATEFROMFILE 상대 경로가 등장할 수 있습니다. 이 경로/파일명도 번역하지 않습니다.

## CSV Rules

CSV는 단순 쉼표 구분 파일처럼 보이지만, 파일명과 컬럼 위치에 따라 의미가 다릅니다.

- 첫 컬럼 첫 글자가 `;`인 줄과 빈 줄은 무시합니다.
- 숫자는 반각 숫자 기준으로 다루는 것이 안전합니다.
- eramaker 기본 CSV 설명에서는 표시 문자열에 스프레드시트식 큰따옴표 wrapping을 쓰지 않는 것을 전제로 설명합니다.
- EraTranslator에서는 실제 게임 호환성을 위해 CSV-like 파서를 유지하되, 컬럼 의미에 따라 번역 여부를 보수적으로 결정해야 합니다.
- `GameBase.csv`의 `タイトル`, `作者`, `追加情報` 같은 값은 표시용 텍스트입니다.
- `Palam.csv`, `Abl.csv`, `Talent.csv`, `Mark.csv`, `Exp.csv`, `Train.csv`, `Item.csv`, `Str.csv` 등은 보통 번호/키와 표시명을 함께 갖습니다.
- `CharaXX.csv` 계열은 `名前`, `呼び名`, `基礎`, `能力`, `素質`, `経験`, `相性`, `助手`, `フラグ` 등 지시어와 값으로 구성됩니다.
- `Str.csv`의 특수 치환 토큰류는 게임 관습에 따라 이름/호칭 placeholder로 동작할 수 있으므로 무조건 자연어로 보지 않습니다.

### CSV Implementation Checklist

- 파일명 stem을 namespace 후보로 등록합니다.
- 첫 번째 컬럼이 숫자/키이고 두 번째 컬럼이 표시명인 파일은 두 번째 컬럼을 번역 대상으로 봅니다.
- CSV 키로 참조될 수 있는 표시명은 `IsReferenceBearingKey`로 다뤄 ERB 참조 rewrite 후보에 넣습니다.
- `CharaXX.csv`의 명령어 컬럼은 번역하지 않고, 이름/호칭/문장성 값만 번역합니다.
- 리소스 지정 CSV의 리소스명, 원본 파일명, 좌표/크기/딜레이는 번역하지 않습니다.

## ERB Basic Syntax

ERB는 줄 단위 명령 중심이지만, Emuera 확장 문법 때문에 단순 line split만으로는 부족합니다.

- 줄 시작 `;`는 주석으로 취급합니다.
- Emuera에서는 행 끝 주석도 사용할 수 있지만, `PRINT foo;bar`처럼 단순 문자열 PRINT의 인자는 세미콜론 이후도 표시 문자열일 수 있습니다.
- 줄 시작 공백과 탭은 명령 해석에서 대체로 무시됩니다.
- 명령어와 인자는 반각 공백 또는 탭으로 구분됩니다.
- 변수 배열 접근은 `:`를 사용합니다. `FLAG:0`, `ABL:5:0`, `EXP:A:1` 같은 형태가 있습니다.
- 일부 캐릭터 변수는 `변수명:캐릭터:키` 형태의 이중 인덱스를 갖습니다.
- 문자열 변수는 `STR`, `SAVESTR`, `TSTR`, `CSTR`, `GLOBALS` 등으로 나타납니다.
- `%STR:0%`는 문자열 표시, `{MONEY}` 또는 `{MONEY + 1}`은 수식 표시 성격의 FORM placeholder입니다.
- `\`는 다음 문자를 시스템 기호로 해석하지 않게 하는 escape로 쓰입니다.

### ERB Comment And Continuation Rules

- `;!;`로 시작하는 줄은 Emuera에서는 유효한 줄로 취급될 수 있으므로 일반 주석으로 버리면 안 됩니다.
- `;#;`로 시작하는 줄은 debug mode 전용 성격입니다. 번역기에서는 기본적으로 코드성 줄로 보고 자연어 추출은 보수적으로 처리합니다.
- `{` 단독 줄부터 `}` 단독 줄까지의 line continuation은 해석 전에 이어붙는다고 봐야 합니다.
- continuation 내부의 주석은 이어붙인 뒤 의미가 달라질 수 있으므로, 단순히 줄별 세미콜론 이후를 잘라내면 안 됩니다.

## PRINT And FORM Syntax

PRINT 계열은 번역 대상이 가장 많이 나오는 영역입니다.

- `PRINT`, `PRINTL`, `PRINTW` 계열의 tail은 보통 표시 문자열입니다.
- `PRINTV`, `PRINTS` 계열은 변수/문자열 변수 출력입니다. 변수명 자체는 번역하지 않습니다.
- `PRINTFORM`, `PRINTFORML`, `PRINTFORMW` 계열은 FORM 문자열을 표시합니다. 내부의 `%...%`, `{...}`는 보호해야 합니다.
- `PRINTDATA`, `DATALIST`, `DATA`, `DATAFORM` 계열은 표시 후보를 여러 개 제공하는 구조입니다. `DATAFORM`은 FORM placeholder 보호가 필요합니다.
- `STRDATA`는 `PRINTDATA`와 같은 블록 구조를 사용하지만 화면 출력 대신 문자열 변수에 대입합니다.
- `PRINT_IMG` 등 이미지/리소스 계열은 문자열 인자가 리소스명/파일명일 수 있으므로 자연어로 추출하지 않는 것이 안전합니다.
- FORM 확장에서는 `{식, 폭, LEFT|RIGHT}`와 `%문자열식, 폭, LEFT|RIGHT%` 같은 정렬 지정이 가능합니다.
- `@"..."`는 FORM 문법을 문자열식 안에서 사용하기 위한 raw/form string 성격입니다.
- `\@ 조건 ? 참 # 거짓 \@` 형태의 inline conditional은 `#` 양쪽 텍스트를 분리 추출하되 조건식은 보호해야 합니다.

### PRINT Implementation Checklist

- PRINT tail 전체가 자연어 표시문이면 문장 단위로 유지합니다.
- Tail에 `%...%`, `{...}`, `<...>`가 섞인 경우 placeholder로 보호하고 주변 자연어를 추출합니다.
- `PRINT ... ; comment`는 PRINT 종류와 인자 성격에 따라 세미콜론 이후가 주석인지 표시 문자열인지 다르게 봅니다.
- FORM placeholder 내부 수식, 변수, CSV 참조는 번역하지 않습니다.
- `PRINT <愛液>`처럼 꺾쇠 안이 HTML 태그가 아니라 표시 토큰인 경우 내부 자연어를 살릴지, 전체 placeholder로 보호할지 문맥별로 판단합니다.

## Variables, References, And CSV Key Lookup

Emuera는 CSV 표시명을 코드 인덱스로 사용할 수 있는 확장 문법이 있어 번역 시 가장 위험합니다.

- `ABL:Skill`, `ABL:2`, `ABL:"Skill"`, `ABL:(ABLNAME:2)`처럼 CSV 이름을 인덱스로 사용할 수 있습니다.
- 변수명과 CSV 항목명이 충돌하면 변수 해석이 우선될 수 있으므로, 괄호가 없는 동적/문자열 인덱스는 보수적으로 봐야 합니다.
- 숫자처럼 보이는 CSV 키는 숫자 인덱스로 오해될 수 있습니다.
- 대표 namespace에는 `ITEM`, `ITEMPRICE`, `ITEMSALES`, `BASE`, `MAXBASE`, `DOWNBASE`, `ABL`, `TALENT`, `EXP`, `MARK`, `PALAM`, `JUEL`, `SOURCE`, `EX`, `NOWEX`, `TEQUIP`, `FLAG`, `TFLAG`, `CFLAG`, `STR`, `SAVESTR`, `TCVAR`, `TSTR`, `CSTR`, `GLOBAL`, `GLOBALS` 등이 있습니다.
- `GETNUM <변수명>, <문자열식>`은 CSV 이름을 번호로 찾는 대표 패턴입니다.
- EM+EE 문서 기준으로 `GETNUM`은 command 형식과 expression function 형식을 모두 지원합니다.
- `ERDNAME(variableName, index, dimension)`은 ERD key 이름을 반환하는 expression function입니다.
- `CSVNAME`, `CSVABL`, `CSVTALENT`, `CSVEXP`, `CSVCFLAG` 등 CSV 조회 함수류는 인자가 표시용 텍스트가 아니라 참조일 수 있습니다.
- `VARSIZE("CSTR")`, `LOADTEXT`, `SAVETEXT` 등은 문자열 인자가 코드/파일/설정 의미를 가질 수 있어 보호 대상입니다.

### Reference Rewrite Checklist

- 직접 참조: `ABL:従順`, `EXP:index:愛情経験`, `GETNUM(EXP, "絶頂経験")`처럼 namespace와 key를 정적으로 읽을 수 있으면 rewrite 대상으로 등록합니다.
- 간접 참조: `keyName = "外見年齢"` 후 `CFLAG:{keyName}` 같은 경우 변수 literal 생산 지점을 추적합니다.
- 동적 참조: 문자열 조립, 함수 반환, 외부 입력 기반 키는 자동 rewrite 대신 검수 경고로 남깁니다.
- key-list 함수 인자: `CALC_CHARA_SINGLE_DATA("TALENT", target, "気骨*3,反抗的")`처럼 문자열 내부가 CSV 키 목록이면 일반 문장 번역에서 제외하고 키 단위로 추적합니다.
- collision: 여러 원문 키가 같은 번역 키가 되면 suffix 또는 검수 정책을 적용합니다.

## Operators And Expressions

연산자와 표현식은 자연어처럼 보이는 일본어 키를 포함할 수 있지만, 기본적으로 번역 대상이 아닙니다.

- 산술, 비교, 논리 연산자는 반각 기호 기준입니다.
- 문자열 비교와 문자열 연결도 확장되어 있습니다.
- `? #` 삼항 연산자가 있으며 문자열 삼항은 `\@ ... ? ... # ... \@` 형태로 자주 등장합니다.
- `'=` 문자열식 대입 연산자는 문자열형 변수에 문자열식을 대입할 때 사용됩니다.
- `++`, `--`는 단독 증감문으로 쓰입니다.
- short-circuit 논리 평가가 있으므로 조건식의 순서 자체가 의미를 가질 수 있습니다.

## ERH And User-Defined Variables

ERH는 번역기에서 "선행 선언 및 매크로 정보"로 다뤄야 합니다.

- ERH에는 `.ERB`보다 먼저 처리할 내용을 둡니다.
- 기본적으로 `#DIM`, `#DIMS`, `#DEFINE` 중심으로 구성됩니다.
- ERH의 `#DIM`, `#DIMS`는 광역 변수 선언입니다.
- `#DIM`은 정수형, `#DIMS`는 문자열형 변수 선언입니다.
- 최대 3차원 변수까지 선언할 수 있습니다.
- `SAVEDATA`, `CHARADATA`, `GLOBAL`, `CONST` 같은 키워드는 변수 성격을 바꿉니다.
- ERB 함수 내부의 `#DIM`, `#DIMS`는 private/local 변수 선언입니다.
- 변수명은 명령어명과 충돌하면 안 됩니다.
- ERH 매크로는 실제 ERB 해석 전에 전개될 수 있으므로, 완전한 정확도를 원하면 전처리 단계 모델링이 필요합니다.

### ERH Implementation Checklist

- ERH의 `#DIM`, `#DIMS` 선언 문자열 중 lookup 배열 성격인 값은 일반 문장이 아니라 참조키로 볼 수 있습니다.
- EM+EE 계열에서는 ERH 선언 변수명과 같은 `VariableName.csv`, `VariableName.ERD`, `VariableName@1.ERD` 파일이 namespace source가 될 수 있습니다.
- `#DEFINE` 매크로가 PRINT/FORM/CSV 참조를 생성하는 경우 현재 정적 추출만으로는 누락될 수 있습니다.
- 선언된 사용자 변수명은 identifier extractor의 reserved/known symbol 목록에 반영할 수 있습니다.
- ERH에서 발견한 `#DIMS CONST` 배열은 `SELECTCASE`, `FINDELEMENT`, wrapper 함수와 결합해 lookup table로 사용되는지 추적합니다.

## HTML_PRINT And Resource Syntax

HTML_PRINT는 HTML과 닮았지만 브라우저 HTML과 동일하지 않습니다. 번역기는 태그 구조와 표시 텍스트를 분리해야 합니다.

- `HTML_PRINT`의 인자는 일반 PRINT tail이 아니라 문자열식입니다.
- 태그는 `<태그명 속성='값'>텍스트</태그명>` 형태를 사용합니다.
- 속성값은 작은따옴표 또는 큰따옴표로 감쌉니다. Emuera 문자열과 구분하기 위해 작은따옴표가 권장됩니다.
- 주요 태그: `p`, `nobr`, `br`, `button`, `nonbutton`, `font`, `b`, `i`, `u`, `s`, `img`, `shape`.
- `button`/`nonbutton`의 표시 텍스트와 `title`은 번역 후보입니다.
- `value`, `pos`, `align`, `face`, `color`, `bcolor`, `src`, 좌표/크기류는 보호 대상입니다.
- 리소스 CSV의 리소스명은 `<img src='...'>`나 `SPRITECREATED("...")`에서 참조되므로 번역하면 안 됩니다.
- 이미지 파일은 resources 폴더 안에 두며, 리소스 CSV에서 원본 파일명과 crop 영역을 지정합니다.

### HTML/Resource Implementation Checklist

- HTML 태그명과 속성명은 번역하지 않습니다.
- 태그 사이의 텍스트 노드는 번역 대상으로 봅니다.
- `title`처럼 사용자에게 보이는 속성만 번역 후보로 봅니다.
- `src`, 리소스명, 파일명, path-like literal, 이미지/audio 확장자는 보호합니다.
- HTML entity와 Emuera FORM placeholder가 섞인 경우 entity/placeholder를 모두 보호한 뒤 자연어만 번역합니다.

## Current EraTranslator Mapping

현재 코드 기준으로 이 문서의 규칙은 아래 컴포넌트와 직접 연결됩니다.

- `EraTranslator/Services/FileScanner.cs`: 대상 폴더, 확장자, namespace/function/dims registry 구성.
- `EraTranslator/Services/CsvExtractor.cs`: CSV/ERD 파일별 컬럼 분류와 번역 대상 추출.
- `EraTranslator/Services/ErbExtractor.cs`: ERB/ERH 표시 텍스트, PRINT/DATA/HTML/대입식 추출.
- `EraTranslator/Services/ErbReferenceExtractor.cs`: CSV key, GETNUM, namespace:key, indirect variable 참조 추적.
- `EraTranslator/Services/ErbIdentifierExtractor.cs`: 함수명/변수명 추출과 identifier rewrite 후보 구성.
- `EraTranslator/Services/PlaceholderProtector.cs`: `%...%`, `{...}`, `<...>`, escape, 변수 참조 보호.
- `EraTranslator/Services/SymbolRewritePlanner.cs`: 번역된 CSV/ERD/lookup key와 ERB 참조 rewrite 계획.
- `EraTranslator/Services/OutputWriter.cs`: 저장 시 span replacement, 심볼 rewrite, identifier rewrite, 조사 rewrite 적용.
- `EraTranslator/Services/TranslationQualityRules.cs`: 토큰 손실, 언어 누수, 식별자 유효성, 길이 차이 검수.

## Regression Test Candidates

새 문법 규칙은 가능하면 문서만 추가하지 말고 테스트로 고정합니다.

- End-of-line comment: `A = B ; comment`와 `PRINT foo;bar`를 다르게 처리.
- Special comments: `;!;PRINTW ...`는 주석으로 버리지 않음.
- Continuation block: `{ ... }` 내부 `#DIM CONST`와 주석 처리 순서.
- FORM width: `{A,10,LEFT}`, `%STR:0,10,LEFT%` 보호.
- Inline conditional: `\@ cond ? text # text \@`에서 조건식 보호, 양쪽 텍스트 추출.
- String expression assignment: `STR '= TSTR:0 + "ABC"`에서 코드와 문자열 분리.
- CSV-name index: `ABL:Skill`, `ABL:"Skill"`, `ABL:(RESULTS:0)` 참조 추적.
- Ambiguous key: CSV 키와 변수명이 같은 경우 검수 경고.
- `GETNUM(namespace, "key")`와 key-list function 문자열 내부 CSV key 추적.
- `GETNUM namespace, "key"` command 형식과 `ERDNAME(namespace, index, dimension)` placeholder 보호.
- `STRDATA stringVariable` 블록 내부 `DATA/DATAFORM` 추출.
- `VARS QUESTION = "表示文"` 문자열 초기값 추출과 변수명 보호.
- HTML_PRINT: `<button value='1' title='説明'>開始</button>`에서 `説明`, `開始`만 추출.
- Resource CSV: `リソース名A, image.png, ...`는 번역하지 않음.
- ERH lookup array: `#DIMS CONST ARRAY="対応娼婦","プレイ傾向"`의 key 성격 판단.
- EMEE special comment: `;^;PRINTW ...`를 대상 엔진 설정에 따라 코드 라인으로 처리.

## Known Limits

- 위키는 완전한 formal grammar가 아니라 설명 문서입니다.
- 영어 페이지 일부는 일본어 문서의 포팅 상태가 불완전할 수 있습니다.
- Emuera.EM+EE 문서는 다른 브랜치 문서이므로 기본 Emuera 대상 프로젝트에 무조건 적용하지 않습니다.
- 게임별 커스텀 함수는 인자 타입을 문법만으로 알 수 없습니다.
- 매크로 전개, 동적 문자열 조립, 사용자 입력 기반 CSV key는 정적 분석만으로 완전히 해결할 수 없습니다.
- 따라서 최종 안정성은 문서화, 정적 추출 규칙, 회귀 테스트, 저장 후 Emuera 실행/컴파일 검증을 조합해야 합니다.

## Recommended Next Steps

- 이 문서를 기준으로 `CsvExtractor`의 파일별 schema table과 `ErbExtractor`의 command table을 분리합니다.
- `ProtectedCodeArgumentFunctionNames`를 코드 중복 없이 공유 목록으로 모읍니다.
- `HTML_PRINT` 속성별 번역/보호 정책을 table-driven으로 바꿉니다.
- `;!;`, line continuation, FORM width, string expression assignment 케이스를 테스트에 추가합니다.
- 실제 샘플 게임에서 실패한 줄은 이 문서의 Regression Test Candidates에 먼저 추가한 뒤 구현합니다.
