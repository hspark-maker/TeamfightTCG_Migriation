# 서버 검증 전환 로드맵

> 최종 갱신: 2026-08-27 · 브랜치 `feature_Firestore` · HEAD `7e54b40d9`
> 진행 상태 추적용. 설계 근거·구현 세부는 각 Phase 담당이 갖는다.
> 선행 문서(P0~P3)는 삭제됨 — `git show 8a494e06b:docs/OutGamePlan/FIRESTORE_SAVE_ROADMAP.md`

## 방향

클라가 계산하고 Firestore에 직접 쓰던 구조를 서버 판정으로 옮긴다.

1. **로컬 세이브 제거** — 캐시 봉투·revision 자기검증·오프라인 폴백 제거
2. **로컬 검증 제거** — 추첨·성공률·자격·지급 판정을 서버로
3. **요청 모델** — 클라는 "무엇을 하고 싶다"를 보내고, 서버가 스펙을 근거로 판정

**클라의 Firestore 직접 읽기/쓰기는 유지한다.** 접근 통제는 Security Rules가 맡는다.

| 항목 | 결정 |
|---|---|
| 검증 범위 | 세이브 쓰기 전부. 슬롯별로 서버/클라 소유를 가른다 |
| 클라 Firestore 접근 | 유지. 룰이 슬롯 단위로 쓰기를 통제 |
| 판정 로직 | 난수·지급·자격이 걸린 것은 전부 Callable |
| 로컬 캐시 | 세이브 캐시는 완전 제거(R2 완료). 스펙 캐시는 별개로 유지 |
| 전환 순서 | 도메인별 점진 |
| 전투 시뮬 서버 재현 | **하지 않는다** (매치 티켓으로 대체) |

---

## 기반 실태

### 서버 (배포되어 살아 있다)

| 함수 | 트리거 | 리전 | 런타임 |
|---|---|---|---|
| `ping` | callable (v2) | asia-northeast3 | nodejs24 |
| `devBumpRevision` | callable (v2) | asia-northeast3 | nodejs24 |
| `submitMatchResult` | callable (v2) | asia-northeast3 | nodejs24 |

- `functions/package.json` — node 24 · firebase-functions ^7 · firebase-admin ^13.6
- `functions/src/index.ts` — 3줄 배럴. `onRequest` 없음
- `functions/src/firebaseApp.ts` — `setGlobalOptions({maxInstances:10, region:"asia-northeast3"})` 가
  **import 최상단**에 있어야 한다(`onCall` 은 import 시점 평가). 같은 파일이 `DATABASE_ID="cardbattle"` 로
  `getFirestore(app, DATABASE_ID)` 싱글턴 생성
- `functions/src/save/saveDocument.ts` — 서버 쓰기의 **단일 창구** `mutateSave(env, uid, mutate)`.
  트랜잭션 1회: read → 문서 없으면 `failed-precondition` → `assertWritableSchema`(미달 `failed-precondition` /
  초과 `out-of-range`) → `revision+1` → `transaction.update`.
  응답 `SaveMutationResult { revision, updatedSlots }` — `SlotPatch` 는 leaf 패치가 아니라 **슬롯 전체 값**
- `SCHEMA_VERSION = 7` ↔ `UserSaveData.VERSION = 7` — **수동 동기화**
- 프로젝트 `bm-cardbattle` 하나. DB는 `databases/cardbattle` 하나뿐 (`(default)` 없음)
- `functions/scripts/` — `grant-admin.js`(admin 클레임 부여·회수, 부여 후 `revokeRefreshTokens`) ·
  `test-firestore-rules.js` · `test-match-result.js`

### 클라 접점

| 층 | 실태 |
|---|---|
| Callable 접점 | `Core/Firebase/ICallableService` + `FunctionsCallableService`. 리전 상수 하드코딩, 매 호출 `UseFunctionsEmulator` 재적용, 타임아웃 `FirebaseTimeouts.CallableMilliseconds = 15000` |
| 페이로드 변환 | `Core/Firebase/CallablePayload.ToPrimitiveMap` / `ToResponse<T>` — 직렬화기는 `DataSaveManager.SaveSerializerSettings` 공유 |
| 채택 창구 | `ServerSaveCommands.InvokeAsync` → `PlayerSaveCloud.AdoptServerResult` → `DataSaveManager.AdoptServerSlots` → `OnServerSlotsAdopted` |
| 오류 분류 | `Core/Firebase/CloudFailureClassifier.Classify` → `ECloudFailureKind { Transient, Rejected, Unusable }` |
| 세이브 쓰기 | `PlayerSaveCloud.PushAsync` **한 곳**. `RunTransactionAsync` 안에서 `TryReadMeta` 로 revision 대조 후 `Set(..., SetOptions.Overwrite)` |
| 문서 상한 | `PlayerSaveCloud.DOCUMENT_MAX_BYTES = 300000` (경고선 256KB) → 초과 시 `BlockSession(DocumentTooLarge)` |
| 에뮬레이터 | `FirebaseEmulatorConfig`(Functions·Firestore·Auth **셋 묶음**). Firestore 적용은 `FirebaseManager.cs:118` `Settings.Host` + `SslEnabled=false`(SDK 1회 창), Auth 는 `:41` |

`ServerSaveCommands.InvokeAsync` 는 `s_inFlight` 게이트로 직렬화 → `CanRunServerCommand` 확인 →
`SuspendUploadsAsync()` → 호출 → 채택 → `finally` 에서 `ResumeUploads()`.
클라 업로드와 서버 쓰기가 겹쳐 revision이 엇갈리는 것을 여기서 막는다.
현재 실사용 커맨드는 디버그 2개(`ping`·`devBumpRevision`) — 도메인 커맨드는 R4부터.

### 스펙

