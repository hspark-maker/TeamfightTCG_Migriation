# 엔타이틀먼트 이행 계획

계정이 **무엇을 받을 권리가 있고 무엇을 이미 썼는가**를 세이브 슬롯에서 서버 전용 컬렉션으로 옮기고, 현행 `grants/current` 문서를 그 컬렉션에 흡수한다.

- 상태: **계획 (미착수)** · 작성 2026-09-03
- 범위: 구조 전환만. 일일 지급 기능 자체는 이 문서 밖이다.
- 선행 조사: 이 문서의 모든 줄번호는 2026-09-03 기준 실측이다. 착수 전에 해당 위치를 다시 확인할 것.

## 용어

**엔타이틀먼트(entitlement)** — 계정에 귀속된 권리 하나. 상용 게임 백엔드가 공통으로 쓰는 이름이며(PlayFab · Steam Inventory · Epic EOS 모두 이 축을 서버 전용으로 둔다) 이 프로젝트도 그 관행을 따른다.

수령 이력과 같지 않다는 점이 중요하다. 이 축이 담는 것은 셋이다.

- **아직 안 쓴 권리** — 모험 정점 승리로 생긴 미수령 보상
- **이미 쓴 권리** — 수령한 랭크 티어·도감 보상·챕터
- **면제 권리** — 튜토리얼 무료 강화 한 방(보상이 아니라 비용 면제다)

---

## 1. 왜 하는가

### 지금 무엇이 깨져 있나

보상 수령 판정 자체는 정상이다. `claimReward` 는 낙인 목록을 클라 페이로드로 받지 않고, 트랜잭션 안에서 서버가 직접 읽은 세이브 문서로만 판정한다(`functions/src/commands/claimReward.ts:352-354`, `functions/src/save/saveDocument.ts:191`).

무너지는 것은 **그 문서를 클라가 쓸 수 있다**는 쪽이다. 룰은 슬롯의 타입과 크기만 보고 이전 값(`resource.data`)과 대조하지 않는다. 룰 전체에서 `resource.data` 를 참조하는 자리는 `schemaVersion` 동등과 `revision + 1` 두 곳뿐이고(`firestore.rules:108-109`), 슬롯 값에 대한 이전 값 대비 검사는 **0건**이다. `affectedKeys()` · `diff()` 도 쓰이지 않는다.

그래서 표식을 비우는 업로드가 `revision+1` 만 지키면 통과한다.

| 표식 | 지금 걸린 검사 | 지우면 |
|---|---|---|
| `rank.claimedTiers` | `is list && size() <= 20` (`firestore.rules:82-83`) | 티어당 반복 수령 (`claimReward.ts:207-215`) |
| `albumReward.claimedKeys` | 슬롯 `size() <= 128` 뿐, 안쪽 키 미검사 (`:88`) | 무제한 반복 — 자격을 소유 카드로 매번 재계산한다 (`claimReward.ts:333-338`) |
| `adventure.clearedNodeIds` · `claimedChapterIds` | 슬롯 `size() <= 128` 뿐 (`:89`) | 정점·챕터당 반복 |
| `adventure.pendingRewardNodeId` | 없음 | 저작된 정점을 임의로 예약. `hasNode` 는 표 밖 정점만 막는다 (`claimReward.ts:251-256`) |

클라 쪽 방어도 없다. 업로드는 dirty 슬롯을 통째로 덮고(`Assets/Scripts/OutGame/Save/4.Cloud/PlayerSaveDocument.cs:44`), `ServerSlotRehydrator.cs:33` 이 말하는 "슬롯 동결"은 그 주석 한 줄뿐 구현이 없다. 영수증도 대신하지 못한다 — 인자를 대조하지 않고 명령 이름만 보므로 새 txId 를 붙이면 매번 새 실행이 성립한다(같은 결론이 `functions/src/commands/claimBattleReward.ts:141-143` 주석에 이미 있다).

### 구멍의 진짜 위치 — 부분 이행 상태

