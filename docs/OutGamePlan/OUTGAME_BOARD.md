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
| 랭크 API | `RankManager` (Points/GetInfo/ApplyBattleResult/SetConfig/ResetForDebug) | ⬜ 미구현(설계 승인 2026-07-27) | 캐시 없음 = **`Init` 없음 → 부트 순서 무접촉**. 불변식: 티어=`points` 순수 파생(도달티어 별도 저장 금지) · 강등없음=가감 시 하한 클램프 · 예외 미발생 |
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
| **PKG-STARTER-PACK** | 스타터팩 정의 + 개봉 흐름(클릭 1회, G-28에서 3D 뜯기 폐기) | `CardPackOpener`(팩 API)·`CardPack.prefab` | `UI/Shop/PackRevealView.cs`·`PackClickHandle.cs`·`OutGame/CardPack/PackHandoff.cs` + `StarterPack.asset` + `CardPack.unity`·`CardPack.prefab` 배선 | PKG-ONBOARD-BOOT | outgame-engineer+UI | ✅ 완료(컴파일·씬배선 완료, Play 검증 대기) |
| **PKG-FIRSTBATTLE** | 구매→캐리어→CardPack 씬·[획득]→덱 슬롯0 저장→목적지 이동 | `PackHandoff`·`DeckSaveManager`·`DeckConfig`·`TutorialConfig.Begin` | `UI/Shop/PackAcquireController.cs` + ~~`UI/Lobby/LobbyFirstRunRedirect.cs`~~(→ 튜토리얼 스텝 0) + `CardPack.unity` 배선 | PKG-STARTER-PACK | UI+outgame | ✅ 완료(컴파일·씬배선 완료, Play 검증 대기) |
| **PKG-OUTGAME-TUT** | 아웃게임 첫시작 튜토리얼 **P1~P4** — 진행도 영속 + 스텝 해석 + 강제 게이트 | `CardPackOpener`·`PackHandoff`·`OwnershipManager.HasAnyOwnedSaved`·`TutorialConfig.Begin`(전부 순수 소비) | 신규 `OutGame/Tutorial/`(6) + `UI/Tutorial/`(2) + `Save/2.Domain/TutorialSaveData.cs` / 수정 `UserSaveData`·`GameManager`·`LobbyTabController`·`OwnershipManager`(주석)·`OwnershipDebugTool` / **삭제 `UI/Lobby/LobbyFirstRunRedirect.cs`** | PKG-FIRSTBATTLE | outgame-engineer | ✅ 완료(코드+검수+컴파일 에러 0) — **씬 배선·SO 저작 대기** |
| **PKG-OUTGAME-TUT-WIRE** | 동 **P5·P6** — Pack 탭·구매 버튼 앵커 배선 + **14스텝 저작(전투 3회·구매 사이클 2회)** + 팩 개봉 안내 + 결과 기반 커밋 | `OutgameTutorialData`(SO 저작)·`EOutgameTutorialAnchor`·`PackRevealView`/`PackShowcaseController` 결과 이벤트 | `LobbyScene.unity`(탭1 `tutorialAnchor`·buyButton 앵커) + `OutgameTutorial.asset` + `OutgameTutorialData`/`Runner`/`Bridge`/`GateUI` + `PackRevealView`/`PackShowcaseController` | PKG-OUTGAME-TUT | outgame-engineer+사용자(씬) | ✅ 완료(컴파일 에러 0, Play 검증 대기) |

| **PKG-TUT-REWARD** (선택·후속) | 튜토리얼 전투 보상 미지급 가드 | `TutorialConfig.IsActive` | `Battle/TurnRunner.cs` 또는 `Reward/RewardService.cs` | PKG-FIRSTBATTLE | battle-engineer | ⬜ 보류(선택) |

