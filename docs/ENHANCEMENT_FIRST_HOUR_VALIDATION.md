# 강화 개편 첫 1시간 수동 검증

2026-09-10. 범위: `CARD_ENHANCEMENT_REWORK.md` 완료 기준 4의 측정 준비. 기준 6의 패배·재도전 시나리오에도 같은 기록을 사용할 수 있다.

**실제 신규 계정 60분 플레이는 미측정. 기준 4는 완료되지 않았다.** Unity MCP 권한 철회 상태에서 Unity 실행·연결·플레이를 시도하지 않았다. 아래 테스트 결과는 합성 이벤트와 오프라인 컴파일 결과이며 사용자 플레이 증거가 아니다.

## 기록 시작과 종료

기록기는 `UNITY_EDITOR || DEVELOPMENT_BUILD`에서만 컴파일된다. 기본 비활성. 자동 시작·네트워크 전송·계정 ID 수집·세이브 수정은 없다.

실제 플레이가 허용된 환경에서 다음 순서로 진행한다.

1. 신규 QA 계정을 정상적인 계정 생성 경로로 준비한다. 기록기로 계정을 생성하거나 초기화하지 않는다. 빌드/커밋, 데이터 반영 버전, 플레이어 구분(기본·느린 진행·분산 투자), 기기, 시작 UTC를 별도 관찰 메모에 적는다. 계정 비밀정보는 적지 않는다.
2. Editor에서는 Play 진입 직후 `Tools > QA > Enhancement First Hour > Begin Local Report`를 누른다. 초기화 대기 중 시작해도 된다. 실제 첫 조작 전에 시작하고, 시작이 늦었으면 누락 시간을 메모하여 첫 접속 전체 측정으로 오인하지 않는다.
3. Development Build에서는 기존 SRDebugger의 `First-hour QA > Begin local report`를 누른다. `DISABLE_SRDEBUGGER` 빌드에는 이 버튼이 없다. 별도 자동 기동 옵션이나 단축키는 추가하지 않았다.
4. Console의 `[FirstHourQA] Local report:` 경로를 확인한다. 파일명과 플레이어 구분을 관찰 메모에 연결한다. 시작을 두 번 눌러도 새 기록을 중복 생성하지 않는다.
5. 정상 가이드·보상·강화·덱 편성으로 60분간 플레이한다. 테스트 치트, 무료 재화 추가, 시간 배속, 디버그 승리, 특정 랜덤 드롭 강제는 사용하지 않는다. 신규 계정 준비와 실제 보상 수령은 게임 본래 동작이며 기록기가 대신 실행하지 않는다.
6. 실제 패배 결과를 확인한 직후 `Mark Observed Defeat`를 한 번 누른다. 재도전 버튼을 누르기 직전에 `Mark Retry Attempt`를 한 번 누른다. Development Build도 같은 SRDebugger 항목을 쓴다. 정점 ID, 사유, 누른 실제 UTC를 관찰 메모에 적는다. 수동 표식 누락·중복이 있으면 원본 JSONL을 고치지 말고 메모에서 정정한다.
7. 60분 시점에 현재 덱, 조각 잔액, 가이드 진행, 실제 체감·막힌 지점을 메모한다. `End Local Report` / `End local report`로 종료한다. `Reveal Last Report`로 파일을 찾는다. 60분 자동 종료나 자동 합격 판정은 없다.

파일 위치: `Application.persistentDataPath/Diagnostics/EnhancementFirstHour/<UTC>-<random>.jsonl`. 이는 로컬 진단 파일이며 게임 세이브/오프라인 캐시가 아니다. 한 줄씩 기록하고 매번 flush한다. 기록기를 다시 시작하면 항상 새 파일을 만들며 이전 기록을 읽거나 게임 상태에 적용하지 않는다. 기존 파일 내용은 변경하지 않는다.

## 무엇을 측정하는가

모든 행에 UTC, 시작 후 단조 증가 시간 `elapsedSeconds`, 서버 채택 조각 잔액, 관측 수입·지출 누계, 상세·강화 체류 누계, 관측 전투 수, 수동 패배·재도전 수가 함께 들어간다.

