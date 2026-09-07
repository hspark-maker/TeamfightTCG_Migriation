# 룰렛 겜블링 시스템 기획 (2026-09-04)

## 목표

아웃게임의 뽑기 축이 카드팩 개봉 하나뿐이라 짧은 주기로 돌려볼 거리가 없다. 룰렛을 세워 두 가지를 얻는다.

1. **티켓이라는 전용 재화를 소비하는 순환**을 만든다.
2. 기존 재화 4종(골드·다이아·조각·에너지)에 **새 수급처**를 붙인다.

## 확정된 기획

| 축 | 값 |
|---|---|
| 칸 수 | 8칸 |
| 상품 | 전부 재화 |
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

→ 로컬 단계에서 룰렛 칸을 **스펙 표로 저작하지 않는다.** ScriptableObject 로 시작한다.

> **뒷이야기**: 2단계에서 `AdventureChapter` 식 Composition 경로(SO → Firestore 블롭 직접 업로드)로 이 벽을 우회해 봤으나 되돌렸다. 칸 값을 기획자가 사내 시트 툴에서 관리해야 했고, 그 경로는 표 이름이 같아 정식 표와 한 문서를 다툰다. 결국 이 벽을 정면으로 통과했다 — 2-2 절의 배관 순서를 보라.

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

## 1단계 — 로컬 (완료)

> 화면·회전 안무·칸 배선·로비 진입·개발 가드가 전부 섰다. 계획서와 갈린 이름 둘: `RouletteOverlay` 라는 타입은 없고 `RoulettePanel`(`PooledUIBase` 상속)이며, 오버레이 관용구도 `TryGetOrCreate` 가 아니라 `UIPoolManager.AddOrUpdateUI<RoulettePanel>()` 이다.

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

- **`currency` 에 `RouletteTicket` 을 넣지 못하게 막는다.** 티켓으로 티켓을 뽑는 무한 인쇄기를 저작 실수로 여는 관문이다. `OnValidate` 에서 경고하고 주입 시에도 검사한다.
- 기획자 규약(가중치 0 의 뜻)은 코드 주석이 아니라 `[Tooltip]` 에 적는다.

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

`RouletteSpinOutcome` 은 `slotIndex` · `currency` · `amount` · `result` 를 든다. **`slotIndex` 가 계약의 핵심**이다. 판이 멈출 자리를 결과가 정하고 화면은 그 값에만 따른다. 화면이 칸을 고르는 경로를 두면 2단계에서 연출과 실제 지급이 갈린다.

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
| `Bulb` 8개 | `RouletteBulbRing` (평시 마퀴) |

**회전 안무**

1. 버튼을 잠그고 판을 가속 회전시킨다. **`ServerWaitOverlay` 를 쓰지 않는다** — 회전 자체가 대기 표시이고 딤이 판을 덮으면 연출이 죽는다. 입력 차단은 버튼 잠금으로 족하다.
2. 최소 회전 시간(약 2.5초)을 보장한다. 결과가 먼저 오면 남은 시간을 채우고, 늦으면 등속 회전을 유지한다.
3. 결과의 `slotIndex` 를 목표로 감속 정지한다. 정지 각은 결과에서만 나온다.
4. 획득 연출은 기존 `CurrencyGainEffectPlayer` 를 쓴다.
5. **실패·거절이면 급정지하지 않고 원래 자리로 감속 복귀한 뒤** 안내를 띄운다. 급정지는 결함으로 읽힌다.

**로비 진입 버튼**: 로비 셸에 버튼을 붙여 오버레이를 연다. **붙일 정확한 자리는 착수 시 스크린샷을 받아 정한다** — 하단이 이미 꽉 차 있어 프리팹 YAML 추론으로는 헛짚는다.

### 1단계 검증

1. Unity 컴파일 에러 0.
2. 로비 버튼 → 룰렛이 뜨고 칸 8개가 `RouletteConfig` 저작대로 채워진다.
3. 회전 → 감속 정지한 칸이 결과의 `slotIndex` 와 일치한다(로그 대조).
4. 가중치를 극단으로 저작해(한 칸만 999) 그 칸에만 멈추는지 본다.
5. 연속으로 눌러도 회전이 겹치지 않는다.
6. 회전 중 닫기를 눌러도 다음 진입이 깨지지 않는다.

