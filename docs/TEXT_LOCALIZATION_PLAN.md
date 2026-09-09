# 텍스트 로컬라이징 도입 계획 (2026-09-08)

이 문서는 각 구현 에이전트가 참조하는 공유 브리핑이다. 클래스 구성·메서드 시그니처·알고리즘은 여기서 정하지 않고 담당 에이전트가 판단한다. 여기에는 목표, 실측된 현황, 확정 방침, 참고할 기존 코드·인프라, 지뢰, 거시 단계만 둔다.

## 목표

유저에게 보이는 텍스트가 네 방식(서버 스펙 표 · ScriptableObject · 스크립트 리터럴 · 프리팹 직접 기입)에 흩어져 있다. 언어를 하나 더 붙이려면 이 네 곳을 전부 손대야 하고, 같은 문자열이 두세 곳에 중복되어 있어 어느 값이 화면에 뜰지 예측하기 어렵다.

이 작업으로 얻으려는 것은 세 가지다.

1. 유저 노출 텍스트의 **조회 창구를 하나**로 만든다. 화면 코드는 키와 인자만 넘기고 문장을 조립하지 않는다.
2. 기획자가 시트에서 만지는 텍스트와 클라 개발자가 만지는 텍스트의 **소유 경계**를 세운다.
3. 한국어 외 언어를 붙일 때 **코드·프리팹을 다시 열지 않아도 되는** 구조를 만든다. 실제 2차 언어 저작은 이번 범위 밖이다.

## 실측 현황

| 방식 | 규모 | 값을 끼우는 방식 | 핵심 문제 |
|---|---|---|---|
| 서버 스펙 CSV (`docs/SpecData/*_sheet.csv`) | 텍스트 열 보유 표 10개, 텍스트 셀 약 182개 | `OutGame/Spec/SpecSource` 가 파싱해 `CardSpec` · `SynergyData` 등에 대입 | 내장 폴백 없이 서버 스냅샷만 읽는다. 열 추가는 업로더·지문 검증·CS 생성까지 동시에 바뀐다 |
| ScriptableObject (`Assets/SO/`) | 한글 보유 .asset 12개 | 인스펙터 값을 코드가 그대로 `.text` 에 대입 | 룰렛 이름·랭크 이름·모험 정점명이 시트와 이중으로 존재 |
| 스크립트 리터럴 (`Assets/Scripts/`) | 291파일 2,879건 (로그·주석 제외) | `$"…{값}…"` 보간 217건이 지배적, `string.Format` 6건 | 보간은 컴파일 후 문자열이 사라져 기계 추출이 안 된다 |
| 프리팹·씬 직접 기입 | 자체 프리팹 65개 + 씬 3개, TMP `m_text` 한글 약 250줄 | 치환 규약 없음 (`<sprite>` · `${}` · 키 컴포넌트 0건) | 런타임에 덮이는 플레이스홀더와 진짜 라벨을 구분할 자동 기준이 없다 |

텍스트 열을 가진 표: Card(displayName · cardExplain) · SynergyDef(displayName · effectDescription) · SynergyTierDef(label · effectSummary) · Mission(title · description) · AlbumThemeInfo(displayName · description) · CardGrade · CardPack · RankGrade · PassSeason · Roulette · AIDeck(deckName).

### 이미 존재하는 이중 진실원

- **랭크 등급명**: `RankGrade_sheet.csv` 의 displayName 이 진실원인데, `OutGame/Rank/RankConfig` 에 "언랭크" 리터럴이 따로 있고 `RankPromoteOverlay.prefab` 에는 "실버 1" 이 박혀 있다.
- **코드 기본값 vs 프리팹 저장값**: `[SerializeField] string` 한글 기본값이 코드에 32건 있고, 그중 상당수가 프리팹 YAML 에도 저장되어 있다. 이 경우 프리팹 값이 이긴다. 대표: `UI/Battle/TurnBannerUI.playerText` · `UI/Battle/CoinFlipUI.frontText` · `UI/Lobby/RankPromoteOverlay.titleGradeUp` · `UI/Match/MatchmakingShell.searchingTitle` · `UI/CardDetail/CardDetailOverlayView.evolveLabel`.
- **미션 제목·설명**: 서버 `functions/src/missions/catalog.ts` 가 `Mission_sheet.csv` 의 값을 하드코딩 사본으로 든다. 주석이 스스로 인정하고 있다.
- **룰렛·모험 이름**: `OutGame/Roulette/RouletteConfig.displayName` 과 `Roulette_sheet.displayName`, `OutGame/Adventure/AdventureNodeDef.displayName` 과 서버 AdventureChapter 표가 각각 짝이다.
- **시너지 티어 요약의 수치**: `SynergyTierDef.effectSummary` 의 "1명당 1 피해" 와 `SynergyEffectDef.parameters` 의 damagePerMember 가 같은 수를 두 곳에서 쥔다.

