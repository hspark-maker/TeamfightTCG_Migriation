# 로비 마이그레이션 & 매칭 플로우 — 작업 핸드오프

> 이 문서는 여러 로컬/세션에 걸쳐 작업을 이어가기 위한 **현재 상태 + 향후 작업** 기록이다.
> 세부 규칙의 진실원은 항상 코드. 아키텍처 전반은 [`ARCHITECTURE.md`](./ARCHITECTURE.md) 참고.
> 최종 갱신: 2026-07-24

---

## 0. 30초 요약 (TL;DR)

- **게임 UI/UX를 `MainMenu.unity` → `New/LobbyScene.unity`(하단 5탭 셸)로 이전 중.** 당분간 두 씬 **병존**, 점진 이식 후 MainMenu 폐기 예정.
- **현재 진입 씬 = `LobbyScene`** (빌드 index 0). 5탭: Shop / Collection / Match / Cards / Pack (`LobbyTabController` 구동, 기본 Match 탭).
- **현재 동작하는 대전 흐름 = 단순 직결**: Match 탭 `PlayBtn` → `LobbyMatchLauncher.StartAiBattle()` → 즉시 `BattleScene` 로드(AI 대전).
- **매칭 연출 플로우(매칭중→완료→상대패 공개→덱선택)는 코드만 완성돼 있고 보류 상태** — 씬에 미연결. 되살리려면 §4의 배선 체크리스트만 수행하면 됨.

---

## 1. 씬 · 빌드 구성 현황

### 빌드 세팅 (`ProjectSettings/EditorBuildSettings.asset`)
| index | 씬 | 역할 |
|---|---|---|
| 0 | `Assets/Scenes/New/LobbyScene.unity` | **시작 씬**. 5탭 로비 셸 |
| 1 | `Assets/Scenes/MainMenu.unity` | 레거시 메뉴(병존, 점진 폐기) |
| 2 | `Assets/Scenes/BattleScene.unity` | 전투 |
| 3 | `Assets/Scenes/TutorialSetup.unity` | 튜토리얼 |

### LobbyScene 구조 (세션2에서 Unity MCP로 정리·검증)
- `LobbyRoot`에 **`MainMenuInitializer` 부착**(`cardRegistry = Assets/SO/CardRegistry.asset`) → 데이터 로드(CardCatalog / Ownership / Production / DeckSave)가 로비에서 완결. `[DefaultExecutionOrder(-100)]`.
- 과거 MainMenu 매니저들이 통째로 복사돼 있던 것 정리 완료: 패널 필드가 전부 NULL이라 `Start()`에서 NRE 나던 **`MainMenuManager` GameObject 제거**, 중복 `MainMenuInitializer`는 1개(LobbyRoot의 것)로 정리.
- **시작 버튼 = `Content/Tab_Match/MatchContent/PlayBtn`**. onClick = `LobbyMatchLauncher.StartAiBattle`.
- **`LobbyTabController` 5탭**(Shop / Collection / Match / Cards / Pack): 각 `BottomBar/TabMenu/Btn_<name>/Button`·`/TabFocus`(selectedMark)에 연결 완료(전부 NULL이던 것 복구).
- **`SceneTransition`**(`SceneTransitionVideo`, `toBattleClip = ToBattleScene`) 존재·배선됨 → 전투 전환 영상 동작.
- **Collection 탭**엔 갤러리 UI(Header / RewardBar / Gallery / Row_0~3)가 이미 구축돼 있음(스크립트 미연결로 추정 — 이식 필요).

---

## 2. 현재 동작 흐름 (단순 직결, 활성)

