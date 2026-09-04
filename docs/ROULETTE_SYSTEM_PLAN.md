# 룰렛 겜블링 시스템 기획 (2026-09-04)

## 목표

아웃게임의 뽑기 축이 카드팩 개봉 하나뿐이라 짧은 주기로 돌려볼 거리가 없다. 룰렛을 세워 두 가지를 얻는다.

1. **티켓이라는 전용 재화를 소비하는 순환**을 만든다.
2. 기존 재화 4종(골드·다이아·조각·에너지)에 **새 수급처**를 붙인다.

## 확정된 기획

| 축 | 값 |
|---|---|
| 칸 수 | 8칸 |
| 상품 | 전부 재화. 그중 한 칸이 확률이 아주 낮은 잭팟 칸 |
| 비용 | 티켓 1장에 1회전 |
| 천장·누적 보정 | 없다 |
| 연속 회전(10연차) | 없다 |
| 카드·팩 상품 | 넣지 않는다 |
| 티켓 수급 경로 | **이번 범위 밖.** 디버그 지급으로만 채워 검증한다 |
| 진입 | 로비 버튼 |
| 회전 연출 | 누르면 즉시 돌고, 결과가 정해지면 그 칸에서 감속 정지 |

상품이 재화뿐이므로 **세이브 슬롯(소유·성장)을 건드리지 않고 지갑만 바뀐다.** 이 사실이 뒤의 설계를 크게 단순하게 만든다.

## 목업 실태

`Assets/Assets/Prefabs/UI/Lucky_Spin.prefab` (Layer Lab GUI Pro-SimpleCasual 출신)

```
Lucky_Spin
├─ Panel                반투명 딤
├─ Ribbon               타이틀 리본 + "LUCKY SPIN" 글자
├─ Lucky_Spin_board     700x700 — 회전 대상
│  └─ Item_01 ~ Item_08 각 칸: 아이콘 Image + 수량 TMP(" X1")
├─ Lucky_Spin           1000x1100 고정 프레임
│  ├─ Bulb              전구 8개(On 4 / Off 4 교차)
│  └─ Text              "12시간 30분" — 안내 텍스트 자리
├─ Button               "스핀 돌리기"
└─ Back_Button          닫기(X)
```

- **커스텀 스크립트가 하나도 없다.** `m_Script` GUID 3종이 전부 uGUI 내장(`Image`·`TextMeshProUGUI`·`Button`)이다. 컴포넌트를 얹고 배선만 하면 된다.
- **바늘은 별도 오브젝트가 아니다.** 포인터가 `lucky_spin.png` 프레임 스프라이트에 그려져 있다. 회전하는 것은 판이고 바늘은 고정이다.

---

## 왜 두 단계로 나누는가 — 로컬에서 어디까지 되는지 실측

서버 파트를 뒤로 미루기로 했다. "서버 배포 없이 어디까지 만들 수 있는가"를 코드로 확인했고, 세 개의 벽이 나왔다.

### 벽 1 — 재화 잔액은 클라가 만들 수 없다

`Assets/Scripts/OutGame/Currency/CurrencyManager.cs` 에 **잔액을 더하는 public API 가 아예 없다.** `Adopt` 의 유일한 호출자가 `WalletCloud.Adopt` 이고, 그것은 서버 응답의 `wallet` 만 받는다. 클라가 할 수 있는 것은 `CurrencyPendingTicket` 으로 **표시값에 낙관 델타를 얹는 것**뿐이고, 그 델타는 서버 응답이 오면 걷힌다.

→ 로컬 단계에서 티켓을 차감하거나 재화를 지급할 수 없다. 억지로 뚫으면 "잔액의 진실원은 서버 지갑 문서" 규약이 무너진다.

### 벽 2 — 스펙 표는 로컬만으로 늘릴 수 없다 (부팅이 막힌다)

