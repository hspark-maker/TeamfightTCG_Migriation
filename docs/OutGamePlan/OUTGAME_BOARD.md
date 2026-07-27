# 아웃게임 작업 보드 (BOARD) — 운영 현황판

> **이 문서는 "누가/언제/진행률"(운영 상태)의 단일 진실원.** "왜/무엇"(전략·경계·계약)은 [`OUTGAME_ROADMAP.md`](./OUTGAME_ROADMAP.md), 구조·시각은 [`STRUCTURE.md`](./STRUCTURE.md).
> **여러 세션(터미널)을 동시에 켜서 병렬 개발하기 위한 표.** 사용자가 아래 "동시성 등급"을 보고 **지금 싱글로 갈지, 병렬로 몇 세션 켤지 직접 판단**한다.
> 각 세션은 착수 시 자기 패키지 행을 `진행`(+담당 세션 표식)으로, 완료 시 `검수`→`완료`로 바꾼다 — **보드가 세션 간 유일한 공유 상태**다.

## 동시성 등급 — 이게 이 보드의 핵심

| 등급 | 뜻 | 어떻게 |
|---|---|---|
| 🔴 **순차 전용** | 공유 계약/부트/스키마를 만듦·바꿈. 모두의 전제 | **반드시 먼저, 한 번에 하나.** 이게 끝나야 아래가 안전 |
| 🟠 **그룹 내 순차** | 같은 씬/파일을 공유하는 패키지 묶음 | 같은 그룹끼리는 하나씩(또는 worktree 격리). **그룹 밖과는 병렬 OK** |
| 🟢 **병렬 안전** | 독립 파일만 만짐 | 아무 때나 다른 세션과 동시 진행 가능 |

> 규칙의 근거: **기존 공유 계약을 소비만 하면 병렬(🟢), 공유 계약을 만들/바꾸면 순차(🔴).** (ROADMAP "의존 웨이브")

## 상태 범례

`대기`(deps 미충족) · `준비`(deps 완료, 착수 가능) · `진행`(세션 작업 중) · `검수`(tcg-reviewer 게이트) · `완료` · `보류`(계약 변경으로 blocked)

## 계약 동결 현황 (🟢 병렬의 전제)

🟢 병렬 패키지는 아래 **동결된 계약만** 소비한다. 이 표의 계약을 바꿔야 하면 그 작업은 🔴 순차로 강등된다.

| 계약 | 소유 창구 | 상태 | 비고 |
|---|---|---|---|
| 재화 API | `CurrencyManager` (Earn/Spend/CanAfford/OnCurrencyChanged/Save) | 🧊 동결 | Spend는 0 허용·음수 거부 |
| 소유 API | `OwnershipManager` (Grant/Revoke/IsOwned/OwnedCount/OnOwnershipChanged) | 🧊 재동결(G-23) | 시그니처 불변. `GrantDefaults` 삭제(신규=소유0). 검수 통과, 전체지급은 `OwnershipDebugTool`로 일원화. **`HasAnyOwnedSaved()`는 레거시 세이브 마이그레이션 1회 판정 전용**(2026-07-27) — 첫실행 판정 대리로 쓰지 말 것 |
| 카드 창구 | `CardCatalog` (SetSource/KeyOf/Count/IsReady) | 🧊 동결 | KeyOf = SO 파일명 |
| 시각 창구 | `GameClock` (Since/디버그 점프) | 🧊 동결 | |
| 세이브 스키마 | `UserSaveData` (version=1, 값 객체 조립) | 🧊 동결 | 필드 추가만. `tutorial` 슬롯 추가(2026-07-27)도 **VERSION 1 유지** — 구 세이브는 노드 없이 기본값(0/false)으로 읽힘 |
| 생산 API | `CollectionProductionManager` (GetInfo/Harvest/OnChanged) | 🧊 동결 | |
| 팩 API | `CardPackOpener` (SetShop/TryPurchase→OpenedPack) | 🧊 동결 | |
| 보상 API | `RewardService.GrantBattleReward → BattleReward` | 🧊 동결 | 반환값이 팝업 입력 |
| 랭크 API | `RankManager` (Points/GetInfo/ApplyBattleResult/SetConfig/ResetForDebug) | 🧊 신규 동결(PKG-RANKTIER-CORE, 2026-07-27) | 캐시 없음 = **`Init` 없음 → 부트 순서 무접촉**. 불변식: 티어=`points` 순수 파생(도달티어 별도 저장 금지) · 강등없음=가감 시 하한 클램프 · 예외 미발생(`try/catch` 0). `Config`·`Save()`는 private. `GetInfo→RankInfo`(TierIndex/DisplayName/Badge/Points/NextRequired/IsMaxTier) — **최대 티어면 `NextRequired == Points`**(0 아님). `RankConfig.tiers`는 코드 필드 초기화자로 20티어(5랭크×4단계) 기본 테이블 보증 |
| **통합 부트 순서** | `GameManager.Boot()` + `MainMenuInitializer` + `OutgameTutorialBridge` | 🧊 재동결(G-TUT, 2026-07-27) | BootScene 없음. GameManager(BeforeSceneLoad: `Load` → **`OutgameTutorialProgress.Init`** → `CurrencyInit`) → LobbyScene `MainMenuInitializer.Awake`[-100](SetSource·Init) → 씬 브리지 `Awake`(EnsureData 멱등)/`Start`(현재 스텝 진입). ~~`LobbyFirstRunRedirect`~~ **삭제** — 첫실행 자동 구매는 스텝 0 `AutoPurchase`. 검수 통과 |
| **튜토리얼 진행도 API** | `OutgameTutorialProgress` (IsCompleted/StepIndex/Init/Save/CommitStep/Complete/ResetForDebug) | 🧊 신규 동결(2026-07-27) | 진행도 슬롯 매핑을 아는 **유일 창구**(러너·브리지·UI는 이 API로만). 불변식: `outgameCompleted` 우선(인덱스 파생 금지) · 커밋은 스텝 실행 **전** · `migrationChecked` 낙인으로 레거시 판정 계정당 1회 |

