# 강화 가이드 서버 검증

최종 통합 반영: 메인 작업에서 전체 npm 테스트 37개·ESLint 통과 후 `reportAdventureWin`을 단독 배포했다. 2026-09-10 20:03 KST, `bm-cardbattle / asia-northeast3`, revision `reportadventurewin-00013-luh`, `ACTIVE` 확인. 아래 담당 검증의 “배포하지 않음”은 통합 배포 이전 시점의 기록이다. 실계정 지급·SDK 왕복은 여전히 미검증이다.

2026-09-10. `CARD_ENHANCEMENT_REWORK.md` 6절 기준 **1·2·6의 서버 범위**를 검증했다. 모험 승리가 일일·주간 미션에 집계되지 않는 결함을 재현하고 `functions/src/commands/reportAdventureWin.ts`를 수정했다. 추가 파일은 이 보고서와 `functions/scripts/test-enhancement-guide.js`다. 실제 플레이·배포는 하지 않았고 CSV·보상 금액은 변경하지 않았다.

## 검증 방식과 결과

현재 `docs/SpecData/*_sheet.csv`를 타입 행에 맞춰 읽고 실제 `grantTutorialCards`, `openPack`, `enhanceCard`, `getMissions`, `claimMission`, `reportAdventureWin`, `claimReward`, `syncGuideProgress`, `submitMatchResult`를 실행한다. 성장·보상 판정, 팩 지급, 미션 집계, 지갑 계산, `mutateSave`, 영수증 코덱은 실제 모듈이다. Firestore I/O는 메모리 트랜잭션, Cloud Run 재생 결과는 제어된 패배/통신 장애로 대체했다. 모든 트랜잭션에서 읽기가 쓰기보다 먼저인지 검사하며 커밋 취소·콜백 재실행도 주입한다.

| 항목 | 확인한 증거 |
|---|---|
| 기준 1: 서버 규칙 | 현재 CSV 비용 10/20/150, 조각 결제, 카드·키워드 강화 성공률 1000/1000. 모든 카드의 내부 Lv1~4 스냅샷에서 Lv3=2성 시너지, 카드별 `keywordUnlockLevel`, Lv3/Lv4 진화 단계 대조 |
| 확정 카드 | 실제 `TutorialStarterGrant`·`TutorialFirstCard` 지급과 `KeywordDeck` 구매. 키워드 팩은 비복원 6장/풀 6장이라 추첨 순서 양 극단 모두 돌보미 1·2·3·4와 깜밤이 8 확보. 무료 팩 중복 조각 0 |
| 기준 2: 확정 예산 | 실제 순차 보상 수령으로 가이드 300 + 모험 정점별 30/30/30/60/60/30 = 540. 첫 시간 성장 전에 일반 랜덤 팩·중복·일일·주간·랭크·패스 보상 불필요 |
| 권장 경로 | 무료 강화 제외 480 소비·60 잔여. 깜밤이 무료 강화 포함 470 소비·70 잔여. 돌보미 1/2/3 + 에이스 1/8, 돌보미 2/3/4 + 에이스 4/9 모두 13단계 완주 |
| 고정 카드 9 | `guide.05`가 조각 30과 버섯냥 1장을 소유 슬롯·지갑·미션 낙인과 함께 지급. 커밋 취소·재시도·응답 유실 재요청에 중복 지급 없음. 기존 소유 계정은 현재 `CardDuplicate` 보상을 별도 추가 지급하고 기존 강화 보존 |
| 소급 달성 | 잠긴 단계도 저장된 성장·덱·클리어로 인정. 이미 모든 목표를 끝낸 계정이 최초 조회 후 덱을 바꿔도 13단계 순차 수령 가능. 강화 3회/중복 소유 항목을 서로 다른 카드 3장으로 오인하지 않음 |
| 기준 6: 재접속·이탈 | 무료·유료 강화와 고정 카드 수령의 동일 영수증 재생은 쓰기 0회. 오래된 revision 영수증은 추가 차감 없이 거절. 카드 표 장애 중 강화 후 조회로 진행도 복구. 지연·중복 트리거, 덱 변경, 35일 경과 후에도 가이드 달성·수령 유지 |
| 선행 모험 | 추천 덱과 무관하게 6정점 먼저 클리어 가능. 이후 가이드가 클리어를 소급 인정하며 재전투 불필요. 패배는 승리 신고를 하지 않는 계약이고 정점 재도전 자격 유지 |
| 미수령 모험 복구 | 승리 신고 응답 재생, AlreadyPending/RewardPending, 보상 표 통신 장애 후 재수령 검사. pending 낙인을 보존하고 복구 후 지급·클리어를 한 번만 확정 |
| 수정: 모험 승리 집계 | `reportAdventureWin` 최초 승인과 같은 트랜잭션에서 일일·주간 `CompleteBattle`/`WinBattle` +1. 현재 미션 정의명과 대조. 모험 3승으로 기존 `daily.completeBattle3`의 조각 40 실제 수령, 일반 AI 패배와 같은 카운터에 합산 |
| 수정 회귀: 중복 방지 | 수정 전 최초 승인 응답의 완료 카운터가 없어 실패함을 재현. 수정 후 커밋 취소·콜백 재실행·동일/새/없는 txId·다른 pending 정점·클리어 정점 재신고 검사. 보상 수령은 다시 집계하지 않으며 다음 날 수령해도 완료 날짜를 바꾸지 않음 |
| 운영 호환: Mission 선택 의존성 | Mission 표 누락(live 환경 요청)·조회 오류(test 환경 요청) 모두 모험 승인과 `CompleteBattle`/`WinBattle` 카운터 원자 저장 성공. 정의가 필요한 `missions` 응답은 생략해 파생 완료 수를 허위 0으로 채택시키지 않음. 경고 로그를 남기며, 표 복구 후 `getMissions`가 보존한 카운터로 정상 계산. 카탈로그 장애 중 커밋 취소/재시도·중복 신고도 검증 |
| 다른 투자 복구 | 다른 카드에 10 + 180 = 190조각 추가 투자, `NotAffordable` 재현. 실제 일반 AI전 `submitMatchResult`로 패배 15판 정산 → 일일 전투 3회 보상 40조각을 5일간 수령 → 환급 없이 13단계 완주. 승리·시너지 발동·랜덤 보상 없이 복구 |
| 패배 집계 멱등 | 재생 서비스 장애는 pending으로 유지하며 미션 집계 없음. 복구 후 동일 제출은 한 번만 집계. 확정 매치 재제출·동일 일일 미션 재수령으로 추가 지급 없음 |