- `Assets/Table/SpecDatas.cs` 는 헤더에 `SpecDataGenerator에서 만들어진 파일입니다. 수정하지 마세요` 가 박힌 **자동 생성 파일**이다. 표를 늘리려면 정식 시트 저작과 CS 생성이 필요하다.
- `Assets/Scripts/Editor/SpecLocalCsvImporter.cs` 는 **기존 표를 CSV 로 갈아끼울 뿐 새 표를 만들지 못한다** — 없는 표는 `json에 없음 · CSV만 있음(새 표는 시트에서 만들어야 한다)` 로 보고만 하고 넘어간다.
- `SpecPayloadCodec.cs:21` 의 `TableNames` 는 업로드 목록이 아니라 **콘텐츠 지문의 재료**다. `SpecSource.TryCombinedFingerprint` 가 이 배열을 순회해 로컬 표를 접고, `BattleContentSync` 가 서버 `{env}/specs/_index` 의 `tables` 맵과 1:1 대조한다.

**로컬에만 표를 추가했을 때 실제로 벌어지는 일**(코드로 확인)

| 상황 | 떨어지는 지점 | 결과 |
|---|---|---|
| 코드 생성까지 하고 bytes 만 로컬로 구움 | `BattleContentSync.cs:320-322` — 서버 `tables` 에 새 이름이 없어 `Remote spec index entry '{표}' is missing` throw | 싱글은 `OfflineAllowed` 로 갈리지만 **초기화 경로는 그것도 실패로 친다**(`:222`) |
| `TableNames` 에 이름만 추가 | `BattleContentSync.cs:101` — `로컬 표 '{이름}' 생성 실패` → `Blocked` | 같음 |

두 경우 모두 `SpecSyncStep.cs:27-30` → `MainInitializer.FailToRecovery` → `Destroy(_context.Root)` 로 이어져 **로비 씬이 로드되지 않고 StartScene 복구 화면에 고정된다.** `SpecSyncStep` 은 `Initialize.prefab` 에 무조건 꽂혀 인증 직후에 돈다. 우회로는 없다 — `EContentRunMode.Test` 는 env 를 바꿀 뿐 test env 에도 `_index` 가 필요하고, `SpecSyncStep`·`BattleContentSync` 에는 `#if UNITY_EDITOR` 분기도 스킵 플래그도 하나도 없다.

→ 로컬 단계에서 룰렛 칸을 **스펙 표로 저작하지 않는다.** ScriptableObject 로 시작하고 서버 단계에서 표로 이관한다. 모험(`AdventureConfig`)이 밟은 것과 같은 경로다.

### 벽 3 — 추첨 판정은 결국 서버로 간다

카드팩(`openPack`)이 이미 그렇다. 클라 `CardPackOpener.Precheck` 는 왕복을 아끼는 낙관 검사일 뿐이고 판정은 서버가 한다. 룰렛도 같은 자리에 놓아야 한다.

→ 로컬 단계의 추첨은 **개발용 임시 구현**이고 서버 단계에서 통째로 교체된다. 교체 비용을 0에 가깝게 하려면 그 경계를 처음부터 인터페이스로 그어 둔다.

### 결론 — 단계 나누기

| | 1단계 (로컬) | 2단계 (서버) |
|---|---|---|
| 칸 저작 | ScriptableObject | 스펙 표 2개로 이관 |
| 추첨 | 클라 로컬 가중치 추첨 | callable `spinRoulette` |
| 티켓 차감 | **하지 않는다**(무제한 회전) | 서버 지갑 차감 |
| 재화 지급 | **하지 않는다**(연출만 재생) | 서버 지갑 지급 |
| 검증되는 것 | 화면·연출·칸 배선·가중치 분포·로비 진입 | 잔액 정합·멱등성·거절 갈래 |

1단계만으로도 **화면과 손맛은 완성된다.** 빠지는 것은 숫자가 실제로 움직이는 부분뿐이다.

---

## 1단계 — 로컬 (먼저 만드는 것)

### 1-1. 칸 저작 (`Assets/Scripts/OutGame/Roulette/`)

`RouletteConfig` : ScriptableObject. `AdventureConfig`·`ProfileConfig` 와 같은 관용구다.

