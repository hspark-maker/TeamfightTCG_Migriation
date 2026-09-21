# Functions 효율 개선 7~10

## 인덱스와 조회

`firestore.indexes.json`은 문서 ID로만 조회하는 다음 필드의 자동 인덱스를 제외한다.
`receipts.result/before/after/changes`, `wallet.balances/paidBalances`,
`missions.progress/claimed`. 랭킹 `seasonId/points`, 지급 목록 `status`, 영수증 메타데이터는 유지한다.
문서 읽기·쓰기 횟수를 줄이는 변경은 아니며, 인덱스 저장량과 쓰기 부하를 줄인다.
저장소 밖 운영 도구에서 이 필드로 검색한다면 배포 전에 해당 쿼리를 조정해야 한다.

지갑·영수증을 함께 조회한다. 자격 검사는 영수증 재생을 판정한 뒤에만 조회하므로
재생 요청에 추가 자격 문서 읽기가 생기지 않는다. 정상 조회 단계는 2→1,
자격 검사가 있는 성공 요청은 3→2다. 지갑 누락 실패는 영수증 선조회로 1읽기 증가한다.
`claimPayout`은 지급 문서·지갑·영수증을 한 번에 읽어 2→1단계로 줄인다.
정상 문서 읽기 수와 지급·영수증의 원자성은 유지한다.

## 가이드 미션

`openPack`, `enhanceCard`, `limitBreakCard`, `claimReward`는 갱신할 슬롯을 확정한 뒤
`applyGuideProgress`로 기존 미션 상태에 가이드를 합쳐 한 번만 저장한다.
일일·주간 이벤트와 가이드 최고 달성값·수령 낙인을 보존한다.
카드 스펙을 못 읽으면 병합을 생략하고 기존 세이브 트리거가 보완한다.
팩은 이미 읽은 Card 표를 사용한다. 강화·한계돌파·보상은 캐시 미스 시 Card 조회가 추가되며
기존 스펙들과 병렬로 요청한다.

`syncGuideProgress`는 직접 덱 저장, 다른 명령, 스펙 실패 시의 보완 경로로 유지한다.
같은 정의와 세이브 상태의 가이드가 이미 반영된 경우 후속 미션 읽기 1회는 유지하되
추가 쓰기는 없다. 미달성 카운터의 생략된 키와 0은 같은 값으로 비교한다.
트리거 실패 재시도를 활성화했으며, 중복·역순 배달도 최고 달성값을 유지한다.
세이브 필드나 보안 규칙에 처리 표시를 추가하지 않는다.

## 측정

`request_cost`는 다음 callable의 handler 진입부터 반환·예외까지 한 번 기록한다.
`openPack`, `enhanceCard`, `limitBreakCard`, `claimReward`, `submitMatchResult`,
`getRankSnapshot`, `getRankLeaderboard`, `findAiMatch`, `claimPayout`, `claimBattleReward`.
요청 데이터·UID·응답 내용은 이 로그에 기록하지 않는다.

- `durationMs`: handler 전체 경과시간. 플랫폼 cold start, 인증 처리, 응답 직렬화와 클라이언트 네트워크 제외.
- `succeeded`: 정상 반환은 1, 예외는 0. 정상 반환한 pending/flagged도 1이므로 전투 성공률이 아니다.
- `counts.txAttempts/txReadCalls/txReadDocuments`: 랭크 확보를 포함한 계측 트랜잭션의 시도·조회 호출·관측 문서 수.
- `counts.txQueuedWrites/txCommittedWrites`: 중단된 시도를 포함한 큐잉 쓰기와 성공한 최종 시도의 쓰기를 구분.
- `counts.specIndexLoads/specBlobLoads`: 실제 조회를 시작한 횟수. `Reads`는 성공적으로 반환된 문서 수.
  `CacheHits`와 `SharedLoads`는 완료 캐시 적중과 진행 중인 조회 공유를 구분한다.
  공유 조회의 실제 I/O는 처음 시작한 요청에만 귀속한다.
- `counts.replayAttempts/replayHttpCalls/replayTransportFailures`: 재생 재시도와 HTTP 호출을 구분.
- `counts.leaderboardTopDocuments/leaderboardProfileReads/leaderboardCountCalls`:
  순위 목록 문서·레거시 프로필 조회·집계 쿼리 호출을 구분. count 호출 수는 읽기 과금 수가 아니다.
- `counts.payoutListDocuments`: 트랜잭션 밖 ready 지급 목록에서 반환된 문서 수.
- `phasesMs`: 스펙 I/O·공유 대기, 트랜잭션·읽기, 재생 전체·인증·HTTP 본문 수신,
  랭킹·지급 목록 조회 시간. 병렬·중첩 구간이 있으므로 합계가 `durationMs`와 같지 않다.