| 기록 | 의미와 검토 방법 |
|---|---|
| `session_start` → `baseline_ready` | 시작 시각과 초기화 완료 시각. 초기 잔액은 수입으로 합산하지 않는다. 첫 접속 로딩 시간도 시작 이후 경과시간에 포함된다. |
| `baseline_three_star`, `baseline_synergy`, `baseline_two_synergies` | 첫 관측부터 이미 있던 진행. 새로운 첫 달성 시각으로 처리하지 않는다. 신규 60분 검증에 이 진행이 있다면 계정/시작 시점을 확인한다. |
| `deck_observed` | 저장된 유효 6장 덱의 카드 ID와 표시 성급. `cards:stars`는 `카드ID:성급` 목록이다. 미저장 드래그 미리보기는 제외한다. `baseline_deck`은 시작 상태다. |
| `synergy_composed` | 저장 덱에 실제로 해금된 카드만 넣어 기존 `SynergyResolver`로 판정한 활성 시너지. 서로 다른 ID의 최초 관측에 `ordinal=1,2,...`를 부여한다. 성장 이벤트만으로 활성화되어도 기록한다. 모든 저장 슬롯을 관찰하므로 항상 대표 덱이라는 뜻은 아니다. |
| `two_synergies_same_deck` | 한 덱에서 활성 시너지 두 종류가 동시에 성립한 최초 관측. 서로 다른 슬롯에 하나씩만 구성한 경우와 구별한다. |
| `synergy_battle_used` | `TurnEvents.TurnStarted`의 실제 로컬 소유자 필드에 적용된 활성 시너지. ID마다 최초 한 번 기록한다. 단순 덱 확정·대치·상세 미리보기로는 기록하지 않는다. 구성 ID와 대조해 같은 시너지를 실전에 가져갔는지 확인한다. |
| `battle_local_turn_observed` | 해당 필드에서 로컬 턴을 처음 관측한 시각. 모험 정점, 튜토리얼 여부, 멀티 여부를 남긴다. 총 입장 횟수나 승패 판정은 아니다. |
| `three_star_acquired` | 소유 카드가 표시 3성 이상으로 최초 관측된 시각. 같은 카드를 재통지해도 중복 집계하지 않는다. `ordinal=1,2`가 이번 구간의 첫·두 번째 서로 다른 카드다. `GrowthStar.FromLevel`을 사용하며 내부 레벨 3을 3성으로 오인하지 않는다. |
| `shard_balance_changed` | `CurrencyManager.GetServerBalance(Shard)` 변화. 낙관 표시 차감·취소 복원은 제외한다. 양수 순변동을 `observedShardIncome`, 음수 순변동을 `observedShardSpend`에 합산한다. |
| `detail_open`, `detail_closed` | 기존 `CardDetailOverlayView.IsOpen`을 0.1초 간격으로 관찰. 기존 닫힘 이벤트도 사용한다. |
| `enhancement_started`, `enhancement_result_ready`, `enhancement_settled` | 기존 UI 이벤트의 시각. 시작→결과 준비는 연출 구간, 결과 준비→정리는 결과판 대기 구간을 검토하는 근거다. 이 시작 이벤트 전에 발생한 서버 요청 대기는 강화 구간에 포함되지 않는다. |
| `enhancement_interrupted` | 상세 비활성화, 후속 강화 시작, 종료 등으로 완전한 정리 이벤트를 관측하지 못한 구간. 완료된 강화 연출과 분리한다. |
| `manual_marker` | `manual_defeat`, `manual_retry`를 관찰자가 명시 입력. 자동 판정값이 아니다. 공개 `Mark(string)` API로 QA 구간 설명도 남길 수 있다. 게임 동작은 실행하지 않는다. |
| `application_pause`, `application_focus`, `scene_loaded`, `session_end` | 중단·백그라운드·씬 전환·정상 종료의 검토 근거. `session_end`는 목표 달성 선언이 아니다. |

## 목표 판정표

신규 계정 기본 진행·느린 진행·분산 투자 진행을 각각 독립된 기록으로 검토한다. 파일명, 실제 측정값, 관찰 메모를 아래 표의 복사본에 채운다. 현재 표는 모두 미측정이다.

