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
| 소유 API | `OwnershipManager` (Grant/Revoke/IsOwned/OwnedCount/OnOwnershipChanged) | 🧊 동결 | |
| 카드 창구 | `CardCatalog` (SetSource/KeyOf/Count/IsReady) | 🧊 동결 | KeyOf = SO 파일명 |
| 시각 창구 | `GameClock` (Since/디버그 점프) | 🧊 동결 | |
| 세이브 스키마 | `UserSaveData` (version=1, 값 객체 조립) | 🧊 동결 | 필드 추가만 |
| 생산 API | `CollectionProductionManager` (GetInfo/Harvest/OnChanged) | 🧊 동결 | |
| 팩 API | `CardPackOpener` (SetShop/TryPurchase→OpenedPack) | 🧊 동결 | |
| 보상 API | `RewardService.GrantBattleReward → BattleReward` | 🧊 동결 | 반환값이 팝업 입력 |
| **통합 부트 순서** | `MainMenuInitializer` + `GameManager.Boot()` | 🧊 동결 | Load·CurrencyInit=GameManager(BeforeSceneLoad), SetSource·Ownership·Production·SetShop=MainMenuInitializer. PKG-BOOT 완료 |

---

## 🔴 순차 전용 — 먼저·한 번에 하나 (병렬 금지)

| ID | 패키지 | 산출(무엇을 동결하나) | 담당 | 상태 | 검증 |
|---|---|---|---|---|---|
| **PKG-BOOT** | 통합 부트 배선 | `MainMenuInitializer`에 `CardPackOpener.SetShop(cardShop)` 배선(+`[SerializeField] CardShop`, null→빈 상점 fallback). ※ `DataSaveManager.Load()`+`CurrencyManager.Init()`은 이미 `GameManager.Boot()`(BeforeSceneLoad) 소유 → 중복 추가 안 함. `EnsureBoot`는 `CardCatalog.IsReady` 가드로 이미 no-op | outgame-engineer | ✅ 완료 | 통합 씬 부트 시 골드·소유·팩 로드, 재시작 후 값 유지 |
| **PKG-TUNE** | 튜닝 SO 배선 | `RewardConfig`/`CardShop`(+`NormalPack.Pool`·packId)/`CollectionLayoutConfig` 에셋 생성+매니저 배선(D-12 `SetConfig` 연결). ※ `.asset`은 에디터 작업(사용자) | outgame-engineer(코드)+사용자(에셋) | ✅ 완료(코드·검수) — 인계 있음 | 팩 개봉 시 Pool 카드 나옴, 보상 환산이 SO 값 반영 |

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
| **PKG-POPUP** | F-20 전투 보상 팝업 | `RewardService`/`BattleReward` | 신규 `UI/Reward/*`(PooledUIBase) | PKG-BOOT ✅ | UI | ⬜ 준비 |
| **PKG-FILTER** | F-21 덱빌더 소유 필터 | `OwnershipManager.IsOwned` | `UI/MainMenu/DeckBuilderUI.cs` 단일 | PKG-BOOT ✅ | UI | ⬜ 준비 |

> ※ `PKG-FILTER`는 `MainMenu` 폴더지만 **`DeckBuilderUI.cs` 단일 파일**이라 🟠 그룹(`MainMenuManager`)과 파일이 안 겹침 → 병렬 안전.

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
| PKG-RANK | 랭크 엔드포인트 | 서버 권위 전환과 함께(범위 밖) |
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
| 2026-07-26 | 워크플로우를 Phase 순차 → 동시성 등급(🔴/🟠/🟢) 분류로 전환. 사람이 다중 세션으로 병렬 실행하는 모델. 보드 신설 | (전체 운영 방식) | 문서만 |