이 값은 코드가 관측한 연산이며 청구량 추정치가 아니다. SDK 내부 재시도, 실패 RPC의 서버 처리,
인덱스 엔트리 과금 등은 포함하지 않는다. 다른 callable과 별도 백그라운드 트리거의 비용도 별도다.
기존 `tx_cost` 로그는 유지한다. 배포 전후 동일 command·트래픽 조건에서 `durationMs` p50/p95,
문서 수, 재시도, 캐시 적중과 공유 횟수를 비교한다. 실제 운영 효과는 배포 후 확인한다.

## 추가 개선 (2026-09-21)

로비에서 세이브 동기화 이후 최신 미션·출석 상태를 읽는 흐름, 가이드 보완 트리거,
서버 전투 재생과 영수증 중복 지급 방지는 유지한다.

- `getMissions`: 미달성 카운터의 missing/0을 같은 값으로 비교한다. 진행 변화가 없는 조회의
  미션 문서 쓰기 1회를 제거하며, 실제 진척과 최고 달성값은 계속 저장한다.
- `mutateSave`: 팩을 줄 가능성이 있는 명령이라는 이유만으로 통계·업적 문서를 읽지 않는다.
  7개 팩 생산자가 실제 팩 수를 확정한 뒤 `preparePackStatistics(count)`를 호출한다.
  이 호출은 **모든 트랜잭션 쓰기보다 앞**이어야 한다. 팩 0개는 추가 읽기 0회이며,
  팩이 있으면 기존과 같이 통계·업적 2문서를 읽고 원자적으로 저장한다.
  준비한 수와 응답의 팩 수가 다르면 지급 전체를 취소해 집계 누락을 막는다.
  영수증 재생은 준비 콜백에 진입하지 않고, 준비 상태는 재시도마다 초기화한다.
- `commitPassExp`: 동일 시즌·현재 스키마에서 실제 상태가 같으면 패스 쓰기 1회를 생략한다.
  신규 문서, 시즌 초기화, 스키마 변경과 경험치 증가는 저장한다. 최신 응답용 조회는 유지한다.
- `spinRoulette`: 트랜잭션 안에서 추첨한 뒤 팩 당첨일 때만 랭크를 읽는다.
  재화 당첨당 랭크 읽기 1회가 줄며, 저장소 CSV의 100회 주기에서는 97회에 해당한다.
  티켓·당첨 주기·소유·미션·통계는 같은 트랜잭션에서 확정한다.
- `submitMatchResult`: 최초 매치 조회에서 참가자·서버 시드를 검사한 뒤 confirmed/flagged는
  저장된 결과를 즉시 반환한다. 완료 재조회는 매치 1읽기이며 스펙·설정·정산 트랜잭션·재생이 없다.
  현재 Card 전체 조회·파싱은 구 solo AI 복원에만 남기고, 새 전투는 봉인된 스냅샷을 사용한다.
  시너지용 고정 Card/SynergyTierDef와 pending 정산의 검증은 유지한다.

### 검증

로컬 구현과 별도로 현재 운영 소스를 기준으로 만든 배포본에도 같은 검사를 실행했다.
`test-mission-query-efficiency-emulator.js` 4개, `test-pack-statistics-efficiency-emulator.js` 4개,
`test-result-efficiency-emulator.js` 9개, 기존 `test-achievements-emulator.js` 17개가 통과했다.
`test-pass-write-efficiency.js`의 8개 시나리오와 `test-roulette-cycle-command.js`의 9개 시나리오도 통과했다.
실제 Firestore 검사는 localhost 에뮬레이터와 demo 프로젝트만 사용하며 외부 재생·스펙은 테스트 대역이다.
배포본 전체 lint·TypeScript 빌드·테이블 세대 일치 검사가 통과했다.

작업 저장소에서는 통계·업적·계정 경험치 순수 검사 25개, 팩 통계·계정 경험치·제작 에뮬레이터
검사 23개와 온보딩 영구 영수증 회귀도 통과했다. 별도로 실행한 기존
`test-guide-mission-growth.js`는 CSV 보상의 Shard 5와 테스트 기대값 10이 달라 실패한다.
이 기대값·스펙 CSV는 이번 효율 개선에서 변경하지 않았다.

### 배포

2026-09-21 `bm-cardbattle` / `asia-northeast3`에 `getMissions`, `submitMatchResult`,
`openPack`, `claimAttendance`, `claimMission`, `claimReward`, `claimPassReward`,
`claimBattleExperience`, `spinRoulette` 9개를 배포했다. 기존 운영 소스에서 이번 효율 변경만
반영했고, 제작·재화·별도 성장 변경은 포함하지 않았다. 테이블·리소스 업로드와 앱 빌드는 없다.

9개 ACTIVE 상태와 소스 세대 변경, 조회·정산·룰렛 주소의 미인증 요청 거절을 확인했다.
실제 계정 플레이에 따른 비용 감소율은 배포 후 운영 로그로 비교해야 한다.
증거: `Build/FunctionsEfficiencyDeployment-20260921-185847/verification.json`,
`source-changes.json`, `test-results.log`, `roulette-tests.log`.