| 항목 | 목표/검토 기준 | 실제 결과 |
|---|---|---|
| 첫 시너지 구성 | 약 20분. `synergy_composed ordinal=1`과 카드·덱 확인 | 미측정 |
| 첫 시너지 실제 사용 | 같은 ID의 `synergy_battle_used` 시각을 별도 기입 | 미측정 |
| 두 번째 시너지 구성·실전 | 약 30분. `ordinal=2`와 `two_synergies_same_deck`, 실제 사용 ID를 함께 확인 | 미측정 |
| 첫 3성 | 약 40분. `three_star_acquired ordinal=1` | 미측정 |
| 두 번째 3성 | 약 55~60분. `three_star_acquired ordinal=2` | 미측정 |
| 각 달성 때 조각 | 해당 행의 관측 수입·소비·잔액, 초기 잔액을 함께 기입 | 미측정 |
| 60분 덱·여유 조각 | 권장 3성 2장 + 2성 4장. 마지막 덱 기록과 실제 선택 덱 화면 대조 | 미측정 |
| 반복 강화·상세 체류 | 전체/강화 누계와 개별 시작·결과·종료 차이 검토 | 미측정 |
| 패배·재도전·이탈 | 수동 표식 수와 관찰 메모 대조, 진행 막힘 여부 확인 | 미측정 |

목표 시간은 플레이 테스트 목표다. 도달하지 못한 기록을 결측으로 지우거나 자동 합격 처리하지 않는다. 시너지 구성만으로 실전 사용을 대체하지 않는다.

종료한 JSONL을 PowerShell에서 읽는 예:

```powershell
$qaReport = '실제 종료한 JSONL 절대 경로'
$qaRows = Get-Content -LiteralPath $qaReport -Encoding utf8 | ForEach-Object { $_ | ConvertFrom-Json }
$qaRows | Where-Object { $_.kind -in @('synergy_composed', 'two_synergies_same_deck', 'synergy_battle_used', 'three_star_acquired') } |
    Select-Object kind, detail, elapsedSeconds, observedShardIncome, observedShardSpend, shardBalance
$qaRows | Select-Object -Last 1
```

## 패배·재접속·투자 변경 보조 시나리오

기준 6의 전체 완료를 이 도구만으로 판정하지 않는다. 기본 신규 60분 측정과 별도 기록에서 다음을 확인한다.

- 첫 시너지 구성 후 실제 전투에서 패배 → 패배 표식 → 같은 정점 재도전 직전 재도전 표식 → 정상 입장과 가이드 재진행을 확인한다. 승패 팝업 디버그 미리보기는 증거로 쓰지 않는다.
- 강화 상세가 열린 상태에서 닫기, 연속 강화, 다른 카드로 이동 후 강화를 진행한다. UI 누계·결과 대기 구간과 실제 조작 불편을 함께 메모한다.
- 추천 외 카드에 분산 투자, 덱 교체, 목표를 미리 달성한 경우의 가이드 복귀를 정상 조작으로 확인한다. 기록기는 가이드의 영구 막힘 여부를 자동 판정하지 않는다.
- 재접속 직전 기록 종료, 앱 재시작, 기존 계정 정상 로그인 후 새 기록 시작. 두 파일을 관찰 메모로 연결한다. 오프라인 경과시간, 시작 전 누락, 기존 3성·시너지의 baseline 여부를 별도로 적는다. 두 구간의 `elapsedSeconds`나 달성 순번을 그대로 합쳐 첫 달성 시간을 만들지 않는다.
- 비정상 종료 시 남은 완전한 JSONL 행까지만 사용한다. 마지막 행이 잘렸으면 원본을 보존하고 분석 사본에서 제외한다. `session_end`가 없으면 종료 시각·마지막 미완료 체류 구간을 확정하지 않는다.

## 측정 한계와 메인 훅