위 표는 증상이고, 원인은 한 층 위에 있다. **클라이언트가 자기 진행도 문서를 직접 쓴다**는 것이다. 서버 권위 설계에서는 나오지 않는 구조이며, 상용 백엔드는 진행도를 포함해 전부를 서버가 소유한다.

이 프로젝트는 그 전환이 **절반쯤 진행된 상태**다.

| 축 | 현재 | 근거 |
|---|---|---|
| 재화 잔액 | **서버 소유** | `wallet/current`, 룰 `allow write: if false` (`firestore.rules:124`) |
| 무료 한 방 | **서버 소유** | `grants/current`, 같은 정책 (`:144`) |
| 카드팩 추첨 | **서버 판정** | callable `openPack`, 클라 `Precheck` 는 낙관 검사일 뿐 |
| 튜토리얼 카드 지급 | **서버 판정** | callable `grantTutorialCards` |
| 덱 확정 | **서버 검증** | callable `lockDeck` |
| **진행도 세이브** | **클라 쓰기** | `save/current`, 룰 `allow update` (`firestore.rules:107`) |

그래서 "왜 grants 만 밖에 있지?"라는 물음은 방향이 뒤집혀 있다. 관행대로라면 물음은 "왜 세이브만 클라가 쓰지?"가 된다. `grants` 는 예외가 아니라 먼저 옳게 간 조각이고, 재수령 구멍은 이 이행이 덜 끝난 자리에서 나왔다.

**이 계획은 그 이행의 한 조각이다.** 끝내면 권리 축이 서버로 넘어가지만 세이브는 여전히 클라 쓰기로 남는다. 무엇이 남는지는 6절에 적었다 — 특히 **자격 위조 축은 이 계획으로 막히지 않는다.**

### 왜 지금 구조가 이렇게 됐나

`grants/current` 는 이 문제를 이미 한 번 우회한 결과다. 무료 한 방은 **판정에 실패해도 열어 줘야 하는** 권리라(신규 계정 지급이 Gold 뿐이라 온보딩이 시키는 강화는 낼 돈이 없다) `readGrants` 가 문서 부재·필드 손상을 미사용으로 읽는다(`functions/src/growth/tutorialGrants.ts:47-53`). 그런 fail-open 권리는 손실이 1회로 묶여야 하고, 그 상한은 클라가 못 지울 때만 유지된다. 그래서 세이브 밖으로 나갔다.

즉 `grants` 는 예외가 아니라 **먼저 옳게 간 사례**다. 이 계획은 나머지 권리를 같은 자리로 모은다.

### 목표

1. 권리의 진실원을 서버 전용 컬렉션 하나로 통일한다.
2. 축을 늘려도 세이브 스키마·룰·클라 슬롯 배관을 건드리지 않는 형태로 만든다.
3. 일일 지급이 붙을 자리를 미리 열어 둔다(기능은 나중).

---

## 2. 목표 구조

```
envs/{env}/users/{uid}/entitlements/{entitlementId}
```

문서 하나가 권리 하나다. `entitlementId` 는 `{kind}__{key}` 로 조립하며, 이 조립 규칙을 아는 자리는 서버 모듈 하나로 제한한다.

### 문서 스키마

```ts
interface Entitlement {
  kind: EntitlementKind;   // 아래 표
  key: string;             // kind 안에서 유일. 없는 축은 "default"
  state: "granted" | "consumed";
  grantedAt: Timestamp;    // serverTimestamp — 권리가 생긴 시각
  consumedAt?: Timestamp;  // 실제로 쓴 시각. 즉시 소비 축은 grantedAt 과 같다
  source: string;          // 만든 callable 이름 — 감사용
  txId: string;            // 영수증 번호 — 재생 추적용
  expiresAt?: Timestamp;   // 일일 지급 전용. 지금은 아무도 쓰지 않는다
}
```

`granted` 는 권리가 있고 아직 안 썼다는 뜻이고, `consumed` 는 썼다는 뜻이다. 상용 백엔드의 grant/redeem 축과 같은 구분이다.