| 필드 | 타입 | 뜻 |
|---|---|---|
| `displayName` | string | 화면 표시 이름 |
| `price` | long | 1회전 비용(티켓 장수). 1단계에서는 표시만 한다 |
| `slots` | `RouletteSlotDef[]` | 칸 8개. 배열 인덱스가 곧 판 위 위치다 |

`RouletteSlotDef` (`[Serializable]`)

| 필드 | 타입 | 뜻 |
|---|---|---|
| `currency` | `ECurrencyType` | 지급 재화 |
| `amount` | long | 지급량 |
| `weight` | int | 추첨 가중치. 0 이면 1 로 본다 |
| `isJackpot` | bool | 잭팟 연출 대상 |

- **`currency` 에 `RouletteTicket` 을 넣지 못하게 막는다.** 티켓으로 티켓을 뽑는 무한 인쇄기를 저작 실수로 여는 관문이다. `OnValidate` 에서 경고하고 주입 시에도 검사한다.
- 기획자 규약(가중치 0 의 뜻, 잭팟 칸이 하나여야 한다는 것)은 코드 주석이 아니라 `[Tooltip]` 에 적는다.

주입은 `Assets/Scripts/Core/Initialization/OutgameConfigStep.cs` 에 `[SerializeField] RouletteConfig rouletteConfig;` 를 얹고 `RouletteManager.SetConfig(rouletteConfig)` 를 부른다(같은 파일의 `AdventureProgress.SetConfig`·`ProfileManager.SetConfig` 옆).

### 1-2. 도메인 (`Assets/Scripts/OutGame/Roulette/`)

| 파일 | 역할 |
|---|---|
| `RouletteManager.cs` | Manager. 설정 보관 + `SpinAsync` 창구 + `Precheck` |
| `IRouletteSpinSource.cs` | **2단계 교체 지점.** 회전 결과를 내는 능력 |
| `LocalRouletteSpinSource.cs` | 1단계 구현. 클라 가중치 추첨, 지갑 미변경 |
| `RouletteSpinOutcome.cs` | 결과값. `readonly struct` |
| `ERouletteSpinResult.cs` | 성공·거절 갈래 |

**`IRouletteSpinSource` 계약** — 1단계와 2단계를 가르는 유일한 이음매다.

```
UniTask<RouletteSpinOutcome> SpinAsync(CancellationToken token)
```

`RouletteSpinOutcome` 은 `slotIndex` · `currency` · `amount` · `isJackpot` · `result` 를 든다. **`slotIndex` 가 계약의 핵심**이다. 판이 멈출 자리를 결과가 정하고 화면은 그 값에만 따른다. 화면이 칸을 고르는 경로를 두면 2단계에서 연출과 실제 지급이 갈린다.

`LocalRouletteSpinSource` 는 `RouletteConfig.slots` 의 가중치로 한 칸을 뽑아 즉시 돌려준다. **서버 지연을 흉내 내는 인위적 대기는 넣지 않는다** — 2단계에서 진짜 지연이 붙었을 때 연출이 견디는지가 그때 드러나야 한다.

- **개발 전용임을 코드에서 드러낸다.** `LocalRouletteSpinSource` 는 `EContentRunMode.Test` 또는 에디터에서만 주입하고, 그 밖에서는 룰렛 진입 자체를 막는다. 조용히 로컬 추첨으로 폴백하면 출시 빌드에서 공짜 재화가 도는 것처럼 보이는 화면이 나온다.
- **1단계에서 지갑을 건드리지 않는다.** 획득 연출은 재생하되 잔액은 그대로다. 화면에 "로컬 모드 — 잔액 미반영" 표식을 띄워 오해를 막는다.

### 1-3. 재화 신설 (표시 축만)

`ECurrencyType` 에 티켓을 미리 넣어 두면 2단계에서 아이콘·이름 저작을 다시 하지 않아도 된다. 잔액은 서버가 줄 때까지 0 이고, 클라가 티켓 키를 서버에 보내는 경로가 1단계에는 없으므로 안전하다.

