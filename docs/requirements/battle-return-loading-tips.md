# 전투 복귀 로딩 팁

## Overview

`Assets/Assets/Prefabs/UI/Common/BattleReturnLoadingCover.prefab`에 여러 팁 중 하나를 무작위로 표시한다. 전투 종료 후 로비로 돌아가는 동안 짧은 정보를 제공한다.

상용 게임 조사:

- **Towa and the Guardians of the Sacred Tree**: 공식 업데이트에서 로딩 화면에 팁을 추가했다. 대기 화면을 정보 제공에 활용하는 사례다. [공식 패치 노트](https://www.bandainamcoent.com/news/towa-and-the-guardians-of-the-sacred-tree-patch-notes-december-2025)
- **Super Smash Bros. Ultimate**: 커뮤니티 분석에 따르면 미노출 팁·현재 모드·진행도를 고려해 선택한다. 반복 완화와 상황별 선별의 확장 사례다. [SmashWiki](https://www.ssbwiki.com/List_of_tips_(SSBU))

이번 범위는 복귀당 한 문구 표시와 직전 문구 연속 반복 방지다. 상황별 선별은 추후 확장한다. 사례의 내부 알고리즘을 그대로 재현하는 요구사항은 아니다.

## Goals

- 여러 팁을 데이터로 관리한다.
- 복귀 커버가 열릴 때 한 번 추첨한다.
- 커버가 닫힐 때까지 선택한 문구를 유지한다.
- 문구 추가·수정을 코드 변경 없이 처리한다.

## Out of Scope

- 로딩 중 자동 순환·다음 팁 버튼.
- 진행도별 필터·가중치·노출 이력 저장.
- 다국어 번역.
- 팁을 읽게 하려고 로딩 시간을 늘리는 변경.
- 기존 로딩 화면 레이아웃·아트·연출 재설계.

## Technical Requirements

### 현재 구조

2026-09-15 체크아웃 기준 확인 결과다.

| 항목 | 확인 결과 |
|---|---|
| 팁 표시 | 프리팹 `Text_Value (1)`의 TMP 텍스트 |
| 현재 문구 | `Tip : 귀여움은 세상을 정복할 수 있어요!` |
| 표시 영역 | RectTransform 약 899 × 71, 글자 크기 30.1 |
| 커버 생성 | `LoadingCoverView.LoadScene`에서 전투→로비일 때 전용 프리팹 선택 |
| 팁 제어 | 현재 목록·추첨·텍스트 참조 필드 없음 |
| 스펙 관리 | 저장소 원본은 CSV, 런타임은 서버 스냅샷을 `SpecSource.Manager`로 조회 |
| 로컬라이징 | 패키지는 설치됨. 현재 체크아웃에서는 `GameText`·Localization 설정 에셋을 찾지 못함 |

참고 코드:

- `Assets/Scripts/UI/Common/LoadingCoverView.cs`
- `Assets/Scripts/UI/Common/SyncUiPrefabCatalog.cs`
- `Assets/Scripts/OutGame/Spec/SpecSource.cs`
- `Assets/Scripts/OutGame/Spec/SpecPayloadCodec.cs`
- `Assets/Scripts/Core/Initialization/SpecSheetPreloadStep.cs`

### 데이터 관리

기존 운영 흐름에 맞춰 `LoadingTip` 정식 시트를 추가하는 방향으로 설계한다.

| 열 | 형식 | 용도 |
|---|---|---|
| `id` | int | 고유 식별자 |
| `text` | string | `Tip :`를 제외한 문구 |
| `enabled` | int | 1: 사용, 0: 제외 |

- 저장소 원본 경로: `docs/SpecData/LoadingTip_sheet.csv`.
- 문구와 사용 여부의 진실원은 시트 한 곳이다. 코드와 SO에 동일 목록을 중복 저작하지 않는다.
- 시트 추가에는 생성 타입·동기화 목록·서버 발행·버전 호환 검토가 함께 필요하다. CSV 추가만으로 런타임에 반영되지 않는다.
- 정식 스키마 변경 및 생성 산출물 갱신은 별도 명시적 지시 후 진행한다. 이 PRD 작성은 해당 실행 권한을 뜻하지 않는다.
- `Assets/Resources/SpecData.bytes`는 에이전트가 수정·생성·재생성하지 않는다. `SpecLocalCsvImporter`도 실행하지 않는다.
- 자동 생성 파일 `Assets/Table/SpecDatas.cs`와 `Assets/Table/SpecDataManager.cs`를 수동 수정하지 않는다.
- 새 필수 표를 클라이언트에 먼저 추가하면 초기화가 막힐 수 있으므로, 구현 시 기존 서버 스냅샷과의 호환 및 반영 순서를 검토한다.

### 표시·선택 규칙

- 기존 TMP 텍스트를 재사용하고, 첫 화면이 그려지기 전에 유효한 문구 하나를 선택한다.
- 사용 중이고 공백이 아닌 행만 후보에 포함한다.
- 후보가 2개 이상이면 직전 팁을 제외하고 동일 확률로 추첨한다.
- 직전 팁은 세션 메모리에만 유지한다. 커버 인스턴스가 파괴되어도 다음 복귀까지 유지하며, 앱 재실행 시 초기화한다.
- 후보가 1개면 해당 문구를 표시한다.
- 후보가 없으면 기존 프리팹 문구를 표시한다. 팁 선택 실패로 복귀를 막지 않는다.
- 한 번의 커버 표시 중에는 재추첨하지 않는다.
- 전투용 `MatchRandom`과 분리된 난수를 사용한다.
- 복귀 시 추가 서버 요청 없이 이미 로드된 데이터를 조회한다.
- 문구는 기존 영역에 들어오는 짧은 한 문장으로 저작한다. `Tip :` 접두사는 표시할 때 한 번만 붙인다.
- 기존 최소 노출 시간·페이드·보상 정산 대기·씬 전환 순서를 유지한다.
- 시작·계정 변경용 커버는 기존 동작을 유지한다.

## Acceptance Criteria

- [ ] 등록한 여러 팁이 반복 복귀에서 선택된다.
- [ ] 유효 후보가 2개 이상이면 같은 팁이 연속 표시되지 않는다.
- [ ] 커버 인스턴스가 바뀌어도 직전 팁 제외가 적용된다.
- [ ] 한 번의 로딩 중에는 문구가 바뀌지 않는다.
- [ ] 비활성·빈 문구는 선택되지 않는다.
- [ ] 후보 0개·1개에서도 복귀가 정상 완료된다.
- [ ] 모든 문구가 기존 영역에서 잘리지 않는다.
- [ ] 팁 선택이 전투 난수·보상 정산·복귀 시간에 영향을 주지 않는다.
- [ ] 복귀 시 팁 조회를 위한 추가 서버 요청이 발생하지 않는다.
- [ ] 시작·계정 변경용 커버는 기존대로 동작한다.
- [ ] 시트 반영 후 문구 추가·수정에 C# 변경이 필요하지 않는다.

## Open Questions

- 최초 팁 문구 목록 확정 필요. 현재 문구는 유지하되, 추가 문구는 실제 게임 규칙과 대조해 저작한다.
- 정식 시트 스키마 추가·생성 산출물·서버 발행의 실행 범위와 반영 순서는 구현 착수 시 확정한다.

문서 작성 완료는 구현 완료를 의미하지 않는다. 위 체크리스트는 구현 검수 시 갱신한다.

## 구현 상태 (2026-09-15)

- 로컬 구현: 최초 6개 팁 CSV, 공식 생성기의 C# 전용 산출물, 선택적 스펙 동기화, 복귀 커버 TMP 배선 완료.
- `LoadingTip`은 필수 표가 아니다. 기존 서버·캐시의 표 누락은 허용하며, 팁만 변경되면 앱 재시작 없이 채택한다. 팁 다운로드 실패·1초 초과는 기본 문구로 처리한다.
- 문구는 `docs/SpecData/LoadingTip_sheet.csv`에서 수정한다. 전체 비노출은 행을 유지하고 `enabled=0`으로 설정한다. 현재 발행 도구의 빈 표 거부 규칙은 유지한다.
- C# 타입은 정식 `SpecDatas.cs`의 `LoadingTip` 선언을 사용한다. 정식 시트 편입 후 임시 `LoadingTip.Generated.cs`와 전용 생성 메뉴·자동 정리 감시자는 제거했다. 문구 수정에는 C# 재생성이 필요 없다.
- 검증 메뉴: `Tools/Card Battle/검증/전투 복귀 로딩 팁`. 기존 지문 호환, 표 추가·수정·삭제, 필수 표 누락 거부, 선택 표 수신 실패·시간 초과, 추첨 2,000회, TMP 배선·6문구 크기 검증 통과. 결과는 `Temp/LoadingTipValidation.txt`에 기록한다.
- 원격 Google Sheet 반영·Firestore 발행은 수행하지 않았다. 서버 반영 전 실제 게임은 프리팹 기본 문구를 사용한다. `SpecData.bytes`와 기존 `SpecDatas.cs`·`SpecDataManager.cs`는 변경하지 않았다.
- 실제 전투 종료→로비 복귀, 시작·계정 변경 커버의 플레이 모드 검수는 남아 있다. 위 화면 동작 체크리스트는 이 검수 후 확정한다.