---

## 🔴 순차 전용 — 먼저·한 번에 하나 (병렬 금지)

| ID | 패키지 | 산출(무엇을 동결하나) | 담당 | 상태 | 검증 |
|---|---|---|---|---|---|
| **PKG-BOOT** | 통합 부트 배선 | `MainMenuInitializer`에 `CardPackOpener.SetShop(cardShop)` 배선(+`[SerializeField] CardShop`, null→빈 상점 fallback). ※ `DataSaveManager.Load()`+`CurrencyManager.Init()`은 이미 `GameManager.Boot()`(BeforeSceneLoad) 소유 → 중복 추가 안 함. `EnsureBoot`는 `CardCatalog.IsReady` 가드로 이미 no-op | outgame-engineer | ✅ 완료 | 통합 씬 부트 시 골드·소유·팩 로드, 재시작 후 값 유지 |
| **PKG-TUNE** | 튜닝 SO 배선 | `RewardConfig`/`CardShop`(+`NormalPack.Pool`·packId)/`CollectionLayoutConfig` 에셋 생성+매니저 배선(D-12 `SetConfig` 연결). ※ `.asset`은 에디터 작업(사용자) | outgame-engineer(코드)+사용자(에셋) | ✅ 완료(코드·검수) — 인계 있음 | 팩 개봉 시 Pool 카드 나옴, 보상 환산이 SO 값 반영 |
| **PKG-ONBOARD-OWN** | 소유 기본지급 제거(계약 변경) | `OwnershipManager.GrantDefaults()` **삭제**(Init 호출 제거, 전체지급은 `OwnershipDebugTool`로 일원화) → 신규 유저 소유 0. 판정 기준 `OwnedCount==0` 확립 (G-23). 실경로 `OutGame/Collection/` | outgame-engineer | ✅ 완료(검수 통과·컴파일 대기) | 세이브 초기화 후 부팅 시 소유 0, 도감 전부 잠김(정상). 스타터팩 후 6장 |
| **PKG-ONBOARD-BOOT** | 로비 첫실행 리다이렉트(BootScene 없음) | 신규 `UI/Lobby/LobbyFirstRunRedirect.cs`: 로비 `Start`에서 `HasAnyOwnedSaved()==false`면 `TryPurchase(starter)`→캐리어→`LoadScene("PackTest")`, 실패 시 로비 유지 (G-24). ~~BootScene/BootRouter 폐기~~. **2026-07-27: 이 패키지는 `PKG-OUTGAME-TUT`에 흡수 — `LobbyFirstRunRedirect.cs` 삭제, 첫실행 진입은 튜토리얼 스텝 0(`AutoPurchase`)** | outgame-engineer(코드)+사용자(씬) | ✅ 완료 → 대체됨(PKG-OUTGAME-TUT) | 첫실행=CardPack 자동전환, 기존=로비 |
| **PKG-RANKTIER-SAVE** | 랭크 세이브 슬롯(H-28) | 신규 `Save/2.Domain/RankSaveData.cs`(`long points` 단일 필드) + `UserSaveData`에 `rank` 슬롯 1줄. **VERSION 1 유지**(필드 추가만 = 하위호환, `tutorial` 슬롯 선례). 이게 머지되면 나머지 랭크 패키지가 전부 🟢/🟠로 내려간다 | outgame-engineer | ✅ 완료(코드) — 검수·컴파일 대기 | 구 세이브(rank 노드 없음) 로드 시 `points=0` 정상. 읽는 쪽이 아직 없어 동작 영향 0 |

> **PKG-TUNE 코드 배선 완료(검수 통과, feature_Collection)**: `RewardService.SetConfig`→`DataLibrary`, `CatalogRows.SetLayout`→`MainMenuInitializer` 2곳 배선(+`[SerializeField]` 슬롯). CardShop은 PKG-BOOT에서 이미 배선됨. **사용자 인계(에디터)**: ① `RewardConfig.asset` 생성(`Create→TCG/Reward Config`) ② 인스펙터 할당 3건(`DataLibrary.battleRewardConfig`, `MainMenuInitializer.cardShop`←CardShop.asset, `MainMenuInitializer.collectionLayout`←CollectionLayoutConfig.asset) ③ `NormalPack.asset` packId="0" ↔ `PackOpeningView.packId` 정합 확인. Unity 콘솔 컴파일·Play E2E는 사용자 재량(정적 타입·시그니처는 검증됨).

> **PKG-BOOT가 끝나기 전엔 🟠/🟢 전부 통합 씬 검증 불가**(테스트 씬 EnsureBoot로만 부분 검증). PKG-BOOT를 최우선 단독 처리. PKG-TUNE은 PKG-BOOT와 파일이 겹치지 않으면 병행 가능(코드부는 독립, SO 에셋은 사용자).

---

## 🟠 그룹 내 순차 — MainMenu 씬/파일 공유 (그룹 밖과는 병렬 OK)

> 아래 셋은 `MainMenuManager`/로비 씬을 공유 → **같은 세션에서 하나씩**, 또는 세션마다 worktree 격리. `PKG-ENTRY`(허브)를 먼저 얹고 나머지를 이어붙이는 순서 권장.