- `Assets/Scripts/OutGame/Currency/ECurrencyType.cs` : `Count` **앞**에 `RouletteTicket` 추가. 순서·기존 값을 바꾸지 않는다(enum 이름이 곧 서버 지갑 키다).
- `Assets/Scripts/OutGame/Currency/CurrencyLook.cs:97` `DefaultNameOf` 스위치에 `"룰렛 티켓"` 추가. 안 적으면 화면에 영문 이름이 그대로 노출된다.
- `Assets/SO/Currency/CurrencyLook.asset` : `type: 4` 엔트리 + `icon` · `barIcon` · `displayName`. **`barIcon` 을 비우면 그 재화는 상단바 칸을 못 받아 획득 연출이 통째로 스킵된다**(`CurrencySlotBoard` · `CurrencyHud.IsLendable`).
- `WalletPatch.cs:12` · `CurrencyManager.cs:52` 의 "항상 4키" 주석 갱신.

그 밖의 `(int)ECurrencyType.Count` 배열·루프는 전부 자동으로 늘어난다.

### 1-4. 화면 (`Assets/Scripts/UI/Roulette/`)

프리팹은 목업 `Lucky_Spin.prefab` 을 그대로 쓴다.

| 파일 | 역할 |
|---|---|
| `RouletteOverlay.cs` | `SingletonOverlay<RouletteOverlay>` 상속. 열고 닫기, 칸 채우기 |
| `RouletteWheelView.cs` | 판 회전 소유. 가속·등속·감속 정지 |

**오버레이 관용구**: `TryGetOrCreate(RuntimeOverlayPrefabs.Get<RouletteOverlay>, out _)` 로 얻는다(`CardRewardOverlay`·`RankPromoteOverlay` 와 같다). 씬에 저작하지 않고 Addressables 타입 색인에서 세운다 — **`UIPrefab` 라벨 그룹 asset 에 항목을 추가해야 한다.** 빠뜨리면 런타임에 "UI Prefab Not Exist" 가 난다.

**배선**

| 프리팹 노드 | 붙일 것 |
|---|---|
| 루트 | `RouletteOverlay` |
| `Lucky_Spin_board` | `RouletteWheelView` |
| `Item_01`~`Item_08` | 아이콘 Image · 수량 TMP 를 슬롯 0~7 배열로. 값은 `RouletteConfig` 에서 채운다 |
| `Lucky_Spin/Text` | 티켓 보유량 표시로 전용(현재 "12시간 30분") |
| `Button` | 스핀 |
| `Back_Button` | 닫기 |
| `Bulb` 8개 | 잭팟 연출 |

**회전 안무**

1. 버튼을 잠그고 판을 가속 회전시킨다. **`ServerWaitOverlay` 를 쓰지 않는다** — 회전 자체가 대기 표시이고 딤이 판을 덮으면 연출이 죽는다. 입력 차단은 버튼 잠금으로 족하다.
2. 최소 회전 시간(약 2.5초)을 보장한다. 결과가 먼저 오면 남은 시간을 채우고, 늦으면 등속 회전을 유지한다.
3. 결과의 `slotIndex` 를 목표로 감속 정지한다. 정지 각은 결과에서만 나온다.
4. `isJackpot` 이면 전구 점멸을 얹는다.
5. 획득 연출은 기존 `CurrencyGainEffectPlayer` 를 쓴다.
6. **실패·거절이면 급정지하지 않고 원래 자리로 감속 복귀한 뒤** 안내를 띄운다. 급정지는 결함으로 읽힌다.

**로비 진입 버튼**: 로비 셸에 버튼을 붙여 오버레이를 연다. **붙일 정확한 자리는 착수 시 스크린샷을 받아 정한다** — 하단이 이미 꽉 차 있어 프리팹 YAML 추론으로는 헛짚는다.

### 1단계 검증

