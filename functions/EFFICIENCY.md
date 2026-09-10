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