```
[LobbyScene] (시작 씬)
   5탭 셸 (기본 Match 탭)
      └ Content/Tab_Match/MatchContent/PlayBtn
            └ onClick → LobbyMatchLauncher.StartAiBattle()
                  DeckConfig 비었으면 첫 유효 슬롯 자동 적용 (TryApplyFirstValidDeck)
                  DeckConfig.ClearEnemyDeck()      // 매칭 홀더 오염 방지
                  DeckConfig.SetMultiplayer(false)
                  SceneTransitionVideo.PlayOverlay()
                  SceneManager.LoadScene("BattleScene")
                        │
[BattleScene]           ▼
   GameInitializer.StartBattleAsync() → InitializeSinglePlayerFields()
      playerField = DeckConfig.PlayerDeck
      enemyField  = DeckConfig.HasEnemyDeck ? DeckConfig.EnemyDeck : aiDeckConfig.GetRandomDeck()
                    (단순 흐름에선 홀더 미설정 → 항상 랜덤 폴백)
```

관련 파일:
- `Assets/Scripts/UI/Lobby/LobbyMatchLauncher.cs` — `StartAiBattle()`, `TryApplyFirstValidDeck()`
- `Assets/Scripts/Battle/DeckConfig.cs` — 이번 판 덱 static 홀더
- `Assets/Scripts/Battle/GameInitializer.cs` — 전투 씬 필드 초기화(홀더 우선 + 랜덤 폴백)

---

## 3. 매칭 플로우 (코드 완성 · **보류** · 씬 미연결)

> 배경: "로비 씬 UI" 커밋의 오류가 많아 버리고 단순 직결로 회귀(세션2 결정). `MatchFlowController.cs`는 **삭제하지 않고 미사용 보관**(컴파일 정상). 아래는 되살릴 때를 위한 완전한 참고.

### 설계된 흐름
```
PlayBtn → ② 매칭중(AI 고정, 페이크 타이머 1.8s) → ③ 매칭완료("상대를 찾았습니다" 0.9s)
        → ④ 상대 패 전부 공개 + 내 출전 덱 선택 → ⑤ "전투 시작" → BattleScene
```
- 씬 전환 없이 **패널 오버레이**(SetActive 토글)로 ②③④ 진행.
- **"패 구성" = 저장 덱 6슬롯 중 선택**일 뿐, 전투 셔플/초기배치 로직은 **미변경**(사용자 결정). 즉 앞 3장/대기 3장은 기존처럼 전투 진입 시 셔플로 결정.
- AI 덱은 ②에서 `aiDeckConfig.GetRandomDeck()`로 **1회 확정** → `DeckConfig.SetEnemyDeck()`에 저장 → ④에서 6장 전부 공개 → 전투에서 그 덱 그대로 사용.

### 관련 코드 (이미 존재, 컴파일 검증됨)
- **신규** `Assets/Scripts/UI/Lobby/MatchFlowController.cs` — ②③④ 상태머신.
  - 공개 진입점(버튼 바인딩 대상): `OnPlayPressed()`, `OnCancelPressed()`, `OnStartBattlePressed()`
  - 내부: `RunMatchFlowAsync(CancellationToken)`(UniTask), `ConfirmEnemyDeck()`, `RevealEnemyHand()`(CardElement 6개 스폰), `SetupMyDeckSelection()`(6슬롯 버튼 + 첫 유효 슬롯 자동선택)
  - 재활용: `CardElement`(카드 타일), `DeckGroup`(선택 덱 6장 표시), `GameReadyPanel`(6버튼 패턴)
- **수정** `Assets/Scripts/Battle/DeckConfig.cs` — 상대 덱 홀더 추가:
  `EnemyDeck` / `SetEnemyDeck(IEnumerable<CardData>)` / `HasEnemyDeck`(Count>0) / `ClearEnemyDeck()`
- **수정** `Assets/Scripts/Battle/GameInitializer.cs:63-76` — enemyField를 `HasEnemyDeck`면 홀더값, 아니면 기존 `aiDeckConfig.GetRandomDeck()` 폴백. (기존 경로 무손상)
- **수정** `LobbyMatchLauncher.StartAiBattle` / `MainMenuManager.GameStart` — 진입 시 `DeckConfig.ClearEnemyDeck()`로 레거시 경로 홀더 오염 방지.

---

## 4. 매칭 플로우를 되살릴 때 — Unity 에디터 배선 체크리스트