### 인프라 상태

- `com.unity.localization` 1.5.12 는 manifest 에만 있고 사용 흔적이 0건이다. Locale · StringTable · LocalizationSettings 에셋이 없고 코드·프리팹 참조도 없다.
- 폰트 6종(`Assets/Fonts`)은 전부 동적 아틀라스이며 `m_FallbackFontAssetTable` 이 비어 있다. TMP Settings 의 폴백도 비어 있다. Jalnan · ONE Mobile POP 은 한글·라틴 전용 TTF 라 일본어·중국어·키릴을 넣으면 즉시 글리프 누락이 난다.
- `Utils/KoreanText` 는 토큰 시스템이 아니라 주격 조사(이/가) 선택기다. 호출부는 `UI/CardDetail/CardDetailOverlayView` 와 `UI/Shop/PackPurchaseFailurePopup` 두 곳뿐이지만, 문장 구조가 한국어에 묶여 있다는 신호다.
- 서드파티 UI 키트 텍스트가 자체 화면 프리팹에 남은 곳은 실질적으로 없다. `Assets/Images/_Vendor/LayerLab/…` 의 데모 자산 1건은 참조되지 않는다.

## 확정 방침

### 진실원은 2원 체제

| 부류 | 진실원 | 소유자 | 예 |
|---|---|---|---|
| 데이터성 텍스트 (서버가 읽거나 기획자가 만지는 것) | 스펙 시트의 언어 열 | 기획 | 카드 이름·설명, 시너지 설명, 미션 제목, 도감 테마명, 팩 이름, 랭크 등급명, 룰렛 상품명 |
| UI 고정 문구 (버튼·팝업·배너·안내) | Unity Localization StringTable | 클라 개발 | "내 턴", "스핀 돌리기", 구매 실패 안내, 로딩 복구 화면 문구 |

선택하지 않은 안과 이유:

- **StringTable 단일 진실원**: "스펙 값의 진실원은 CSV" 규칙(`CLAUDE.md`)과 충돌한다. 서버 함수 네 곳(`getMissions.ts` · `completionTable.ts` · `pass/passSpec.ts` · `roulette/rouletteSpecReader.ts`)이 시트 텍스트 열을 실제로 읽으므로 이 경로가 끊긴다.
- **스펙 시트에 Localization 표 신설**: 버튼 라벨 하나 고칠 때마다 `SpecData.bytes` 바이너리 재생성과 서버 업로드에 묶인다. 다중 세션이 같은 바이너리를 다투게 되고 머지 시 옛 굽기본을 삼키는 함정([[specdata-bytes-merge-trap]])이 다시 열린다.

### 접근은 "파사드 먼저, 흡수는 점진" 이며 시트만 예외

전부 정리하고 나서 로컬라이징을 얹지 않는다. 리터럴 2,879건을 두 번 만드는 셈이 된다. 대신 **키가 없으면 한국어 리터럴을 그대로 돌려주는 조회 창구**를 먼저 두고, 네 방식을 그 뒤로 하나씩 옮긴다. 옮기지 않은 화면은 그대로 뜬다.

시트 언어 열만은 점진이 불가능하다. 시트 저작 · CS 생성 · TableNames · bytes · 서버 업로드가 한 묶음이라([[spec-table-add-blocks-boot]]) 점진 추가마다 부팅 게이트를 다시 통과해야 한다. 시트는 한 번에 일괄로 간다.

### 층별 책임

- **키 조회**: 조회 창구가 맡는다. 화면 코드가 StringTable API 를 직접 부르지 않는다.
- **수치 치환**: 포맷 문자열은 조회 창구 쪽 데이터가 소유하고, 화면은 인자만 넘긴다. 지금 `UI/Growth/KeywordGrowthPanel` · `UI/Shop/PackCardView` · `UI/Shop/PackOddsPopup` · `UI/Roulette/RoulettePanel` 처럼 UI 파일 안에서 보간하는 방식은 걷는다. `UI/Album/Insert/AlbumInsertSession.skipLabelFormat` 처럼 포맷을 데이터로 빼둔 곳이 이미 있으니 그 방향이다.
- **한국어 조사**: 언어 의존 규칙이므로 조회 창구의 후처리로 들어간다. UI 가 `KoreanText` 를 직접 부르는 현 구조는 끊는다.
- **데이터 텍스트의 언어 선택**: 소비처(CardSpec.DisplayName 소비처만 27파일 41곳)가 아니라 로딩 지점인 `OutGame/Spec/SpecSource` · `OutGame/Spec/SynergySpecSource` 계층이 맡는다. `CardSpec` 은 문자열 파싱을 소유하지 않는 순수 데이터라 이 경계가 자연스럽다.