1. Unity 컴파일 에러 0.
2. 로비 버튼 → 룰렛이 뜨고 칸 8개가 `RouletteConfig` 저작대로 채워진다.
3. 회전 → 감속 정지한 칸이 결과의 `slotIndex` 와 일치한다(로그 대조).
4. 가중치를 극단으로 저작해(한 칸만 999) 그 칸에만 멈추는지 본다.
5. 잭팟 칸에 멈추면 전구 연출이 붙는다.
6. 연속으로 눌러도 회전이 겹치지 않는다.
7. 회전 중 닫기를 눌러도 다음 진입이 깨지지 않는다.

---

## 2단계 — 서버 (뒤로 미룸)

1단계가 끝난 뒤 착수한다. 이 절은 그때 참고할 조사 결과다.

### 2-1. 재화 키를 서버에 연다

- `functions/src/currency/currencyKeys.ts:10` `CURRENCY_KEYS` 에 `"RouletteTicket"` 추가. **서버 전체에서 필수 수정은 이 한 줄뿐**이다(`wallet.ts`·`walletStore.ts` 는 전부 이 배열 순회).
- 같은 파일 4~6행의 낡은 주석을 **같은 커밋에서 지운다.** "firestore.rules 의 `balances.keys().hasOnly([...])` 와 같아야 한다"고 적혀 있으나 전수 확인 결과 룰에 그런 검사가 없다(지갑 문서는 `allow write: if false` + 값 검증 없음, 세이브 문서에서 `currency` 는 오히려 금지 필드). 그대로 두면 다음 사람이 룰을 고치려다 헛돈다.
- `firestore.rules` : **수정 없음.**

> **⚠️ `functions-currency/` 를 같은 배포에 묶지 않으면 티켓 잔액이 삭제된다.**
> 그 codebase 는 `scripts/shared-files.js` 가 `currencyKeys.ts` 를 복사해 만드는 미러다. 소스는 자동으로 맞지만 **이미 배포된 번들에는 4키짜리 `normalize()` 가 구워져 있고, 그 함수는 모르는 키를 버린다.** 낡은 `devGrantCurrency` 가 한 번이라도 돌면 그 지갑의 `RouletteTicket` 이 통째로 사라지고 `clampPaid` 도 같은 축으로 유상분을 지운다. currency 를 먼저 올려도 안전하다 — 5키를 아는 쪽이 4키 지갑을 만나면 빠진 키를 0 으로 채울 뿐이다.

**깨지는 회귀 단언**(4키 하드코딩, 배포와 무관한 순수 테스트)

| 파일 | 줄 |
|---|---|
| `functions/scripts/test-currency.js` | 17 · 26 · 31 · 37 · 44 · 60 · 62 |
| `functions/scripts/test-wallet-store.js` | 22 · 58 · 61 · 95 · 104 · 113 · 136 · 148–150 · 156 · 161–162 · 167 · 182 · 231 |
| `functions/scripts/test-wallet-migration.js` | 11 · 19 · 55 |
| `functions/scripts/test-fresh-account.js` | 27 |
| `functions-currency/scripts/test-wallet-mirror.js` | 30 · 39 |

**디버그 지급은 코드 변경 0줄이다** — `devGrantCurrency` 가 `CURRENCY_KEYS` 조회라 새 키를 자동으로 통과시킨다. `OutgameDebugActions` 에 void 래퍼 하나와 `DebugCurrencyButton` 에 같은 이름 메서드만 추가하면 된다(Button OnClick 이 void 시그니처에 직결된다).

### 2-2. 칸 저작을 스펙 표로 이관

`CardPack`(헤더·비용) + `CardPackDrop`(가중 행) 관용구를 복제한다. 칸 표 하나로 합치면 회전 비용이 8행에 중복 저작되어 어긋난다.

**`Roulette_sheet.csv`** — `id`(int) · `rouletteId`(string) · `displayName`(string) · `channel`(string) · `priceType`(string) · `price`(long) · `slotCount`(int) · `sortOrder`(int) · `artKey`(string)

**`RouletteSlot_sheet.csv`** — `id`(int) · `rouletteId`(string) · `slotIndex`(int) · `rewardType`(string, Currency 고정) · `rewardId`(string) · `amount`(long) · `weight`(int) · `isJackpot`(int) · `#memo`(string, `#` 접두는 데이터 아님)