### kind 목록

| kind | key | 대체하는 것 | 상태 |
|---|---|---|---|
| `freeShot` | `enhanceCard` · `enhanceKeyword` | `grants/current` 의 두 불리언 | `consumed` 로 바로 생김 |
| `rankTier` | 티어 인덱스 | `rank.claimedTiers` | `consumed` 로 바로 생김 |
| `albumReward` | 보상 키 | `albumReward.claimedKeys` | `consumed` 로 바로 생김 |
| `adventureNode` | nodeId | `adventure.clearedNodeIds` + `pendingRewardNodeId` | **`granted` → `consumed` 2단계** |
| `adventureChapter` | chapterId | `adventure.claimedChapterIds` | `consumed` 로 바로 생김 |

네 축은 부여와 소비가 같은 순간이라 문서 생성이 곧 소비다. 두 단계를 실제로 쓰는 것은 모험 정점 하나뿐이며, 이것이 이 설계의 핵심이다.

`pendingRewardNodeId` 를 별도 필드로 두지 않는다. 승리 신고가 `adventureNode__{id}` 문서를 `granted` 로 만들고(권리 발생), 수령이 그것을 `consumed` 로 바꾼다(권리 소비). 오르내리는 값이 사라지고 문서 생성과 상태 전이만 남는다.

- "이미 다른 정점이 미수령인가" 판정은 `where("state","==","granted")` 쿼리 한 번이다. 승리 신고는 빈번하지 않아 비용이 문제되지 않는다.
- 현행 거절 사유 셋(`RewardPending` · `AlreadyPending` · `AlreadyCleared`, `functions/src/commands/reportAdventureWin.ts:88-102`)은 그대로 유지한다. 판정 근거만 슬롯 값에서 문서 상태로 바뀐다.

### 왜 문서 하나에 맵으로 담지 않는가

현행 `writeGrantUsed` 가 `transaction.set` 으로 문서 전체를 덮는다(`tutorialGrants.ts:84`). 축이 늘고 구·신 서버 인스턴스가 공존하는 배포 순간, 구 인스턴스의 쓰기가 새 축을 지운다. 문서를 가르면 이 문제가 구조적으로 사라진다. 스키마 승급도 필요 없어진다 — 지금 `GRANT_SCHEMA_VERSION` 은 쓰이기만 하고 `readGrants` 가 읽지도 않는 죽은 축이다(`tutorialGrants.ts:19`, `:47-53`).

### 읽기 비용

클라는 로그인당 컬렉션 1회 목록 읽기로 전량을 받는다. 계정당 문서 수 상한은 저작 규모로 정해진다 — 무료 한 방 2 + 랭크 티어 20 + 도감 보상 + 정점 24 + 챕터 4. 현재 저작 기준 100개 미만이다.

일일 지급이 붙으면 매일 1개씩 쌓이므로 그때 `expiresAt` 에 Firestore TTL 정책을 걸어야 한다. 이 계획은 필드만 미리 두고 정책은 걸지 않는다.

---

## 3. 이행 단계

각 단계는 배포 가능한 단위다. 앞 단계가 끝나기 전에 뒤를 시작하지 않는다.

### P0 — 계약 확정과 검증 뼈대

착수 전 준비다. 코드 동작은 바뀌지 않는다.

- 서버 모듈 신설: `functions/src/entitlements/entitlementStore.ts`
  - `entitlementRef(db, env, uid, kind, key)` · `readEntitlements(transaction, ...)` · `writeEntitlement(...)` · `hasEntitlement(...)`
  - `walletStore` 관용구를 따른다 — `db` · `transaction` · `now` 를 전부 인자로 받고 `HttpsError` 를 모른다(`functions/src/currency/walletStore.ts` 참조).