| ID | 패키지 | 소비 계약 | 만지는 파일 | deps | 담당 | 상태 |
|---|---|---|---|---|---|---|
| **PKG-ENTRY** | MainMenu 통합 진입(도감·상점 토글) | 기존 뷰 | `UI/MainMenu/MainMenuManager.cs` + 로비 씬 | PKG-BOOT ✅ | UI | ⬜ 준비 |
| **PKG-HUD** | F-17 골드 HUD 상주 | `CurrencyManager.OnCurrencyChanged` | `UI/HUD/GoldHud.cs`(존재) + 로비 헤더 | PKG-BOOT ✅ | UI | ⬜ 준비 |
| **PKG-SHOPTAB** | Shop 탭 레이아웃(진행 중) | `PackOpeningView` | `UI/Shop/*` + 로비 씬 | PKG-BOOT | UI | 🔄 진행(기존 브랜치) |

---

## 🟢 병렬 안전 — 독립 파일, 아무 때나 동시

> 서로 다른 파일만 만짐 → 별도 세션에서 **무조건 동시 진행 가능**. deps(PKG-BOOT)만 충족되면.

| ID | 패키지 | 소비 계약 | 만지는 파일 | deps | 담당 | 상태 |
|---|---|---|---|---|---|---|
| **PKG-POPUP** | F-20 전투 보상 팝업 | `RewardService`/`BattleReward` | `UI/Battle/GameResultPopup.cs`(전투씬 처리로 스코프 확정) | PKG-BOOT ✅ | UI | ✅ 완료(검수 통과·컴파일 OK, Play 검증 대기) |
| **PKG-FILTER** | F-21 덱빌더 소유 필터 | `OwnershipManager.IsOwned` | `UI/MainMenu/DeckBuilderUI.cs` 단일 | PKG-BOOT ✅ | UI | ⬜ 준비 |

> ※ `PKG-FILTER`는 `MainMenu` 폴더지만 **`DeckBuilderUI.cs` 단일 파일**이라 🟠 그룹(`MainMenuManager`)과 파일이 안 겹침 → 병렬 안전.

---

## 🟠 온보딩 흐름 그룹 — 도메인 G (Wave 0 후, 그룹 내 순차)

> 이 그룹의 패키지들은 온보딩 씬/컨트롤러 흐름(로비·CardPack 씬)을 공유 → **같은 세션에서 하나씩** 또는 worktree 격리. deps = 🔴 PKG-ONBOARD-OWN·BOOT 완료(소유0 판정·부트 순서 동결).

| ID | 패키지 | 소비 계약 | 만지는 파일 | deps | 담당 | 상태 |
|---|---|---|---|---|---|---|
| **PKG-STARTER-PACK** | 스타터팩 정의 + 개봉 흐름 **(2026-07-27 G-29: 스와이프 뜯기 + 카드 더미 넘기기로 재확장)** | `CardPackOpener`(팩 API)·`CardPack.prefab` | `UI/Shop/PackRevealView.cs`·`PackTearHandle.cs`·`PackCardStack.cs`·`PackCardView.cs`·`OutGame/CardPack/PackHandoff.cs` + `StarterPack.asset` + `CardPack.unity`·`CardPack.prefab`·`PackCard.prefab` 배선 | PKG-ONBOARD-BOOT | outgame-engineer+UI | ✅ 완료(컴파일·씬배선 완료, Play 검증 대기) |
| **PKG-FIRSTBATTLE** | 구매→캐리어→CardPack 씬·[획득]→덱 슬롯0 저장→목적지 이동 | `PackHandoff`·`DeckSaveManager`·`DeckConfig`·`TutorialConfig.Begin` | `UI/Shop/PackAcquireController.cs` + ~~`UI/Lobby/LobbyFirstRunRedirect.cs`~~(→ 튜토리얼 스텝 0) + `CardPack.unity` 배선 | PKG-STARTER-PACK | UI+outgame | ✅ 완료(컴파일·씬배선 완료, Play 검증 대기) |
| **PKG-OUTGAME-TUT** | 아웃게임 첫시작 튜토리얼 **P1~P4** — 진행도 영속 + 스텝 해석 + 강제 게이트 | `CardPackOpener`·`PackHandoff`·`OwnershipManager.HasAnyOwnedSaved`·`TutorialConfig.Begin`(전부 순수 소비) | 신규 `OutGame/Tutorial/`(6) + `UI/Tutorial/`(2) + `Save/2.Domain/TutorialSaveData.cs` / 수정 `UserSaveData`·`GameManager`·`LobbyTabController`·`OwnershipManager`(주석)·`OwnershipDebugTool` / **삭제 `UI/Lobby/LobbyFirstRunRedirect.cs`** | PKG-FIRSTBATTLE | outgame-engineer | ✅ 완료(코드+검수+컴파일 에러 0) — **씬 배선·SO 저작 대기** |
| **PKG-OUTGAME-TUT-WIRE** | 동 **P5·P6** — Pack 탭·구매 버튼 앵커 배선 + **14스텝 저작(전투 3회·구매 사이클 2회)** + 팩 개봉 안내 + 결과 기반 커밋 | `OutgameTutorialData`(SO 저작)·`EOutgameTutorialAnchor`·`PackRevealView`/`PackShowcaseController` 결과 이벤트 | `LobbyScene.unity`(탭1 `tutorialAnchor`·buyButton 앵커) + `OutgameTutorial.asset` + `OutgameTutorialData`/`Runner`/`Bridge`/`GateUI` + `PackRevealView`/`PackShowcaseController` | PKG-OUTGAME-TUT | outgame-engineer+사용자(씬) | ✅ 완료(컴파일 에러 0, Play 검증 대기) |
| **PKG-PACK-HINT** | 개봉 씬 가이드 제거 + 뜯기 상시 안내 (2026-07-27) | `OutgameTutorialGateUI`(무수정)·`PackRevealView` 스테이지 | `UI/Tutorial/OutgameTutorialBridge.cs`(`suppressGuideUI` + 클릭 직접 구독) · `UI/Shop/PackRevealView.cs`(`tearHint`) + `CardPack.unity` 배선(**사용자**) | PKG-OUTGAME-TUT-WIRE | outgame-engineer | ✅ 완료(컴파일 에러 0) — **씬 배선 대기** |