- 클라가 대조·다운로드하는 표는 **6개** (`SpecPayloadCodec.TableNames`):
  `Card` `Card_Test` `CardPack` `CardPackDrop` `TournamentReward` `AlbumReward`
- `OutGame/Spec/BattleContentSync` 가 Firebase Unity SDK로 `envs/{env}/specs/{table}` 메타와 `rows` 를
  `Source.Server` 로 직접 읽는다. 판정 4갈래: `Current` / `OfflineAllowed`(싱글 폴백) /
  `UpdatedRestartRequired` / `Blocked`(멀티는 실패 시 무조건)
- `Editor/SpecFirestoreUploader` — Firestore REST `documents:commit`. URL 에 `?key=` + 모든 요청에
  `SpecAdminAuth.TryGetIdToken` 을 `Authorization: Bearer` 로 실어 `isAdmin()` 통과.
  지원 타입 `int`/`long`/`string`, 표당 원자 커밋 1회, 메타 `updateTime` precondition,
  가드 `MAX_COMMIT_WRITES=500` · `MAX_COMMIT_BYTES=10MiB` · 행 경고 900KB
- `SpecSnapshotCache` — `persistentDataPath/spec-cache/{env}.json`. **세이브 캐시가 아니므로 R2 삭제 대상 아니었다**

### 룰 — `firestore.rules` 하나 (159줄, rules_version '2')

진실원은 루트 `firestore.rules` 하나 (`firestore.rules.prod` 는 삭제됨).
헬퍼: `isKnownEnv(envId)` = `live|test` · `isSignedIn()` · `isAdmin()` = `request.auth.token.admin == true`

| 경로 | read | write |
|---|---|---|
| `envs/{envId}/users/{userId}/save/{docId}` | `isOwner() && isKnownEnv && docId=='current'` | 아래 |
| `envs/{envId}/specs/{table}` (+ `/rows/{rowId}`) | `isSignedIn() && isKnownEnv` | `isAdmin() && isKnownEnv` |
| `envs/{envId}/matches/{matchId}` (+ `/{sub=**}`) | `if false` | `if false` (Admin SDK 전용) |
| `/{document=**}` catch-all | `if false` | `if false` |

```
create: isValidSave() && schemaVersion == 7 && revision == 1
update: isValidSave() && schemaVersion >= resource.data.schemaVersion
                     && revision == resource.data.revision + 1
delete: if false
```

`isValidSave()` 의 `hasOnly` 와 `hasAll` 은 **동일한 15키** (메타 5 + 슬롯 10):
`schemaVersion revision updatedAt deviceId appVersion` /
`currency ownership deck cardGrowth keywordGrowth rank albumReward tournament tutorial profile`

메타 검증: `schemaVersion is int > 0` · `revision is int > 0` · `deviceId is string size()==32` ·
`appVersion is string size()<=64` · `updatedAt == request.time`(클라 시각 금지)

슬롯 검증 깊이는 3단계:

| 깊이 | 슬롯 | 내용 |
|---|---|---|
| 내부 필드까지 | `currency` | `hasOnly(['balances'])`, `balances.hasOnly(['Gold','Diamond','Energy','Shard'])`, 4재화 각 `is int && 0 <= x <= 1e12` |
| 내부 필드까지 | `rank` | `hasOnly(['points','claimedTiers'])`, `points is int >= 0`, `claimedTiers is list size()<=20` |
| map + 키 개수 상한만 | `ownership`≤2000 · `cardGrowth`≤2000 · `albumReward`≤128 · `tournament`≤128 · `tutorial`≤128 · `keywordGrowth`≤64 · `deck`≤32 · `profile`≤64 | 안쪽 미검증 |

**함정**

- `profile` 내부를 안 보는 것은 의도다 — `ProfileSaveData` 의 `nickname`/`avatarId`/`frameId` 는 초기값이 없어
  신규 계정 첫 업로드에 **null 로 실린다**. `profile.nickname is string` 을 요구하면 신규 유저가 막힌다
- **문서 전체 바이트 상한은 룰에 없다**(룰에서 문서 크기를 못 잰다). 클라의 `DOCUMENT_MAX_BYTES` 가 갖는다
- **`affectedKeys()` 는 아직 0건** — 슬롯 동결 미적용(R5부터). 지금은 클라가 `currency.balances.Gold` 를
  상한(1e12) 안에서 임의로 올려도 통과한다
- **익명 인증**이라 `request.auth != null` 은 앱에 박힌 API key만 있으면 누구나 얻는다 →
  **"인증됨"을 신뢰 신호로 쓰는 규칙을 앞으로 추가하지 마라**
- **낙관적 잠금은 룰 층에서 막힌다**(`resource` 읽기가 커밋과 원자적). 막는 것은 인터리브지 lost update 가 아니다 —
  `SetOptions.Overwrite` 라 충돌 후 재채택해 다시 밀면 상대 기기 변경분이 통째로 사라진다.
  그래서 `PlayerSaveCloud` 는 충돌 시 재시도 대신 `BlockSession(RemoteAhead)`

### 회귀 하네스 — `Tools/firestore-rules-tests/`

- **33케이스**, `node:test` 의 `test('...')` 형식. 포트 8081 · 프로젝트 `tcg-rules-test`(루트 8080과 분리)
- `rules.test.js` 가 `../../firestore.rules` 를 `readFileSync` 해 `initializeTestEnvironment` 에 직접 주입.
  로컬 `firebase.json` 의 `emulator-bootstrap.rules` 는 자리채움
- 픽스처 `fixtures/saveDocument.js` 의 top-level 키는 룰의 15키와 정확히 일치.
  `freshAccountDocument()` 가 신규 계정 실제 산출물(빈 map · 빈 덱 슬롯 · profile 3필드 null)을 덮는다