- 헤더 3줄(한글설명/필드명/타입), BOM 필수, 첫 열 int id, bool 타입 없다.
- `minGrade` 축은 넣지 않는다 — 최고등급 선택 로직을 통째로 끌고 오는데 지금 기획엔 등급 잠금이 없다.
- **배관 순서를 지킨다**: 정식 시트에 표 저작 → CS 생성(`SpecDatas.cs` 자동 갱신) → `SpecPayloadCodec.TableNames` 에 두 이름 추가 → `SpecFirestoreUploader` 로 업로드. **네 가지가 한 묶음이다** — 하나라도 빠지면 부팅이 복구 화면에서 멈춘다(위 "벽 2" 참조). 표를 추가하면 옛 스펙 캐시 파일도 지운다(안 지우면 매 부팅 LogError).
- 이관 후 `RouletteConfig` 는 그림만 남긴다(모험의 `AdventureNodeDef` 와 같은 모양).

### 2-3. callable `spinRoulette`

**`mutateSave` 가 아니라 `mutateWallet` 을 탄다.** `mutateSave` 는 `slots` 가 비어도 세이브 쓰기와 revision +1 을 무조건 실행한다. 지갑만 바꾸는 선례는 `claimBattleReward`·`devGrantCurrency` 이고 둘 다 `functions/src/currency/walletTransaction.ts` 의 `mutateWallet` 을 쓴다.

| 파일 | 역할 |
|---|---|
| `functions/src/roulette/rouletteDraw.ts` | 순수 추첨. Firestore·HttpsError 를 모른다 |
| `functions/src/roulette/rouletteSpecReader.ts` | 스펙 래퍼(`packs/packSpecReader.ts` 판박이) |
| `functions/src/commands/spinRoulette.ts` | callable |
| `functions/src/index.ts` | 재수출 한 줄 |

순수 모듈은 `packDraw.ts` 의 난수 주입형을 본뜨되 **import 하지 않는다**(룰렛이 팩에 묶인다). `RollFn` 을 자체 선언하면 구조적 타입이라 `node:crypto` 의 `randomInt` 가 그대로 들어간다. 유효 가중치 `w > 0 ? w : 1` 은 `packDraw` 와 같은 관용구를 쓴다.

**요청** `{env, rouletteId, txId?}` / **응답** `{rouletteId, slotIndex, isJackpot, gain:{currency, amount}, wallet:{rev, balances}}`

`revision`·`updatedSlots` 는 싣지 않는다 — `ServerCommandResult` 의 계약대로 "세이브를 안 쓴 명령"이다.

**처리 순서**

1. uid·env 검증 → `rouletteId` 길이 검사 → `invalid-argument`
2. 트랜잭션 **밖**에서 스펙 읽기 → 행 없으면 `RouletteNotFound`, 유효 칸 0개면 `EmptyPool`
3. `clientReceiptId(request.data?.txId, randomUUID())`
4. `mutateWallet(...)` — `guard` 는 넘기지 않는다(소진 자격이 티켓 잔액 자체다)
   - `canAfford` 실패 → `InsufficientTicket`
   - **추첨을 차감보다 먼저** 한다(openPack 과 같은 순서)
   - `spend` → `grant` → **차감과 지급을 한 `nextWallet` 으로** 묶는다. 그래야 영수증 `changes` 가 순증감 한 줄로 남는다
5. 로그: `rouletteId` · `slotIndex` · `currency` · `amount` · `isJackpot` · `price` · `rev`

**거절 사유 코드** — `rejectDomain` 이 `HttpsError("permission-denied", "코드: 설명")` 로 던지고, **사유를 message 앞머리에 싣는 것이 와이어 계약**이다(details 는 Unity SDK 가 버린다). 클라 enum 이름이 이 문자열과 정확히 같아야 한다.

`RouletteNotFound` · `EmptyPool` · `InsufficientTicket` · `TxIdReused`(mutateWallet 이 직접 던진다)