`guide.12` 일반 팩은 두 번째 3성 달성 **이후** 실제 지급한다. 이때 생기는 중복 조각은 위 540과 잔여 60/70 계산에서 분리했다. 성장 상한이나 다른 투자 금지는 추가하지 않았다.

## 메인 클라이언트 작업 전달

다음 비용 안내의 권장 경로 기준값은 아래와 같다. 실제 표시는 이미 달성한 성장·소유·편성 상태와 무료 강화 사용 여부를 반영해야 한다. 가이드 조건을 새로 강화할 필요는 없다.

| 단계 | 목표까지 비용 | 조건 |
|---|---:|---|
| guide.03 | 최대 30 | 서로 다른 돌보미 3장 0→1성 |
| guide.04 | 60 | 위 3장 1→2성 |
| guide.06 | 최대 60, 깜밤이 무료 강화 완료 시 50 | 깜밤이 8·버섯냥 9를 2성으로 올리고 돌보미 3장과 같은 덱 |
| guide.08 | 150 | 현재 덱의 기존 2성 중 첫 3성 선택 |
| guide.10 | 최대 30 | 마지막 한 자리 0→2성 |
| guide.12 | 150 | 현재 덱의 다른 2성 카드 두 번째 3성 |

**승리 연결은 수정 완료, 모험 패배 집계는 후속 필요.** `BattleOutcome.TryCapture`는 모험 승리만 `reportAdventureWin`에 신고한다. `LobbyMatchLauncher`는 모험의 서버 매치 생성/잠금을 건너뛰고, 별도 모험 패배 보고 API도 없다. `claimBattleReward`는 이미 잠긴 일반 AI 매치 전용이므로 재사용하지 않았다. 신규 패배 증거 프로토콜·재시뮬 편입은 이번 범위에서 제외했다. 반복 조각 복구 안내는 **일반 AI전 패배 3회 → 일일 40조각**으로 연결해야 한다. 기존 `ServerSaveCommands`가 공통 `missions` 봉투를 채택하므로 승리 집계 응답용 신규 클라이언트 계약은 필요 없다.

