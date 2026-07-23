# 아웃게임 시스템 로드맵 — 도메인 분류 · 태스크 · 순서

> 목표 성장 루프를 붙이기 위한 아웃게임(메타 진행) 전체 계획.
> 구현 세부(스키마 필드·API 시그니처)는 확정하지 않고 담당 에이전트가 경계·규약 안에서 결정한다.
> 상세 규약의 진실원은 코드와 `.claude/agents/outgame-engineer.md`.

## 목표 성장 루프

```
대전 → 재화(골드) 획득 → 카드팩 구매 → 신규 카드셋 획득 → 덱 강화 → (상위 랭크 도전)
        ↑ 남은 카드 수 비례            ↑ 도감이 골드 상시 생산(오프라인 누적·수확)
```

현재 프로젝트는 전투(인게임)만 존재하고 아웃게임 요소가 전무하다. 재화·카드 소유권·세이브(덱 제외)·시간/생산 개념이 0건이며 `Assets/Scripts/Currency/` 빈 폴더만 예약돼 있다.

## 확정 스코프

| 항목 | 결정 |
|---|---|
| 범위 | 전체 루프를 **단계별 Phase**로 순차 구현 (기반→성장). 뽑기·카드팩·전투보상 포함 |
| 재화 | **단일 골드** — 전투 보상·도감 생산·카드팩 구매가 하나의 골드 공유 |
| 랭크 | **범위 밖** — 엔드포인트 표기만. 기존 랜덤매칭 유지 (향후 서버 권위 전환과 함께 기획) |

## 반드시 지킬 경계·규약

근거: `docs/ARCHITECTURE.md` §9, `.claude/agents/outgame-engineer.md`

- **의존 방향 단방향**: `UI → 아웃게임 레이어 → Card(읽기 전용)`. 아웃게임 레이어는 **`Battle/`·`Network/`(CardRegistry 포함) 참조 금지**. `Battle/`·`Network/`·전투 규칙·와이어 프로토콜 **수정 금지**(전투 보상 캡처만 예외적으로 battle-engineer가 담당 — D 참조).
- **배치**: 로직·데이터 → `Assets/Scripts/Currency/`(및 신규 최상위 폴더), 화면 → `Assets/Scripts/UI/Collection/`. **`.unity`·프리팹 편집은 범위 밖** — "무엇을 어느 프리팹에 붙일지"만 문서화.
- **세이브 규약**: 식별자는 **문자열 키(인덱스 금지)**, 하위호환(필드 추가만), **버전 필드 필수**, 부트 순서 `[DefaultExecutionOrder(-100)]`, **로드 실패 시 진행도 0 덮어쓰기 금지**. 선례: `DeckSaveManager.cs`.
- **마스터 데이터 드리프트**: 카드 전체 목록이 이미 3곳(`CardRegistry.asset`/`MainMenuInitializer.cs:8`/`DeckBuilderUI.cs:10`)에 중복. 아웃게임이 **4번째 목록이 되면 안 됨** — 기존 목록을 주입받는 단일 창구를 세운다(B).
- **시각·생산**: 생산은 `(마지막 정산 시각, 현재 시각) → 생산량` **경과시간 순수 함수** + 상한 클램프(`Update` 누산 금지). 시각은 **단일 창구**로 감싸 디버그 시간 점프 가능. 저장 UTC, 시계 역행 처리 정의.
- **재화 서비스**: **범용**(컬렉션 전용 필드 금지), 잔액 변경 **단일 진입점**, 차감은 **성공/실패 반환**(음수 잔액 금지), 변경 이벤트로 UI 갱신.
- **표시명 정본**: `CardData.displayName`. 세이브 키는 `DeckSaveManager`와 동일하게 안정적 문자열 키.
- **관용구**: "순수 static 파사드 + SO 설정(미배선 시 기본 fallback)"(`GameTiming`/`SynergyResolver`). 공용 재사용: `UIPoolManager`·`PooledUIBase`·`DataLibrary`·`UIAnimator`·`SoundManager`. 신규 UI 프리팹은 Addressable + `UIPrefab` 라벨 필수.

---

## 도메인 분류 (6개)

