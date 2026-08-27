# 서버 검증 전환 로드맵

> 최종 갱신: 2026-08-27 · 브랜치 `feature_Firestore`
> 이 문서는 **진행 상태 추적**용이다. 설계 근거와 구현 세부는 각 Phase 담당이 갖는다.
> 선행 문서: [FIRESTORE_SAVE_ROADMAP.md](FIRESTORE_SAVE_ROADMAP.md) (P0~P3 완료분)

## 왜 방향이 바뀌었나

선행 로드맵은 **클라이언트가 계산하고 Firestore에 직접 쓰는** 구조로 P0~P3까지 왔다.
요금제가 Blaze로 바뀌며 Cloud Functions가 열렸고, 커밋 `8e63e8c`(파이어베이스 업데이트)로 기반이 들어왔다.

목표 셋:

1. **로컬 세이브 제거** — 캐시 봉투·revision 자기검증·오프라인 폴백을 걷어낸다
2. **로컬 검증 제거** — 판정(추첨·성공률·자격·지급)을 클라에서 서버로 옮긴다
3. **요청 모델** — 클라는 "무엇을 하고 싶다"를 보내고, 서버가 스펙을 근거로 판정한다

**클라의 Firestore 직접 읽기/쓰기는 유지한다.** 접근 통제는 Security Rules가 맡는다.

## 확정된 전제

| 항목 | 결정 |
|---|---|
| 검증 범위 | 세이브 쓰기 전부. 단 슬롯별로 서버/클라 소유를 가른다 |
| 클라 Firestore 접근 | **유지**. 룰이 슬롯 단위로 쓰기를 통제 |
| 판정 로직 | 난수·지급·자격이 걸린 것은 전부 Callable |
| 로컬 캐시 | **완전 제거** |
| 전환 순서 | 도메인별 점진 |
| 전투 시뮬 서버 재현 | **하지 않는다** (매치 티켓으로 대체) |

## 기반 실태

커밋 `8e63e8c` 가 깔아둔 것:

- `functions/` — Node 24 · `firebase-functions@7`(v2) · 리전 `asia-northeast3` · `ping`(onRequest) 하나
- `.firebaserc` — 프로젝트 `bm-cardbattle`
- `firebase.json` — 명명 DB `cardbattle` 타깃 + functions 코드베이스 + predeploy(lint/build)
- `FirebaseRootPath.DatabaseId = "cardbattle"` — 기본 `(default)` 아님
- Firebase 스킬 묶음 — `firebase-security-rules-auditor`, `firebase-firestore/references/**/security_rules.md`

아직 안 된 것:

| 층 | 상태 |
|---|---|
| functions | ✅ R0: 의존성 설치·`tsc` 빌드·lint 통과. 배포는 로그인 후 1회 남음 |
| 클라 Functions SDK | ✅ R0: `ICallableService`/`FunctionsCallableService` 신설. 접점은 이 하나뿐(미결 #12) |
| 규칙 | `firestore.rules` = `allow read, write: if true` **전면 개방**. `.prod` 는 P2 이전 `payload` 통짜 스키마라 실효 상실 |
| 세이브 쓰기 | `PlayerSaveCloud.PushAsync` **한 곳**. 트랜잭션 + revision 낙관적 잠금 + 문서 전체 `SetOptions.Overwrite` |
| 스펙 | 6표만 서버에: `Card` `Card_Test` `CardPack` `CardPackDrop` `TournamentReward` `AlbumReward` |
| 스펙 업로더 | `SpecFirestoreUploader` 가 **웹 API key로 REST 직접 write** → 규칙을 닫는 순간 죽는다 |

---

## 척추 — 슬롯 동결(freeze)

문서 top-level이 슬롯 단위로 갈라져 있다는 점이 그대로 지렛대가 된다.

```javascript
// 클라가 바꿔도 되는 슬롯만 화이트리스트
request.resource.data.diff(resource.data).affectedKeys()
  .hasOnly(['revision','updatedAt','deviceId','appVersion', 'deck','tutorial','profile'])
```

- 클라는 **지금처럼 문서 전체를 Overwrite** 한다. 서버 권위 슬롯은 읽은 값을 그대로 실어 보내므로
  `diff()` 에 안 잡혀 통과한다 → **클라 쓰기 코드를 고칠 필요가 없다**
- 서버(Admin SDK)는 규칙을 우회하므로 동결된 슬롯을 자유롭게 쓴다
- **도메인 승격 = 그 슬롯 이름을 화이트리스트에서 빼는 것.** 전용 callable 배포와 같은 순간에 한 줄이 움직인다
- 중간 단계(passthrough 서버 함수) 사다리가 필요 없다 — 이 설계의 가장 큰 이득

### 슬롯 소유권 최종 그림

| 소유 | 슬롯 | 룰의 역할 |
|---|---|---|
| **서버** | `currency` `ownership` `cardGrowth` `keywordGrowth` `rank` `albumReward` `tournament` | `affectedKeys` 에서 제외 — 클라가 바꾸면 거부 |
| **클라** | `deck` `profile` `tutorial` | 형식·타입·크기·범위 검증 |
| 메타 | `schemaVersion` `revision` `updatedAt` `deviceId` `appVersion` | revision 단조 +1, `updatedAt == request.time`, 타입·길이 |

### 따라오는 계약 하나 — 응답 채택

callable이 서버 권위 슬롯을 쓰면 `revision` 이 오른다. 클라가 이를 모르면 다음 저장에서
`RevisionConflictException` → `BlockSession`(재시작 모달)이 터진다.

> **모든 callable 응답은 `{ revision, updatedSlots }` 를 돌려주고,
> 클라는 이를 `DataSaveManager.Data` 와 `PlayerSaveCloud.Revision` 에 채택한다.**

이 계약이 깨지면 서버 호출마다 세션이 끊긴다. R0에서 단일 창구로 세워두고 이후 전 Phase가 얹힌다.

---

## Phase 현황

| Phase | 상태 | 목표 | 선행 |
|---|---|---|---|
| **R0** 기반 배선 | ⬜ 대기 | functions 배포·에뮬레이터, 클라 Callable 서비스, 왕복 1회 | — |
| **R1** 룰 1차 배포 | ⬜ 대기 | 소유권·형식·revision 뼈대. 슬롯 동결은 아직 없음 | R0 |
| **R2** 로컬 캐시 제거 | ⬜ 대기 | 캐시 4계층 삭제, 상태머신 정리 | — (병행 가능) |
| **R3** 스펙 서버화 | ⬜ 대기 | 업로더 권한 이관 + 미업로드 SO 7종 승격 | R1 |
| **R4** 계정 생성·스타터 | ⬜ 대기 | 신규 문서 생성을 서버가 소유 | R1 |
| **R5** 카드팩 | ⬜ 대기 | 추첨 난수·소유·간식·차감 | R3 |
| **R6** 성장·강화 | ⬜ 대기 | 성공 판정·비용·한계돌파·키워드 | R3 |
| **R7** 보상 수령 4종 | ⬜ 대기 | 랭크티어·도감·토너먼트 정점/챕터 | R3 |
| **R8** 전투 출구 | ⬜ 대기 | 매치 티켓으로 전투골드·랭크·토너먼트 낙인 | R3 |
| **R9** 룰 최종 동결·정리 | ⬜ 대기 | 서버 슬롯 7종 동결, 디버그·데드코드 정리 | R4~R8 |

---

### R0 — 기반 배선 ⬜

**목표**: 클라에서 서버 함수를 한 번 왕복시키고, 배포·로컬 반복 루프를 만든다.

- [ ] `npm install` → `npm run build` → `firebase deploy --only functions` 1회 성공
- [ ] `firebase.json` 에 `emulators` 블록 추가 (functions + firestore + auth). 지금 없다
- [ ] 클라 Callable 접점 신설 — `Assets/Scripts/Core/Firebase/`
  - `ICallableService` + `FunctionsCallableService`.
    **CLAUDE.local.md 규약**: 외부 시스템 접점(Service)은 반드시 인터페이스 추상화
  - `FirebaseFunctions.GetInstance(app, "asia-northeast3")` —
    `DefaultInstance` 는 us-central1이라 그대로 쓰면 404
  - 에뮬레이터 스위치 `UseFunctionsEmulator(origin)` 를 `ContentProfileConfig` 런모드에 물린다
  - 타임아웃은 기존 `FirebaseTimeouts` 옆에
- [ ] `ping` 을 `onCall` 로 교체 (`request.auth` 확인용)
- [ ] **응답 채택 계약 구현** — `{ revision, updatedSlots }` 를 받아
      `DataSaveManager`/`PlayerSaveCloud.Revision` 에 반영하는 단일 창구
- [ ] 오류 규약 확정: `HttpsError` code ↔ 클라 `FunctionsErrorCodes` ↔
      기존 UX 표면 3분할(`Failed` 재시도 / `Blocked` 재시작 / `Offline` 배너)

**완료 판정**: 게임 실행 → 익명 로그인 → `ping` onCall → uid가 서버 로그에 찍히고 클라가 응답 수신.
에뮬레이터로도 같은 왕복.

---

### R1 — 룰 1차 배포 ⬜

**목표**: 전면 개방을 끝낸다. **아직 슬롯은 동결하지 않는다** — 게임이 지금 그대로 돌아야 한다.

`firestore.rules` 재작성. 현재 `.prod` 는 `payload` 스키마 시절 것이라 폐기하고 새로 쓴다.

- [ ] `envs/{env}/users/{uid}/save/{doc}`
  - `isOwner()` · `env in ['live','test']` · `doc == 'current'`
  - `keys().hasOnly([메타 5 + 슬롯 10])` — 알 수 없는 필드 차단
  - 메타 타입·크기: `schemaVersion is int > 0` · `revision is int` ·
    `deviceId.size() == 32` · `appVersion.size() <= 64` · `updatedAt == request.time`
  - `create`: `revision == 1` / `update`: `revision == resource.data.revision + 1` 이고
    `schemaVersion >= resource.data.schemaVersion` / `delete: if false`
  - 슬롯별 형식 검증(각 슬롯이 map/list인지, 크기 상한)
- [ ] `envs/{env}/specs/{table}` 및 `rows/{id}` —
      `allow read: if request.auth != null`(클라 `BattleContentSync` 가 읽는다), `write: if false`
- [ ] 그 외 전부 거부
- [ ] `firebase-security-rules-auditor` 스킬로 감사 1회
- [ ] rules 에뮬레이터 회귀 3종: 남의 uid 읽기 거부 / revision 건너뛰기 거부 / 정상 왕복 통과

**같이 처리해야 하는 것**

- **`SpecFirestoreUploader` 가 죽는다** — API key로 REST write를 한다.
  R3에서 이관할 때까지의 임시 통로를 정하거나(관리자 uid 예외), R3를 R1과 붙여서 진행한다(미결 #6)
- **디버그 경로 점검** — R1 단계에선 살아 있지만 R9 동결 때 전부 죽는다:
  `OutgameDebugActions.GrantCurrency`(**`#if UNITY_EDITOR` 가드 없음**) · `CardGrowthManager.DebugMaxAll` ·
  `OwnershipManager.GrantEntireCatalog` · `RankManager.SetTierForDebug` ·
  `TournamentProgress.ResetForDebug` · `UnlockAllCardsButton`

**완료 판정**: 규칙 배포 후 정상 플레이 왕복이 되고, 다른 uid 문서 접근이 막힌다.

---

### R2 — 로컬 캐시 제거 ⬜

**목표**: 진실원이 서버 문서인 이상 캐시 봉투는 순수 부채다. **R0/R1과 독립이라 병행 가능.**

사라지는 것:

- [ ] `OutGame/Save/1.Repository/` 전체 — `IRepository` · `IAtomicRepository` ·
      `JsonFileRepository` · `PlayerPrefsRepository`(이미 dead)
- [ ] `2.Domain/PlayerSaveCacheEnvelope`
- [ ] `DataSaveManager` — `SetRepository` · `TryLoadCache` · `WriteCache` ·
      `MarkUploadedRevision` · `HasLocalSave`(dead)
- [ ] `PlayerSaveCloud` — `AdoptUnsyncedCache` · `FallbackToCache` · `IsCacheOwnedByOther` ·
      PlayerPrefs `ownerUid`
- [ ] `GameManager.Initialize` 의 `SetRepository(new JsonFileRepository(...))`

남기는 것: `CreateSnapshot`/`SnapshotOf` — dirty 판정과 "값이 안 바뀌었으면 revision 안 올림" 최적화가 여기 걸려 있다.

**상태머신이 바뀐다**: 캐시 폴백이 없으므로 부트 실패는 `Failed` 하나.
`Offline` 은 "부트 후 쓰기 실패 중"만 남는다.

**이 Phase에서 결정할 것(미결 #4)**: 쓰기 실패 시 클라 메모리와 서버가 갈린다 →
(a) 메모리 재시도 큐 (b) 실패 즉시 세션 차단

**완료 판정**: `persistentDataPath/Save/` 에 파일이 생기지 않고, 정상 부팅·저장 왕복이 된다.
비행기 모드 부팅은 재시도 화면.

---

### R3 — 스펙 서버화 ⬜

**목표**: 서버가 판정에 쓸 근거를 서버에 둔다. R5~R8의 공통 선행조건.

**(a) 업로더 권한 이관** — 선행 로드맵 미결 #2의 해결

- [ ] `SpecFirestoreUploader` 를 관리자 전용 callable(`specUpload`, Admin SDK)로 이관.
      관리자 판별은 custom claim
- [ ] 좋은 관용구는 그대로 이식: 표당 원자 커밋, 메타 `updateTime` precondition 낙관적 락,
      500 writes/10MiB 가드

**(b) 미업로드 스펙 승격** — 전부 SO에만 있어 서버가 못 본다

| 스펙 | 진실원 | 막는 Phase |
|---|---|---|
| 랭크 티어·승점·등급별 보상 | `OutGame/Rank/RankConfig` | R5·R7·R8 |
| 강화 비용 곡선·성공률·진화 관문 | `OutGame/Growth/CardGrowthConfig` | R6 |
| 키워드 강화 비용·HpPerLevel | `OutGame/Growth/KeywordGrowthConfig` | R6 |
| 전투 보상 공식 | `OutGame/Reward/BattleReward` | R8 |
| 토너먼트 정점/챕터 구조·requiredGrade | `OutGame/Tournament/TournamentConfig` | R7·R8 |
| 앨범 구성(테마↔페이지↔카드) | `OutGame/Album/CardAlbumConfig` | R7 |
| 튜토리얼 스텝 지급 목록 | `OutGame/Tutorial/OutgameTutorialData` | R9 |

**첫 판단거리(미결 #3)**: 업로더는 필드 타입을 `int`/`long`/`string` 만 지원한다.
중첩 구조(챕터→정점, 테마→페이지→카드)를 (a) 평탄한 행으로 재저작할지 (b) 업로더 타입 지원을 넓힐지.

**이중 진실원 처리**: 승격된 스펙의 SO는 폐기하지 말고 **클라 표시용 폴백**으로 남긴다
(`SpecSource` 가 이미 서버캐시↔내장본 폴백 구조).
단 **판정 근거는 서버 표가 유일**하다 — 고지 확률과 실제 추첨이 갈리면 안 된다.

**완료 판정**: `envs/test/specs/` 아래 13표. 업로더가 API key 없이(callable 경유) 업로드 성공.

---

### R4 — 계정 생성 · 스타터 ⬜

**목표**: "신규 계정" 판정과 최초 지급을 서버가 소유한다. **슬롯 승격 규약을 여기서 확립한다.**

지금은 `PlayerSaveCloud.IsFreshAccount`(원격 문서 부재) → `InstallSaveDependent` →
`CurrencyManager.Init(fresh)` 가 `STARTING_GOLD = 100` 을 넣고, `StarterDeck.GrantIfNoDeck` 가 초기 덱을 준다.

- [ ] `ensureAccount` callable — 문서 없으면 서버가 생성(스타터 골드·초기 소유·초기 덱), 있으면 그대로 반환
- [ ] 부트가 "읽기 → 없으면 로컬 생성"에서 "`ensureAccount` 1회 → 반환 문서 채택"으로
- [ ] `IsFreshAccount` 플래그와 `CurrencyManager.Init(_freshAccount)` 인자 제거
- [ ] 룰: `allow create: if false` (문서 생성은 서버만)

**완료 판정**: 콘솔에서 문서 삭제 → 재부팅 → 골드 100·스타터 덱이 서버 로그의 지급 기록과 함께 생성된다.

---

### R5 — 카드팩 ⬜

**목표**: 취약도 1위를 끊는다. `CardPackOpener` 는 **무시드 `System.Random`** 으로 클라에서 뽑고
클라에서 지급·차감한다. 재현 불가라 사후 감사조차 안 된다.

- [ ] `openPack(packId, count)` callable
  - 잠금 규칙(`PackUnlockRules` + `CardPack.minRankGrade` + 유저 `rank.points`)
  - `CardPackDrop` 풀 해석 — 랭크별 **최고 만족 등급 하나만**, 하위 합산 없음
  - `CardCatalog` 포함 필터 → 잔액 차감 → 가중치 추첨 → 신규/중복 판정
  - 중복은 `cardGrowth.entries[*].snack += 1`
  - `uniqueDraw`(비복원)는 뽑을 때마다 잔여 풀에서 합을 다시 계산하는 순서까지 그대로
  - 응답: 뽑힌 카드 + 신규 여부 + 갱신 슬롯 + 새 revision
- [ ] 클라 `CardPackOpener` 를 **연출 데이터 소비자**로 축소.
      `PackRevealView` 는 이미 `OpenedPack`/`DrawnCard` 를 받으므로 형태가 맞는다
- [ ] `PackOdds`(고지 확률)가 서버와 **같은 표**를 보게
- [ ] **룰 동결**: `ownership` · `cardGrowth` · `currency` 를 `affectedKeys` 화이트리스트에서 제외

**연출이 왕복을 흡수한다** — Entering→Swipe→Tearing 구간에 응답이 도착하면 지연이 안 보인다.
응답 실패 시 되돌릴 지점을 미리 정할 것.

**완료 판정**: 팩 구매 → 서버 로그의 추첨·차감과 콘솔 문서가 일치.
클라에서 잔액·소유를 직접 쓰려 하면 룰이 거부.

---

### R6 — 성장 · 강화 ⬜

**목표**: 확률 판정과 비용 차감이 같은 프로세스에 있는 구조를 끊는다.
`CardGrowthManager.TryEnhance` 는 `System.Random.NextDouble() < SuccessRate`(무시드)로
클라가 성공을 정하고, **실패해도 비용은 소모**된다.

- [ ] `enhanceCard(cardId)` / `limitBreak(cardId)` / `enhanceKeyword(keyword)` callable
- [ ] 근거: R3에서 올린 `CardGrowthConfig`(레벨별 `cost`/`successRate`/`costCurrency`) ·
      `KeywordGrowthConfig` · `CardSpec.KeywordUnlockLevel`
- [ ] **튜토리얼 무료 한 방 상태 이전(미결 #5)** — `OutgameTutorialGuide.HasFreeShot` 는
      **세이브에 없고 정적 필드**(`s_freeSpentStep`). `tutorial` 은 클라 소유 슬롯이라 거기 두면 조작된다
      → 서버 소유 키를 새로 세운다
- [ ] KeywordGrowth 키 규약 통일 — Currency는 enum 이름(`"Gold"`), KeywordGrowth는 정수 문자열.
      이름 쪽으로 맞춘다 (선행 로드맵 P2 잔가지)
- [ ] **룰 동결**: `cardGrowth` · `keywordGrowth`

파생값(HP 보너스·진화 단계·키워드 해금)은 지금도 레벨에서 매번 계산하므로 서버가 곡선만 가지면 재현된다.

**완료 판정**: 강화 100회 반복 시 성공률이 스펙과 통계적으로 맞고,
실패 시 비용만 차감된 문서가 서버 로그와 일치.

---

### R7 — 보상 수령 4종 ⬜

**목표**: 자격 판정을 서버로. 이 4종은 자격이 전부 **다른 저장값의 파생**이라 서버가 판정하기 가장 쉽다.

| 수령 | 자격 근거 | 낙인 |
|---|---|---|
| 랭크 티어 | `rank.points >= tier.RequiredPoints` | `rank.claimedTiers` |
| 도감 | `CardAlbum.IsComplete` = **`ownership` 순수 파생** | `albumReward.claimedKeys` (`"p:테마/페이지"`/`"t:테마"`/`"b"`) |
| 토너먼트 정점 | `tournament.pendingRewardNodeId` 와 일치 | `tournament.clearedNodeIds` |
| 토너먼트 챕터 | 챕터 내 전 정점 Cleared | `tournament.claimedChapterIds` |

- [ ] `claimReward(kind, key)` 단일 callable — 형태가 같다(자격 확인 → 지급 → 낙인)
- [ ] 토너먼트 해금 사슬(`StateOf`: 직전 정점 Cleared면 Playable)도 서버가 판정
- [ ] **룰 동결**: `rank` · `albumReward` · `tournament`

**완료 판정**: 미완성 도감 페이지 수령이 `failed-precondition` 으로 거부되고, 이중 수령이 막힌다.

---

### R8 — 전투 출구 (매치 티켓) ⬜

**목표**: 전투 결과가 아웃게임에 반영되는 유일한 출구(`TurnRunner.CaptureResult`)를 서버 판정으로.

> **명시적 비스코프**: 전투 시뮬 전체를 서버에서 재현하지 않는다.
> `Battle/` 8,129줄을 TS로 포팅하는 일이고 프로토 단계의 비용 대비가 맞지 않는다.

**티켓 방식**

1. 진입 시 `battleStart(mode, deckSlot, nodeId?)` → 서버가 티켓 발급(nonce·발급시각·덱 스냅샷·모드),
   문서에 pending 기록
2. 종료 시 `battleFinish(ticket, won, remainingCards)` → 서버가 검증
   - 티켓 유효 · **1회성** · 시간 범위(비정상적으로 짧은 전투 거부)
   - 보상은 서버 스펙으로: 승리 `max(remaining × goldPerCard, winFloor)` / 패배 `loseGold`
   - 랭크는 `RankConfig` 로: 승급전 대기선이면 스냅(승리=다음 등급 진입선, 패배=현 단계 절반),
     아니면 `Clamp(points+delta, DivisionFloor, GradeCeiling-1)`
   - 토너먼트면 전투골드·랭크 없이 `pendingRewardNodeId` 만 (지금 계약과 동일)
3. `EMatchEndReason.GrantsReward()`/`IsVoid()` 갈림은 유지하되 서버가 재확인

- [ ] `battleStart` / `battleFinish` callable
- [ ] `TurnRunner.CaptureResult` 를 티켓 경로로 교체
- [ ] `remainingCards` 상한 클램프(덱 크기)

`remainingCards` 는 여전히 클라 신고값이다 — 상한만 서버가 건다. **완전 방어가 아니다.**
멀티는 `BattleStateHash` 상호 검증이 이미 있으나 서버 검증은 아니다 → 양쪽 티켓 결과 대조는 백로그.

**완료 판정**: 승리 → 서버 계산 골드·랭크가 클라 표시와 일치. 티켓 없이 `battleFinish` 호출 시 거부.

---

### R9 — 룰 최종 동결 · 정리 ⬜

- [ ] 서버 소유 슬롯 7종이 전부 `affectedKeys` 화이트리스트 밖
- [ ] 클라 소유 3종(`deck` `profile` `tutorial`) 형식 검증 마무리
- [ ] 디버그 경로를 `debugMutate`(관리자 claim) callable로 모으고 릴리즈 스트립
- [ ] 데드코드 정리: `CardCatalog.LegacyIdOfName` · `OwnershipManager.HasAnyOwnedSaved`
- [ ] 문서 갱신: `docs/OutGamePlan/STRUCTURE.md` 세이브 절, `.claude/orch-feature-map.md`
      → `node .claude/check-feature-map.js` 검증
- [ ] `firebase-security-rules-auditor` 최종 감사
- [ ] Android IL2CPP 실기 1회

**룰로 못 하는 것 두 가지 — 명시적 한계**

- **덱 ⊆ 소유 카드 정합**: 룰에 리스트 순회가 없다. Firestore 트리거 사후 검증 또는 스코프 밖.
  지금 `DeckSaveManager` 는 `CardCatalog.Contains` 만 보고 소유를 안 본다
- **클라 소유 슬롯 안쪽**: `diff()` 는 top-level만 본다. `tutorial` 안의 모든 필드는 자유다.
  서버가 관리할 값을 거기 두면 조작된다

튜토리얼 스텝은 실제로 카드·덱·팩·첫 티어를 **지급**한다(`TutorialStepExecutor`) →
지급 부분은 R4~R8 callable을 서버가 내부 호출한다.

**완료 판정**: 클라에서 서버 슬롯을 조작해 저장 시도 → `PERMISSION_DENIED`. 전 도메인 왕복 정상.

---

## 미결 결정

| # | 항목 | 언제까지 | 선택지 |
|---|---|---|---|
| 1 | **App Check** | R1 착수 전 | callable은 인증만으로 남용을 못 막는다. (a) 강제 (b) 프로토 동안 미도입 + `maxInstances` 상한만 |
| 2 | **관리자 판별** | R3 착수 전 | (a) custom claim(`admin: true`) + 부여 스크립트 (b) 서비스 계정 + 별도 CLI |
| 3 | **중첩 스펙 업로드** | R3 첫 판단 | 업로더가 `int`/`long`/`string` 만 지원. (a) 평탄한 행 재저작 (b) 타입 지원 확장 |
| 4 | **쓰기 실패 시 클라 메모리** | R2 | 캐시가 없어지면 실패 시 서버와 갈린다. (a) 메모리 재시도 큐 (b) 즉시 세션 차단 |
| 5 | **튜토 무료 한 방 상태** | R6 | 정적 필드라 서버가 못 본다. `tutorial` 은 클라 소유라 거기 못 둔다 → 서버 소유 키 신설 |
| 6 | **R1 ↔ R3 순서** | R1 착수 전 | 룰을 닫으면 `SpecFirestoreUploader`(API key)가 죽는다. (a) R3를 R1에 붙여 진행 (b) 관리자 uid 임시 예외 |
| 7 | **호출량·요금** | R5 이후 실측 | 행위 단위 callable로 바뀌면 오히려 줄 수 있다 — 실측 후 판단 |
| 8 | **계정 연동** | 백로그 | 익명 uid 분실 = 세이브 분실. 캐시까지 없어지면 치명도가 더 오른다 |
| 9 | **멀티 결과 서버 대조** | 백로그 | 양쪽 티켓 결과 + `BattleStateHash` 를 서버가 대조하는 확장점 |
| 10 | **타임아웃 ≠ 미실행** | R4 | Functions SDK `CallAsync` 에 CancellationToken이 없어 타임아웃 레이스가 요청을 취소하지 못한다. 서버는 이미 revision을 올렸는데 클라는 모른다 → 다음 업로드가 충돌 → `Blocked`. R0에선 "안전한 오답"으로 받는다. (a) 요청 멱등키 (b) 타임아웃 후 문서 재-읽기로 revision 복구 |
| 11 | **매니저 캐시 재수화** | R5 첫 작업 | `CurrencyManager`·`OwnershipManager`·`CardGrowthManager`·`DeckSaveManager` 는 `Init()` 에서 파생 상태를 캐싱한다. 서버가 슬롯을 갈아끼워도 캐시는 옛 값이라 UI가 안 따라온다. R0가 `DataSaveManager.OnServerSlotsAdopted` 를 미리 파둔 이유 — 구독자는 R5가 붙인다 |
| 12 | **`Firebase.Functions` 직접 참조 금지** | R0 | `CallablePayload.ToPrimitiveMap` 을 우회해 `CallAsync` 를 직접 부르면 enum·POCO에서 `ArgumentException`. 접점은 `FunctionsCallableService` 하나뿐이어야 한다 |

---

## 위험 요소

1. **revision 충돌로 세션이 끊긴다** — callable이 문서를 쓰면 revision이 오른다.
   응답 채택 계약(`{revision, updatedSlots}`)을 R0에서 제대로 세우지 않으면
   서버 호출마다 `BlockSession` 이 뜬다. **1순위 위험**
2. **왕복 지연 노출** — 팩·강화는 연출이 흡수하지만, 재화 표시처럼 즉각적인 곳은
   낙관적 갱신 후 응답으로 정정하는 규약이 필요
3. **Cold start** — `asia-northeast3`, min instances 0. 부트 경로(`ensureAccount`)가 특히 민감
4. **Node 24 + firebase-functions v7** — 최신 조합이라 예제가 적다. R0에서 배포 1회를 반드시 통과시킬 것
5. **`diff()` 는 top-level만 본다** — 클라 소유 슬롯 안쪽은 전부 자유(미결 #5)
6. **스펙 이중 진실원** — SO와 서버 표가 갈리면 고지 확률과 실제 추첨이 어긋난다
7. **디버그 경로 사망** — R9 동결 시점에 개발용 지급·리셋이 전부 막힌다.
   같은 Phase에서 `debugMutate` 를 안 만들면 그날부터 개발이 멈춘다
8. **`remainingCards` 는 끝까지 클라 신고값** — R8이 끝나도 전투 보상은 완전 방어가 아니다

---

## 검증 방법

각 Phase 공통:

1. **에뮬레이터 왕복** — `firebase emulators:start`(functions + firestore + auth)로 도메인 행위 1회
2. **rules 회귀** — 남의 uid 읽기 / revision 건너뛰기 / 동결 슬롯 조작 3종이 전부 거부되는지
3. **컴파일** — `Unity_ReadConsole` 또는 Editor.log 로 CS 에러 0
4. **콘솔 대조** — `envs/test/users/{uid}/save/current` 해당 슬롯이 서버 로그와 일치
5. **거부 경로** — 조작값 전송 시 `permission-denied`/`failed-precondition`

**전체 완료 판정**

- 세이브 폴더에 파일이 생기지 않는다
- 클라에서 서버 슬롯 조작 저장 → `PERMISSION_DENIED`
- 비행기 모드 부팅 → 재시도 화면, 복구 후 정상 진입
- Android IL2CPP 실기 왕복 성공