| **PKG-TUT-REWARD** (선택·후속) | 튜토리얼 전투 보상 미지급 가드 | `TutorialConfig.IsActive` | `Battle/TurnRunner.cs` 또는 `Reward/RewardService.cs` | PKG-FIRSTBATTLE | battle-engineer | ⬜ 보류(선택) |

> **PKG-OUTGAME-TUT 사용자 인계(에디터)**: ① `LobbyScene`의 구 `LobbyFirstRunRedirect` 오브젝트 Missing Script 제거 후 `OutgameTutorialBridge` 부착(`data` 배선) + `PlayBtn`에 `TutorialAnchor(LobbyPlayButton)` ② `CardPack.unity`의 `AcquireButton`에 `TutorialAnchor(PackAcquireButton)` + `PackOpenDirector`에 브리지 ③ `Assets/SO/Tutorial/OutgameTutorial.asset` 스텝 0~2 저작 ④ **`UIPoolManager` 캔버스 `sortingOrder` 1 → 400**(게이트 300이 실패 팝업을 덮음). 상세는 `STRUCTURE.md` G-TUT 절.
>
> **PKG-OUTGAME-TUT-WIRE 결과**: 예상과 달리 씬 배선·SO 저작만으로 끝나지 않았다 — 3D 팩 개봉과 구매 성공은 **uGUI 클릭으로 판정할 수 없어** kind 2개(`WaitPackOpen`/`WaitPurchase`)와 결과 신호 경로가 추가됐다. 뷰가 static 이벤트로 "일어난 일"만 알리고 브리지가 구독한다(`PackRevealView.OnAnyPackOpened` / `PackShowcaseController.OnAnyPurchased`) → 뷰는 여전히 튜토리얼을 모른다. **결과 기반 커밋** 해결됨. 튜토리얼 구매 스텝은 `pack` 저작으로 상점 진열까지 덮어써 `AutoPurchase`처럼 결과가 고정된다. **경제 데드락**은 `PackShowcaseController`가 잔액으로 구매 버튼을 잠그고 게이트가 딤을 자동으로 걷는 탈출로로 대응(경제 값 무수정) — Play 실측 후 필요하면 `minGold`/`price` 조정.

> **PKG-PACK-HINT 결과(2026-07-27)**: `CardPack` 씬은 팩 1개·[획득] 1개뿐이라 강제 게이트가 길잡이가 아니라 연출을 가리는 잡음이었다 → 브리지에 씬 스위치 `suppressGuideUI`를 두어 **그 씬에서만** 딤·배너를 끄고 **스텝 진행은 그대로** 둔다. 함정 1개: `WaitPackOpen`은 완료가 `OnAnyPackOpened`라 배너만 빼면 되지만 `WaitClick`의 완료는 **게이트가 대신 걸던 `onClick` 구독**이라, 억제 시 브리지가 직접 물어야 한다(`HookSilently`/`DetachSilent`). `GateUI` 무수정. 대체 안내는 튜토리얼이 아니라 **씬 상시 표시**(`PackRevealView.tearHint`, 뜯기 대기 중 표시 → 뜯김 확정에 소멸) — 온보딩이 끝나도 조작법은 필요하기 때문. **사용자 인계(에디터)**: ① `PackOpenDirector` 브리지 `Suppress Guide UI` 체크 ② `UICanvas` **직속**(`RevealPanel` 형제)에 안내 TMP + `CanvasGroup`(alpha 0 저장, Raycast Target 해제) → `PackRevealView.tearHint` 배선 ③ `AcquireButton`의 `TutorialAnchor`는 **떼지 말 것**(억제 모드의 클릭 감지 경로).
>
> PKG-TUT-REWARD는 Battle 경계 교차라 **battle-engineer** 전용. 첫 전투 보상 지급을 허용해도 무해 → 필수 아님.

---

## 🟢/🟠 랭크 그룹 — 도메인 H (설계 승인 2026-07-27, 게이트 후)

> **표시용 티어 진행도**(칭호). 보상·난이도·매칭 영향 없음 → 재화·소유·생산·팩 계약을 **하나도 안 건드린다**.
> deps = 🔴 `PKG-RANKTIER-SAVE` 완료. 캐시를 안 두는 설계라 **`GameManager.Boot()` 무수정** = 부트 계약 무접촉이라 나머지가 전부 🟢/🟠다.
> 전략·불변식·엣지 목록은 [`OUTGAME_ROADMAP.md` H절](./OUTGAME_ROADMAP.md), 구조도는 [`STRUCTURE.md` H절](./STRUCTURE.md).