- 룰 블록 신설: `firestore.rules` 의 `grants` 블록 아래. 읽기는 본인 uid + 알려진 env 로 열고 `list` 를 허용한다(현행 `grants` 는 `docId == 'current'` 고정이라 목록 읽기가 막혀 있다). 쓰기는 `if false`.
- 룰 테스트 케이스 추가: `Tools/firestore-rules-tests/rules.test.js` 의 20~21c(grants) 다음 자리. 픽스처는 `fixtures/entitlementDocument.js` 를 새로 만든다.
  - 거부 케이스는 반드시 `seed(1)` 로 문서를 먼저 심고 update 로 위반을 쏜다. create 로 쓰면 `allow create: if false` 때문에 무조건 실패해 검증이 공허해진다(`Tools/firestore-rules-tests/README.md:69-72`).

### P1 — 무료 한 방 이관 (파일럿)

가장 작은 축으로 배관을 먼저 검증한다.

- 서버: `enhanceCard.ts:115-133` · `enhanceKeyword.ts:109-126` 의 grants 읽기·쓰기를 새 모듈 호출로 교체한다.
  - **트랜잭션 읽기 순서 제약**: 모든 읽기가 모든 쓰기보다 앞서야 한다. `mutateSave` 는 세이브 → 지갑 → 영수증 순으로 읽고, 영수증이 "마지막 무조건 읽기"임을 주석으로 못박아 두었다(`saveDocument.ts:219-220`). 콜백 안의 권리 읽기는 그 뒤, 첫 쓰기 앞이어야 한다.
  - **쿼리를 쓰지 않는다**: 축이 2개로 고정이므로 `transaction.getAll(cardRef, keywordRef)` 로 문서를 직접 지정한다. 쿼리로 바꾸면 `freeShot: false` 일 때 읽기 자체를 건너뛰는 현재 최적화가 깨진다(`enhanceCard.ts:113` 주석).
- 클라: `Assets/Scripts/OutGame/Tutorial/Cloud/TutorialGrantsCloud.cs` 를 `OutGame/Entitlement/Cloud/EntitlementCloud.cs` 로 옮기고 컬렉션 읽기로 바꾼다. 축이 늘면 튜토리얼 전용이 아니게 되므로 이름과 자리를 함께 옮긴다.
  - `RefreshAsync` 재읽기 경로도 함께 바뀐다(`OutGame/Growth/CardGrowthManager.cs:204-210` · `KeywordGrowthManager.cs:135-141` 이 부른다).
  - 표시 경로는 건드리지 않는다. 화면은 grants 를 직접 보지 않고, `CardGrowthManager.TryGetStepAt:232-240` 이 `HasFreeShot` 일 때 cost 를 0 으로 갈아끼운 결과만 그린다.
- **옛 문서 쓰기는 이 단계에서 중단한다.** 무료 한 방은 마이그레이션 대상이 명확하고(P4), 이중 쓰기의 이득이 없다.

### P2 — 수령 표식 이관 (이중 쓰기)

축이 셋이라 한 번에 뒤집지 않는다. `rank` → `albumReward` → `adventure` 순으로, 각 축마다 아래 세 걸음을 밟는다.

1. **이중 쓰기**: 서버가 새 컬렉션과 옛 슬롯에 **둘 다** 쓴다. 판정 근거는 아직 옛 슬롯이다. 클라는 그대로 돈다.
2. **판정 전환**: 서버 판정 근거를 새 컬렉션으로 바꾼다. 옛 슬롯 쓰기는 유지한다(구 클라 표시용).
3. **클라 전환**: 클라 읽기를 새 컬렉션으로 바꾼다.

`adventure` 는 두 단계 상태 전이가 얽혀 마지막이다.

축별로 손대는 자리:

| 축 | 서버 | 클라 읽기 |
|---|---|---|
| `rank` | `claimReward.ts:99` · `:207-226` | `OutGame/Rank/RankRewardManager.cs:128`(`StateOf` 첫 분기) |
| `albumReward` | `claimReward.ts:325-340` | `OutGame/Album/AlbumRewardManager.cs:121` |
| `adventure` | `claimReward.ts:245-265` · `:286-304`, `reportAdventureWin.ts:84-121` | `OutGame/Adventure/AdventureProgress.cs:163` · `:172` · `:285` · `:50` |

