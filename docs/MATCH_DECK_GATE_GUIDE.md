# 전투 전 덱 확인/편집 스텝 — 시스템 정리 + 배선 가이드

> ⛔ **폐기(2026-08-03). 착수 지시로 쓰지 말 것.** 이 문서는 덱 화면을 **BattleScene**에 호스팅하던 설계를 전제한다.
> 그 설계는 로비 호스팅으로 뒤집혔다 — 현행 진실원은 `docs/OutGamePlan/STRUCTURE.md`의 **"로비 연결"** 문단이다.
> 특히 아래 내용은 이제 **사실이 아니다**: 게이트 호출자 = `GameInitializer`(→ `LobbyMatchLauncher`) · 스킵 조건 `TutorialConfig.IsActive`(→ `ShowDeckGate`로 분기) · `EnsureBoot`/`fallbackCardRegistry`/`fallbackDeckImages`(→ 삭제됨) · `MatchFlowController`(→ 삭제됨) · BackButton = 전투 포기(→ 오버레이 닫기).
> 프리팹 계층·컨트롤러 책임 분담 정리(§2~§4)는 여전히 유효해 참고용으로 남긴다.

BattleScene의 인게임 시퀀스 **앞에** 덱 화면을 끼워 넣는 작업의 실측 현황과 남은 배선을 정리한다.
조사 기준: `MatchDeckRoot.prefab`, `BattleScene.unity`, `Scripts/UI/Match/*`.

---

## 1. 결론 먼저

**UI·컨트롤러·프리팹 내부 배선이 전부 끝나 있고, 씬 배치도 이미 돼 있다.** 남은 것은 둘뿐이다.

| # | 남은 것 | 종류 |
|---|---|---|
| **A** | `MatchDeckShell.RunSelectionAsync()`를 부르는 코드가 **어디에도 없다** | 코드 — `GameInitializer.cs` **1개 파일** |
| **B** | `MatchDeckRoot`가 SafeArea **첫 번째 자식**이고 **켜진 채로 시작**한다 | 에디터 — `BattleScene.unity` |

지금 상태로 Play하면 **덱 화면이 뜨긴 하는데 아무도 안 기다려주고, 전투가 그대로 진행된다.**

### 확정된 스코프 결정

| 항목 | 결정 |
|---|---|
| 멀티플레이어 | **게이트 제외.** 한쪽이 덱 화면에 머무는 동안 상대가 타임아웃 없이 대기한다(§5) |
| 덱 선택 | **없음. 항상 0번 슬롯 디폴트** — 코드 수정 불필요, 기존 동작 그대로(§4) |
| 덱 이미지 | 편집 화면 가로 리스트(`MatchDeckStripController`)에서만 사용 — **이미 구현돼 있음** |
| 전투 씬 단독 Play 지원 | **생략.** `fallbackCardRegistry`/`fallbackDeckImages` 미배선인 채로 둔다(§5) |

---

## 2. 시스템 구조

### 2-1. 프리팹 3층

```
MatchDeckRoot.prefab                        guid 66ffb7617a312064db3f733154b3f638
│  RectTransform + MatchDeckShell           ← 게이트 본체. Canvas는 없다(부모 Canvas 필요)
│
├── MatchDeckPanel (인스턴스)                guid 8557e05054a01bc4e89b37fcba98f665
│   │  MatchDeckPanelView                   ← 순수 렌더러(상태 없음)
│   ├── Bg / BgGlow
│   ├── Header > TitleRibbon > TitleText
│   ├── Content
│   │   ├── EnemySection > EnemyInfoBar(PlayerNameText, PowerBadge)
│   │   │                + EnemyCardContainer(Row_0/1 > EnemySlot_0..5)   ※ 렌더링 코드 없음(목업)
│   │   ├── MySection    > MyInfoBar   (PlayerNameText, PowerBadge)       ※ InfoBar도 미사용(목업)
│   │   │                + MyCardContainer (Row_0/1 > MySlot_0..5)        ← 여기만 코드가 그린다
│   │   └── VsBadge / VsBand
│   └── BottomBar : EditButton · BackButton · BattleButton
│
└── MatchDeckEditPanel (인스턴스)            guid d56afa364699d9946983674cb557efc5
    ├── TopBar / DeckStrip                  ← MatchDeckStripController + ScrollRect
    │       └ 칸 프리팹: MatchDeckStripCard.prefab (DeckCard.prefab 배리언트)
    ├── BottomBar / Btn_MatchBack           ← 편집 화면의 유일한 종료 경로
    └── DeckEditPanel (중첩 인스턴스)         guid 8134ddc58443c3348a644266ea3669ec
            ↑ 로비 Tab_Deck의 편집 패널을 **그대로 재사용**. DeckEditController가 여기 있다.
```

