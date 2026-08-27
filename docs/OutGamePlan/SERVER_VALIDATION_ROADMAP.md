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

층별 실태:

| 층 | 상태 |
|---|---|
| functions | ✅ R0: 의존성 설치·`tsc` 빌드·lint 통과. 배포는 로그인 후 1회 남음 |
| 클라 Functions SDK | ✅ R0: `ICallableService`/`FunctionsCallableService` 신설. 접점은 이 하나뿐(미결 #12) |
| 규칙 | `.prod` 는 커밋 `809d040d3` 에서 신 스키마(메타 5 + 슬롯 10) 기준으로 교정됨. 전면 개방인 것은 루트 `firestore.rules`(= `allow read, write: if true`) 하나뿐 |
| 세이브 쓰기 | `PlayerSaveCloud.PushAsync` **한 곳**. 트랜잭션 + revision 낙관적 잠금 + 문서 전체 `SetOptions.Overwrite` |
| 스펙 | 6표만 서버에: `Card` `Card_Test` `CardPack` `CardPackDrop` `TournamentReward` `AlbumReward` |
| 스펙 업로더 | `SpecFirestoreUploader` 가 **웹 API key로 REST 직접 write** → 규칙을 닫는 순간 죽는다 |

---

## 척추 — 슬롯 동결(freeze)

문서 top-level이 슬롯 단위로 갈라져 있다는 점이 그대로 지렛대가 된다.

```javascript
// 클라가 바꿔도 되는 슬롯만 화이트리스트
request.resource.data.diff(resource.data).affectedKeys()
  .hasOnly(['schemaVersion','revision','updatedAt','deviceId','appVersion', 'deck','tutorial','profile'])
```

> **`schemaVersion` 을 빠뜨리면 전 유저 쓰기가 막힌다.** 클라는 항상 15키(메타 5 + 슬롯 10)를 통째로
> 실어 보내므로 `schemaVersion` 이 화이트리스트에 없으면 `hasOnly` 가 매 저장마다 거부한다.

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
| **R0** 기반 배선 | 🟡 진행중 | 코드·설정 완료(에뮬레이터 블록, 클라 Callable 서비스, `ping` onCall, 채택 계약, 오류 분류기). **배포 1회 + 실왕복 검증 남음** | — |
| **R1** 룰 1차 배포 | 🟡 진행중 | 소유권·형식·revision 뼈대(슬롯 동결은 아직 없음). 룰 본문 + 회귀 33케이스 + **실배포 완료(2026-08-27)**. 실플레이 왕복 실측만 남음 | R0 |
| **R2** 로컬 캐시 제거 | ✅ 완료 | 캐시 4계층 삭제, 상태머신 정리 — 커밋 `d19590e2b` | — (병행 가능) |
| **R3** 스펙 서버화 | ⬜ 대기 | 업로더 권한 이관 + 미업로드 SO 7종 승격 | R1 |
| **R4** 계정 생성·스타터 | ⬜ 대기 | 신규 문서 생성을 서버가 소유 | R1 |
| **R5** 카드팩 | ⬜ 대기 | 추첨 난수·소유·간식·차감 | R3 |
| **R6** 성장·강화 | ⬜ 대기 | 성공 판정·비용·한계돌파·키워드 | R3 |
| **R7** 보상 수령 4종 | ⬜ 대기 | 랭크티어·도감·토너먼트 정점/챕터 | R3 |
| **R8** 전투 출구 | ⬜ 대기 | 매치 티켓으로 전투골드·랭크·토너먼트 낙인 | R3 |
| **R9** 룰 최종 동결·정리 | ⬜ 대기 | 서버 슬롯 7종 동결, 디버그·데드코드 정리 | R4~R8 |

---

### R0 — 기반 배선 🟡

**목표**: 클라에서 서버 함수를 한 번 왕복시키고, 배포·로컬 반복 루프를 만든다.

- [x] `npm install` → `npm run build` — `functions/node_modules` 존재, `functions/lib/` 산출물이 `src` 보다 최신
- [ ] `firebase deploy --only functions` 1회 성공 — **미배포**. `firebase login:list` 가
      `No authorized accounts` 라 CLI 로그인이 없어 배포 자체를 시도할 수 없다
- [x] `firebase.json` 에 `emulators` 블록 추가 — auth 9099 / functions 5001 / firestore 8080 /
      ui 4000 / `singleProjectMode`
- [x] 클라 Callable 접점 신설 — `Assets/Scripts/Core/Firebase/ICallableService.cs` +
      `FunctionsCallableService.cs`
  - `ICallableService` + `FunctionsCallableService`.
    **CLAUDE.local.md 규약**: 외부 시스템 접점(Service)은 반드시 인터페이스 추상화
  - 리전 `asia-northeast3` 로 인스턴스를 잡는다 —
    `DefaultInstance` 는 us-central1이라 그대로 쓰면 404
  - 에뮬레이터 스위치 `UseFunctionsEmulator(origin)` 배선 완료
  - 타임아웃은 `FirebaseTimeouts.CallableMilliseconds`
- [x] `ping` 을 `onCall` 로 교체 — `functions/src/commands/ping.ts` (v2 `onCall`)
- [x] **응답 채택 계약 구현** — `ServerSaveCommands.InvokeAsync` → `PlayerSaveCloud.AdoptServerResult`
      → `DataSaveManager.AdoptServerSlots` + `OnServerSlotsAdopted` 단일 창구
- [x] 오류 규약 확정 — `Core/Firebase/CloudFailureClassifier.cs` 가 `ECloudFailureKind` 로
      `Transient`/`Rejected`/`Unusable` 3분할

**남은 미완 2건**

1. `firebase deploy --only functions` 1회 (CLI 로그인 선행)
2. **클라 Firestore 에뮬레이터 스위치** — 지금은 함수만 로컬로 가고 Firestore는 프로덕션에 붙는다

**완료 판정**: 게임 실행 → 익명 로그인 → `ping` onCall → uid가 서버 로그에 찍히고 클라가 응답 수신.
에뮬레이터로도 같은 왕복.

---

### R1 — 룰 1차 배포 🟡

**목표**: 전면 개방을 끝낸다. **아직 슬롯은 동결하지 않는다** — 게임이 지금 그대로 돌아야 한다.

**R1은 재작성이 아니다.** `.prod` 는 커밋 `809d040d3` 에서 메타 5 + 슬롯 10 키로 이미 교정됐다 —
`firestore.rules.prod` 를 출발점으로 보강하고, 회귀 테스트로 고정하는 일이다.

**슬롯 *내부* 필드는 검증하지 않는다** — `ProfileSaveData` 의 `nickname`/`avatarId`/`frameId` 는
초기값이 없어 신규 계정 첫 업로드에서 null 로 실린다. `profile.nickname is string` 을 요구하면
신규 유저가 막힌다.

- [x] `envs/{env}/users/{uid}/save/{doc}`
  - `isOwner()` · `env in ['live','test']` · `doc == 'current'`
  - `keys().hasOnly([메타 5 + 슬롯 10])` — 알 수 없는 필드 차단
  - 메타 타입·크기: `schemaVersion is int > 0` · `revision is int` ·
    `deviceId.size() == 32` · `appVersion.size() <= 64` · `updatedAt == request.time`
  - `create`: `revision == 1` / `update`: `revision == resource.data.revision + 1` 이고
    `schemaVersion >= resource.data.schemaVersion` / `delete: if false`
  - 슬롯별 형식 검증 — **각 슬롯이 있으면 `is map`, 없으면 통과**까지만 한다.
    슬롯 안쪽은 안 본다(위 `ProfileSaveData` null 이유). 크기 상한은 룰에서 문서 크기를
    잴 수 없어 못 넣는다 — 클라의 `DOCUMENT_MAX_BYTES` 가 계속 갖는다
- [x] `envs/{env}/specs/{table}` 및 `rows/{id}` —
      `allow read: if request.auth != null`, `write: if false`.
      read 를 여는 이유는 `Assets/Scripts/OutGame/Spec/BattleContentSync.cs` 가
      **클라이언트 SDK로 이 경로를 직접 읽기** 때문이다.
      현재 `.prod` 는 read 도 `if false` 라 **그대로 배포하면 클라 부트의 스펙 동기화까지 죽는다**.
      `write` 는 R3(a) 까지 임시 개방한다 — 미결 #6 결정 참조
- [x] 그 외 전부 거부 — `match /{document=**}` catch-all deny 를 글자로 남겼다
- [x] `firebase-security-rules-auditor` 스킬로 감사 1회 — **Major 2건을 잡아 고쳤다**
  - **슬롯 10개가 전부 optional 이라 세이브를 통째로 비울 수 있었다.** `hasAll` 이 메타 5키만
    요구하고 슬롯 검사도 "없으면 통과" 라, 메타 5키 + `revision+1` 만 보내면 소유·재화·랭크가
    사라진다. `delete: if false` 로 막으려던 세이브 소멸이 **필드 생략으로 우회**됐다.
    → `hasAll` 을 15키로. `ToFieldMap` 은 Overwrite 와 짝이라 언제나 15키를 실어 회귀 없음
  - **`create` 에 schemaVersion 상한이 없었다.** `update` 는 `>=` 라 못 내리므로, 첫 문서를
    `999999` 로 만들면 영구 고착되고 룰 층에 복구 경로가 없다 → `create` 는 현재 버전과
    정확히 같을 것을 요구. **`UserSaveData.VERSION` 을 올리면 이 줄도 같이 올려야 한다**
    (안 올리면 신규 계정만 못 만들어지는 부분 고장 — 회귀 테스트 `8b` 가 잡는다)
- [x] rules 회귀 테스트 — `Tools/firestore-rules-tests/` 에 **33케이스**, 전부 통과.
      요구된 3종(남의 uid 읽기 / revision 건너뛰기 / 정상 왕복) 포함.
      포트 8081 · 프로젝트 `tcg-rules-test` 로 갈라 루트 에뮬레이터(8080)와 안 부딪힌다.
      룰은 `initializeTestEnvironment` 가 `.prod` 를 직접 주입하므로 `firebase.json` 포인터와 무관하다.
      **뮤테이션 검증 3회**: `hasOnly` 에서 슬롯 키를 빼면 통과 케이스 4개가 깨지고,
      감사 이전 룰 상태를 재현하면 `7c`·`7d`(세이브 비우기)가, `create` 의 버전 고정을 지우면
      `8b` 가 깨진다 — 테스트가 룰을 실제로 물고 있다

**같이 처리해야 하는 것**

- **`SpecFirestoreUploader`** — API key로 REST write를 한다. **결정(미결 #6): specs `write` 를
  R3(a) 까지 임시 개방으로 둔다.** 로드맵이 적어 둔 "관리자 uid 예외"안은 성립하지 않는다 —
  이 업로더는 인증 토큰 자체가 없어 `request.auth` 가 null 이라 uid 로 걸러낼 대상이 아니다
**하네스 픽스처는 반드시 클라 실제 산출물이어야 한다 — 한 번 데였다**

기준 룰의 재화 검증(`balances.Diamond is int` 등 4키 전부 요구)을 "클라는 Gold 하나만 넣으니
신규 계정이 전부 막힌다"고 판정했는데 **틀렸다**. `CurrencySaveData.Normalize` 가
`ECurrencyType.Count` 까지 순회하며 없는 키를 0으로 채우고, `CurrencyManager` 가 Init·Save
양쪽에서 그걸 먼저 부른다. 클라 문서는 언제나 4재화를 싣는다.

원인은 픽스처가 손으로 만든 `{ Gold: 100 }` 이었다는 것이다. **합성 페이로드는 옳은 룰을
틀렸다고 판정하게 만들고, 그 판정을 믿으면 룰을 약하게 고치게 된다** — 실제로 존재 검사를
끼워 넣어 키 삭제·타입 변조를 열 뻔했다. 테스트 `13b` 가 이 계약을 반대 방향에서 못박는다.

**감사에서 나왔지만 지금 안 고친 것 — R9로 넘긴다**

- **중첩 크기 무제한** — `keys()` 는 top-level만 본다. 슬롯 안을 1MiB까지 채워 revision마다
  갱신하면 쓰기·스토리지 비용이 유저 한 명으로 팽창한다. `ownership.cardIds.size()` 류
  개수 상한 3줄로 완화할 수 있으나, **슬롯 안쪽을 보기 시작하는 순간 `ProfileSaveData` null
  같은 사고가 다시 열린다.** 근본 해법은 R9의 서버 쓰기 이관이다
- **`deviceId` hex 미검증 · `appVersion` 문자셋 무검증** — 자기 문서·로그 오염 수준. R9
- **익명 인증이라 `request.auth != null` 은 앱에 박힌 API key만 있으면 누구나 얻는다** —
  `specs` 읽기는 사실상 공개다. 마스터데이터라 피해는 낮지만, **"인증됨"을 신뢰 신호로 쓰는
  규칙을 앞으로 추가하지 마라**
- **`specs` 임시 개방의 진짜 위험은 변조가 아니라 무제한 문서 생성**이다 — `{envId}`·`{table}`
  둘 다 와일드카드라 미인증자가 임의 경로에 문서를 찍어낼 수 있다. R3(a) 이관이 유일한 완화책

**낙관적 잠금은 룰 층에서 실제로 막힌다** — 룰의 `resource` 읽기가 커밋과 원자적이라
같은 rev N에서 출발한 두 쓰기가 둘 다 N+1로 착지할 수 없다. 클라 `RunTransactionAsync` 는
필수가 아니라 오류 품질용이다. 다만 막는 것은 **인터리브지 lost update 가 아니다** —
`SetOptions.Overwrite` 라 충돌 후 원격을 재채택해 다시 밀면 상대 기기 변경분이 통째로 사라진다.
`PlayerSaveCloud` 가 충돌 시 재시도 대신 `BlockSession(RemoteAhead)` 로 세션을 접는 건 맞는 선택이다.

- **디버그 경로 점검** — R1 단계에선 살아 있지만 R9 동결 때 전부 죽는다:
  `OutgameDebugActions.GrantCurrency`(**`#if UNITY_EDITOR` 가드 없음**) · `CardGrowthManager.DebugMaxAll` ·
  `OwnershipManager.GrantEntireCatalog` · `RankManager.SetTierForDebug` ·
  `TournamentProgress.ResetForDebug` · `UnlockAllCardsButton`

**실클라 × 닫힌 룰 검증 — update 통과, create 미검증**

닫힌 룰을 에뮬레이터에 걸고 Unity 클라를 붙였다. 기존 문서 채택 → 저장 2회 → revision 4,
**룰 거부 0건**. `deck`·`tutorial`·`cardGrowth` 상한도 실제 문서에서 안 걸렸다.

`create` 는 못 봤다. 문서를 지우고 재부트하면 **Unity Firestore 네이티브 클라이언트가
에디터 2회차 Play 에서 "client is offline" 을 뱉는다**(네이티브 인스턴스가 도메인 리로드를
넘어 살아남는다). 룰 문제가 아니라 에디터 완전 재시작이 필요한 항목이다. 여기에
커밋 `c556b6a2d` 의 단발 `ReadAsync`(재시도의 주체는 복구 화면의 사람)가 겹쳐,
이 일시적으로 보이는 조건이 첫 시도에 곧바로 부트 실패로 간다 — 설계 의도와는 일치하지만
에디터 반복 Play 루프에서는 매번 걸린다.

**그래서 `create` 는 하네스가 유일한 방어선이다.** 실클라가 만드는 첫 문서 모양을 그대로
픽스처로 떠서 `14`·`14c`·`14d` 로 덮었다(빈 성장 map · 빈 덱 슬롯 · profile 3필드 null).

**룰 진실원이 `firestore.rules` 하나로 통일됐다(2026-08-27).** `firestore.rules.prod` 는 삭제했다.

통합처인 `origin/박형석작업용` 이 이미 그 구조로 가 있었고, 그쪽 룰이 우리 것보다 앞서 있었다 —
`.prod` 이원화 폐기 · `specs` 를 `admin` 커스텀 클레임으로 폐쇄 · `matches` 중첩 거부 ·
슬롯 크기 상한(감사가 R9로 미룬 항목). 그래서 **그 파일을 베이스로 삼고 우리 감사분을 얹는
합집합**으로 갔다. 덮어쓰기가 아니다.

우리가 얹은 것 중 실제로 커버리지를 더한 건 **`create` 의 `schemaVersion` 고정 하나**다.
`hasAll` 15키와 `revision > 0` 은 기준 룰의 슬롯별 검증이 이미 같은 구멍을 막고 있어
잉여였다(뮤테이션으로 확인 — 빼도 8b 만 깨진다). 명시성·방어 겹으로 남겨 뒀다.

**`specs` 결정이 바뀌었다(미결 #6 갱신)**: 임시 개방 → **`admin` 클레임 전용**.
기준 브랜치에 `functions/scripts/grant-admin.js` 가 있어 R3(a) 의 절반이 이미 있다.
대가로 **`SpecFirestoreUploader` 는 ID 토큰을 실기 전까지 죽는다** — 룰 주석에도 적혀 있다.

**배포 완료(2026-08-27).** 전면 개방(`allow read, write: if true`)은 끝났다.

1. ~~포인터 스왑~~ — 불필요해졌다. `firestore.rules` 가 곧 진짜 룰이고 `firebase.json` 은 이미 그걸 가리킨다
2. [x] 회귀 33케이스 재실행 전부 통과 → `firebase deploy --only firestore:rules --project bm-cardbattle`
       (`firestore.indexes.json` 이 없으므로 `--only firestore` 를 그냥 치면 안 된다).
       릴리즈된 곳은 `projects/bm-cardbattle/releases/cloud.firestore/cardbattle` —
       **CLI 완료 문구는 DB 이름을 생략하고 `released rules ... to cloud.firestore` 라고만 찍는다.**
       명명 DB(`cardbattle`)로 갔는지는 `--debug` 의 PATCH 경로로만 확인된다.
       이 프로젝트에 `(default)` DB 는 아예 없다
3. [ ] 배포 후 정상 플레이 왕복 + 타 uid 접근 차단 실측 — **사람이 1회 돌려야 한다**

**실측은 F8 디버그 오버레이의 CLOUD 블록 하나로 끝난다.** 계기판 `CLOUD LIVE / Ready / rev N` +
버튼 셋(`PING` · `BUMP` · `DENY?`). 두 ContentProfile 모두 `useLocalEmulators: 0` 이라
에디터 Play 가 곧 실서버 왕복이다.

| 잴 것 | 조작 | 통과 판정 |
|---|---|---|
| 부트 채택 | Play | 로비 진입. 막히면 `LoadingCoverView` 복구 화면이 뜬다 |
| 읽기 + 서버 시야 | `PING` | `database=cardbattle` · `schemaVersion` == `documentSchemaVersion` · `revision` 이 계기판과 일치 |
| **클라 쓰기(룰의 본무대)** | `+G`/`+D`/`+E`/`+S` | `rev` 가 정확히 +1, `Ready` 유지. 4재화를 다 눌러야 `balances` 검증 4줄을 전부 밟는다 |
| 서버 쓰기 + 채택 계약 | `BUMP` → 곧바로 `+G` | 로그의 `→ N` 과 `(채택 후 N)` 이 같고, 이어지는 클라 저장이 N+1 로 통과 |
| **타 uid·미지 env·매치 차단** | `DENY?` | `규칙 진단 3/3 차단` |

`DENY?` 는 실클라 SDK로 세 경로를 `Source.Server` 로 실제 읽어 본다
(`OutgameDebugActions.ProbeRuleDenials` → `PlayerSaveCloud.ProbeReadDeniedAsync`):
남의 uid · `envs/dev/...` · `matches/...`. **셋 다 없는 문서를 겨눈다** — 룰이 문서 존재보다
소유자·환경을 먼저 보므로 없어도 `permission-denied` 가 나와야 하고, "없어서" 통과하면 그건 룰이 열린 것이다.
캐시 히트는 룰을 안 거치므로 `Source.Server` 가 필수다.

> 콘솔 Rules Playground 로도 같은 걸 볼 수 있지만 그건 **룰만** 평가한다. R1 이 남긴 실측의
> 값어치는 "룰 문법이 맞나"가 아니라 **실클라 × 실배포 룰** 이라 `DENY?` 쪽이 진짜 판정이다.

> **에디터 2회차 Play 의 "client is offline" 은 룰 문제가 아니다** — Firestore 네이티브 인스턴스가
> 도메인 리로드를 넘어 살아남아 생긴다. 보이면 Unity 를 완전 재시작하고 다시 재라.
> 룰을 의심하기 전에 이걸 먼저 배제해야 한다.

> **배포 즉시 `SpecFirestoreUploader` 가 죽는다** — specs `write` 가 `admin` 클레임 전용이 됐고
> 업로더는 웹 API key + REST 라 `request.auth` 가 null 이다. 스펙 갱신은 R3(a) 의
> `specUpload` callable 이관까지 막힌다(`functions/scripts/grant-admin.js` 가 절반을 갖고 있다).

> **회귀 하네스의 `node_modules` 가 깨져 있었다** — 커밋된 `package-lock.json` 은 멀쩡한데
> `@firebase/rules-unit-testing` 실체가 없어 `ERR_MODULE_NOT_FOUND` 로 죽는다.
> `npm install` 한 번이면 복구된다. "테스트가 안 돈다"를 "룰이 틀렸다"로 읽지 말 것

**순서 제약**: callable 경로의 `Rejected`/`Unusable` UX 표면이 서기 전에 룰을 닫으면
룰 거부가 유저에게 아무 표면 없이 삼켜진다. 배포는 그 뒤여야 한다.

**완료 판정**: 규칙 배포 후 정상 플레이 왕복이 되고, 다른 uid 문서 접근이 막힌다.

---

### R2 — 로컬 캐시 제거 ✅

**커밋 `d19590e2b` 에서 완료.** 단 **미결 #4(쓰기 실패 시 클라 메모리)는 여전히 미결이다** —
아래 분류기 하이브리드는 R2가 채택한 방침일 뿐 결정 항목이 닫힌 것은 아니다.

**목표**: 진실원이 서버 문서인 이상 캐시 봉투는 순수 부채다. **R0/R1과 독립이라 병행 가능.**

본체는 커밋 `d19590e2b`(온라인 전용 부트 — 재시도 경로·오프라인 폴백·로컬 캐시 폐기)에서 처리됐다.
아래 삭제 목록은 전수 grep 0건으로 확인(2026-08-27): `OutGame/Save/1.Repository/` 폴더 자체가 없고,
`PlayerSaveCacheEnvelope`·`FallbackToCache`·`IsCacheOwnedByOther`·PlayerPrefs `ownerUid` 전역 참조 0건,
`GameManager` 의 `SetRepository` 도 없다. `CreateSnapshot` 만 dirty 대조용으로 의도대로 남았다.

사라진 것:

- [x] `OutGame/Save/1.Repository/` 전체 — `IRepository` · `IAtomicRepository` ·
      `JsonFileRepository` · `PlayerPrefsRepository`(이미 dead). 디렉터리째 없다
- [x] `2.Domain/PlayerSaveCacheEnvelope`
- [x] `DataSaveManager` — `SetRepository` · `TryLoadCache` · `WriteCache` ·
      `MarkUploadedRevision` · `HasLocalSave`(dead)
- [x] `PlayerSaveCloud` — `AdoptUnsyncedCache` · `FallbackToCache` · `IsCacheOwnedByOther` ·
      PlayerPrefs `ownerUid`
- [x] `GameManager.Initialize` 의 `SetRepository(new JsonFileRepository(...))`

남긴 것: `DataSaveManager.CreateSnapshot` — dirty 판정과 "값이 안 바뀌었으면 revision 안 올림"
최적화가 여기 걸려 있다. `PlayerSaveCloud` 3곳에서 쓴다. (`SnapshotOf` 는 통합돼 사라졌다)

**상태머신**: 의도대로 갈렸고, P3에서 `Blocked` 가 더해져 3분할이 됐다.

| 상태 | 사건 | 표면 |
|---|---|---|
| `Failed` | 부트 채택 실패 | 복구 화면(`LoadingCoverView`) |
| `Offline` | 부트 후 쓰기 실패 중 | 배너(`CloudSyncBannerView`) |
| `Blocked` | 이 클라로는 더 못 쓴다 (`ECloudBlockReason` 3종) | 재시작 모달 |

**미결 #4 방침(항목은 아직 열려 있다) = (a)+(b) 하이브리드, 분류기가 가른다.** `CloudFailureClassifier.Classify` 가
`Transient` 면 메모리 재시도(`Offline` + `PlayerSaveCloud.RetryPending` — 못 올린 변경분만
다시 태우고 재-pull 은 하지 않는다. 복귀 훅은 `GameManager` → `FirebaseManager.RetryPending`),
`Rejected`/`Unusable` 이면 `BlockSession` 으로 세션 차단.
도메인 거절(재화 부족 등) 한 번에 재시작을 요구하지 않기 위한 갈림이다.

**`SpecSnapshotCache` 는 R2 범위 밖이다** — `persistentDataPath/spec-cache/{env}.json` (+ `.bak` 회전)에
지금도 로컬 파일을 쓴다. 이건 **세이브 캐시가 아니라 스펙 캐시**라 삭제 대상이 아니었다.
아래 완료 판정을 볼 때 이 파일을 보고 오판하지 말 것 — 판정 대상은 `Save/` 폴더다.
스펙 캐시의 존폐는 R3에서 다룬다.

**완료 판정(미실행 — 사람이 1회 돌려야 한다)**:

- [ ] `persistentDataPath/Save/` 에 파일이 생기지 않는다
- [ ] 정상 부팅·저장 왕복
- [ ] 비행기 모드 부팅 → 재시도 화면 (도구는 준비됨: 커밋 `933e0c14c` 방화벽 토글 + 에디터 메뉴)

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

> ⚠️ **백지에서 설계하지 마라 — 이미 상당 부분이 `origin/박형석작업용`(hspark-maker)에 있다.**
>
> | 있는 것 | 위치 |
> |---|---|
> | `submitMatchResult` onCall (배포되어 실사용 중) | `functions/src/index.ts` |
> | 양쪽 제출 대조 판정 (순수 모듈) | `functions/src/matchResult.ts` |
> | 클라 제출 + 재시도 큐 | `Assets/Scripts/Network/MatchResultSubmission.cs` |
>
> 설계가 아래 티켓 방식과 **다르다**. 매치 문서 `envs/{env}/matches/{matchId}` 에 두 플레이어가
> 각자 제출하고, 서버가 **양쪽을 대조**해 `pending`/`flagged`/`confirmed` 를 정한다
> (nonce 교차 · deckHash 교차 · `finalStateHash` 일치 · state chain 이 1스텝 차이까지 허용 ·
> `remaining` 교차 · `contentFingerprint`). 즉 **미결 #9(멀티 결과 서버 대조)가 이미 구현돼 있다.**
>
> 다만 **보상 지급은 아직 없다** — 매치 문서에 판정만 쓰고 세이브 문서는 안 건드린다.
> R8이 할 일은 "티켓을 새로 설계"가 아니라 **이 판정 결과를 지급으로 잇는 것**일 수 있다. 먼저 읽어라.
>
> **합류 시 충돌 3건**: (a) `functions/src/index.ts` 가 양쪽에서 갈린다(우리 `ping`·`devBumpRevision`
> ↔ 그쪽 `submitMatchResult` + 구 `onRequest` ping) (b) `MatchResultSubmission.cs` 가
> `Firebase.Functions` 를 직접 참조한다 — 우리 미결 #12(접점 단일화)와 **관용구가 갈린다**.
> 어느 쪽이 위반인지는 정해진 바 없다. 합류 방향은 (a) 우리가 그쪽으로 들어가는 것으로 정해졌지만,
> 그건 방향이지 관용구 우선순위가 아니다 — 건별로 합류 시점에 정한다
> (c) 그쪽은 실패분을 **PlayerPrefs 큐**에 쌓는다 — R2가 로컬 저장을 걷어낸 것과 방향이 반대이고,
> **미결 #4의 세 번째 선택지**(영속 재시도 큐)를 사실상 구현한 것이다
>
> **R1 룰과는 충돌하지 않는다** — 매치 문서는 Admin SDK 로만 쓰이고 클라는 callable 응답으로
> 결과를 받는다. 룰에 `envs/{envId}/matches/{matchId}` 를 명시적 거부로 적어 두었다(테스트 `16c`).

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
| 1 | **App Check** | R1 착수 전 | callable은 인증만으로 남용을 못 막는다. (a) 강제 (b) 프로토 동안 미도입 + `maxInstances` 상한만 · **결정(2026-08-27): (b) 프로토 동안 미도입.** `setGlobalOptions` 의 `maxInstances: 10` 상한만 유지한다. App Check 는 룰이 아니라 callable 남용 방지용이라 R1과 독립이고, 도입하면 Unity 클라 배선 + 에뮬레이터 디버그 토큰 배선이 따라붙는다 |
| 2 | **관리자 판별** | R3 착수 전 | (a) custom claim(`admin: true`) + 부여 스크립트 (b) 서비스 계정 + 별도 CLI |
| 3 | **중첩 스펙 업로드** | R3 첫 판단 | 업로더가 `int`/`long`/`string` 만 지원. (a) 평탄한 행 재저작 (b) 타입 지원 확장 |
| 4 | **쓰기 실패 시 클라 메모리** | 미결 | (a)+(b) 하이브리드 — `CloudFailureClassifier` 가 `Transient` 는 메모리 재시도 큐(`RetryPending`), `Rejected`/`Unusable` 은 즉시 세션 차단(`BlockSession`) · **R2가 완료됐음에도 이 항목은 여전히 미결이다**(2026-08-27) |
| 5 | **튜토 무료 한 방 상태** | R6 | 정적 필드라 서버가 못 본다. `tutorial` 은 클라 소유라 거기 못 둔다 → 서버 소유 키 신설 |
| 6 | **R1 ↔ R3 순서** | R1 착수 전 | 룰을 닫으면 `SpecFirestoreUploader`(API key)가 죽는다. (a) R3를 R1에 붙여 진행 (b) 관리자 uid 임시 예외 · **결정(2026-08-27): (c) specs 는 `read: request.auth != null` / `write` 는 임시 개방.** R3(a) 에서 `specUpload` callable 이관과 함께 닫는다. **(b)안은 성립하지 않는다** — `SpecFirestoreUploader` 는 웹 API key + REST 라 `request.auth` 가 아예 null 이어서 uid 화이트리스트로 통과시킬 수 없다 |
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