| ID | 패키지 | 소비 계약 | 산출 계약 | 만지는 파일 | deps | 담당 | 등급 | 상태 |
|---|---|---|---|---|---|---|---|---|
| **PKG-RANKTIER-CORE** | 랭크 창구 + 튜닝 SO (H-29·H-30) | `DataSaveManager.Data`/`Save` | **`RankManager` 창구 동결** + `RankConfig` 스키마 | 신규 `OutGame/Rank/RankConfig.cs`·`RankManager.cs` | RANKTIER-SAVE | outgame-engineer | 🟢 (전부 신규 파일) | ✅ 완료(검수 통과·컴파일 에러 0) |
| **PKG-RANKTIER-WIRE** | SO 주입 (H-30) | `RankManager.SetConfig` | `RankConfig.asset` 저작 | 수정 `Utils/DataLibrary.cs`(필드1+호출1) + `Assets/SO/Rank/RankConfig.asset`(**사용자**) | CORE ✅ | outgame-engineer(코드)+사용자(에셋) | 🟢 | ✅ 완료(코드·검수 통과) — 사용자 에셋 인계 잔여 |
| **PKG-RANKTIER-BATTLE** | 전투 종료 훅 (H-31) | `RankManager.ApplyBattleResult` (멀티 배제 게이트 제거 — 프로토 스코프 밖) | 없음(순수 소비) | 수정 `Battle/TurnRunner.cs`(`CaptureResult` 내부 1줄) | CORE ✅ | **battle-engineer** | 🟠 (TurnRunner 그룹) | ✅ 완료(검수 통과·컴파일 에러 0, Play 검증은 HUD 후 일괄) |
| **PKG-RANKTIER-HUD** | 로비 랭크 표시 (H-32) | `RankManager.GetInfo` | 없음 | 신규 `UI/HUD/RankHud.cs` + `LobbyScene.unity` 배선 | CORE ✅, WIRE ✅(코드) | UI | 🟠 **로비 씬 그룹** | ✅ 완료(검수 발견 0·컴파일 에러 0·씬 배선 완료, **사용자 저장 필요**) |
| **PKG-RANKTIER-REWARD** | 티어 달성 보상 (H-33) | `CurrencyManager.Earn/Save` · `RankManager.GetInfo` | **`RankRewardManager` 창구** + 세이브 필드 `RankSaveData.claimedCount` + `RankTier.rewardGold` | 신규 `OutGame/Rank/RankRewardManager.cs` · `UI/Rank/`(3) / 수정 `RankSaveData`·`RankConfig`·`DataLibrary`(1줄) / 씬 `RankReward` 버튼 | HUD ✅ | outgame-engineer | 🟠 | ✅ 완료(검수 통과·컴파일 에러 0·런타임 스모크 OK) — **씬 배선·프리팹 저작 대기** |

**격리 판정 — 착수 전 반드시 확인**

| 대상 | 경합 | 판정 |
|---|---|---|
| `LobbyScene.unity` | ~~`PKG-SHOPTAB`~~(✅ 반납 확인 2026-07-27), `PKG-ENTRY`(⬜), `PKG-HUD`(⬜) | ✅ **해소.** `PKG-RANKTIER-HUD` 씬 배선 완료. 다만 `.unity` YAML은 머지 불가라 **남은 `PKG-ENTRY`/`PKG-HUD`도 만지는 노드가 달라도 동시 편집 금지** 원칙은 유지 |
| `Battle/TurnRunner.cs` | **`PKG-TUT-REWARD`(⬜ 보류)** — 후보 파일이 정확히 같은 `CaptureResult` | ⚠️ **동일 메서드 경합.** 둘 다 battle-engineer 전용 → 같은 세션에서 하나씩. ~~착수 시 튜토리얼 집계 정책 결론~~ → **BATTLE 완료(2026-07-27)**: 랭크 훅은 멀티/튜토리얼 구분 없이 무조건 가감으로 결론. PKG-TUT-REWARD 착수 시엔 보상 지급 가드만 다루면 됨(랭크 라인과 무충돌) |
| `Utils/DataLibrary.cs` | `PKG-TUNE`(✅ 완료) | ✅ 충돌 없음 |
| `Save/2.Domain/UserSaveData.cs` | 없음 | ✅ 단독 — 그래서 SAVE 게이트가 수 분에 끝났다 |

**착수 순서**: `SAVE`(✅) → `CORE`(✅) → (`WIRE`✅ ∥ `BATTLE`✅) → `HUD`(✅ 코드+씬배선) → `REWARD`(✅ 코드) → 문서 정합(✅). **도메인 H 코드 종결.** 남은 사용자 인계는 에셋/아트뿐 — `RankConfig.asset` 저작·`DataLibrary` 배선 + 티어 배지 아트(랭크당 재사용 또는 20단계 개별). 둘 다 없어도 동작한다.