포인트: 편집 기능은 새로 만든 게 아니라 **로비 `DeckEditPanel.prefab`을 통째로 중첩**해서 쓴다.
`MatchDeckEditPanel`이 추가한 건 상단 가로 덱 리스트(DeckStrip)와 좌하단 뒤로가기(Btn_MatchBack)뿐이다.
원본 BackButton은 이 배리언트에서 삭제됐으므로, `editBackButton`이 비면 **편집 화면에서 나갈 방법이 없다**
(`MatchDeckShell.cs:66`이 LogError로 잡아준다).

### 2-2. 스크립트 역할

| 스크립트 | 위치 | 역할 |
|---|---|---|
| `MatchDeckShell` | `Scripts/UI/Match/MatchDeckShell.cs` | **게이트 + 상태 진실원**. `SelectedSlot`(저장 슬롯 인덱스)과 두 패널 중 무엇을 보일지만 안다 |
| `MatchDeckPanelView` | 같은 폴더 | MySlot 6칸 렌더 + 하단 3버튼 → 셸 호출. 상태 없음 |
| `MatchDeckStripController` | 같은 폴더 | 편집 화면 상단 가로 덱 리스트. 세이브를 **읽기만** 한다(삭제 콜백 null → 삭제 버튼 안 뜸) |
| `DeckEditController` | `Scripts/UI/Deck/` | 로비와 공유. 편성·저장·미완성 확인 팝업 전부 여기 |
| `DeckEditCollectionGrid` | `Scripts/UI/Deck/` | 편집 화면 카드 풀. `CardCatalog.All` + `OwnershipManager.IsOwned`로 소유 카드만 |

상태를 셸이 `DeckConfig`가 아니라 **슬롯 인덱스**로 드는 이유: `DeckConfig`는 직렬화 없는 씬 캐리어라 "몇 번 슬롯인지"를 표현하지 못한다.

### 2-3. 게이트 계약

```csharp
public async UniTask<bool> RunSelectionAsync(CancellationToken _ct)
```

- `true`  = "전투 시작"이 눌렸고 **`DeckConfig.PlayerDeck`이 확정된 상태**
- `false` = 유저가 포기(BackButton) 했거나 씬이 내려갔다 → **호출자가 복귀를 처리**한다(셸은 씬 이름을 모른다)

`Confirm()`은 `TryConfirmSelection()`이 성공했을 때만 게이트를 연다 — 유효 덱이 없으면 화면을 유지한다.

### 2-4. 내부 흐름

```
RunSelectionAsync
  ├ EnsureBoot()            ← 전투 씬 단독 Play 폴백. 로비 경유 시 IsReady로 즉시 return(사실상 no-op)
  ├ Open()                  ← SetActive(true) + ResolveSlot + 매치 패널 표시
  └ 게이트가 열릴 때까지 대기
        │
        ├ [EditButton]  → OpenEditor() → strip.Build + editController.Open(slot)
        │                    ├ 리스트 클릭 → editController.SwitchTo (6/6이면 조용히 저장)
        │                    └ Btn_MatchBack → RequestExit → (저장 판정) → OnEditorExit → 매치 패널 복귀
        ├ [BackButton]  → Cancel()   → false
        └ [BattleButton]→ Confirm()  → DeckConfig.Set(...) → true
```

---

## 3. 현재 배선 실측

### MatchDeckRoot.prefab / MatchDeckShell