> **PKG-OUTGAME-TUT 사용자 인계(에디터)**: ① `LobbyScene`의 구 `LobbyFirstRunRedirect` 오브젝트 Missing Script 제거 후 `OutgameTutorialBridge` 부착(`data` 배선) + `PlayBtn`에 `TutorialAnchor(LobbyPlayButton)` ② `CardPack.unity`의 `AcquireButton`에 `TutorialAnchor(PackAcquireButton)` + `PackOpenDirector`에 브리지 ③ `Assets/SO/Tutorial/OutgameTutorial.asset` 스텝 0~2 저작 ④ **`UIPoolManager` 캔버스 `sortingOrder` 1 → 400**(게이트 300이 실패 팝업을 덮음). 상세는 `STRUCTURE.md` G-TUT 절.
>
> **PKG-OUTGAME-TUT-WIRE 결과**: 예상과 달리 씬 배선·SO 저작만으로 끝나지 않았다 — 3D 팩 개봉과 구매 성공은 **uGUI 클릭으로 판정할 수 없어** kind 2개(`WaitPackOpen`/`WaitPurchase`)와 결과 신호 경로가 추가됐다. 뷰가 static 이벤트로 "일어난 일"만 알리고 브리지가 구독한다(`PackRevealView.OnAnyPackOpened` / `PackShowcaseController.OnAnyPurchased`) → 뷰는 여전히 튜토리얼을 모른다. **결과 기반 커밋** 해결됨. 튜토리얼 구매 스텝은 `pack` 저작으로 상점 진열까지 덮어써 `AutoPurchase`처럼 결과가 고정된다. **경제 데드락**은 `PackShowcaseController`가 잔액으로 구매 버튼을 잠그고 게이트가 딤을 자동으로 걷는 탈출로로 대응(경제 값 무수정) — Play 실측 후 필요하면 `minGold`/`price` 조정.

> PKG-TUT-REWARD는 Battle 경계 교차라 **battle-engineer** 전용. 첫 전투 보상 지급을 허용해도 무해 → 필수 아님.

---

## 🟢/🟠 랭크 그룹 — 도메인 H (설계 승인 2026-07-27, 게이트 후)

> **표시용 티어 진행도**(칭호). 보상·난이도·매칭 영향 없음 → 재화·소유·생산·팩 계약을 **하나도 안 건드린다**.
> deps = 🔴 `PKG-RANKTIER-SAVE` 완료. 캐시를 안 두는 설계라 **`GameManager.Boot()` 무수정** = 부트 계약 무접촉이라 나머지가 전부 🟢/🟠다.
> 전략·불변식·엣지 목록은 [`OUTGAME_ROADMAP.md` H절](./OUTGAME_ROADMAP.md), 구조도는 [`STRUCTURE.md` H절](./STRUCTURE.md).

| ID | 패키지 | 소비 계약 | 산출 계약 | 만지는 파일 | deps | 담당 | 등급 | 상태 |
|---|---|---|---|---|---|---|---|---|
| **PKG-RANKTIER-CORE** | 랭크 창구 + 튜닝 SO (H-29·H-30) | `DataSaveManager.Data`/`Save` | **`RankManager` 창구 동결** + `RankConfig` 스키마 | 신규 `OutGame/Rank/RankConfig.cs`·`RankManager.cs` | RANKTIER-SAVE | outgame-engineer | 🟢 (전부 신규 파일) | ⬜ 준비 |
| **PKG-RANKTIER-WIRE** | SO 주입 (H-30) | `RankManager.SetConfig` | `RankConfig.asset` 저작 | 수정 `Utils/DataLibrary.cs`(필드1+호출1) + `Assets/SO/Rank/RankConfig.asset`(**사용자**) | CORE | outgame-engineer(코드)+사용자(에셋) | 🟢 | ⬜ 대기 |
| **PKG-RANKTIER-BATTLE** | 전투 종료 훅 (H-31) | `RankManager.ApplyBattleResult`·`DeckConfig.IsMultiplayer` | 없음(순수 소비) | 수정 `Battle/TurnRunner.cs`(`CaptureResult` 내부 2줄) | CORE | **battle-engineer** | 🟠 (TurnRunner 그룹) | ⬜ 대기 |
| **PKG-RANKTIER-HUD** | 로비 랭크 표시 (H-32) | `RankManager.GetInfo` | 없음 | 신규 `UI/HUD/RankHud.cs` + `LobbyScene.unity` 배선(**사용자**) | CORE, WIRE | UI | 🟠 **로비 씬 그룹** | ⬜ 대기 |
| **PKG-RANKTIER-REWARD** (선택·후속) | 티어 승급 보상 | `CurrencyManager.Earn` | 세이브 필드 추가(수령 티어) | `OutGame/Rank/*` + 씬 `RankReward` 버튼 | HUD | outgame-engineer | 🟠 | ⬜ 보류(범위 밖) |