### 프리팹은 하이브리드

- 코드 `[SerializeField] string` 기본값과 겹치는 부류는 **필드를 키로 바꾸고 코드가 SetText** 한다. 프리팹은 건드리지 않는다.
- 순수 정적 라벨(SettingUI · Tab_Match · DeckEditPanel 등)만 `LocalizeStringEvent` 부착 대상이다. 프리팹 하나당 세션 하나로 직렬화한다.
- `PurchasedAssets` · Layer Lab 데모 프리팹 45개는 건드리지 않는다.

### StringTable 은 기능 폴더 단위로 분할

표를 하나로 두면 모든 세션이 같은 .asset 에 키를 추가한다. Common · Lobby · Battle · Shop · Album · Growth · Match · Tutorial 처럼 `UI/` 하위 폴더에 대응하는 단위로 나눠 파일 세트가 겹치지 않게 한다. 분할 기준의 최종 결정은 1단계 담당이 한다.

## 참고할 기존 코드·인프라

- **조립 단일 지점이 이미 있는 곳**: `Battle/Synergy/SynergyText` (Name · Requirement · TierRequirement · Body) 와 이를 쓰는 `UI/Keyword/ExplainPopupUI.ExplainPopupData.ForSynergy`. 이 안에서만 키 조회로 바꾸면 소비처가 무변경이다. `TierRequirement` 의 "장" 접미와 `Body` 의 "설명 없음" 리터럴은 키 대상이다.
- **키워드 설명**: `Card/KeywordIconConfig` (displayName · explain · effectLabel) 가 SO 진실원이고, `Prefabs/UI/KeywordExplain.prefab` 의 같은 문구는 런타임에 덮이는 저작 미리보기다.
- **스펙 파이프라인**: `Editor/SpecLocalCsvImporter` 는 기존 bytes 의 표만 교체하므로 로컬에서 새 표를 만들 수 없다. 열 추가는 정식 시트 스키마 변경으로 `SpecDatas.cs` 재생성이 필요하고, 생성기는 `com.cookapps.specdatamanager` 패키지(PackageCache)에 있다. 서버 업로드는 `Editor/SpecFirestoreUploader`. 로컬 임포터의 함정은 [[speclocal-csv-importer-trap]], 시트 저작 규약은 [[specsheet-table-authoring]].
- **텍스트를 읽는 서버 코드**: `functions/src/missions/catalog.ts` · `functions/src/commands/getMissions.ts` · `functions/src/completionTable.ts` · `functions/src/pass/passSpec.ts` · `functions/src/roulette/rouletteSpecReader.ts`. 언어 열을 추가하면 이들이 어느 열을 내려줄지 결정해야 한다.
- **미션 텍스트 클라 수신**: `OutGame/Mission/MissionSnapshot.Title`. 서버가 텍스트를 내려주는 유일한 경로라 언어 열 설계가 여기까지 닿는다.
- **폰트**: `Assets/Fonts` 의 TMP 에셋 6종과 `TMP Settings.asset`.
- **Unity Localization 공식 문서**: 패키지 1.5.x 기준 StringTable · Locale · CSV 확장 · Smart String. 담당 에이전트가 context7 로 현재 문서를 확인한 뒤 API 를 고른다.

## 가장 틀리기 쉬운 지점