---

## 2단계 — 서버 (구현·배포 완료)

칸 값의 진실원은 **정식 스펙시트 두 표**(`Roulette` · `RouletteSlot`)다. `RouletteConfig.asset` 은 표현 축 + 런타임 그릇으로 남아, 초기화 때 표 값을 덮은 사본이 `RouletteManager` 에 꽂힌다(모험의 `AdventureNodeDef` 와 같은 자리).

> **한때 SO 를 그대로 서버에 올리는 Composition 경로(AdventureChapter 식)로 갔다가 되돌렸다.** 부팅이 막힐 위험이 0이라는 장점이 있었지만, 칸 값을 기획자가 사내 스펙시트 툴에서 관리해야 해서 폐기했다. 되돌린 결정적 이유가 하나 더 있다 — **Composition 경로도 표 이름이 `Roulette` 이라 정식 표와 같은 Firestore 문서를 두고 다툰다.** 열 목록이 달라 나중에 올리는 쪽이 `UploadSnapshot` 의 컬럼 계약 검사에 걸린다. 두 경로를 겸용할 수 없다.

### 2-1. 재화 키를 서버에 연다

- `functions/src/currency/currencyKeys.ts` 의 `CURRENCY_KEYS` 에 `"RouletteTicket"` 추가. **서버 전체에서 필수 수정은 이 한 줄뿐**이다(`wallet.ts`·`walletStore.ts` 는 전부 이 배열 순회).
- 같은 파일 상단의 낡은 주석을 지웠다. "firestore.rules 의 `balances.keys().hasOnly([...])` 와 같아야 한다"고 적혀 있었으나 전수 확인 결과 룰에 그런 검사가 없다(지갑 문서는 `allow write: if false` + 값 검증 없음, 세이브 문서에서 `currency` 는 오히려 금지 필드). `firestore.rules` 는 **수정 없음.**

> **⚠️ `functions-currency/` 를 같은 배포에 묶지 않으면 티켓 잔액이 삭제된다.**
> 그 codebase 는 `functions-currency/scripts/shared-files.js` 가 `currencyKeys.ts` 를 복사해 만드는 미러다. 소스는 자동으로 맞지만 **이미 배포된 번들에는 옛 4키짜리 `normalize()` 가 구워져 있고, 그 함수는 모르는 키를 버린다.** 낡은 `devGrantCurrency` 가 한 번이라도 돌면 그 지갑의 `RouletteTicket` 이 통째로 사라진다.

**디버그 지급은 서버 코드 변경 0줄이다** — `devGrantCurrency` 가 `CURRENCY_KEYS` 조회라 새 키를 자동으로 통과시킨다. 클라는 F8 디버그 오버레이의 `+TICKET` 버튼(`OutgameDebugActions.GrantRouletteTicket`, 10장)이 유일한 입구다. 티켓 수급 경로가 기획 범위 밖이라 이것이 없으면 회전이 늘 `InsufficientTicket` 이다.

### 2-2. 칸 저작 — 스펙시트 두 표

한 표로 합치지 않는다. 손저작이라 회전 비용이 8행에 중복되면 어긋난다(`CardPack` + `CardPackDrop` 과 같은 이유).

**`docs/SpecData/Roulette_sheet.csv`** — `id`(int) · `rouletteId`(string) · `displayName`(string) · `priceType`(string) · `price`(long) · `sortOrder`(int)

**`docs/SpecData/RouletteSlot_sheet.csv`** — `id`(int) · `rouletteId`(string) · `slotIndex`(int) · `rewardType`(string, Currency 고정) · `rewardId`(string) · `amount`(long) · `weight`(int) · `#memo`(string, `#` 접두는 데이터 아님)