> **CORE 반납 결과(2026-07-27)**: 신규 파일 2개만 추가, **수정 파일 0**. `RankConfig.tiers` 기본 테이블은 코드 필드 초기화자에 `브론즈 0 / 실버 50 / 골드 150 / 플래티넘 300 / 다이아몬드 500`(승 +10 · 패 −5) — 배지 아트가 사용자 인계분이다. `RankConfig.asset`은 아직 없고, 없어도 `CreateInstance` fallback으로 기본 테이블이 살아 있다(WIRE는 순수 튜닝·아트 주입). ※ 기본 테이블은 이후 20티어로 세분화됨(아래 노트).
>
> **WIRE 반납 결과(2026-07-27)**: `Utils/DataLibrary.cs`에 `[SerializeField] RankConfig rankConfig` 필드 + `InitializeSingleton()`에 `RankManager.SetConfig(this.rankConfig)` 1줄(기존 `RewardService.SetConfig` 선례와 동형). tcg-reviewer 검수 통과(계약 소비만·이중 진실원 없음·부트 무접촉). ⚠️ **컴파일 검증은 사용자 재량** — 세션에서 Unity MCP 연결이 끊겨(`Connection revoked`) 콘솔 확인 불가. **사용자 에디터 인계**: ① `Assets/SO/Rank/RankConfig.asset` 생성(`Create → Card Battle/Rank Config`) ② PKG-TUNE에서 `battleRewardConfig`를 배선한 **동일 `DataLibrary` GameObject**의 `Rank Config` 슬롯에 할당 ③ (선택) 티어 배지는 HUD 인계와 함께. 미배선이어도 fallback으로 크래시 없음.
>
> **BATTLE 반납 결과(2026-07-27)**: `Battle/TurnRunner.cs`의 `CaptureResult`에 `RankManager.ApplyBattleResult(_won);` **1줄만** 추가(보상 지급·영속 직후, `resultCaptured` 가드 안). 수정 파일 1개·산출 계약 0. **멀티 배제 게이트(`if (!DeckConfig.IsMultiplayer)`) 제거** — 프로토 스코프 밖이라 모든 전투(싱글·멀티·튜토리얼·부전승) 결과를 무조건 가감(사용자 결정). `resultCaptured` 단일 깔때기라 전투당 1회. tcg-reviewer 검수 통과(결정론·이중진실원·계약파손·의존방향 5관점 전부 발견 0), Unity 콘솔 컴파일 에러 0(잔존 2건은 Photon Fusion 에디터 플러그인 기존 노이즈, 무관). **Play 검증은 HUD 배선 후 로비 복귀 시 일괄**(랭크 포인트 증감이 배지/포인트에 반영되는지). `ApplyBattleResult`의 첫 소비처가 생겨 도메인 H의 "대전 → 티어 진행" 루프가 닫힘.
>
> **RankConfig 세분화(2026-07-27, WIRE 후속)**: 기본 테이블을 5티어 → **20티어(5랭크 × 4단계 1~4)** 로 세분화(`RankConfig.cs` 필드 초기화자만 수정, `RankManager` 로직·`RankConfig` 스키마 무변경 = 계약 불변). 균등 25포인트 간격(`브론즈 1`=0 … `다이아몬드 4`=475), 각 랭크 4단계에서 승급(브론즈 4 → 실버 1). `RankManager`는 `tiers`를 오름차순 임의 개수로 일반 처리하므로 코드 무영향. 배지 인계분이 최대 20슬롯으로 늘지만 랭크당 1장 재사용 가능(HUD 저작 재량).
> ⚠️ **이름 겹침 주의**: 로비 씬의 `RankInfo`(RectTransform 노드, `RankHud` 부착 지점)와 C# `RankInfo`(`GetInfo` 반환 struct)는 **이름만 같고 무관**하다.
> 검수 유보 2건(구현엔 반영 안 함, 소비처 생길 때 재판단): ① `tiers`가 빈/전원 null이면 `TierIndex=0`인데 `IsMaxTier=true`("0번이자 최대 티어" — 승인된 동작이나 HUD가 `IsMaxTier`로 연출을 분기하면 오표시) ② `ResetForDebug`는 현재 소비처 0 — 필요하면 `OwnershipDebugTool`(`OutgameTutorialProgress.ResetForDebug` 선례)에 배선.

> **씬 배선 대상(실측 확인, 신규 노드 생성 0)**: `Tab_Match/MatchContent/PlayBtn/RankInfo`(RectTransform만 — 여기에 `RankHud` 부착) 하위의 `RankBadge`(Image 230×230) = 티어 배지, `RankPower`(오벌 프레임 프리팹, 내부 TMP `"82"`) = 포인트. ⚠️ **`RankText`는 건드리지 말 것** — `RankInfo`가 아니라 형제 노드 `RankReward`(Button 추가된 프리팹 인스턴스) 안의 캡션 `"랭크보상"`이다. 티어명은 배지 스프라이트로 표현하기로 결정(사용자).
>
> ⚠️ **`RankHud`에 `GoldHud` 패턴 복제 금지** — `GoldHud`의 `OnEnable` 즉시 렌더는 `CurrencyManager.Init()`이 `BeforeSceneLoad`에서 끝나기에 안전한 것이다. `RankConfig` 주입은 `DataLibrary.Awake`(순서 0)라 `RankHud.OnEnable`이 먼저 돌 수 있고(비결정), 이벤트를 의도적으로 뺐으므로 잘못된 첫 렌더가 굳는다. **최초 렌더는 `Start()`**, `OnEnable`은 `m_started` 가드.
>
> ⚠️ **`RankConfig.tiers`는 C# 필드 초기화자로 기본 테이블 필수** — `List<>`는 `CreateInstance` fallback에서 빈 리스트가 되고(`BattleReward`의 스칼라 기본값과 다름), `DataLibrary`가 **BattleScene에 없어** 전투 씬 직접 Play는 항상 fallback을 탄다.