- **프리팹 값이 코드 기본값을 이긴다.** `[SerializeField] string` 을 키로 바꾸는 순간 Unity 가 프리팹을 다시 쓴다([[serialized-field-removal-pollutes-prefab]]). 같은 프리팹을 다른 세션이 레이아웃 작업 중이면 조용히 되돌아간다([[prefab-save-silently-reverts]]). 프리팹별 소유자를 정하고 저장 확인은 YAML 을 직접 본다.
- **보간 문자열은 기계 추출이 안 된다.** 217건은 손으로 포맷 문자열로 되돌려야 한다. 어순이 언어마다 달라지므로 위치 인덱스 대신 이름 있는 인자를 검토할 것.
- **시트 언어 열 추가는 한 커밋이어야 한다.** 시트 · `SpecDatas.cs` · `SpecData.bytes` · Firestore 업로드 · 지문 검증이 원자적으로 바뀌지 않으면 전 유저 초기화가 막힌다. 이 작업 중 다른 세션의 스펙 수정을 막는다. 새 표를 만드는 우회로로 Composition 업로드를 쓰지 않는다([[composition-upload-bypasses-sheet]]).
- **한국어 폴백이 있어야 점진 이관이 산다.** 키 미등록을 예외로 던지면 이관 도중 화면이 깨진다. 폴백은 "키 없음" 을 개발 빌드에서만 드러내는 방식이 필요하다.
- **런타임에 덮이는 프리팹 텍스트와 진짜 라벨을 가르는 기준이 없다.** 66개 파일을 눈으로 갈라야 한다. 코드에서 `.text` 대입이 있는지 먼저 확인하고 나서 프리팹을 연다.
- **폰트 폴백이 비어 있다.** 2차 언어는 범위 밖이지만, 조회 창구가 로케일을 갖는 순간 TMP 폴백 테이블이 없는 상태로는 테스트가 불가능하다. 최소한 폴백 체인의 자리는 이번에 만든다.
- **서버 미션 카탈로그 사본.** 시트 언어 열을 설계하기 전에 이 사본을 걷지 않으면 서버가 어느 언어를 내려줄지 결정할 자리가 없다.

## 거시 단계

각 단계는 독립 커밋 단위이며, 앞 단계가 끝나야 뒤 단계가 의미를 가진다. 담당 표기는 `CLAUDE.md` 의 라우팅을 따른다.

| 단계 | 내용 | 담당 | 완료 판정 |
|---|---|---|---|
| 0. 골격 | Localization 설정 · Locale(ko) · 기능별 StringTable 분할 · 한국어 폴백 조회 창구 · 포맷 인자 · 조사 후처리 자리. 화면은 아직 하나도 옮기지 않는다 | architecture-engineer 설계 검토 → cavecrew-builder | 기존 화면이 전부 그대로 뜨고, 키 하나를 등록한 샘플 문구가 조회 창구로 나온다 |
| 1. 이중 진실 해소 | 서버 미션 카탈로그 사본 제거 · 룰렛·랭크·모험 이름의 SO/리터럴 중복을 시트 쪽으로 정리 · 시너지 티어 요약의 수치를 파라미터에서 끼우도록 분리 | outgame-engineer, 서버 함수 수정은 별도 세션 | 같은 문자열이 두 곳에 남지 않는다 |
| 2. 시트 언어 열 일괄 추가 | 텍스트 열 보유 표 10개에 언어 열 스키마를 한 번에 추가 · CS 재생성 · bytes · 업로드 · 지문 · `SpecSource` 계층에서 언어 선택 | 시트 스키마 작업은 사용자 주도, 클라 해석은 outgame-engineer | 서버 스냅샷으로 부팅되고 카드·시너지 문구가 언어 열에서 나온다 |
| 3. 코드 리터럴 · SO 이관 | `UI/` 하위 폴더별로 분담. 시너지 조립 단일 지점부터 시작. 보간은 포맷 인자로 전환. `KoreanText` 직접 호출 제거 | 폴더별 병렬 세션, 규모가 크면 Workflow 파이프라인 | 로그 제외 한글 리터럴이 조회 창구 밖에 남지 않는다 |
| 4. 프리팹 정적 라벨 | 코드 기본값과 겹치는 부류는 키로 전환(프리팹 무변경) · 순수 정적 라벨만 컴포넌트 부착 · 프리팹 하나당 세션 하나 | cavecrew-builder, 순차 | TMP `m_text` 한글이 플레이스홀더만 남는다 |
| 5. 폰트 폴백 자리 | TMP 폴백 체인과 로케일별 폰트 에셋 슬롯. 실제 2차 언어 폰트는 범위 밖 | cavecrew-builder | 로케일을 바꿔도 예외 없이 한국어로 떨어진다 |

각 단계 완료 후 `tcg-reviewer` 검수와 `Unity_ReadConsole` 컴파일 검증을 거친다. 3단계는 폴더별 병렬이 가능하나 같은 프리팹을 두 세션이 만지지 않도록 착수 전에 겹침 표를 만든다([[parallel-work-protocol]]).

## 범위 밖

- 2차 언어의 실제 번역 저작과 폰트 아틀라스 제작
- 언어 설정 UI 와 세이브 반영
- 서드파티 데모 프리팹의 텍스트
- 로그 · 에디터 도구 · 테스트 코드의 문자열
