# 보상 CSV 반영 상태

2026-09-09. `docs/REWARD_DISTRIBUTION_PLAN.md`의 수치를 기존 CSV 스키마 안에서 저작했다. **CSV 저작과 로컬 blob 생성은 실제 지급 기능의 완료를 뜻하지 않는다.** 서버 업로드·배포는 하지 않는다.

## 변경 대상

- `Reward_sheet.csv`: 모험·랭크·일일/주간·가이드·패스·카드 중복 보상. 도감과 전투 정산 계수는 보존했다.
- `AdventureReward_sheet.csv`: 기존 호환용 재화 표를 통합 Reward의 모험 재화 값과 일치시켰다. blob에는 원래 이 표가 없으며 공식 임포터도 새 표로 추가하지 않는다. 팩을 표현하려고 기존 재화 스키마를 변경하지 않았다.
- `Mission_sheet.csv`: 일일 6개, 주간 5개, 가이드 13개 정의. 퇴역 미션의 기존 ID·키는 남기고 비활성화했다. 가이드는 기존 문자열 필드에 정의했으며 새 CS 필드는 만들지 않았다.
- `CardEnhance_sheet.csv`: 10/20/150. 상한까지 명시 행이 모두 존재하므로 기존 CardEnhanceRule의 비상 폴백은 변경하지 않았다.
- `PassLevel_sheet.csv`: 시즌 S1의 누적 경험치 300~3,000. 시즌 기간·ID는 보존했다.
- `RouletteSlot_sheet.csv`: 8칸, 가중치 합계 100, 팩 2칸 포함.

같은 소유자·종류·지급물인 Reward 행은 기존 ID를 유지한다. 제거한 보상 ID는 다른 보상에 재사용하지 않는다. 새 행은 기존 최대 ID 뒤에 추가한다. 모든 표의 필드명·타입 줄과 자동 생성 CS는 변경하지 않는다.

## 현재 코드와 연결되지 않은 정의

| CSV 정의 | 현재 상태 | 필요한 작업 |
|---|---|---|
| Reward의 `Pack` | 현재 서버·클라 보상 해석기는 Currency만 처리하여 팩 행을 제외 | 미개봉 팩 소유·수령·1회 소비 구현 |
| Reward의 `PackChoice / UnlockedThemePack` | 예약한 선택 보상 키. 실제 팩 ID가 아님 | 해금된 테마팩 목록과 선택 지급 구현 |
| Reward의 `Card / 9` | 버섯냥 보상 저작. 현 일반 보상 지급기는 미지원 | 기존 튜토리얼 카드 지급과 일회성 가이드 지급 연결 |
| Reward의 `Guide / guide.01~guide.13` | 수령 명령·가이드 진행 시스템 없음 | 계정당 1회 순차 수령과 소급 달성 기록 |
| Mission의 `period=guide`, `Guide.*` 조건 | 기존 서버 미션은 일일·주간 catalog만 사용 | 가이드 전용 서버 상태 판정 연결 |
| Reward의 `CardDuplicate / Common,Rare,Arcane,Mythic` | 등급별 추가 조각 저작 위치. 현재 팩 중복은 간식만 지급 | 해당 행 읽기·조각 지급·0원 튜토리얼팩 제외 |
| Mission의 새 조건·활성 여부·EXP | 서버가 CSV 대신 기존 catalog 사본을 사용 | Mission 표 소비로 전환하거나 공식 생성 경로로 동기화 |
| `WinBattle`와 모험의 전투/키워드/시너지 집계 | 현 이벤트에 일반 승리 키와 모험 연결이 없음 | 승패 검증 결과에서 해당 미션 집계 |
| RouletteSlot의 `Pack` 2칸 | 현 추첨기는 두 행을 버린다 | 팩 지급 지원 전에는 설계한 확률 100%가 런타임과 다름 |

특히 현재 코드에 이 blob을 서버 업로드하면 팩 보상이 빠지거나 팩만 주는 미션을 수령하지 못할 수 있다. 룰렛도 팩 칸을 제외한 가중치로 돌아간다. **이 상태는 후속 지급 구현을 위한 테이블 저작본이며, 콘텐츠 배포 준비 완료 상태가 아니다.**

## 가이드 조건 계약

`guide.01~guide.13`의 순서는 sortOrder 1~13이다. 보상 ownerType은 `Guide`, ownerId는 같은 missionId다. passExp는 0이다. period `guide`는 계정당 1회·만료 없음이라는 예정 의미이며 기존 일일·주간 기간 코드에 그대로 넘기지 않는다.

`Guide.CaretakerCardsAtStar1`은 서로 다른 돌보미 3장의 1성 이상 보유, `Guide.CaretakerDeckAtStar2`는 같은 덱에 돌보미 3장 2성 이상 편성, `Guide.CaretakerTraceDeck`은 돌보미 3장과 깜밤이·버섯냥 2성 이상을 함께 편성한 두 시너지 조합이다. `Guide.DeckCardsAtStar2/3`은 현재 덱의 해당 성급 이상 카드 수다. 정점·장 키는 정의된 AdventureNode/Chapter 조건의 숫자와 대응한다. 기존 달성은 소급 인정하고 확정된 가이드 완료를 덱 변경으로 취소하지 않는다.

## 검증 범위

- CSV 열 수·타입·행 ID·소유자별 보상 순서 중복, 카드/팩 참조를 검사한다.
- 가이드 300 + 모험 1장 240 = 540과 각 강화 단계의 선지급 잔액을 검산한다.
- 일일/주간/패스/랭크/모험 합계, 일일·주간 경험치 100/160, 룰렛 확률 합계 100을 대조한다.
- 공식 `SpecLocalCsvImporter.Import`로 bytes를 생성하고 생성된 SpecDataManager에서 다시 읽어 원본과 대조한다.
- Unity 콘솔 컴파일 오류 확인은 데이터 파싱 검증과 별개로 수행한다. 지급 미지원 항목을 컴파일 성공으로 완료 처리하지 않는다.

## 검증 결과

- CSV 6개 검증 통과: Reward 141행, Mission 30행, AdventureReward 44행, RouletteSlot 8행, CardEnhance 3행, PassLevel 10행.
- 공식 임포터가 기존 22개 표를 재생성했다. 변경된 런타임 표 5개를 생성된 SpecDataManager로 읽고 **1,398개 필드 값이 CSV와 일치**함을 확인했다.
- 모험·가이드 기본 조각 540과 소비 480, 룰렛 가중치 100, 행 ID·보상 순서·팩 및 카드 참조·경험치 합계 검증 통과.
- 독립 검수에서 추가 지적 없음. 공식 임포트 이후 Unity_ReadConsole 오류 조회 0건.
- 위 결과는 저작·파싱·컴파일 확인이며, 표에 표시한 지급 미지원 기능의 실행 검증은 아니다.