> **REWARD 반납 결과(2026-07-27)**: 신규 4파일(`OutGame/Rank/RankRewardManager.cs` + `UI/Rank/` 3개), 수정 3파일(`RankSaveData` +4줄 · `RankConfig` rewardGold 컬럼 · `DataLibrary` +1줄). **`RankManager` 무수정**(동결 계약 무접촉) · `UserSaveData.VERSION` 1 유지.
> 설계 결정 3개: ① 수령 상태 = **단조 증가 커서** `claimedCount`(수령 완료 개수). 강등이 없어 수령 집합이 항상 프리픽스라 정수 1개로 접힌다 — `bool[20]`/키 리스트는 표현 못 할 상태(구멍 뚫린 수령)를 허용하고 테이블 길이 변경에 취약. **기본값 0 = 미수령**이라 센티널 불필요. ② 보상량은 `RankTier.rewardGold` — 티어와 **같은 원소**라 인덱스 드리프트가 구조적으로 불가능(별도 SO 분리 대비). ③ 패널은 **씬 직접 저작**(사용자 결정) — `PooledUIBase` 아님, `SetActive` 토글.
> 상태 3종(`Locked`/`Claimable`/`Claimed`)은 `StateOf` 단일 판정이고 **`Claimed`를 가장 먼저 검사**한다 — `RankManager.ResetForDebug()`가 points만 0으로 되돌려 `claimedCount > 도달티어`가 되는 구간에서 재수령이 뚫리기 때문. "달성했지만 순차 차례 아님"은 `Locked`에 포함(하이라이트는 항상 1개).
> tcg-reviewer 검수 **계약 위반 0**(warn 5·nit 8). 그중 2건은 반영: ① `Claim`이 `DataSaveManager.Save()`+`CurrencyManager.Save()`를 둘 다 부르던 것을 **후자 하나로** — `CurrencyManager.Save()`가 내부에서 `DataSaveManager.Save()`를 부르므로 앞세우면 "커서만 오르고 골드 미반영" 상태가 한 번 디스크에 쓰인다. ② `Build()`가 Content 자식을 전부 Destroy해 **`rowPrefab`이 씬 목업 행으로 배선되면 첫 Open 후 행 0개**가 되던 경로 → 원본은 숨기기만 하고 사본을 `SetActive(true)`.
> Unity 실측: 컴파일 에러 0 · 신규 타입 어셈블리 등록 확인 · 경계 인덱스(-5/9999) 예외 없이 `Locked`+`Claim` false·부작용 0 · 현재 세이브(브론즈 2, claimedCount 0)에서 행0만 `Claimable`.
> ⚠️ **`RankConfig.asset`의 `rewardGold`는 코드 초기화자에 매달려 있다** — 에셋 YAML엔 `rewardGold` 키가 **0개**인데 런타임엔 20개 값이 살아 있다(합계 15,800). Unity가 `List<RankTier>` 초기화자로 만든 원소를 재사용하고 YAML에 없는 필드를 덮어쓰지 않기 때문. **지금은 저작 없이 동작하지만** 인스펙터에서 `tiers` 크기를 건드리면 0으로 리셋될 수 있다 → 한 번 저장해 YAML에 굳히는 것을 권장.
> **사용자 인계(에디터)**: ① `LobbyCanvas` 직속(`LobbyRoot` 밖, `DragLayer` 형제)에 `RankRewardOverlay` + `RankRewardPanel` — **스크립트 노드는 켜둔 채** 딤+패널 자식을 `root`에 배선 ② `RankRewardRow.prefab` 저작(`Assets/Assets/Prefabs/UI/RankUI/`, **Addressable 불필요**) ③ `ClaimPopup` + `RankRewardClaimPopup` ④ **`Tab_Match`의 중복 `RankHud` 제거**(정상본은 `PlayBtn/RankInfo`) ⑤ `RankReward` 활성화 + `onClick`→`Open()` — **④와 한 세트**(따로 하면 배지 저작 시 보상 버튼 아이콘이 덮인다). 티어명 TMP는 반드시 `MalgunGothic_TMP`(Quicksand는 한글 글리프 없음).
> 미해결로 남긴 검수 지적: `HasAnyClaimable` 소비처 0(알림점 미도입) · 수령 팝업에 취소 경로 없음(딤에 Button 달아 `Hide()` 연결하면 코드 수정 없이 해결) · 스크롤 위치가 행 인덱스 비율 근사(행 높이 미반영) · `RankConfig`가 `RankManager`/`RankRewardManager` 두 static 캐시에 이중 보관(부트가 둘 다 주입해 현재 실해 없음).
>
> **HUD 반납 결과(2026-07-27)**: 신규 `UI/HUD/RankHud.cs` 1개(수정 파일 0, 산출 계약 0). `Start()` 최초 렌더 + `OnEnable`의 `m_started` 가드로 위 함정 회피, 이벤트 구독 0(`OnDisable` 없음), `Badge`/`Points` 2필드만 소비(`IsMaxTier` 미참조 → 검수 유보 ① 회피), 배지 폴백 배열 없음(진실원 = `RankConfig.tiers[].badge` 단일). tcg-reviewer 검수 **발견 0**, Unity 콘솔 컴파일 에러 0.
> **씬 배선도 이 세션에서 완료**(`RankInfo`에 `RankHud` 부착 + `badgeImage`→`RankBadge`, `pointText`→`RankPower/Text`, `RankReward` 비활성). 배선은 `LobbyScene`이 에디터에 **dirty 상태로 열려 있어** YAML 직접 편집 대신 에디터 API(Undo 등록)로 수행했고, **씬 저장은 사용자 몫으로 남겨 뒀다**(사용자의 다른 미저장 변경과 함께 저장 여부를 판단하도록). ⚠️ 저장 전에 씬을 discard하면 배선이 날아간다.
> **도메인 H 코드 종결** — 남은 건 `RankConfig.asset` 저작·배선 + 티어 배지 아트(에셋/아트 인계분).

---

## 실전 예시 — 어떻게 켜나

- **혼자 순차**: PKG-BOOT 하나만 진행 → 끝나면 다음.
- **2세션 병렬**: PKG-BOOT 끝난 뒤 → 세션A `PKG-POPUP`, 세션B `PKG-FILTER` (둘 다 🟢, 파일 안 겹침).
- **3세션**: 위 2개 + 세션C `PKG-ENTRY`(🟠 허브). HUD/SHOPTAB은 ENTRY 안착 뒤 이어서.

---

## Wave 2+ — 신규 컨텐츠 (후속, 설계 세션 필요)

| ID | 패키지 | 비고 |
|---|---|---|
| PKG-SHOPLIST | 상점 팩 목록(ShopController/타일) | F-19에서 스코프 제외됨 |
| PKG-RANK-SERVER | **PvP 실력 랭크** 엔드포인트 | 서버 권위 전환과 함께(범위 밖). ※ 구 이름 `PKG-RANK` — 표시용 티어(도메인 H, `PKG-RANKTIER-*`)가 편입되며 이름이 겹쳐 개명(2026-07-27). 선행 조건 2개: 랜덤매칭 UI의 로비 이식 + 클라 권위 → 서버 권위 전환 |
| — | pity·추가 재화·등급 가중 등 | 필요 시 `outgame-design-session`으로 승인 후 편입 |

---

## 기획 변경 대응 절차 (수시 변경 흡수)

기획은 수시로 바뀐다. 변경을 **2종류로 갈라** 처리한다 — 이 구분이 병렬을 안 깨는 핵심이다.