**서버 리더가 버릴 행**: `rewardType != Currency` · `amount <= 0` · 미지 `rewardId` · **`rewardId == "RouletteTicket"`**.

### 2-4. 클라 교체

`ServerRouletteSpinSource` 를 만들어 `IRouletteSpinSource` 자리에 끼운다. **화면은 손대지 않는다.**

- `SpinRouletteResult` : `ServerCommandResult` 상속 DTO.
- 호출은 `ServerSaveCommands.InvokeAsync<SpinRouletteResult>("spinRoulette", ...)` — **세이브·지갑을 건드리는 서버 호출의 유일한 창구다.** txId 삽입·명령 직렬화·유실 응답 1회 재시도가 전부 그 안에 있다.
- **첫 await 이전에** `CurrencyPendingTicket.Hold(ECurrencyType.RouletteTicket, -1)` 를 걸어 누른 프레임에 티켓 표시가 줄게 하고 `finally` 에서 `Settle()` 한다.
- 응답 채택은 손대지 않는다 — `ServerSaveCommands.RunAsync` 가 `pending.Settle(_notify:false)` → `WalletCloud.Adopt` 순서로 처리하고, **그 두 줄 사이에는 아무것도 끼우면 안 된다**(뒤집으면 이중 계상).
- 실패 팝업은 `PackPurchaseFailurePopup` 을 선례로 삼는다. 망 실패는 `NetworkFailurePopup`, 도메인 거절은 `SimpleYNPopup` 문구 분기.

### 2단계 검증

1. 회귀 단언을 5키로 고치고 `node` 로 전부 통과시킨다. 새 추첨 모듈은 `packDraw` 테스트를 본떠 가중치 분포·`weight 0` 폴백·티켓 행 드롭을 덮는다.
2. `functions` 와 `functions-currency` 를 **같은 배포에 묶는다.**
3. 디버그 버튼으로 티켓 지급 → 상단바에 티켓이 뜨는지 → 회전 → 서버 잔액과 화면 잔액 일치(`CurrencyManager.GetServerBalance` 로 대조).
4. 티켓 0장에서 `InsufficientTicket` 팝업, 비행기 모드에서 망 실패 팝업. 두 경우 모두 판이 원래 자리로 복귀하는지.
5. 왕복 중 앱을 죽였다 켜서 티켓이 두 번 빠지지 않는지(같은 txId 재시도를 서버 영수증이 막는 자리다).

---

## 함정 목록

| # | 함정 | 증상 |
|---|---|---|
| 1 | 스펙 표를 로컬에만 추가 | **부팅이 복구 화면에서 멈춘다.** 로비도 안 뜬다 |
| 2 | `functions-currency/` 를 따로 배포 | 티켓 잔액이 조용히 삭제된다 |
| 3 | `mutateSave` 로 룰렛을 붙임 | 바꿀 슬롯이 없는데 세이브 쓰기와 revision 이 오른다 |
| 4 | 화면이 멈출 칸을 스스로 고름 | 2단계에서 연출과 실제 지급이 갈린다 |
| 5 | 칸에 `RouletteTicket` 저작 | 티켓 무한 인쇄기 |
| 6 | `CurrencyLook.asset` 의 `barIcon` 미저작 | 티켓 획득 연출이 통째로 스킵된다 |
| 7 | Addressables `UIPrefab` 그룹 등록 누락 | 런타임 "UI Prefab Not Exist" |
| 8 | 표 추가 후 옛 스펙 캐시 방치 | 매 부팅 LogError |
| 9 | 로컬 추첨이 출시 빌드로 샘 | 잔액이 안 움직이는 공짜 재화 화면 |
| 10 | 거절 시 판을 급정지 | 결함으로 읽힌다 |

## 범위 밖

- 티켓 수급 경로(일일 무료 · 정점 클리어 · 다이아 교환 · 승리 누적)
- 천장 · 누적 보정 · 연속 회전
- 카드 · 팩 상품 칸