> 코드는 그대로 두고 씬 배선만 하면 됨. 진행 방식은 "에디터에서 직접 배선"으로 합의됨.

### 0) 컨트롤러 GameObject
- [ ] Match 탭 콘텐츠 하위에 빈 GameObject `MatchFlow` 생성 → `MatchFlowController` 컴포넌트 추가

### A) 패널 3개 생성 (Layer Lab GUI Pro-SimpleCasual 프리팹 위, 플레인 Image 금지)
- [ ] **② 매칭중 패널** — 상태 `TMP_Text` + 취소 버튼
- [ ] **③ 완료 패널** — "상대를 찾았습니다"(버튼 없음, 자동 전환)
- [ ] **④ 덱선택 패널** — 상대 패 컨테이너(`GridLayoutGroup` 권장) + 덱 슬롯 버튼 6개 + `DeckGroup` + "전투 시작" 버튼 + "뒤로" 버튼

### B) `MatchFlowController` 인스펙터 참조
| 필드 | 연결 대상 |
|---|---|
| `aiDeckConfig` | `Assets/SO/AIDeckConfig.asset` (★ BattleScene GameInitializer와 동일 에셋) |
| `matchingPanel` / `foundPanel` / `deckSelectPanel` | ②/③/④ 패널 |
| `matchingStatusText` | ② 패널 안 TMP_Text |
| `enemyCardContainer` | ④ 상대 패 컨테이너 Transform |
| `cardElementPrefab` | 기존 CardElement 프리팹(DeckGroup deckSlots가 쓰는 것) |
| `enemyCardMod` | `Full`(이름/설명/hp) 또는 `Simple`(초상만) |
| `deckButtons` | ④ 덱 슬롯 버튼 6개 (배열 크기 6) |
| `myDeckGroup` | ④ 안 DeckGroup 컴포넌트 |
| `startBattleButton` | ④ "전투 시작" 버튼 |
| `deckPreviewImages` / `emptySlotSprite` | (옵션) 슬롯 미리보기 |
| `lobbyTabController` / `deckTabIndex` | (옵션) "유효 덱 없음" 팝업 → 덱 편성 탭 이동 |
| `matchingDuration` / `foundDuration` | 기본 1.8 / 0.9초 |

### C) 버튼 onClick
- [ ] **Match 탭 PlayBtn** → `MatchFlowController.OnPlayPressed` (현재 `LobbyMatchLauncher.StartAiBattle`에서 교체)
- [ ] **② 취소 버튼** → `OnCancelPressed`
- [ ] **④ 뒤로 버튼** → `OnCancelPressed`
- [ ] **⑤ 전투 시작 버튼** → 인스펙터에 **걸지 말 것**(`Awake`에서 코드 자동 바인딩, 중복 방지)

### D) 테스트
- [ ] LobbyScene Play → Match 탭 PlayBtn → ②(1.8s) → ③(0.9s) → ④ 상대 6장 공개 + 덱 선택 → 전투에서 그 덱 확인

---

## 5. 향후 작업 (백로그)

- **탭 콘텐츠 이식** (대부분 Placeholder):
  - ~~**Cards / Collection = 도감**~~ → **완료.** 도감은 신규 앨범(`Assets/Scripts/UI/Album/*` + `OutGame/Album/*`)으로 재구현돼 로비 탭 idx 4(`Tab_Collection_New.prefab`)에 배선돼 있다. 구 도감(`UI/Collection/*` 방치 생산 컬렉션)은 2026-08-14에 코드·에셋째 삭제됐다.
  - **Pack = 카드팩**: `Assets/Scripts/UI/Shop/PackOpeningView`, `RevealCardView` (PackTest 검증). 데이터는 `Assets/Scripts/OutGame/CardPack/*`.
  - **Shop / Box**: 구현체 미정.