- 필수 메인 수정 훅: **없음**. 기존 core/UI 파일 수정 없이 동작한다. `CurrentCard` 접근자는 요구하지 않는다. 개별 카드 상세 체류시간·읽기 전용 상세 여부·실제 클릭 시각은 구분하지 못한다.
- 승패 결과의 공용 읽기 이벤트가 없어 패배·재도전은 수동이다. 자동화가 추가로 필요하면 메인 담당자가 결과 확정 지점의 읽기 전용 이벤트를 제공해야 한다. 랭크 증감이나 결과 팝업 활성화로 패배를 추측하지 않았다.
- 실전 사용은 적용된 시너지를 가진 **로컬 턴 도달**로 정의한다. 개별 시너지 발동 횟수/효과량, 실제 공격 성공, 3성의 체감 효율은 측정하지 않는다. 로컬 턴 전 패배·항복은 자동 전투 관측에서 빠질 수 있다. 중간 시작은 그다음 로컬 턴부터 관측한다.
- 구성 시각은 저장 덱/성장 통지 시점이다. 사용자가 직접 드래그했는지, 가이드가 자동 편성했는지는 이벤트만으로 구분하지 못하므로 관찰 메모에서 확인한다. 튜토리얼 고정 덱 사용도 태그를 보고 검토한다.
- 조각은 서버가 채택한 잔액 **순변동의 합**이다. 한 응답 안에서 수입·소비가 상쇄되면 개별 총액과 지급 출처를 복원할 수 없다. 시작 전/오프라인 중 거래도 측정하지 못한다. 지갑 교체·관리자 지급이 있는 기록은 별도 표시한다. 기준 2의 지급 경로 감사나 서버 거래 원장 대체물이 아니다.
- `detailSeconds`에는 강화 체류가 겹친다. 두 값을 더하지 않는다. 상세의 서버 대기 시간은 포함되지만 강화 시작 이벤트 전 대기는 `enhancementSeconds`에 없다. 0.1초보다 짧은 창, 프레임 정지·백그라운드에는 더 큰 관측 오차가 있을 수 있다.
- 경과·체류시간은 실제 단조 시계 기준이며 배속과 무관하다. 백그라운드·에디터 일시정지 시간을 자동 제외하지 않는다. focus/pause 기록과 관찰 메모를 함께 보고 능동 조작시간과 구분한다.
- 씬 로드 뒤 `TurnEvents`를 재구독한다. 씬 로드 없이 다른 코드가 이벤트를 초기화하는 특수 경로는 별도 확인이 필요하다.
- 재시작 이어쓰기/자동 복원은 의도적으로 없다. 새 구간은 새 기준선으로 시작한다. `Reveal Last Report`의 메모리 경로는 도메인 리로드 후 사라질 수 있지만 진단 디렉터리의 파일은 남는다.
- 일반 파일 I/O 실패는 기록을 중단하고 경고를 남긴다. 게임 이벤트 호출자에게 오류를 전파하거나 서버 작업을 재시도하지 않는다. 디스크 flush는 OS 수준 강제 동기화 보장이 아니다.

## 오프라인 검증 하네스

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/EnhancementFirstHourTests/run.ps1
```

Unity 설치의 Roslyn/Mono 실행 파일과 기존 `Library/ScriptAssemblies`를 읽는다. Unity Editor·MCP·임포터를 실행하지 않는다. 출력과 합성 JSONL은 시스템 임시 디렉터리에만 생성한다.

합성 환경에서 실제 기록기 소스와 실제 `SynergyResolver`/`SynergyState`/`SynergyRuntime`을 컴파일한다. 게임 매니저·이벤트·Unity 수명·직렬화는 작은 대역이다. 별도 단계에서 설치된 Unity와 기존 게임 DLL의 실제 API에 대해 메뉴/기록기를 컴파일하고, 릴리스 조건에는 QA 타입이 없는지도 검사한다. 기존 DLL이 오래됐으면 API 검사가 실패할 수 있으며, 그것을 이유로 Unity를 자동 실행하지 않는다.

검증 범위: 기본 비활성, 이중 시작/종료, 초기화 기준선, 낙관 차감/취소 제외, 실제 잔액 순변동, 시너지 해금·인원수·중복 억제, 구성과 실전 분리, 로컬 소유자 필터, 씬 전환 재구독, 서로 다른 3성 카드, 상세/연출 체류, 수동 표식, 종료 후 구독 해제, 재시작 새 구간·기존 달성 제외, 파일 I/O 실패 격리.

2026-09-10 실행 결과: 합성 검증 27개 assertion 통과. 기존 Unity/게임 API 컴파일 및 릴리스 제외 검사 통과. 테스트 중 `Can not write to a closed TextWriter` 경고는 의도적으로 주입한 I/O 실패 격리 검사다. 실제 Unity 플레이·메뉴 클릭·기기 UI 체류·신규 계정 60분 검증은 미실행이다.
