# 재화 독립 서비스 분리 — 인계 문서

> 최종 갱신 2026-08-28 · 브랜치 `feature_Firestore` · HEAD `1d0d1aa31`
> 상위 문서: `SERVER_VALIDATION_ROADMAP.md` (이 작업은 그 R6~R9를 재화 축으로 앞당긴 것)

## 왜 하는가

인앱결제가 붙을 예정이라 재화는 게임 내부 도메인으로 남지 않는다. 실화폐가 오간다.
지금 구조의 문제 셋: 잔액이 세이브 문서의 `currency` 슬롯 안에 있고 · 클라가 그 슬롯을 직접 쓰며 · 배포 단위가 하나라 결제 검증 코드와 장애 반경이 게임 로직과 섞인다.

## 확정된 결정 — 재론하지 말 것

| # | 결정 |
|---|---|
| 1 | **배포 단위 분리.** `firebase.json` 에 codebase `currency` 추가 |
| 2 | **저장소 분리.** `envs/{env}/users/{uid}/wallet/current` + `ledger/{txId}`. 세이브에서 `currency` 슬롯 제거(15키→14키) |
| 3 | **IAP 는 구조·자리만.** 원장 스키마와 진입점 자리만. 기록·멱등·영수증 검증은 IAP 착수 때 |
| 4 | **원자성 우선.** 지갑 문서를 쓰는 코드는 단일 원본. 복합 명령은 default codebase 에서 save+wallet 두 문서를 한 트랜잭션에 쓴다. Firestore 트랜잭션은 문서는 걸쳐도 **프로세스 경계는 못 넘는다** — 독립 functions 가 보장하는 것은 배포·의존성·IAP 진입점 격리이지 쓰기 배타성이 아니다 |
| 5 | **소유권 이전 먼저, 저장소 전환 나중.** 도메인 callable 이 당분간 기존 `currency` 슬롯에 쓴다. 그래야 모든 단계가 플레이 가능하다 |

## 지금 상태

**커밋 2개**

- `6c95b114a` C1·C2 — `functions-currency/` codebase · 미러 동기화 장치 · `walletStore.ts` · 룰 지갑 블록 · 순수 회귀 · 하네스 43케이스
- `1d0d1aa31` C3 — `claimReward`(랭크 티어 · 토너먼트 정점) + 클라 2곳 전환

**배포 상태 (실측 2026-08-28)**

| 함수 | 상태 |
|---|---|
| `currencyPing` (codebase `currency`) | 배포됨 · URL POST **401**(정상) |
| `claimReward` (codebase `default`) | **미배포 — 404.** 클라만 올리면 수령이 `not-found` 로 떨어진다 |
| `firestore.rules` 지갑 블록 | **미배포.** 지금은 무해(아무도 지갑을 안 씀), C6 전엔 필수 |

**주의 — "재화 로직이 currency codebase 로 갔다"가 아니다.** 지금 그 codebase 에 있는 것은 진단용 `currencyPing` 뿐이다. `walletStore.ts` 는 callable 이 아니라 라이브러리라 배포 대상이 아니고, 실제로 재화를 움직이는 `claimReward`·`openPack` 은 세이브 슬롯도 함께 만져서 default codebase 에 있다(결정 4). 지갑만 만지는 명령은 C6 에서 이사한다.

---

## 즉시 할 일

### 1) 배포

```bash
firebase deploy --only firestore:rules --project bm-cardbattle
firebase deploy --only functions:claimReward --project bm-cardbattle
```

- **`--only functions` 를 그냥 치면 abort 한다.** 남의 함수 `lockDeck` 삭제 여부를 묻고 non-interactive 라 멈춘다. 함수를 지정해라
- `--only functions:currency` 는 codebase 라벨이 조회 범위를 갈라 abort 하지 않는다(실측)

### 2) 배포 직후 판정 — 배포 로그는 근거가 아니다

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST \
  -H "Content-Type: application/json" -d '{"data":{}}' \
  https://asia-northeast3-bm-cardbattle.cloudfunctions.net/claimReward