- **덱 편성 UI 로비 이식**: 현재 덱 편성(`DeckBuilderUI`)은 MainMenu 씬에만 있음. 로비에 덱 탭/화면으로 이식 필요(매칭 플로우 ④의 덱 선택과도 연계).
- **MainMenu 폐기**: 모든 기능 로비 이식 완료 후.
- **별건 버그(미수정)**: `Assets/Scripts/UI/CardElement.cs:52`가 이름 표시에 `displayName` 대신 `_card.name`(에셋 파일명) 사용 → 카드 타일에 파일명 노출. **공유 컴포넌트라 모든 사용처에 영향** → 고칠 때 회귀 확인 필요.
- **매칭 플로우 데이터 주의**: AI 덱 엔트리(`AIDeckConfig.asset`)가 6장 미만이면 상대 필드도 그만큼만 초기화됨(`HasEnemyDeck`은 Count>0만 검사). SO 데이터가 각 6장인지 책임.

---

## 6. 컨벤션 · 도구 · 주의점

- **언어**: 코드 주석·문서 한국어. 공개 API `<summary>`.
- **네이밍**: 파라미터 `_camelCase`, 지역변수 `t_camelCase`, 인스턴스 필드 `this.` 명시, static `s_`, 비동기 `UniTask`(fire-and-forget은 `.Forget()`).
- **어셈블리**: asmdef 없음 → `Assembly-CSharp` 단일, 전역 네임스페이스.
- **UI 에셋**: 로비/아웃게임 UI는 **Layer Lab GUI Pro-SimpleCasual 프리팹** 활용(플레인 Image 금지).
- **씬 배선**: Unity MCP `Unity_RunCommand`(에디터 스크립트)로 수행이 안전. 컴파일 검증은 `Unity_GetConsoleLogs` 또는 `Unity_RunCommand`. 프리팹 인스턴스 버튼의 onClick은 프리팹 오버라이드로 저장됨.
- **덱 데이터**: 덱 = `List<CardData>` 6장. 카드 안정 키 = `CardData.name`(SO 에셋 파일명, `displayName` 아님). 영속 = `DeckSaveManager`(6슬롯, `Assets/Scripts/OutGame/Deck/`). **(2026-07-30)** 저장소가 `decks.json` 독립 파일 → `UserSaveData.deck`(`Save/outgame_save.json`)로 통합됐고, 구 파일은 초기화 시 1회 이관 후 `decks_migrated.json`으로 보관된다. `DeckSaveManager.SetCardRegistry(...)`를 `LoadFromSave` 전에 반드시 호출(안 하면 덱 카드 복원 실패 — 과거 버그).
- **AI 덱**: `Assets/SO/AIDeckConfig.asset` (9덱, 각 6장). `GetRandomDeck()`으로 무작위 선택.

---

## 7. 핵심 파일 빠른 참조

| 목적 | 파일 |
|---|---|
| 로비 탭 전환 | `Assets/Scripts/UI/Lobby/LobbyTabController.cs` |
| 로비 AI 대전 진입(현행) | `Assets/Scripts/UI/Lobby/LobbyMatchLauncher.cs` |
| 매칭 플로우(보류) | `Assets/Scripts/UI/Lobby/MatchFlowController.cs` |
| 로비 데이터 초기화 | `Assets/Scripts/UI/MainMenu/MainMenuInitializer.cs` |
| 이번 판 덱 홀더 | `Assets/Scripts/Battle/DeckConfig.cs` |
| 전투 씬 초기화 | `Assets/Scripts/Battle/GameInitializer.cs` |
| 덱 저장/로드 | `Assets/Scripts/Battle/DeckSaveManager.cs` |
| AI 덱 풀 | `Assets/Scripts/Battle/AIDeckConfig.cs` · `Assets/SO/AIDeckConfig.asset` |
| 카드 타일(재사용) | `Assets/Scripts/UI/CardElement.cs` |
| 덱 6칸 표시(재사용) | `Assets/Scripts/UI/MainMenu/DeckGroup.cs` |
| 카드 단일 진실원(레지스트리) | `Assets/Scripts/Network/CardRegistry.cs` · `Assets/SO/CardRegistry.asset` |
| 아웃게임 카드 조회 | `Assets/Scripts/OutGame/Card/CardCatalog.cs` |