- **픽스처는 반드시 클라 실제 산출물이어야 한다.** 손으로 만든 합성 페이로드는 옳은 룰을 틀렸다고 판정하게 만들고,
  그 판정을 믿으면 룰을 약하게 고치게 된다(테스트 `13b` 가 이 계약을 못박는다)

> ⚠️ **JDK 없으면 안 돈다.** `npm test` → `Could not spawn 'java -version'`. 그런데
> `firebase emulators:exec` 가 그 상태로 **exit code 0** 을 돌려준다.
> 판정은 종료코드가 아니라 **`# pass 33` 출력 줄**로 하라.

---

## 척추 — 슬롯 동결(freeze)

문서 top-level이 슬롯 단위로 갈라져 있다는 점을 그대로 지렛대로 쓴다.

```javascript
// 기존 isValidSave() 에 얹는 한 줄. 클라가 바꿔도 되는 슬롯만 화이트리스트
request.resource.data.diff(resource.data).affectedKeys()
  .hasOnly(['schemaVersion','revision','updatedAt','deviceId','appVersion', 'deck','tutorial','profile'])
```

> **`schemaVersion` 을 빠뜨리면 전 유저 쓰기가 막힌다.** 클라는 항상 15키를 통째로 보내므로
> 화이트리스트에 없으면 `hasOnly` 가 매 저장마다 거부한다.

- 클라는 지금처럼 문서 전체를 Overwrite 한다. 서버 권위 슬롯은 읽은 값을 그대로 실어 보내므로
  `diff()` 에 안 잡혀 통과 → **클라 쓰기 코드를 고칠 필요가 없다**
- 서버(Admin SDK)는 룰을 우회한다
- **도메인 승격 = 그 슬롯 이름을 화이트리스트에서 빼는 것.** 전용 callable 배포와 같은 순간에 한 줄이 움직인다

| 소유 | 슬롯 |
|---|---|
| **서버** | `currency` `ownership` `cardGrowth` `keywordGrowth` `rank` `albumReward` `tournament` |
| **클라** | `deck` `profile` `tutorial` |
| 메타 | `schemaVersion` `revision` `updatedAt` `deviceId` `appVersion` |

### 응답 채택 계약

callable이 서버 권위 슬롯을 쓰면 revision이 오른다. 클라가 모르면 다음 저장에서
`RevisionConflictException` → `BlockSession(RemoteAhead)`(재시작 모달).

> **모든 쓰기 callable 응답은 `{ revision, updatedSlots }` 를 돌려주고,
> 클라는 이를 `DataSaveManager.Data` 와 `PlayerSaveCloud.Revision` 에 채택한다.**

양쪽 다 R0에서 서 있다. 읽기 전용 callable(`ping`)은 대상이 아니며 `InvokeReadOnlyAsync` 로 간다.

---

## Phase 현황

| Phase | 상태 | 목표 | 선행 |
|---|---|---|---|
| **R0** 기반 배선 | ✅ 완료 | 코드·배포 완료. 남은 실왕복 실측은 R1 실측표에 합쳤다 | — |
| **R1** 룰 1차 배포 | 🟡 진행중 | 룰 본문·회귀 33·실배포 완료. **실플레이 왕복 실측(사람 1회)만 남음** | R0 |
| **R2** 로컬 캐시 제거 | ✅ 완료 | 커밋 `d19590e2b`. 완료 판정 3건은 미실행 | — |
| **R3** 스펙 서버화 | ⬜ 대기 | (a) 권한 이관 사실상 완료. 본체는 (b) 미업로드 SO 7종 승격 | R1 |
| **R4** 계정 생성·스타터 | ⬜ 대기 | 신규 문서 생성을 서버가 소유 | R1 |
| **R5** 카드팩 | ⬜ 대기 | 추첨 난수·소유·간식·차감 | R3 |
| **R6** 성장·강화 | ⬜ 대기 | 성공 판정·비용·한계돌파·키워드 | R3 |
| **R7** 보상 수령 4종 | ⬜ 대기 | 랭크티어·도감·토너먼트 정점/챕터 | R3 |
| **R8** 전투 출구 | ⬜ 대기 | 이미 있는 매치 대조에 **지급을 잇는다** | R3 |
| **R9** 룰 최종 동결·정리 | ⬜ 대기 | 서버 슬롯 7종 동결, 디버그·데드코드 정리 | R4~R8 |

---

### R1 — 룰 1차 배포 🟡

**목표**: 전면 개방을 끝낸다. **아직 슬롯은 동결하지 않는다** — 게임이 지금 그대로 돌아야 한다.
룰 실태는 위 「기반 실태 · 룰」 절이 정본이다.

- [x] 세이브 문서 룰 · `specs` · `matches` 거부 · catch-all deny
- [x] `firebase-security-rules-auditor` 감사 1회 (Major 2건 반영)
- [x] 회귀 33케이스 (뮤테이션 검증 3회로 실제로 룰을 물고 있음을 확인)
- [x] 배포 완료(2026-08-27) — `firebase deploy --only firestore:rules --project bm-cardbattle`.
      **`firestore.indexes.json` 이 없으므로 `--only firestore` 를 그냥 치면 안 된다.**
      릴리즈 경로 `projects/bm-cardbattle/releases/cloud.firestore/cardbattle` — CLI 완료 문구는 DB 이름을
      생략하므로 명명 DB 로 갔는지는 `--debug` 의 PATCH 경로로만 확인된다
- [ ] **배포 후 정상 플레이 왕복 + 타 uid 접근 차단 실측 — 사람이 1회 돌려야 한다**

**`create` 는 실클라로 미검증** — 문서를 지우고 재부트하면 Unity Firestore 네이티브 클라이언트가
에디터 2회차 Play 에서 "client is offline" 을 뱉는다(네이티브 인스턴스가 도메인 리로드를 넘어 살아남는다).
룰 문제가 아니라 **Unity 완전 재시작**이 필요한 항목이다. 그래서 `create` 는 하네스(`14`·`14c`·`14d`)가
유일한 방어선이다.