| # | 도메인 | 책임 | 담당 |
|---|---|---|---|
| **A** | 기반 인프라 | 세이브 스토어 · 시각 단일창구 · 재화 서비스(단일 골드) | outgame-engineer |
| **B** | 마스터·소유 데이터 | 카드 마스터 단일창구(읽기) · 카드 소유권 모델 | outgame-engineer |
| **C** | 도감 생산 | 행 파생 모델 · 오프라인 생산 · 완성 보상 | outgame-engineer |
| **D** | 전투 보상 브리지 | 전투 결과 캡처(남은 카드 수) → 캐리어 → 골드 지급 | battle-engineer + outgame-engineer |
| **E** | 카드팩 경제 | 팩 정의 · 구매(골드 차감) · 드로우 → 소유 부여 | outgame-engineer |
| **F** | 아웃게임 UI | 골드 HUD · 도감 갤러리 · 상점 · 덱빌더 소유 필터 | UI(배선 문서화) |

> 랭크(엔드포인트)는 별도 도메인 없이 문서 표기만 — 향후 서버 권위 전환(ARCHITECTURE §10 리스크①)과 함께 기획.

---

## 도메인별 세부 태스크

### A. 기반 인프라 — 모든 도메인의 토대
`Assets/Scripts/Currency/` 및 신규 최상위 폴더(예: `Assets/Scripts/Save/`, `Assets/Scripts/Time/`)

- **A-1 세이브 스토어**: 범용 로컬 JSON 세이브 컨테이너(`DeckSaveManager` 패턴). 버전 필드, 로드 실패 시 진행도 보존. 타 도메인이 각자 섹션을 얹는 구조.
- **A-2 시각 단일 창구**: `DateTime.UtcNow` 래핑 static 파사드 + 디버그 시간 오프셋 주입.
- **A-3 재화 서비스(단일 골드)**: 범용 `CurrencyService`(static 파사드). 잔액/적립/차감(성공·실패 반환)/변경 이벤트. 통화 종류 파라미터화. 세이브 스토어에 영속.

### B. 마스터·소유 데이터
- **B-4 카드 마스터 단일 창구**: 새 목록 없이 부트 시 `MainMenuInitializer.allCards` 주입받는 `CardCatalog`(읽기). 카드 안정 키 규약 확정.
- **B-5 카드 소유권 모델**: 소유 카드 집합(문자열 키) 영속. 기존 9장 기본 소유 마이그레이션. 디버그 해금 도구.

### C. 도감 생산
- **C-6 행 파생 모델**: 전체 카드를 3열로 갈라 행 데이터 파생(하드코딩 금지), 소유 3칸 완성 판정.
- **C-7 행 생산 상태 머신**: 상태 4종(잠김/생산중/수확가능/상한도달), 경과시간 순수함수 생산, 상한 클램프, 오프라인 누적 후 수확 → 재화 적립.
- **C-8 행 진행도 영속**: 마지막 정산 UTC·누적량을 행의 안정 키(카드 키 파생)로 저장. 카드 추가에도 기존 행 진행도 불변.
- **C-9 완성 보상**: 도감 전체 완성 1회성 보상 + 수령 플래그(영속).
- **C-10 생산 튜닝 SO**: 쿨타임·행당 생산량·상한 설정.

### D. 전투 보상 브리지 (경계 교차)
> 결과 캡처는 `Battle/` 수정 필요 → **battle-engineer**, 골드 환산·지급은 **outgame-engineer**로 분리.

- **D-11 (battle) 결과 캡처**: `TurnRunner.CheckGameOver()`(`:161`) 및 이탈 경로(`:140`/`:154`)에서 승/패 + 내 남은 카드 수(`playerField.GetActiveCards().Count + playerField.WaitingCount`) 산출. 중립 사실만 기록.
- **D-12 (battle) 결과 캐리어**: `DeckConfig` 패턴 static 홀더(`Battle/`, 예 `BattleResult`)에 승/패·남은 카드 수·멀티 여부 기록. `GameResultPopup.GoMainMenu`(`:26`) 직전 세팅. RPC 동기화 불필요.
- **D-13 (outgame) 보상 환산·지급**: 로비 부트에서 캐리어 소비 → RewardConfig(SO)로 남은 카드 수→골드 환산 → `CurrencyService.Add` → 캐리어 클리어. 로비 UI가 브리지.

### E. 카드팩 경제
- **E-14 팩 정의 SO**: 팩 종류·가격(골드)·포함 카드 풀·드로우 수.
- **E-15 구매·드로우**: `TryPurchase` → `TrySpend`(실패 시 중단) → 드로우(로컬 랜덤) → `Grant`. 중복 처리 규칙 확정.
- **E-16 획득 결과 데이터**: 신규/중복 구분해 UI 전달.