| 필드 | 상태 |
|---|---|
| `matchPanel` / `editPanel` | ✅ 배선됨 |
| `panelView` / `editController` / `strip` | ✅ 배선됨 |
| `editBackButton` | ✅ Btn_MatchBack |
| `fallbackCardRegistry` / `fallbackDeckImages` | ⬜ None — **의도적으로 비워둔다**(§5) |

`MatchDeckPanelView.shell`은 MatchDeckRoot 안에서 오버라이드로 배선돼 있다(프리팹 단독으로 열면 None으로 보이는 게 정상).

### 하위 프리팹

| 대상 | 상태 |
|---|---|
| `MatchDeckPanelView` : mySlots 6칸 · Edit/Back/Battle 버튼 | ✅ 전부 배선 |
| `MatchDeckStripController` : content · slotPrefab · scroll | ✅ 전부 배선 |

### BattleScene.unity

| 항목 | 실측 |
|---|---|
| 배치 위치 | `Canvas` → `SafeArea` → **`MatchDeckRoot`** ✅ 이미 있음 |
| 형제 순서 | SafeArea 자식 10개 중 **0번(첫째)** ❌ 작업 B |
| 활성 상태 | `m_IsActive` 오버라이드 없음 → **켜진 채로 시작** ❌ 작업 B |

---

## 4. 왜 코드 수정이 1개 파일뿐인가

### 0번 디폴트는 이미 그렇게 동작한다

`DeckSaveManager`에는 **압축 불변식**이 있다(`DeckSaveManager.cs:9-10`):

> 유효 덱은 항상 `[0 .. DeckCount-1]`을 연속 점유하고 `[DeckCount .. SLOT_COUNT-1]`은 전부 빈 칸이다.

`MatchDeckShell.ResolveSlot()`의 기존 폴백이 "첫 유효 슬롯"이므로, 덱이 하나라도 있으면 **결과가 항상 0번**이다.
`DeckConfig`에 슬롯 인덱스를 실어 보내는 등의 계약 변경은 필요 없다.

### 전체 덱 목록·덱 이미지·카드 풀도 이미 있다

| 필요한 것 | 담당 | 상태 |
|---|---|---|
| 유저의 전체 덱 목록 | `MatchDeckStripController.Build()` — `SLOT_COUNT` 순회, 유효 슬롯만 나열 | ✅ 구현 + 배선 완료 |
| 덱 대표 이미지 | `DeckImages.ResolveForSlot(slot)` — 세이브의 `imageKey` → 카탈로그, 없으면 첫 카드 아트 폴백 | ✅ strip이 이미 사용 중 |
| 편집 화면 카드 풀 | `DeckEditCollectionGrid` — 소유 카드만 | ✅ 중첩 프리팹으로 따라옴 |

덱 이미지의 세이브 측 진실원은 `DeckSaveManager.s_imageKeys[6]`이고, 덱의 저장·이동·삭제를 따라다닌다.
키는 인덱스가 아니라 **스프라이트 에셋 이름**이라 카탈로그 순서를 바꿔도 기존 덱 그림이 뒤바뀌지 않는다.

### 편집 패널이 사실상 덱 선택으로 동작한다

매치 패널에는 슬롯 선택 UI가 없지만, 편집 화면의 가로 리스트로 슬롯을 갈아타면 출전 덱이 바뀐다:

```
OnStripSlotClicked(3) → SelectedSlot = 3
뒤로가기 → ShowMatchPanel() → 슬롯 3을 그림
전투 시작 → 슬롯 3으로 출전
```

화면에 보이는 덱과 실제 출전 덱은 항상 일치하므로 그대로 둔다(의도된 동선).

---

## 5. 작업 A — 코드 훅 (GameInitializer)

### 삽입 지점

`Assets/Scripts/Battle/GameInitializer.cs`의 `StartBattleAsync()`.
**필드 초기화보다 반드시 앞**이어야 한다 — `playerField.Initialize(DeckConfig.PlayerDeck, ...)`가 덱을 소비하기 때문이다.