**세 축 모두 `StateOf` 의 첫 분기가 수령 여부다.** 도달 검사보다 앞선다(`RankRewardManager.cs:122-124` 주석, 서버 `claimReward.ts:184-186` 과 같은 순서). 이 순서를 바꾸지 않는다.

**낙관 선반영은 건드리지 않는다.** `OutGame/Reward/RewardClaimCommand.cs:22` 의 `s_inFlight` 는 메모리 HashSet 이고 세이브에 쓰지 않는다. 표시 전용이며 해금 사슬·`CanEnter`·챕터 완주 판정에는 태우지 않는다는 계약이 `AdventureProgress.cs:178-186` 에 명시돼 있다. 그대로 둔다.

### P3 — 세이브 슬롯 비우기

- 서버가 옛 슬롯에 쓰는 코드를 제거한다. `claimReward` 의 `slots` 반환에서 해당 필드가 빠진다.
  - **주의**: `SlotPatch` 는 "갱신 후 **전체 값**"이라 `transaction.update` 가 슬롯 맵을 통째로 교체한다(`saveDocument.ts:43-44`, `:264-268`). 그래서 지금 각 명령이 소관 밖 필드까지 명시적으로 실어 보내고 있다. 필드를 뺄 때 같은 슬롯의 **남는 필드를 빠뜨리면 그 필드가 소멸한다**.
  - `rank` 슬롯에는 `points` 가 남는다. `albumReward` · `adventure` 슬롯은 비게 된다.
- **슬롯 자체는 남긴다.** 룰의 `hasAll` 이 14키를 요구하므로 슬롯을 없애면 구 클라의 모든 저장이 거부된다. 빈 맵으로 유지하고 `SCHEMA_VERSION` 도 올리지 않는다(슬롯 추가·필드 제거는 흡수 정책, `saveDocument.ts:34-37`).
- 룰에서 `rank.claimedTiers` 검사를 제거하고 `rank.keys().hasOnly(['points'])` 로 좁힌다. `firestore.rules:50-53` 의 `hasOnly` 와 `:64-67` 의 `hasAll` 은 **같은 14키를 복제한 두 벌**이라 한쪽만 고치면 계약이 갈린다.
- **`ownership` 은 건드리지 않는다.** 클라가 이 슬롯을 직접 쓰므로(`OwnershipManager.Save()`) 룰을 조이면 튜토리얼 되감기와 디버그가 죽는다. 근거와 선행 작업은 6절에 있다.
- 클라 `UserSaveData` 하위 슬롯에서 해당 필드를 제거한다.
  - **선행 확인**: 직렬화 필드를 지우면 Unity 가 프리팹을 다시 쓴다. 세이브 도메인 클래스는 프리팹에 실리지 않으므로 해당 없지만, 제거 전에 `.prefab` 참조가 없는지 확인한다.

### P4 — 마이그레이션

- 스크립트: `functions/scripts/migrate-entitlements.js` (`functions/scripts/` 관용구를 따른다 — 기존 23개 스크립트와 같은 자리).
- 대상 범위는 두 갈래뿐이다: `envs/live/users/*` · `envs/test/users/*`. `ENVIRONMENTS = ["live","test"]` 로 고정이고 dev 는 없다(`functions/src/save/environments.ts:12`). 이 파일은 `functions-currency` 로 미러되므로 목록을 건드릴 일이 있으면 두 코드베이스를 함께 본다.
- 옮길 것:
  1. `grants/current` → `freeShot__*` 문서(`consumed`). **이 문서는 첫 무료 한 방이 실제로 소진될 때만 생긴다** — `freshAccount.ts` 도 `ensureAccount` 도 만들지 않는다. 대상은 "존재하는 문서 전량"이다.
  2. 세이브의 `rank.claimedTiers` · `albumReward.claimedKeys` · `adventure.clearedNodeIds` · `claimedChapterIds` → 각 `consumed` 문서.
  3. `adventure.pendingRewardNodeId` 가 비어 있지 않으면 → `adventureNode__{id}` 를 `granted` 로.