### F. 아웃게임 UI (프리팹은 문서화만)
`Assets/Scripts/UI/Collection/` 등. 팝업 `PooledUIBase`+`UIPoolManager`, 화면 `MainMenuManager` 패널 토글.

- **F-17 골드 HUD**: 로비·도감·상점 헤더. `CurrencyService.OnChanged` 구독.
- **F-18 도감 갤러리**: 세로 스크롤 3열(행 자동 확장), 행별 생산·수확, 카드 상세+틸트, 완성 보상 버튼.
- **F-19 상점 화면**: 팩 목록·구매·드로우 연출.
- **F-20 전투 보상 팝업**: 로비 진입 시 획득 골드 연출.
- **F-21 덱빌더 소유 필터**: `DeckBuilderUI.InitializeSlots`를 소유 카드만/미소유 비활성으로 필터.
- **F-22 배선 문서**: 신규 프리팹 목록 + Addressable `UIPrefab` 라벨 등록 + `MainMenuManager` 진입 버튼 위치.

---

## 구현 순서 — Phase 로드맵 (각 Phase가 루프의 화살표 1개 완성)

| Phase | 완성되는 루프 구간 | 포함 태스크 | 검증 가능 상태 |
|---|---|---|---|
| **0. 기반** | (토대) | A-1~3, B-4~5 | 재화·소유권이 디버그로 조작·영속됨 |
| **1. 대전→재화** | 대전 → 골드 | D-11~13, F-17·20 | 전투 후 남은 카드 수 비례 골드 획득·표시 |
| **2. 도감 생산** | 재접속 → 수확 · 열람 | C-6~10, F-18 | 행 완성 시 생산·오프라인 누적·수확, 갤러리 열람 |
| **3. 카드팩→덱강화** | 골드 → 카드팩 → 신규셋 → 덱 | E-14~16, F-19·21 | 골드로 팩 구매·신규 카드 소유·덱 편성 |
| **(엔드포인트)** | 상위 랭크 도전 | *범위 밖* | 향후 기획 |

**의존 근거**: A(재화)는 D·C·E의 전제. B(소유·창구)는 C·E·F-21의 전제. D는 재화 유입원이라 C보다 앞. E는 B·D가 모두 선 후 성립.

---

## 대표 참조 파일

- 세이브 선례: `Assets/Scripts/Battle/DeckSaveManager.cs`
- 부트 순서·카드목록 주입: `Assets/Scripts/UI/MainMenu/MainMenuInitializer.cs`(`[DefaultExecutionOrder(-100)]`, `allCards:8`)
- static 파사드+SO fallback 관용구(패턴 참고, Battle 소속): `Assets/Scripts/Battle/Timing/GameTiming.cs`, `Assets/Scripts/Battle/Synergy/SynergyResolver.cs`
- 전투 결과 훅: `Assets/Scripts/Battle/TurnRunner.cs:161,140,154`; 남은 카드 수 `Assets/Scripts/Battle/BattleField.cs:201,15`
- 씬 간 캐리어 패턴: `Assets/Scripts/Battle/DeckConfig.cs`; 결과 팝업 `Assets/Scripts/UI/Battle/GameResultPopup.cs:26`
- 덱빌더 소유 필터 지점: `Assets/Scripts/UI/MainMenu/DeckBuilderUI.cs`
- UI 베이스: `Assets/Scripts/UI/UIManager/PooledUIBase.cs`, `UIPoolManager.cs`, `Assets/Scripts/Utils/DataLibrary.cs`

## 검증 방법 (Phase별 E2E)

- **Phase 0**: 디버그로 골드 가감·카드 소유 토글 → 재시작 후 값 유지. 세이브 손상 주입 시 진행도 보존.
- **Phase 1**: 싱글/멀티 승·패·이탈 각각 → 로비 진입 시 남은 카드 수 비례 골드 지급·HUD 반영. 중복 지급 없음.
- **Phase 2**: 행 완성 → 생산 시작, 디버그 시각 점프로 오프라인 누적·상한·수확 검증. 카드 1장 추가 후 기존 진행도·완성 플래그 불변.
- **Phase 3**: 골드로 팩 구매 → 차감·신규 소유 부여 → 덱빌더 소유 카드만 편성. 골드 부족 시 구매 실패.
- **공통**: Phase 구현 후 `tcg-reviewer` 검수 + 메인이 Unity 콘솔 컴파일 검증.