권장 위치는 전환 영상 대기 **직후**(현행 38행, `if (DeckConfig.IsMultiplayer)` 바로 위):

```
battleIntro.Await();              ← 카메라 뒤로 빼기 + 보드 숨김 (덱 화면 배경으로 딱 좋다)
전환 영상 대기
▶ 여기 게이트
필드 초기화 (DeckConfig.PlayerDeck 소비)
```

`Await()`가 이미 보드를 숨기고 카메라를 잠근 뒤라, 덱 화면 동안 전투 보드가 비쳐 보이지 않는다.

### 코드

필드 추가:

```csharp
    [SerializeField] MatchDeckShell matchDeckShell;   // 전투 전 덱 확인/편집 게이트(비우면 게이트 없이 기존 동작)
```

`StartBattleAsync()` 안, 필드 초기화 직전:

```csharp
        // 덱 확인/편집 게이트 — 통과해야 DeckConfig.PlayerDeck이 확정된다.
        // 필드 초기화가 그 값을 소비하므로 반드시 이보다 앞에 둔다.
        if (!await RunDeckGate()) return;
```

메서드 추가:

```csharp
    // false = 유저가 전투를 포기했다 → 로비로 되돌리고 이 씬의 초기화를 더 진행하지 않는다.
    async UniTask<bool> RunDeckGate()
    {
        if (this.matchDeckShell == null) return true;   // 미배선 = 게이트 없음(기존 경로 그대로)
        if (DeckConfig.IsMultiplayer)    return true;   // 멀티는 상대를 무한정 기다리게 한다 — 아래 참고
        if (TutorialConfig.IsActive)     return true;   // 튜토리얼은 덱이 스크립트 고정

        bool t_confirmed = await this.matchDeckShell.RunSelectionAsync(this.GetCancellationTokenOnDestroy());
        if (t_confirmed) return true;

        // battleIntro.Await()가 걸어둔 카메라 잠금을 여기서 풀지 않으면 화면비 대응(BattleCameraFit)이 영영 멈춘다.
        // 초기화 중 상대 이탈 경로(57행)가 ClearExternalControl을 부르는 것과 같은 이유다.
        BattleCameraFit.ClearExternalControl();
        BattleCleanup.LoadScene("LobbyScene");

        return false;
    }
```

> `BattleCleanup.LoadScene`은 DOTween·풀·TurnRunner 정리와 네트워크 러너 종료까지 함께 한다.
> `SceneManager.LoadScene`을 직접 부르면 이 정리가 빠진다.

### 멀티를 제외하는 이유

- 멀티는 `SyncInitialDecks()`가 양쪽 덱을 교환한 뒤 시드를 commit-reveal로 합의한다. 한쪽이 덱 화면에 머무는 동안 **상대는 그냥 대기**한다 — 타임아웃이 없어 이탈로도 잡히지 않는다.
- 멀티 진입 경로(`RandomMatchPanel` / `MultiplayerLobbyPanel`)는 매칭 **전에** 덱을 이미 확정한다.

멀티에도 넣으려면 제한시간 + 미확정 시 자동 확정이 먼저 필요하다. 프로토 범위 밖이다.

### 전투 씬 단독 Play를 지원하지 않는 이유

`MatchDeckShell.EnsureBoot()`는 `CardCatalog.IsReady`면 첫 줄에서 return한다.
로비를 거쳐 들어오면 `Boot` 프리팹이 `DontDestroyOnLoad`로 따라오므로 **폴백은 한 번도 쓰이지 않는다.**
BattleScene을 직접 Play할 때만 동작하는 개발 편의 장치라 이번 스코프에서 생략한다.

나중에 필요해지면 `fallbackCardRegistry`/`fallbackDeckImages`를 배선하는 것보다
**`Boot.prefab`을 BattleScene에 놓는 편**이 낫다 — `BootInstaller.s_booted` 가드가 두 번째 사본을 자폭시키므로 안전하고
(StartScene·LobbyScene도 같은 방식으로 사본을 들고 있다), 도감·스타터덱·튜토리얼 데이터까지 함께 살아난다.

---

## 6. 작업 B — 씬 배선 (`BattleScene.unity`)