- **미마이그레이션의 결과를 알고 시작한다**: 권리 부재를 미사용으로 읽는 관대함 때문에, 옮기지 않은 계정은 무료 한 방이 **부활한다**(축당 1회가 최대 2회로 는다). 계정이 막히지는 않는다. 수령 표식 쪽은 반대로 재수령이 열리므로 이쪽이 위험하다 — P2 의 판정 전환보다 **먼저** 돌려야 한다.
- 실행 순서: P2 각 축의 1번(이중 쓰기) 배포 직후, 2번(판정 전환) 배포 전.

### P5 — 정리

- `functions/src/growth/tutorialGrants.ts` 제거. 모듈이 `entitlements/` 로 흡수된다.
- `firestore.rules` 의 `grants` 블록 제거.
- 룰 테스트에서 grants 케이스(20~21c) 제거, 픽스처 `fixtures/grantsDocument.js` 제거.
- `functions/scripts/test-enhance.js:202` 가 grants 경로 문자열을 그대로 단언하고 있다. 함께 고친다.
- 마이그레이션 스크립트는 남긴다(재실행 안전하게 작성할 것).

---

## 4. 검증

### 서버 단위 테스트

`functions/` 의 관용구는 에뮬레이터 없이 도는 순수 스크립트다. `npm test` 가 19개를 순차 실행한다(`functions/package.json`).

- 신설: `scripts/test-entitlement-store.js` — 권리 문서 조립·읽기·상태 전이.
- 갱신: `test-enhance.js`(무료 한 방 경로) · `test-claim-reward.js`(수령 판정) · `test-adventure-progress.js` 계열.
- `npm test` 목록에 신설 스크립트를 추가한다. 목록에 안 넣으면 아무도 돌리지 않는다 — `scripts/test-firestore-rules.js` 가 그렇게 잔재로 남아 있다.

### 룰 테스트

```
cd Tools/firestore-rules-tests && npm test
```

Java 21+ 가 필요하고 자체 에뮬레이터(포트 8081)를 띄운다. **종료 코드가 거짓말한다** — JDK 가 없으면 실패해도 exit 0 이므로 `ℹ fail 0` 줄을 눈으로 확인한다.

새로 세울 케이스:
- 권리 컬렉션에 클라 쓰기가 거부되는가
- 남의 uid 권리를 못 읽는가
- 목록 읽기(`list`)가 본인 것만 열리는가
- P3 이후: `rank` 슬롯에 `claimedTiers` 를 실으면 거부되는가

`fixtures/clientContract.js` 가 `PlayerSaveDocument.cs` 의 `FIELD_*` 와 룰의 `hasOnly`/`hasAll` 목록을 정규식으로 파싱해 대조한다. P3 에서 룰을 고치면 이 계약 테스트(0a~0c)가 먼저 깨진다 — 정상이며, 클라와 룰을 함께 고쳤다는 신호다.

### 배포

룰은 파일 통째로 나간다. CI 가 없어 수동 1회다.

```
firebase deploy --only firestore:rules --project bm-cardbattle
firebase deploy --only functions
```

**미배포 변경분이 다른 배포에 묻어 올라간다.** 룰을 고친 채 다른 작업을 배포하지 않는다.

배포 순서는 단계마다 **서버 먼저**다. P1~P2 는 서버가 새 컬렉션을 쓰기 시작해도 구 클라에 무해하지만, 클라가 먼저 나가면 아직 없는 컬렉션을 읽는다.

### 수동 확인

- 신규 계정으로 온보딩을 끝까지 진행 — 카드 강화·키워드 강화 무료 한 방이 각 1회만 도는가.
- 랭크 보상 수령 후 앱 재시작 — 수령 상태가 유지되는가, 알림 점이 꺼지는가(`UI/Common/LobbyEntryAlertDot.cs:25`).
- 모험 정점 승리 → 이탈 → 재진입 — 미수령 권리가 살아 있는가(`AdventureProgress.cs:41` 의 초점 이동).
- 도감 보상 수령 후 테마 화면 재진입.