```
기획 변경 발생
   │
   ├─ 기존 동결 계약(재화·소유·세이브·부트·창구 API)을 건드리나?
   │
   ├─ 아니오 = 순수 추가 ─→ 보드에 🟢/🟠 패키지 추가만. 진행 중 세션 영향 없음.
   │                         (설계 규모 크면 outgame-design-session 승인 후 편입)
   │
   └─ 예 = 계약 변경 ────→ ① 아래 변경 로그 기록
                            ② 그 계약 소비하던 패키지 상태를 "보류"로
                            ③ 계약 수정을 🔴 순차 전용으로 처리(다시 동결)
                            ④ 동결 완료 후 보류 패키지 재개
```

- **로드맵(전략)은 덧붙이기 위주라 진행 중 패키지 스펙을 무효화하지 않는다.** 매일 바뀌는 건 이 보드(운영)가 흡수.
- **로드맵 급 변경(새 도메인)은 한 세션에서 `outgame-design-session`으로만** 한다 → 여러 세션이 로드맵을 동시 편집하지 않아 머지 충돌 없음. 결과는 보드에 패키지로 떨어진다.

## 변경 로그 (기획·계약 변경 추적)

> 계약이 바뀌면 여기 기록하고 **영향 패키지를 `보류`로** 표시한 뒤 재설계 세션. 병렬의 안전판.

| 날짜 | 변경 | 영향 패키지 | 재작업? |
|---|---|---|---|
| 2026-07-27 | **도메인 H(랭크 — 표시용 티어 진행도) 편입 — 계약 변경 1건 + 신규 계약 1건.** ① **세이브 스키마 추가**: `UserSaveData.rank`(`RankSaveData { long points }`) 슬롯 1개 — 필드 추가라 **VERSION 1 유지**(구 세이브는 노드 없이 `points=0`으로 읽힘, 하위호환·재작업 없음). `tutorial` 슬롯과 동일한 선례. ② **신규 계약 `RankManager` 동결 예정**(구현 시). 스코프를 **표시용**(보상·난이도·매칭 무영향)으로 좁혀 재화·소유·생산·팩 계약을 하나도 안 건드린다. 또 **캐시를 두지 않고 세이브 슬롯을 직접 읽어** `Init`이 없으므로 **`GameManager.Boot()` 무수정** = 통합 부트 순서 **무접촉**(재동결 불필요) → 게이트 하나(SAVE)만 🔴이고 나머지는 전부 🟢/🟠. ③ 보드 명칭 충돌 해소: 기존 Wave 2+ `PKG-RANK` → **`PKG-RANK-SERVER`** 개명(PvP 실력 랭크는 여전히 범위 밖) | 없음(순수 추가). `PKG-TUT-REWARD`는 `TurnRunner.CaptureResult` 파일 경합만 — 보류 상태라 실질 영향 없음 | 문서 + 코드(SAVE만 선행 적용) |
| 2026-07-26 | 워크플로우를 Phase 순차 → 동시성 등급(🔴/🟠/🟢) 분류로 전환. 사람이 다중 세션으로 병렬 실행하는 모델. 보드 신설 | (전체 운영 방식) | 문서만 |
| 2026-07-27 | **F-20/PKG-POPUP 스코프 확정 — 로비 팝업 폐기, 전투씬 처리.** 신규 `UI/Reward/*` 로비 진입 팝업·`BattleRewardHandoff` 대신 기존 전투씬 `GameResultPopup`에서 보상 골드 연출(스케일-인+`+N0 골드` 순차 팝, 심플)로 마무리. 전투 로직·`RewardService` 무수정(순수 표시). `RewardService` 계약 불변 | PKG-POPUP만(순수 추가) | 문서+단일 파일 |
| 2026-07-27 | **아웃게임 첫시작 튜토리얼 편입(P1~P4 완료) — 계약 변경 2건 + 파일 삭제 1.** ① **세이브 스키마 추가**: `UserSaveData.tutorial`(`TutorialSaveData`) 슬롯 1개 — 필드 추가라 **VERSION 1 유지**, 구 세이브는 기본값으로 읽힘(하위호환·재작업 없음). ② **부트 순서 추가**: `GameManager.Boot()`에 `OutgameTutorialProgress.Init()`(Load 직후·CurrencyInit 앞) → 통합 부트 순서 재동결. ③ **`LobbyFirstRunRedirect.cs` 삭제** — 첫실행 자동 구매가 튜토리얼 스텝 0(`AutoPurchase`)으로 흡수(존치 시 "소유 0"·"stepIndex 0" 이중 판정 → 이중 구매). 이로써 **첫실행 판정 창구가 진행도로 단일화**되고 `OwnershipManager.HasAnyOwnedSaved()`는 레거시 마이그레이션 1회 판정 전용으로 축소(시그니처 불변, 주석만 정정). 신규 계약 `OutgameTutorialProgress` 동결 | PKG-ONBOARD-BOOT(→ PKG-OUTGAME-TUT에 흡수) · PKG-FIRSTBATTLE(구매 진입부만 이동, 개봉 이후 구간 불변) | 코드+문서 |
| 2026-07-27 | **도메인 G(신규 유저 온보딩) 편입 — 계약 변경 2건.** ① 소유 API `GrantDefaults` 동작: 신규 유저 전체지급→미지급(소유0 시작). 시그니처 불변, 소비처(도감·덱빌더 소유필터)는 빈 상태 대응만 확인. ② 부트 진입: BootScene(index 0) 라우팅 앞단 추가(기존 2계층 불변). 둘 다 🔴 PKG-ONBOARD-OWN/BOOT로 선행·재동결 | 소유 API 소비처(도감·덱빌더): 회귀 확인만(빈 상태). 신규 온보딩 패키지 3종 편입 | 코드+문서 |