1. Hierarchy에서 `Canvas > SafeArea > MatchDeckRoot` 선택
2. **SafeArea 자식 중 맨 아래로 드래그** — 현재 첫째라 나머지 9개 UI(턴 배너, 결과 팝업, 시네마틱 캔버스 등) 밑에 깔린다
3. Inspector 좌상단 체크박스를 **꺼서 비활성**으로 저장
   - `RunSelectionAsync` → `Open()`이 `SetActive(true)`로 켠다
   - 셸의 `EnsureWired()`가 `Awake`와 `Open` 양쪽에 있어서, 비활성 시작이 설계상 정상 경로다
4. `GameManager` 오브젝트 선택 → `GameInitializer`의 새 필드 **`Match Deck Shell`** 에 `MatchDeckRoot` 드래그
   *(작업 A 코드가 들어간 뒤에 필드가 생긴다)*

---

## 7. 검증 체크리스트

로비를 거쳐 전투에 진입한 뒤 확인:

- [ ] 덱 화면이 뜨고 **전투가 시작되지 않는다**(카드가 딜링되지 않음)
- [ ] MySlot 6칸에 **0번 덱**이 그려진다
- [ ] `EditButton` → 편집 패널 전환, 상단 가로 리스트에 저장 덱들이 **덱 이미지와 함께** 뜬다
- [ ] 가로 리스트에 **삭제 버튼이 없다**(매치 화면엔 파괴적 경로를 두지 않는 설계)
- [ ] 가로 리스트에서 다른 덱 클릭 → 편집 대상이 바뀌고, 나가면 그 덱이 매치 패널에 보인다
- [ ] `Btn_MatchBack` → 매치 패널 복귀, 편집분이 반영돼 다시 그려진다
- [ ] `BattleButton` → 덱 화면이 닫히고 코인 토스 → 카드 배치 → 턴 루프로 이어진다
- [ ] `BackButton` → LobbyScene 복귀, 콘솔에 카메라/DOTween 관련 잔여 에러 없음
- [ ] 저장 덱이 하나도 없을 때 `BattleButton`이 비활성이고 `EditButton`은 경고만 남기고 아무 일 없다
- [ ] **멀티 매치는 덱 화면 없이** 기존대로 바로 전투에 들어간다

---

## 8. 알려진 미구현 (이번 스코프 밖)

| 항목 | 현황 |
|---|---|
| `EnemySection` / `EnemySlot_0..5` | 프리팹에만 있고 렌더링 코드 없음. 상대 덱을 보여주려면 `DeckConfig.EnemyDeck`을 읽는 경로 추가 필요(로비 `MatchFlowController.RevealEnemyHand()`가 참고 구현) |
| `MyInfoBar` / `EnemyInfoBar` (이름·전투력) | `MatchDeckPanelView`가 건드리지 않음 — 목업 상태 |
| 로비 덱 선택과의 중복 | `MatchFlowController`가 여전히 `DeckConfig.Set()`으로 덱을 확정해 넘긴다. 게이트가 `Confirm`에서 덮어쓰므로 동작은 문제없지만, 같은 선택을 두 번 하는 구조는 남아 있다 |

---

## 참고 파일

| 대상 | 경로 |
|---|---|
| 게이트 셸 | `Assets/Scripts/UI/Match/MatchDeckShell.cs` |
| 매치 패널 뷰 | `Assets/Scripts/UI/Match/MatchDeckPanelView.cs` |
| 가로 덱 리스트 | `Assets/Scripts/UI/Match/MatchDeckStripController.cs` |
| 편집 컨트롤러(로비 공유) | `Assets/Scripts/UI/Deck/DeckEditController.cs` |
| 덱 이미지 조회 | `Assets/Scripts/UI/Deck/DeckImages.cs` |
| 호스트 | `Assets/Scripts/Battle/GameInitializer.cs` |
| 덱 캐리어 | `Assets/Scripts/Battle/DeckConfig.cs` |
| 덱 세이브 | `Assets/Scripts/OutGame/Deck/DeckSaveManager.cs` |