- 헤더 3줄(한글설명/필드명/타입), BOM 필수, 첫 열 int id, bool 타입 없다.
- **설명 줄에 쉼표를 쓰지 않는다.** 큰따옴표로 감싸도 툴이 열 구분자로 읽어 그 뒤 주석이 한 칸씩 밀린다. 실제로 한 번 겪었다.
- `minGrade` 축은 넣지 않는다 — 최고등급 선택 로직을 통째로 끌고 오는데 지금 기획엔 등급 잠금이 없다.
- **배관 순서를 지킨다**: 사내 시트 툴에 표 저작 → CS 생성(`SpecDatas.cs`·`SpecData.bytes` 자동 갱신) → `SpecPayloadCodec.TableNames` 에 두 이름 추가(+`RowTypeOf` 매핑) → 릴리스 매니저로 업로드·`PublishIndex`. **넷이 한 묶음이다** — 하나라도 빠지면 부팅이 복구 화면에서 멈춘다. 표를 추가하면 옛 스펙 캐시 파일도 지운다(`%USERPROFILE%\AppData\LocalLow\Burgermonster\TeamfightTCG_Migriation\spec-cache\`).

### 2-3. 클라가 표를 읽는 자리

`Assets/Scripts/OutGame/Roulette/RouletteSpec.cs` — `AdventureNodeSpec` 판박이.

- `TryValidateRequired` — `SpecSheetPreloadStep` 에서 표 유효성 확인
- `TryBuildRuntime(authored, out runtime, out error)` — SO 사본(`Instantiate`)에 표 값을 덮어 `OutgameConfigStep` 이 `RouletteManager.SetConfig` 로 넘긴다

**실패해도 초기화를 세우지 않는다.** 룰렛은 곁가지라 저작 실수 하나로 전 유저의 게임을 막지 않는다 — 판만 서지 않고(`RouletteManager.IsAvailable` = false) 로비 버튼이 숨는다. 모험(`AdventureNodeSpec`)과 갈리는 지점이다.

### 2-4. callable `spinRoulette`

**`mutateSave` 가 아니라 `mutateWallet` 을 탄다.** `mutateSave` 는 `slots` 가 비어도 세이브 쓰기와 revision +1 을 무조건 실행한다. 지갑만 바꾸는 선례는 `claimBattleReward`·`devGrantCurrency` 다.

| 파일 | 역할 |
|---|---|
| `functions/src/roulette/rouletteDraw.ts` | 순수 추첨. Firestore·HttpsError 를 모른다 |
| `functions/src/roulette/rouletteSpecReader.ts` | 두 표를 각각 읽는다. `specBlobReader` 를 직접 import(팩 재수출 경유 금지) |
| `functions/src/commands/spinRoulette.ts` | callable |
| `functions/src/index.ts` | 재수출 한 줄 |

순수 모듈은 `packDraw.ts` 의 난수 주입형을 본뜨되 **import 하지 않는다**(룰렛이 팩에 묶인다). `RollFn` 을 자체 선언하면 구조적 타입이라 `node:crypto` 의 `randomInt` 가 그대로 들어간다.

**요청** `{env, rouletteId, txId?}` / **응답** `{rouletteId, slotIndex, gain:{currency, amount}, wallet:{rev, balances}}`

`revision`·`updatedSlots` 는 싣지 않는다 — `ServerCommandResult` 의 계약대로 "세이브를 안 쓴 명령"이다. `displayName`·`sortOrder` 도 응답에 없다(클라가 표에서 직접 읽는 표시 축이다).

**처리 순서**

1. uid·env 검증 → `rouletteId` 길이 검사(64자) → `invalid-argument`
2. 트랜잭션 **밖**에서 두 표를 `Promise.all` 로 읽고 `resolveRouletteBoard(header, slotRows)`. 헤더가 없거나 `price <= 0` 이거나 미지 `priceType` 이면 `RouletteNotFound`, 유효 칸 0개면 `EmptyPool`
3. `clientReceiptId(request.data?.txId, randomUUID())`
4. `mutateWallet(...)` — `guard` 는 넘기지 않는다(소진 자격이 티켓 잔액 자체라 낙인할 바깥 문서가 없다)
   - `canAfford` 실패 → `InsufficientTicket`
   - **추첨·차감·지급이 전부 `mutate` 콜백 안**이다. 추첨을 밖에서 하면 경합 재실행 때 옛 잔액 기준 상품을 새 잔액에 얹는다
   - `spend` → `grant` → **한 `nextWallet` 으로** 묶는다. `mutate` 는 `WalletUpdate` 하나만 돌려주므로 두 번 부르면 rev 가 2 오르고 영수증이 거짓말을 한다
5. 로그: `rouletteId` · `slotIndex` · `currency` · `amount` · `price` · `rev`

**거절 사유 코드** — `rejectDomain` 이 `HttpsError("permission-denied", "코드: 설명")` 로 던지고, **사유를 message 앞머리에 싣는 것이 와이어 계약**이다(details 는 Unity SDK 가 버린다). 클라 enum 이름이 이 문자열과 정확히 같아야 한다.

`RouletteNotFound` · `EmptyPool` · `InsufficientTicket` · `TxIdReused`(mutateWallet 이 직접 던진다)

**행을 버리는 조건(클라·서버 공통)**: `rewardType != Currency`(대소문자·공백 허용) · `amount <= 0` · 미지 `rewardId` · **`rewardId == "RouletteTicket"`** · `slotIndex` 범위 밖 · `slotIndex` 중복(id 낮은 쪽 생존). 서버는 버린 수를 `droppedRows` 로 로그에 남기고, 클라는 8칸을 못 채우면 판을 세우지 않는다.

**`parseCurrency` 를 쓰지 않는다** — Gold 로 폴백해서 오타를 금화로 조용히 바꾼다. 양쪽 다 이름 정확 대조만 쓴다.

### 2-5. 클라 교체

`ServerRouletteSpinSource` 가 `IRouletteSpinSource` 자리에 들어갔다. **화면의 회전 안무는 손대지 않았다.**

- `SpinRouletteResult` : `ServerCommandResult` 상속 DTO. `gain` 은 `ClaimRewardGain` 재사용.
- 호출은 `ServerSaveCommands.InvokeAsync<SpinRouletteResult>("spinRoulette", ...)` — 세이브·지갑을 건드리는 서버 호출의 유일한 창구다.
- **첫 await 이전에** `CurrencyPendingTicket.Hold(PriceType, -Price)` 를 걸어 누른 프레임에 티켓 표시가 줄게 하고, 그 홀드를 `InvokeAsync` 에 **인자로 넘긴다**(안 넘기면 `WalletCloud.Adopt` 직전의 무음 걷기가 없어 이중 계상). 바깥 `finally` 의 `Settle()` 은 요청 인자를 짓다 던지는 경우를 위한 것이고 멱등이라 겹쳐도 안전하다.
- 응답 채택은 손대지 않는다 — `ServerSaveCommands.RunAsync` 가 `pending.Settle(_notify:false)` → `WalletCloud.Adopt` 순서로 처리하고, **그 두 줄 사이에는 아무것도 끼우면 안 된다**(뒤집으면 이중 계상).
- catch 사슬에서 **`OperationCanceledException` 이 반드시 맨 위**다. 분류기가 취소를 `Transient` 로 접기 때문에 아래로 내리면 취소가 망 실패 팝업이 된다.
- 지갑이 이미 움직인 뒤 결과를 못 그리는 세 갈래(빈 gain · 미지 재화 · 판에 없는 칸)는 `Rejected` 가 아니라 **`RewardUnreadable`** 이다. "거절되었다"고 말하면 늘어난 잔액이 거짓말이 된다.
- `RouletteManager.BuildSource` 에서 `EContentRunMode.Test` 게이트를 걷어냈다. 정상 상태는 "언제나 서버"이고 env 는 `ContentProfileConfig.Active.CloudEnvId` 가 가른다. `LocalRouletteSpinSource` 는 파일 전체 `#if` 를 유지한 채 남았고 유일한 진입로는 F8 오버레이의 `SPIN: LOCAL` 버튼이다.
- `RouletteManager.Precheck` 가 `CurrencyManager.CanAfford` 로 티켓을 먼저 본다 — 표시 잔액 기준이라 앞선 회전의 왕복이 안 끝났으면 자동으로 막힌다.
- 티켓 보유량은 패널의 `TicketText` 가 그리고, 갱신은 `CurrencyManager.OnCurrencyChanged` 구독 하나가 전부 덮는다(낙관 홀드·응답 채택·디버그 지급이 모두 이 이벤트를 때린다).

### 2단계 검증

**완료**
1. `functions`: lint · build 통과, `test-roulette.js` 통과(헤더 케이스 · 드롭 10종 · 경계값)
2. `functions-currency`: build · 미러 단언 통과
3. Unity 컴파일 에러 0
4. 서버 배포 완료(두 codebase 를 같은 배포에 묶음), 스펙 표 업로드 완료

**남은 실기 검증**
1. F8 오버레이 `+TICKET` → 패널의 티켓 수가 즉시 갱신되는지 (상단바에는 뜨지 않는 것이 정상 — 티켓 고향 칸을 저작하지 않았고 룰렛 칸은 티켓을 줄 수 없어 `Lend` 경로가 없다)
2. 회전 → 누른 프레임에 낙관 감소 → 응답 뒤 서버 잔액과 일치(`CurrencyManager.GetServerBalance` 로 대조)
3. 티켓 0장에서 판이 돌기 전에 `InsufficientTicket` 안내(`Precheck` 가 잡는다)
4. 비행기 모드 → 판이 2.5초 돌고 제자리로 복귀한 뒤 "회전 결과를 확인하지 못했습니다"
5. 왕복 중 앱 강제 종료 → 티켓이 두 번 빠지지 않는지(같은 txId 재시도를 서버 영수증이 막는다)
6. 시트에서 가중치를 극단으로 바꿔(한 칸만 999) 재업로드 → 그 칸에만 멈추는지

---

## 함정 목록

| # | 함정 | 증상 | 지금 상태 |
|---|---|---|---|
| 1 | 스펙 표를 로컬에만 추가 | **부팅이 복구 화면에서 멈춘다.** 로비도 안 뜬다 | **살아 있다.** 시트·CS생성·TableNames·업로드 넷이 한 묶음이다 |
| 2 | `functions-currency/` 를 따로 배포 | 티켓 잔액이 조용히 삭제된다 | 회피 — 같은 배포에 묶어 올렸다 |
| 3 | `mutateSave` 로 룰렛을 붙임 | 바꿀 슬롯이 없는데 세이브 쓰기와 revision 이 오른다 | 회피 — `mutateWallet` 을 쓴다 |
| 4 | 화면이 멈출 칸을 스스로 고름 | 연출과 실제 지급이 갈린다 | 회피 — 정지각은 응답의 `slotIndex` 에서만 나온다 |
| 5 | 칸에 `RouletteTicket` 저작 | 티켓 무한 인쇄기 | 회피 — 시트 리더(클라)·서버 리더 양쪽이 그 행을 버린다 |
| 6 | `CurrencyLook.asset` 의 `barIcon` 미저작 | 티켓 획득 연출이 통째로 스킵된다 | 회피 — 저작됨(임시로 icon 과 같은 파일) |
| 7 | Addressables `UIPrefab` 그룹 등록 누락 | 런타임 "UI Prefab Not Exist" | 회피 — 주소 `RoulettePanel` 등록됨 |
| 8 | 표 추가 후 옛 스펙 캐시 방치 | 매 부팅 LogError | 회피 — `spec-cache/` 를 비웠다 |
| 9 | 로컬 추첨이 출시 빌드로 샘 | 잔액이 안 움직이는 공짜 재화 화면 | 회피 — 자동 배선을 끊고 F8 디버그 진입로만 남겼다 |
| 10 | 거절 시 판을 급정지 | 결함으로 읽힌다 | 회피 — `ReturnHomeAsync` 로 감속 복귀 |
| 11 | **CSV 설명 줄에 쉼표** | 큰따옴표로 감싸도 툴이 열 구분자로 읽어 그 뒤 주석이 한 칸씩 밀린다 | **살아 있다.** 실제로 한 번 겪었다 |
| 12 | **Composition 경로와 겸용** | 표 이름이 같아 한 Firestore 문서를 다투고, 열 목록이 달라 나중 쪽이 거부된다 | 회피 — Composition 경로를 걷어냈다 |
| 13 | **`getReplayDivergence` 유령 함수** | `firebase deploy --only functions` 가 삭제 판정에서 멈춘다 | **살아 있다.** 함수를 명시 지정해 올려야 한다 |

## 범위 밖

- 티켓 수급 경로(일일 무료 · 정점 클리어 · 다이아 교환 · 승리 누적)
- 천장 · 누적 보정 · 연속 회전
- 카드 · 팩 상품 칸