---

## 5. 함정 목록

착수 전에 읽을 것.

1. **트랜잭션 읽기 순서.** 모든 읽기가 모든 쓰기보다 앞서야 한다. `mutateSave` 는 이미 이 제약에 맞춰 지갑·영수증 읽기를 콜백 진입 전으로 끌어올려 뒀다. 권리 읽기를 첫 쓰기 뒤에 두면 런타임에 터진다.

2. **슬롯은 통째로 교체된다.** `transaction.update({...slots})` 가 맵 값을 대입하므로 소관 밖 필드를 빠뜨리면 소멸한다. 필드를 빼는 P3 에서 특히 위험하다.

3. **룰의 키 목록은 두 벌이다.** `hasOnly` 와 `hasAll` 이 같은 14키를 복제하고 있다. 한쪽만 고치면 그 계정의 이후 저장이 **영구 거부**된다.

4. **슬롯을 없애면 구 클라가 죽는다.** `hasAll` 이 14키를 요구한다. 비우되 남긴다.

5. **권리 부재는 미사용으로 읽힌다.** 이 fail-open 은 온보딩이 멈추지 않게 하는 의도적 설계다. 마이그레이션을 건너뛴 계정에서 무료 한 방이 부활하는 것도 같은 이유다. 새 코드에서 이 관대함을 없애면 신규 계정이 막힌다.

6. **`ServerSlotRehydrator` 는 `rank` · `albumReward` 를 구독하지 않는다**(`ServerSlotRehydrator.cs:30` 의 TODO). 값 자체는 매니저가 세이브를 직독해 새것이지만 화면 갱신 통지가 없다. 지금은 `RewardClaimCommand` 의 in-flight 종료 콜백이 대신 울려 준다(`RankRewardManager.cs:109` · `AlbumRewardManager.cs:87`). 권리를 세이브 밖으로 옮기면 **직독 경로가 사라지므로 이 통지 배선을 새로 세워야 한다.** 빠뜨리면 "수령했는데 화면이 안 바뀐다"가 된다.

7. **`adventure` 는 재수화를 구독한다**(`:26`). 셋 중 유일하다. 이관 후에도 `AdventureProgress.NotifyRehydrated()` 가 계속 울려야 한다.

8. **문서 이름과 코드 이름이 다르다.** 기능 지도(`.claude/orch-feature-map.md`)는 `Tournament` 로 적고 있지만 실제 코드는 `adventure` 슬롯 · `reportAdventureWin` · `AdventureProgress` 다. 이 계획이 끝나면 지도도 함께 고친다.

---

## 6. 이 계획이 끝나도 남는 것

### 자격 위조 축 — 이 계획으로 막히지 않는다

권리를 옮기는 것은 **"이미 받은 것을 또 받는"** 재수령을 막는다. 그러나 **"받을 자격을 위조하는"** 축은 따로 봐야 한다. 자격 판정의 근거가 권리 문서가 아니라 진행도 값 자체이기 때문이다.

| 축 | 자격의 근거 | 이 계획 후 |
|---|---|---|
| 모험 정점 | 미수령 예약 + 저작 표 검사 | **닫힘.** 예약이 권리 문서로 옮겨가고, 표 밖 정점은 `hasNode` 가 이미 막는다 |
| 모험 챕터 | `clearedNodeIds` 로 완주 판정 (`claimReward.ts:295`) | **닫힘.** 클리어 기록이 권리 문서로 옮겨간다 |
| 도감 보상 | `ownership.cardIds` — 완성 여부를 소유 카드로 매번 재계산 (`claimReward.ts:333-338`) | **열려 있다.** 소유 쓰기 주체 이관이 선행 조건이다 |
| 랭크 티어 | `rank.points` (`claimReward.ts:207` 의 `points >= required`) | **열려 있다.** 전투 결과 판정 서버화가 선행 조건이다 |