#### 남은 실측 — F8 디버그 오버레이 CLOUD 블록

계기판 `CLOUD {EMU|LIVE} / {State} / rev N` + `UID {…} / {AuthState}`, 버튼 `PING` · `BUMP` · `DENY?`
(`OutgameDebugOverlay.DrawServerProbes`). **세 ContentProfile 모두 `useLocalEmulators: 0`** 이라
에디터 Play 가 곧 실서버 왕복이다.

| 잴 것 | 조작 | 통과 판정 |
|---|---|---|
| 부트 채택 | Play | 로비 진입. 막히면 `LoadingCoverView` 복구 화면 |
| 읽기 + 서버 시야 (R0 완료 판정 겸함) | `PING` | `database=cardbattle` · `schemaVersion` == `documentSchemaVersion` · `revision` 이 계기판과 일치 |
| **클라 쓰기(룰의 본무대)** | `+G`/`+D`/`+E`/`+S` | `rev` 가 정확히 +1, `Ready` 유지. 4재화를 다 눌러야 `balances` 검증 4줄을 전부 밟는다 |
| 서버 쓰기 + 채택 계약 | `BUMP` → 곧바로 `+G` | 로그의 `→ N` 과 `(채택 후 N)` 이 같고, 이어지는 클라 저장이 N+1 로 통과 |
| **타 uid·미지 env·매치 차단** | `DENY?` | `규칙 진단 3/3 차단` |

`DENY?`(`OutgameDebugActions.ProbeRuleDenials` → `PlayerSaveCloud.ProbeReadDeniedAsync`)는 실클라 SDK로
세 경로를 `Source.Server` 로 읽는다: 남의 uid · `envs/dev/...` · `{env}/matches/rules-probe`.
**셋 다 없는 문서를 겨눈다** — 룰이 문서 존재보다 소유자·환경을 먼저 보므로 없어도 `permission-denied` 가
나와야 하고, "없어서" 통과하면 룰이 열린 것이다. 캐시 히트는 룰을 안 거치므로 `Source.Server` 가 필수다.

**완료 판정**: 규칙 배포 후 정상 플레이 왕복이 되고, 다른 uid 문서 접근이 막힌다.

---

### R2 — 로컬 캐시 제거 ✅

커밋 `d19590e2b`. `OutGame/Save/1.Repository/` 폴더 자체가 없고 캐시 관련 심볼 전부 0건.
현재 구조는 3계층(`2.Domain/` · `3.Manager/DataSaveManager` · `4.Cloud/`).

남긴 것: `DataSaveManager.CreateSnapshot` — dirty 판정과 "값이 안 바뀌었으면 revision 안 올림" 최적화가
여기 걸려 있다(`PlayerSaveCloud` 3곳에서 사용).

**상태머신** `EPlayerSaveCloudState` 7값 — `Disabled Loading Ready Offline Uploading Failed Blocked`.
유저에게 보이는 표면은 셋:

| 상태 | 사건 | 표면 |
|---|---|---|
| `Failed` | 부트 채택 실패 | 복구 화면(`LoadingCoverView`) |
| `Offline` | 부트 후 쓰기 실패 중 | 배너(`CloudSyncBannerView`) |
| `Blocked` | 이 클라로는 더 못 쓴다 (`ECloudBlockReason`: `RemoteAhead` `DocumentTooLarge` `SessionUnusable`) | 재시작 모달 |

R2가 채택한 방침(미결 #4는 여전히 열림): `Transient` → 메모리 재시도(`PlayerSaveCloud.RetryPending` —
못 올린 변경분만 다시 태우고 재-pull 은 안 한다. 복귀 훅 `GameManager` → `FirebaseManager.RetryPending`),
`Rejected`/`Unusable` → `BlockSession`.

**완료 판정(미실행 — 사람이 1회)**

- [ ] `persistentDataPath/Save/` 에 파일이 생기지 않는다 (판정 대상은 `Save/` 폴더다 — `spec-cache/` 는 무관)
- [ ] 정상 부팅·저장 왕복
- [ ] 비행기 모드 부팅 → 재시도 화면 (도구: 커밋 `933e0c14c` 방화벽 토글 + `Editor/OfflineBootTestMenu`)

---

### R3 — 스펙 서버화 ⬜

**목표**: 서버가 판정에 쓸 근거를 서버에 둔다. R5~R8의 공통 선행조건.

**(a) 업로더 권한 — 대부분 완료**

- [x] 관리자 판별 = custom claim `admin: true`
- [x] 룰 `specs write: isAdmin()` 배포
- [x] 업로더가 ID 토큰을 싣는다 (`Editor/SpecAdminAuth` → `SessionState` 보관)
- [ ] **남은 판단**: `specUpload` callable 이관이 필요한가.
      현 구조로 이미 (i) admin 전용 (ii) 표당 원자 커밋 (iii) `updateTime` precondition (iv) 500 writes/10MiB 가드가 선다.
      이관의 실익은 "웹 API key가 URL에 남는 것" + "쓰기 형식 검증을 서버에서" 둘뿐

> `specs` 의 남은 위험은 변조가 아니라 **무제한 문서 생성**(`{envId}`·`{table}` 둘 다 와일드카드).
> `write` 가 `isAdmin()` 이라 미인증자는 못 찍고, `isKnownEnv` 가 env를 2개로 좁힌다.

**(b) 미업로드 스펙 승격** — 전부 SO에만 있어 서버가 못 본다