**격리 판정 — 착수 전 반드시 확인**

| 대상 | 경합 | 판정 |
|---|---|---|
| `LobbyScene.unity` | **`PKG-SHOPTAB`(🔄 진행 중)**, `PKG-ENTRY`(⬜), `PKG-HUD`(⬜) | ⚠️ **충돌.** `.unity` YAML은 머지 불가라 **만지는 노드가 달라도 동시 편집 금지**. `PKG-RANKTIER-HUD` 씬 배선은 **`PKG-SHOPTAB` 반납 후에만** |
| `Battle/TurnRunner.cs` | **`PKG-TUT-REWARD`(⬜ 보류)** — 후보 파일이 정확히 같은 `CaptureResult` | ⚠️ **동일 메서드 경합.** 둘 다 battle-engineer 전용 → 같은 세션에서 하나씩. 착수 시 튜토리얼 집계 정책을 함께 결론내면 왕복 1회로 끝 |
| `Utils/DataLibrary.cs` | `PKG-TUNE`(✅ 완료) | ✅ 충돌 없음 |
| `Save/2.Domain/UserSaveData.cs` | 없음 | ✅ 단독 — 그래서 SAVE 게이트가 수 분에 끝났다 |

**착수 순서**: `SAVE`(✅) → `CORE` → (`WIRE` ∥ `BATTLE`) → `HUD`(SHOPTAB 반납 후) → 사용자 에디터 인계(`RankConfig.asset` 저작 + 티어 배지 아트 + `RankHud` 배선 + `RankReward` 버튼 비활성) → 문서 정합.

> **씬 배선 대상(실측 확인, 신규 노드 생성 0)**: `Tab_Match/MatchContent/PlayBtn/RankInfo`(RectTransform만 — 여기에 `RankHud` 부착) 하위의 `RankBadge`(Image 230×230) = 티어 배지, `RankPower`(오벌 프레임 프리팹, 내부 TMP `"82"`) = 포인트. ⚠️ **`RankText`는 건드리지 말 것** — `RankInfo`가 아니라 형제 노드 `RankReward`(Button 추가된 프리팹 인스턴스) 안의 캡션 `"랭크보상"`이다. 티어명은 배지 스프라이트로 표현하기로 결정(사용자).
>
> ⚠️ **`RankHud`에 `GoldHud` 패턴 복제 금지** — `GoldHud`의 `OnEnable` 즉시 렌더는 `CurrencyManager.Init()`이 `BeforeSceneLoad`에서 끝나기에 안전한 것이다. `RankConfig` 주입은 `DataLibrary.Awake`(순서 0)라 `RankHud.OnEnable`이 먼저 돌 수 있고(비결정), 이벤트를 의도적으로 뺐으므로 잘못된 첫 렌더가 굳는다. **최초 렌더는 `Start()`**, `OnEnable`은 `m_started` 가드.
>
> ⚠️ **`RankConfig.tiers`는 C# 필드 초기화자로 기본 테이블 필수** — `List<>`는 `CreateInstance` fallback에서 빈 리스트가 되고(`BattleReward`의 스칼라 기본값과 다름), `DataLibrary`가 **BattleScene에 없어** 전투 씬 직접 Play는 항상 fallback을 탄다.

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