**둘 다 룰로는 못 막는다.** 자격의 근거가 수령 기록이 아니라 진행도 값 자체이고, 그 값을 클라가 쓰기 때문이다.

- **소유**: 클라가 슬롯을 직접 쓴다. `OwnershipManager.Save()`(`:48-53`)가 메모리 집합을 슬롯에 flush 하고 `Grant`·`GrantAll`·`RevokeAll` 이 모두 그것을 부른다. 서버(`openPack` · `grantTutorialCards`)도 쓰지만 **유일한 기록자가 아니다.** 단조 증가 검사(`hasAll`)로 막으려 해도 소용없다 — 그것은 목록이 줄어드는 것만 막는데, 자격 위조는 **카드를 추가하는** 방향이다. 게다가 `Init` 이 카탈로그에 없는 id 를 걸러낸 뒤 다시 저장하므로(`:40`) 소유가 줄어드는 정상 경로도 있어, 단조 검사는 정상 클라를 막기까지 한다.
- **랭크 점수**: 클라가 계산하고 쓴다. `RankManager.cs:169-192` 가 승패에 따라 `Points` 를 갱신하고, 서버 `submitMatchResult.ts:468` 은 정산용으로 **읽기만** 한다. 점수는 패배 시 내려가므로 단조 검사도 성립하지 않는다.

정리하면 네 축 중 **둘이 닫히고 둘이 남는다.** 계획을 축소할 이유는 아니다 — 이 계획이 없으면 저 둘을 닫아도 재수령이 남는다. 다만 **"이걸 끝내면 보상 어뷰징이 막힌다"고 읽으면 안 된다.**

### 명시적으로 범위 밖

- **카드 소유의 쓰기 주체 이관.** 클라가 소유를 늘리는 자리는 지금 넷이다 — 디버그 셋(`OutgameDebugActions.cs:235` · `:241`, `UI/Debug/UnlockAllCardsButton.cs:25`, `Test/MultiplayerTestDebugPanel.cs:54`)과 **튜토리얼 되감기**(`OutGame/Tutorial/OutgameTutorialRewind.cs:147` · `:171` · `:179`). 정상 플레이 경로는 없다. 그래서 이관 자체는 크지 않지만, 되감기의 카드 재지급을 서버 callable 로 옮기고 `Init` 의 카탈로그 정리 flush(`OwnershipManager.cs:40`)를 서버 쪽으로 넘기는 작업이 함께 붙는다. 그 전에는 룰을 조일 수 없다 — 조이는 순간 되감기와 디버그가 죽는다.
- **랭크 점수의 서버 이관.** 지금은 클라가 승패를 보고 계산해 세이브에 쓴다(`RankManager.cs:169-192`). 서버는 정산용으로 읽기만 한다(`submitMatchResult.ts:468`). **선행 조건은 전투 결과 판정의 서버화**다 — 서버가 승패를 모르면 점수를 계산할 근거가 없으므로 점수 계산만 떼어 옮길 수 없다. 둘 중 이쪽이 훨씬 크다.
- **일일 지급 기능.** `expiresAt` 필드와 TTL 정책 자리만 열어 둔다. 실제 기능에는 날짜 경계 계산이 필요한데, 지금 서버가 시각을 판정에 쓰는 곳은 만료 비교뿐이고 그것도 전부 `now + TTL` 절대시각이다. 리셋 기준 시각과 타임존을 정하는 단일 지점이 없으므로, 그 규칙을 먼저 만들어야 한다.
- **세이브 문서 전체의 서버 권위 전환.** 1절의 부분 이행 표에서 마지막 한 줄을 뒤집는 작업이다. 위 랭크 점수 이관도 그 안에 든다.
- **`deck` 슬롯 재수화**(`ServerSlotRehydrator.cs:28-29` 의 TODO R5+). 이 계획과 무관하다.