| 스펙 | 진실원 | 막는 Phase |
|---|---|---|
| 랭크 티어·승점·등급별 보상 | `OutGame/Rank/RankConfig.cs` | R5·R7·R8 |
| 강화 비용 곡선·성공률·진화 관문 | `OutGame/Growth/CardGrowthConfig.cs` | R6 |
| 키워드 강화 비용·HpPerLevel | `OutGame/Growth/KeywordGrowthConfig.cs` | R6 |
| 전투 보상 공식 | `OutGame/Reward/BattleReward.cs` | R8 |
| 토너먼트 정점/챕터 구조·requiredGrade | `OutGame/Tournament/TournamentConfig.cs` | R7·R8 |
| 앨범 구성(테마↔페이지↔카드) | `OutGame/Album/CardAlbumConfig.cs` | R7 |
| 튜토리얼 스텝 지급 목록 | `OutGame/Tutorial/OutgameTutorialData.cs` | R9 |


**첫 판단거리(미결 #3)**: 업로더는 `int`/`long`/`string` 만 지원. 중첩 구조(챕터→정점, 테마→페이지→카드)를
(a) 평탄한 행으로 재저작할지 (b) 업로더 타입 지원을 넓힐지.

**이중 진실원 처리**: 승격된 스펙의 SO는 폐기하지 말고 **클라 표시용 폴백**으로 남긴다
(`SpecSource` 가 이미 `"서버캐시"`↔`"내장본"` 폴백 구조). 단 **판정 근거는 서버 표가 유일**하다 —
고지 확률과 실제 추첨이 갈리면 안 된다.

**완료 판정**: `envs/test/specs/` 아래 13표(현행 6 + 승격 7). 업로더 업로드 성공.
`SpecPayloadCodec.TableNames` 와 `SchemaVersion` 을 같이 올려야 클라가 새 표를 본다.

---

### R4 — 계정 생성 · 스타터 ⬜

**목표**: "신규 계정" 판정과 최초 지급을 서버가 소유한다. **슬롯 승격 규약을 여기서 확립한다.**

현행 사슬 (`Core/Initialization/SaveDependentManagersStep`):
`PlayerSaveCloud.IsFreshAccount` → `CurrencyManager.Init(IsFreshAccount)` → `StarterDeck.GrantIfNoDeck`

> **지급 조건이 `IsFreshAccount` 하나가 아니다.** `CurrencyManager.Init` 은 `_freshAccount || Balances 가 비었음`
> 이면 첫실행으로 보고 `STARTING_GOLD = 100`. `StarterDeck` 은 `IsFreshAccount` 를 아예 안 보고
> `DeckSaveManager.HasAnySavedDeck()` 기준이다. **이 두 갈래를 하나로 합치는 것이 실제 작업량이다.**

- [ ] `ensureAccount` callable — 문서 없으면 서버가 생성(스타터 골드·초기 소유·초기 덱), 있으면 그대로 반환.
      `mutateSave` 는 "문서 없으면 `failed-precondition`" 이라 **생성 경로를 새로 열어야 한다**
- [ ] 부트를 "읽기 → 없으면 로컬 생성"에서 "`ensureAccount` 1회 → 반환 문서 채택"으로
- [ ] `IsFreshAccount` 플래그와 `CurrencyManager.Init(_freshAccount)` 인자 제거,
      `StarterDeck.GrantIfNoDeck` 의 덱-부재 판정도 회수
- [ ] 룰: `allow create: if false`. 회귀 `14`·`14b`·`14c`·`14d` 가 이때 뒤집힌다

**완료 판정**: 콘솔에서 문서 삭제 → 재부팅 → 골드 100·스타터 덱이 서버 로그의 지급 기록과 함께 생성된다.

---

### R5 — 카드팩 ⬜

**목표**: 취약도 1위를 끊는다.

현행 `OutGame/CardPack/CardPackOpener`: `static readonly System.Random s_rng = new System.Random()` —
시드 인자가 없어 시간 기반이고 서버가 재현할 수 없다. 차감(`CurrencyManager.Spend`) ·
지급(`OwnershipManager.Grant`) · 간식(`CardGrowthManager.AddSnack`) · flush 가 전부 같은 메서드 안에서
클라 로컬로 확정된다.

- [ ] `openPack(packId, count)` callable
  - 잠금 규칙(`PackUnlockRules.IsUnlocked` + `CardPackData.TryGetMinRankGrade` + 유저 `rank.points`).
    `minRankGrade` 는 **시트 문자열 우선, 실패 시 SO 폴백** — 서버도 같은 우선순위여야 한다
  - `CardPackDrop` 풀 해석 — 랭크별 **최고 만족 등급 하나만**, 하위 합산 없음 (`PackSpec`)
  - `CardCatalog` 포함 필터 → 잔액 차감 → 가중치 추첨(`PickWeightedCandidate`) → 신규/중복 판정
  - 중복은 `cardGrowth.entries[*].snack += 1`
  - `uniqueDraw`(비복원)는 뽑을 때마다 잔여 풀에서 합을 다시 계산하는 순서까지 그대로
  - 응답: 뽑힌 카드 + 신규 여부 + `{revision, updatedSlots}`
- [ ] 클라 `CardPackOpener` 를 **연출 데이터 소비자**로 축소 (`PackRevealView` 는 이미 `OpenedPack`/`DrawnCard` 를 받는다)
- [ ] `PackOdds`(고지 확률)가 서버와 같은 표를 보게
- [ ] **룰 동결**: `ownership` · `cardGrowth` · `currency`
- [ ] **미결 #11 착수** — `DataSaveManager.OnServerSlotsAdopted` 는 R0에서 파뒀지만 구독자 0건. 여기서 첫 구독자

**연출이 왕복을 흡수한다** — Entering→Swipe→Tearing 구간에 응답이 도착하면 지연이 안 보인다.
응답 실패 시 되돌릴 지점을 미리 정할 것.

**완료 판정**: 팩 구매 → 서버 로그의 추첨·차감과 콘솔 문서가 일치. 클라에서 잔액·소유 직접 쓰기는 룰이 거부.

---

### R6 — 성장 · 강화 ⬜

현행 `CardGrowthManager.TryEnhance`:

1. `CurrencyManager.Spend(...)` — **비용이 먼저 나간다**
2. `s_rng.NextDouble() < t_step.SuccessRate` — 무시드 `System.Random`, 클라가 성공을 정한다
3. `CurrencyManager.Save()` 는 성공/실패 공통 — **실패해도 차감이 영속되고 환급 코드는 없다**

- [ ] `enhanceCard(cardId)` / `limitBreak(cardId)` / `enhanceKeyword(keyword)` callable
- [ ] 근거: R3에서 올린 `CardGrowthConfig`(`baseEnhanceCost=25` · `costGrowthPerLevel=50` ·
      `baseSuccessRate=1` · `rateDropPerLevel` + 레벨별 오버라이드 행) · `KeywordGrowthConfig` ·
      `CardSpec.KeywordUnlockLevel`
- [ ] **튜토리얼 무료 한 방 상태 이전(미결 #5)** — `OutgameTutorialGuide.HasFreeShot` 는 세이브에 없고
      정적 필드(`s_freeSpentStep`)라 **앱 재시작으로 되살아난다**. 축이 둘(`WaitEnhance`·`WaitKeywordEnhance`).
      `tutorial` 은 클라 소유 슬롯이라 거기 두면 조작된다 → 서버 소유 키 신설.
      현행은 "Cost만 0" 이라 **성공률은 건드리지 않는다** — 서버도 같아야 한다
- [ ] KeywordGrowth 키 규약 통일 — `CurrencySaveData` 는 enum 이름, `KeywordGrowthSaveData` 는 정수 문자열.
      **이름 쪽으로 맞춘다**
- [ ] **룰 동결**: `cardGrowth` · `keywordGrowth`

파생값(HP 보너스·진화 단계·키워드 해금)은 레벨에서 매번 계산하므로 서버가 곡선만 가지면 재현된다.

**완료 판정**: 강화 100회 반복 시 성공률이 스펙과 통계적으로 맞고, 실패 시 비용만 차감된 문서가 서버 로그와 일치.

---

### R7 — 보상 수령 4종 ⬜

| 수령 | 자격 근거 | 낙인 |
|---|---|---|
| 랭크 티어 | `rank.points >= tier.RequiredPoints` | `rank.claimedTiers` (룰이 `size() <= 20`) |
| 도감 | `CardAlbum.IsComplete` = **`ownership` 순수 파생** | `albumReward.claimedKeys` (`"p:테마/페이지"`/`"t:테마"`/`"b"`) |
| 토너먼트 정점 | `tournament.pendingRewardNodeId` 와 일치 | `tournament.clearedNodeIds` |
| 토너먼트 챕터 | 챕터 내 전 정점 Cleared | `tournament.claimedChapterIds` |

- [ ] `claimReward(kind, key)` 단일 callable — 넷 다 형태가 같다(자격 확인 → 지급 → 낙인)
- [ ] 토너먼트 해금 사슬(`StateOf`: 직전 정점 Cleared면 Playable)도 서버가 판정
- [ ] **룰 동결**: `rank` · `albumReward` · `tournament`.
      `rank` 의 기존 내부 필드 검증은 중복이 되지만 **지우지 말고 방어 겹으로 남긴다**

**완료 판정**: 미완성 도감 페이지 수령이 `failed-precondition` 으로 거부되고, 이중 수령이 막힌다.

---

### R8 — 전투 출구 ⬜

**출구가 하나라는 것은 확인됐다.** `Battle/TurnRunner.CaptureResult` 의 유일한 호출자는 같은 파일
`FinalizeResult` 의 `if (_reason.GrantsReward()) CaptureResult(...)` 하나이고, 아웃게임 쓰기 호출이
전부 이 메서드 안에 있다 — `TournamentProgress.MarkRewardPending` · `TournamentResultHandoff.Set` ·
`RewardService.GrantBattleReward` · `BattleRewardHandoff.Set` · `RankManager.ApplyBattleResult` ·
`RankResultHandoff.Set`.

> 예외 — `RankResultHandoff.Set` 은 디버그(`OutgameDebugActions`)와 튜토리얼 첫 티어 진입
> (`OutgameTutorialRunner` · `TutorialStepExecutor`)에서도 불린다. R7·R9 동결 때 같이 볼 것.

#### 이미 있는 것 — 백지에서 설계하지 마라

| 있는 것 | 위치 | 상태 |
|---|---|---|
| `submitMatchResult` onCall | `functions/src/commands/submitMatchResult.ts` | 배포됨 |
| 양쪽 제출 대조 판정 (순수 모듈, Firestore 접근 0) | `functions/src/matchResult.ts` | 동작 중 |
| 클라 제출 + 재시도 큐 | `Assets/Scripts/Network/MatchResultSubmission.cs` | 동작 중 |

매치 문서 `envs/{env}/matches/{matchId}` 에 두 플레이어가 각자 제출하고 서버가 대조해
`pending`/`flagged`/`confirmed` 를 정한다:

- `expectedMatchId(myNonce, opponentNonce)` = 두 nonce XOR → sha256 앞 32 hex. `!= matchId` 면 거부
- `submissionsAgree` — uid 동일 / 승자 충돌 / nonce·deckHash·`finalStateHash`·`contentFingerprint`·`remaining` 교차 대조.
  `stateHashChain` 은 길이가 같으면 일치, **1 차이까지 허용**(긴 쪽 `stateHashChainPrev` == 짧은 쪽 `stateHashChain`)
- `decideMatch` — 1건이면 마감(**120초**) 전엔 `pending`, 넘으면 `flagged/single_submission`;
  3건 이상 `flagged/too_many_submissions`; 2건이면 agree 결과
- 입력 검증 `parseSubmitData` — HEX_16/32/64 · 정수 범위. 동일 uid 재제출은 `sameSubmission` 이면 무시, 다르면 `already-exists`

**다만 이것은 수집·판정일 뿐 지급이 아니다** — "랭크·보상은 클라이언트가 로컬에서 확정하며, 여기서 세이브를
읽지도 쓰지도 않는다"(코드 주석). **R8이 할 일은 이 판정 결과를 지급으로 잇는 것이다.**

#### 남은 합류 부채 2건

- **`MatchResultSubmission.cs` 가 `Firebase.Functions` 를 직접 참조** — `GetHttpsCallable("submitMatchResult")`.
  **미결 #12 위반이 남은 유일한 지점**(그 외 `Firebase.Functions` using 은
  `FunctionsCallableService`·`CloudFailureClassifier`·`ICallableService` 뿐)
- **실패분을 PlayerPrefs 큐에 쌓는다** — 키 `firebase.matchResult.pending.v2`, 상한 8회 초과 시 폐기.
  R2 방향과 반대이고 미결 #4의 세 번째 선택지를 사실상 구현한 것. 단 지키는 것이 세이브가 아니라 매치 제출이라
  R2 판정과는 안 부딪힌다

#### 티켓 방식 (지급을 잇는 설계)

1. 진입 시 `battleStart(mode, deckSlot, nodeId?)` → 서버가 티켓 발급(nonce·발급시각·덱 스냅샷·모드), 문서에 pending 기록.
   **멀티는 이미 nonce 를 쓰므로 여기에 얹을 수 있는지 먼저 볼 것**
2. 종료 시 `battleFinish(ticket, won, remainingCards)` → 서버 검증
   - 티켓 유효 · **1회성** · 시간 범위(비정상적으로 짧은 전투 거부)
   - 보상: 승리 `max(remaining × goldPerCard, winFloor)` / 패배 `loseGold`
   - 랭크는 `RankConfig` 로: 승급전 대기선이면 스냅(승리=다음 등급 진입선, 패배=현 단계 절반),
     아니면 `Clamp(points+delta, DivisionFloor, GradeCeiling-1)`
   - 토너먼트면 전투골드·랭크 없이 `pendingRewardNodeId` 만 (현 계약과 동일)
3. `EMatchEndReason.GrantsReward()`/`IsVoid()` 갈림은 유지하되 서버가 재확인

- [ ] `battleStart` / `battleFinish` callable
- [ ] `TurnRunner.CaptureResult` 를 티켓 경로로 교체
- [ ] `remainingCards` 상한 클램프(덱 크기) — **여전히 클라 신고값이고 상한만 서버가 건다. 완전 방어가 아니다**
- [ ] 멀티는 `submitMatchResult` 의 `confirmed` 를 지급 전제로 삼을지 결정

> **명시적 비스코프**: 전투 시뮬 전체를 서버에서 재현하지 않는다(`Battle/` 8,129줄 TS 포팅).

**완료 판정**: 승리 → 서버 계산 골드·랭크가 클라 표시와 일치. 티켓 없이 `battleFinish` 호출 시 거부.

---

### R9 — 룰 최종 동결 · 정리 ⬜

- [ ] 서버 소유 슬롯 7종이 전부 `affectedKeys` 화이트리스트 밖
- [ ] 클라 소유 3종(`deck` `profile` `tutorial`) 형식 검증 마무리 —
      `profile` 안쪽은 R4가 서버 생성으로 초기값을 채운 **뒤에야** 열 수 있다(null 실림)
- [ ] `deviceId` hex 검증 · `appVersion` 문자셋 검증 (R1 감사 이월분)
- [ ] 중첩 값 크기 상한 (지금은 키 개수만 막혀 있어 슬롯 안을 채워 revision마다 갱신하면 비용이 팽창)
- [ ] 디버그 경로를 `debugMutate`(admin claim) callable로 모으고 릴리즈 스트립.
      **`OutgameDebugActions.GrantCurrency` 와 `Grant{Gold,Diamond,Energy,Shard}` 4종은
      `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 바깥에 있어 릴리스 빌드에도 컴파일된다**
      (같은 파일의 `PingServer`/`BumpServerRevision`/`ProbeRuleDenials` 는 가드 안).
      나머지: `CardGrowthManager.DebugMaxAll` · `OwnershipManager.GrantEntireCatalog` ·
      `RankManager.SetTierForDebug` · `TournamentProgress.ResetForDebug`(호출자 0건) · `UI/Debug/UnlockAllCardsButton`
- [ ] 데드코드 정리: `CardCatalog.LegacyIdOfName` · `OwnershipManager.HasAnyOwnedSaved` (둘 다 참조 0건)
- [ ] `Tools/firestore-rules-tests/package.json` 의 `description` 에서 `firestore.rules.prod` 지우기
- [ ] 문서 갱신: `docs/OutGamePlan/STRUCTURE.md` 세이브 절, `.claude/orch-feature-map.md`
      → `node .claude/check-feature-map.js` 검증
- [ ] `firebase-security-rules-auditor` 최종 감사
- [ ] Android IL2CPP 실기 1회

**룰로 못 하는 것 두 가지**

- **덱 ⊆ 소유 카드 정합**: 룰에 리스트 순회가 없다. Firestore 트리거 사후 검증 또는 스코프 밖.
  지금 `DeckSaveManager` 는 `CardCatalog.Contains` 만 보고 소유를 안 본다
- **클라 소유 슬롯 안쪽**: `diff()` 는 top-level만 본다. `tutorial` 안의 모든 필드는 자유다

튜토리얼 스텝은 실제로 카드·덱·팩·첫 티어를 **지급**한다(`TutorialStepExecutor`) →
지급 부분은 R4~R8 callable을 서버가 내부 호출한다.

**완료 판정**: 클라에서 서버 슬롯을 조작해 저장 시도 → `PERMISSION_DENIED`. 전 도메인 왕복 정상.

---

## 미결 결정 (열린 것만)

| # | 항목 | 언제까지 | 내용 |
|---|---|---|---|
| 3 | 중첩 스펙 업로드 | R3(b) 첫 판단 | 업로더가 `int`/`long`/`string` 만 지원. (a) 평탄한 행 재저작 (b) 타입 지원 확장 |
| 4 | 쓰기 실패 시 클라 메모리 | 열림 | 방침은 분류기 하이브리드(`Transient`→`RetryPending`, 그 외 `BlockSession`). **다만 `MatchResultSubmission` 이 PlayerPrefs 영속 큐라는 세 번째 답을 이미 쓰고 있다** — 표준을 안 정했다 |
| 5 | 튜토 무료 한 방 상태 | R6 | 정적 필드라 서버가 못 보고 앱 재시작으로 되살아난다. `tutorial` 은 클라 소유라 거기 못 둔다 → 서버 소유 키 신설 |
| 7 | 호출량·요금 | R5 이후 실측 | 행위 단위 callable로 바뀌면 오히려 줄 수 있다 |
| 8 | 계정 연동 | 백로그 | 익명 uid 분실 = 세이브 분실. 로컬 캐시까지 없어져 치명도가 올랐다. `Editor/GoogleOAuthSignIn` 은 스펙 업로더용 |
| 10 | 타임아웃 ≠ 미실행 | R4 | `FunctionsCallableService` 는 `UniTask.WhenAny` 로 15초에 끊지만 **요청 취소 수단이 없다**. 서버는 revision을 올렸는데 클라는 모른다 → 다음 업로드 충돌 → `Blocked`. (a) 요청 멱등키 (b) 타임아웃 후 문서 재-읽기로 revision 복구 |
| 11 | 매니저 캐시 재수화 | R5 첫 작업 | `CurrencyManager`·`OwnershipManager`·`CardGrowthManager`·`DeckSaveManager` 가 `Init()` 에서 파생 상태를 캐싱. 서버가 슬롯을 갈아끼워도 캐시는 옛 값. `OnServerSlotsAdopted` 구독자 0건 |
| 12 | `Firebase.Functions` 직접 참조 금지 | R8 | 위반 1건 — `Network/MatchResultSubmission.cs`. `CallablePayload.ToPrimitiveMap` 을 우회하면 enum·POCO에서 `ArgumentException` |

**닫힌 것**: #1 App Check(프로토 동안 미도입, `submitMatchResult` 는 `enforceAppCheck: false`) ·
#2 관리자 판별(custom claim `admin`) · #6 R1↔R3 순서(임시 개방 없이 `isAdmin()` 으로 닫음) ·
#9 멀티 결과 서버 대조(구현·배포 완료, 남은 것은 지급 연결 = R8)

---

## 위험 요소

1. **revision 충돌로 세션이 끊긴다** — 채택 계약이 R0에서 섰지만 **아직 디버그 경로로만 밟혔다**.
   도메인 callable마다 `ServerSaveCommands.InvokeAsync` 경유를 강제할 것. **1순위**
2. **왕복 지연 노출** — 팩·강화는 연출이 흡수하지만, 재화 표시처럼 즉각적인 곳은 낙관적 갱신 후 정정 규약 필요
3. **Cold start** — asia-northeast3, min instances 0. 부트 경로(`ensureAccount`)가 특히 민감
4. **`SCHEMA_VERSION` 삼중 수동 동기화** — `UserSaveData.VERSION`(클라) · `saveDocument.ts`(서버) ·
   `firestore.rules` 의 `create` 고정값(`== 7`). **하나만 올리면 신규 계정 생성이나 전 유저 쓰기가 조용히 막힌다**
   (회귀 `8b` 가 잡는다)
5. **`diff()` 는 top-level만 본다** — 클라 소유 슬롯 안쪽은 전부 자유
6. **스펙 이중 진실원** — SO와 서버 표가 갈리면 고지 확률과 실제 추첨이 어긋난다
7. **디버그 경로 사망** — R9 동결 시점에 개발용 지급·리셋이 전부 막힌다. 같은 Phase에서 `debugMutate` 필수
8. **`remainingCards` 는 끝까지 클라 신고값**
9. **회귀 하네스가 조용히 안 돈다** — JDK 부재 시 `emulators:exec` 가 exit 0. 판정은 `# pass 33` 줄로

---

## 검증 방법

각 Phase 공통:

1. **에뮬레이터 왕복** — `firebase emulators:start`(functions + firestore + auth)로 도메인 행위 1회.
   ContentProfile 의 `useLocalEmulators` 를 켜고 **세 주소를 다 채워야** 한다
   (`FirebaseEmulatorConfig` 가 하나라도 비면 `IsMisconfigured` 로 부트를 세운다)
2. **rules 회귀** — `cd Tools/firestore-rules-tests && npm test`. **`# pass 33` 을 눈으로 확인**
3. **컴파일** — `Unity_ReadConsole` 또는 Editor.log 로 CS 에러 0
4. **콘솔 대조** — `envs/test/users/{uid}/save/current` 해당 슬롯이 서버 로그와 일치
5. **거부 경로** — 조작값 전송 시 `permission-denied`/`failed-precondition`

**전체 완료 판정**

- 세이브 폴더에 파일이 생기지 않는다
- 클라에서 서버 슬롯 조작 저장 → `PERMISSION_DENIED`
- 비행기 모드 부팅 → 재시도 화면, 복구 후 정상 진입
- Android IL2CPP 실기 왕복 성공