```

**401 = 정상**(함수가 실행됐다) · **403 = Cloud Run invoker 바인딩 없음** → 콘솔에서 **소문자** 서비스명 `claimreward` → 권한 → `allUsers` + `Cloud Run 호출자`. 바인딩은 서비스를 새로 만들 때만 걸려서 신규 함수마다 이 함정을 밟는다.

### 3) 실기 왕복 (사람 1회) — 앞이 실패하면 뒤는 볼 필요 없다

1. 랭크 티어 수령 → 잔액이 `Reward` 표 값만큼 늘고 `revision` 정확히 +1
2. 토너먼트 정점 격파 → 수령 → 잔액 증가 **그리고** 그 정점이 Cleared 로 굳고 `pendingRewardNodeId` 가 빈다(콘솔 문서 확인)
3. **이미 받은 티어를 다시 수령 → 경고만, 로비 유지.** 재시작 모달이 뜨면 실패다(거절이 `permission-denied` 가 아니라는 뜻)
4. 분출·잔액 롤업이 왕복 뒤로 밀렸으니 숫자가 뒤로 튀지 않는지

---

## 남은 작업

### C4 — 성장 2종

`enhanceCard` · `enhanceKeyword`. 클라 `CardGrowthManager.cs:173` · `KeywordGrowthManager.cs:118` 의 `CurrencyManager.Spend` 를 걷어낸다.

근거 표 전부 확보됨: `CardEnhanceRule`(단일 행 — `baseEnhanceCost 25` · `costGrowthPerLevel 50` · `baseSuccessPermille 1000` · `rateDropPerLevelPermille 0` · `maxLevel 4` · `maxLimitBreak 3` · `hpPerLevel 4`) · `CardEnhance`(레벨별 오버라이드 3행, 비용 재화 **Shard**) · `KeywordEnhance`(키워드 6종, 비용 재화 **Energy**).

- 성공률 RNG 를 서버로 옮긴다(`openPack` 의 `randomInt` 관용구). 현재 `successPermille` 이 전 행 1000이라 **강화는 항상 성공** — RNG 경로는 만들되 지금은 발화하지 않는다
- 현행 클라는 **실패해도 차감이 영속되고 환급이 없다**. 서버로 옮기면 이 갈래를 한 트랜잭션으로 정리할 수 있다
- 거절 사유는 `NotAffordable`(클라 `EEnhanceOutcome`)

### C5 — 전투·디버그 2종

`RewardService.cs:72`(전투 보상) · `OutgameDebugActions.cs:179`(디버그 지급).

**전투 골드 공식은 이미 있다** — `functions/src/payout.ts` 의 `computeCurrencyPayout(won, remaining, rows)` 가 `max(remaining × win.perCard, win.floor)` / 패배 `lose.flat` 을 낸다. 새로 쓰지 마라. 배선만 남는다.

이 커밋에서 `CurrencyManager.Earn/Spend/Save` 를 삭제한다 → **클라 재화 writer 0**.

### C5.5 — 앨범 구성 스펙 표 승격 (C6 선행, 사용자 승인됨)

서버가 "도감 페이지 완성" 을 판정할 근거가 **어느 표에도 없다**. `Card` 표에 테마·페이지 컬럼이 없고 `AlbumSpec.cs` 도 보상만 감싼다. `CardAlbumConfig` 는 SO 전용이다.

- 데이터는 평탄화 가능: `CardAlbumConfig.themes[].pages[].cardIds` → 행 `themeId | pageId | cardId | order`
- **디자이너 시트가 필요 없다.** `SpecFirestoreUploader.Upload` 는 SpecData 매니저에 묶여 있지만 그 **뒤의 커밋 기계(메타 `updateTime` precondition · 원자 커밋 · 500writes/10MiB 가드)는 `TableSnapshot` 만 받는다** — `CardAlbumConfig.asset` 에서 스냅샷을 직접 지어 넣는 오버로드 + 메뉴 항목이면 재사용된다
- **클라는 손대지 않는다.** `SpecPayloadCodec.TableNames` 에 넣을 이유가 없다 — 서버 전용 판정 근거이고, 넣으면 부팅 왕복만 는다
- Unity 에서 사람이 1회 실행(admin 클레임 로그인 필요)

**토너먼트 챕터 수령도 같은 구멍이다**(챕터↔정점 대응이 `TournamentConfig` SO 에만 있다). 같이 풀 것.

### C6 — 저장소 전환

- `ensureWallet` callable(**default codebase**. 지갑 생성과 세이브 v7→v8 승급이 **같은 트랜잭션**이어야 잔액을 안 잃는다)
- `SCHEMA_VERSION 8` + **전환 전용 `MIN_WRITABLE_SCHEMA_VERSION = 7`**. 없으면 v7 문서를 가진 구 클라의 `openPack` 이 `failed-precondition` → `Unusable` → `BlockSession` 으로 전부 끊긴다
- 룰: `isValidSave()` 를 `hasOnly(15키)` + `hasAll(currency 뺀 14키)` 로 바꾸고 currency 검증을 `(!hasAny(['currency']) || 기존 검증)` 으로 감싼다 → **구/신 클라 공존이 성립한다**
- 모든 callable 을 슬롯에서 지갑으로. `mutateSave` 에 지갑 참여 옵션 추가(**읽기는 콜백 진입 전에** — Firestore 는 모든 읽기가 모든 쓰기보다 앞서야 하고 `openPack` 처럼 mutate 가 재실행되면 순서가 깨진다)
- 클라: `WalletCloud`·`WalletCommands`·`WalletPatch` 신설. **`PlayerSaveCloud.AdoptServerResult` 의 revision+1 단언은 손대지 않는다** — 지갑 응답을 그 경로로 안 보내는 것이 답이다
- **`wallet.rev` 는 단조 증가만 보장한다**(세이브 revision 과 달리 "정확히 +1" 이 아니다). 지갑은 두 codebase 가 쓰고 장차 결제 웹훅처럼 클라가 모르는 정당한 쓰기가 생긴다 — +1 을 강제하면 첫 결제에서 전 유저 세션이 끊긴다
- 지갑만 만지는 명령(`claimBattleReward`·`devGrantCurrency`)을 `functions-currency/` 로 이사
- `SetOptions.Overwrite` 를 이용한다 — v8 클라의 `ToFieldMap` 에서 `FIELD_CURRENCY` 를 빼면 다음 업로드가 원격 잔여 필드를 알아서 지운다. **반대로 승급 전에 v8 클라가 저장하면 잔액 원본이 사라진다** → 부트에서 `ensureWallet` 이 첫 업로드보다 반드시 앞

### C7 — 조이기 (구 클라 소멸 후)

룰을 14키 전용으로 조이고 `MIN_WRITABLE_SCHEMA_VERSION` 제거. 하네스의 15키 케이스를 `assertFails` 로 뒤집는다.

---

## 통합처(`origin/박형석작업용`)와 겹치는 것 — 새로 만들지 마라

머지로 들어온 것들이다.

| 위치 | 내용 |
|---|---|
| `functions/src/payout.ts` | `RewardRow`·`parseRewardRows`(**이번 작업에서 `id`·`order` 를 선택 필드로 덧붙이고 `resolveRewards` 를 같은 파일에 뒀다**) · `resolveTierIndex` · `computeRankPayout`(전투 랭크 점수) · `computeCurrencyPayout`(전투 골드) |
| `commands/submitMatchResult.ts` | 멀티 대조 확정 시 `envs/{env}/users/{uid}/payouts/{matchId}` 에 지급 예정액을 쓴다 |
| `commands/claimPayout.ts` | 클라가 그 우편함을 `list`/`ack` |

**`payouts` 는 "줄 것의 기록"이지 지급이 아니다.** `ack` 는 `status: "claimed"` 만 찍고 잔액을 안 건드린다 — 실제 크레딧은 여전히 클라가 한다. 이 작업이 닫으려는 구멍이 거기다. 그리고 그 경로는 **멀티 전용**이라 싱글 전투 보상은 아직 `RewardService.GrantBattleReward` 가 클라에서 준다.

**랭크 축이 둘이라는 것을 혼동하지 마라** — `computeRankPayout` 은 전투로 인한 **점수 변동**, `claimReward(Rank, tier)` 는 **티어 보상 수령**(`claimedTiers` 낙인)이다.

C6 에서 `claimPayout` 의 `ack` 가 지갑에 크레딧하는 자리가 된다. **저쪽 소유 코드라 착수 전 조율이 필요하다.**

---

## 스펙 표 실태 (실측 2026-08-28)

`envs/test/specs` **12표**, **`envs/live/specs` 0표** — 이 작업의 모든 검증은 `test` 에서만 성립한다. 릴리즈 전 `live` 업로드는 별건(R3 몫).

**`Reward` 표(84행)가 통합 진실원**이다. 컬럼 `id | ownerType | ownerId | order | rewardType | rewardId | amount`. 이 브랜치의 세이브 키와 정확히 일치한다 — 앨범 `t:Theme_Nature`/`p:Theme_Nature/P1`/`b` · 정점 `node_01`~`node_24` · 챕터 `chapter_01`~`chapter_04` · 랭크 티어 인덱스 `0`~`19` · 전투 `win.perCard`=Gold 10 / `win.floor`=Gold 10 / `lose.flat`=Gold 5. **24정점 전부 보상이 저작돼 있다.**

표가 실제로 올라와 있는지는 브랜치 소스가 아니라 Firestore 를 직접 조회해 확인한다(자격증명 불필요 — 룰의 스펙 읽기가 `isSignedIn()` 뿐이다). 방법은 `functions/scripts/check-pack-spec.js` 관용구.

---

## 판정을 틀리게 만드는 것들

- **배포 로그는 호출 가능을 증명하지 않는다.** `Deploy complete!` 를 찍고도 403인 전례가 있다(`openPack`). 판정은 URL POST 의 401/403 으로만
- **`firebase functions:log` 는 3~4분 늦는다.** 방금 한 왕복이 안 보인다고 "호출이 안 갔다" 로 읽으면 멀쩡한 코드를 뒤진다
- **룰 하네스는 종료코드가 거짓말한다.** JDK 가 없으면 실패해도 exit 0 이다 — `# pass N` 출력 줄로만 판정
- **Unity 빈 콘솔은 "통과" 가 아니다.** 컴파일이 아직 안 돈 것일 수 있다 — `Library/ScriptAssemblies/Assembly-CSharp.dll` mtime 이 최근 `.cs` 수정보다 뒤인지 함께 본다. 강제 재컴파일은 MCP `Unity_RunCommand` 로 `AssetDatabase.Refresh()` + `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()`(**전체 한정 필수** — 샌드박스가 자체 네임스페이스로 감싸 `CompilationPipeline` 이 충돌한다)
- **거절은 무조건 `permission-denied`.** `failed-precondition`·`invalid-argument` 는 `CloudFailureClassifier` 가 `Unusable` → `BlockSession` 으로 본다. 잔액 부족·중복 수령으로 세션을 끊으면 안 된다
- **순수 모듈에 `firebase-admin`·`HttpsError` 금지.** `functions/scripts/` 회귀가 `lib/` 를 직접 require 한다
- **미러를 손으로 고치지 마라.** 원본은 `functions/src/currency/`, 미러는 `functions-currency/src/generated/`. `npm test` 끝의 `assert-shared-sync` 가 드리프트를 잡는다(줄끝 정규화 후 비교 — `core.autocrlf=true` 라 바이트 비교가 못 선다)
- **`SCHEMA_VERSION` 3중 동기화**(클라 `UserSaveData.VERSION` · `saveDocument.ts` · 룰). 하나만 올리면 조용히 막힌다
- **미커밋으로 오래 두지 마라.** 2026-08-28 에 C1~C3 미커밋분이 브랜치 전환 + 머지로 통째로 날아갔고, GitHub Desktop 자동 stash 는 `.gitignore` 대상(`node_modules`·`lib`)만 담고 untracked 소스를 안 담았다. **각 단계는 검증이 초록으로 뜬 그 순간 커밋한다**

## 별건으로 남은 것

`Assets/Scripts/OutGame/Save/link.xml` 에 `OpenPackResult`·`OpenPackCard` 가 빠져 있다(R5 때부터). 그 파일 주석이 callable 응답 DTO 도 넣으라고 못박고 있어 **IL2CPP 빌드에서 팩 개봉 결과가 빌 수 있다** — 에디터에선 안 드러나고 실기에서만 터진다.