모험의 `WinBattle` 집계는 기존 정점 승리 신고를 서버가 최초 승인했다는 근거이며 전투 재생으로 승리를 증명한 것은 아니다. 정점별 pending/cleared 단조 상태로 1회만 집계하고, 파괴 수·시너지·키워드·랭크전 승리는 추정하지 않는다. 배포 전 이미 pending/cleared인 정점은 완료 시각 증거가 없어 오늘의 일일·주간 진행도로 소급 가산하지 않는다. 가이드 소급 인정은 기존대로 유지한다.

## 실행한 검증

작업 디렉터리 `functions/`. 최종 수정 후 **`npm test` 전체와 `npm run lint` 모두 종료 코드 0**. `npm test`에 메인이 연결한 신규 통합 9개 시나리오도 포함된다. TypeScript 빌드·콘텐츠 세대 검사도 전체 테스트에서 통과했다. 기존 battle golden 코퍼스는 26개 중 검증 가능한 25개 통과, 구 스키마 1개(boardOrder 없음)는 기존 규칙대로 제외됐다.

```powershell
npm.cmd test
npm.cmd run lint
```

작업 중 수행한 별도 집중 검증:

```powershell
./node_modules/.bin/tsc.cmd
./node_modules/.bin/eslint.cmd src/commands/reportAdventureWin.ts
node --check scripts/test-enhancement-guide.js
node scripts/test-enhancement-guide.js
node scripts/test-guide-mutations.js
node scripts/test-missions.js
node scripts/test-mission-spec.js
node scripts/test-reward-items.js
node scripts/test-claim-mission-pass-exp.js
node scripts/test-enhance.js
node scripts/test-deck-validation.js
node scripts/test-growth.js
node scripts/test-tutorial-grant.js
node scripts/test-batched-save.js
node scripts/test-batched-wallet.js
node scripts/test-batched-commands.js
node scripts/test-claim-reward.js
node scripts/test-tournament-progress.js
```

기존 `test-enhance.js`의 25/75/150 고정 fixture는 일반 수식 회귀로 유지했다. 현재 저작값 10/20/150의 연결 검증은 신규 CSV 기반 테스트가 담당한다. 이 담당 작업에서 `functions/package.json`은 수정하지 않았다. npm 테스트 스크립트 연결과 최종 문서 통합은 메인 담당 범위다.

## 남은 실기·운영 확인

- 기준 1의 실제 화면 표시, 기준 3의 후보/비용/강화 후 잔액, 기준 4·5의 첫 시간 시각·전투 체감은 이 서버 검증의 완료 대상이 아니다.
- 실제 신규 계정에서 첫/두 번째 시너지 사용 시각, 첫/두 번째 3성 시각, 수급·소비·잔액, 재도전 횟수, 강화 화면 체류 시간 측정 필요. 느린 플레이·분산 투자도 따로 측정한다.
- Firebase 에뮬레이터·실서버·실제 SDK 재접속·동시 기기 경합·Cloud Run 재시뮬레이션은 실행하지 않았다. 실패/재시도 테스트는 메모리 트랜잭션 모델과 제어된 외부 재생 응답의 증거다. 일반 AI 매치 생성·덱 잠금은 이미 완료된 문서를 fixture로 제공했다.
- 모험 승리의 서버 신고 전에 앱이 종료되는 경우, 오래된 영수증 거절 뒤 클라이언트가 서버 상태를 다시 채택하는 경우, 가이드에서 일반 AI전으로 이동 가능한지는 실제 플레이 확인 필요.
- 반복 일반전 정산은 활성 랭크 시즌(`PassSeason`)이 필요하다. 현재 CSV S1 기간 안에서 5일 반복을 검증했다. 가이드 자체의 35일 보존과 별개로 미래 시즌 발행까지 보장하지 않는다.
- 메인에서 테스트 환경 관련 7개 표 5.20과 CSV 일치, 운영 live 4.3의 `_index`에 Mission 표가 없음을 확인했다고 전달받았다. 따라서 Mission 카탈로그를 필수 의존성으로 두지 않는다. 본 담당 작업은 발행 상태를 별도 조회하거나 이 서버 코드 변경을 배포하지 않았다. `SpecData.bytes`·생성 CS 수정/재생성, 임포터, 배포, 커밋은 하지 않았다. 다른 에이전트의 변경은 보존했다.
