# 아웃게임 구조도 (STRUCTURE)

> 사용자의 설계 승인과 구조 파악의 기준 문서.
> 도메인 설계 확정 시, 구조 변경 시마다 갱신한다 (CLAUDE.md 아웃게임 운영 정책).
> 갱신 주체: outgame-engineer 또는 메인. 근거 없는 노드 금지 — 실제 파일이 있거나 승인된 설계여야 한다.

## 갱신 이력

| 날짜 | 변경 | 승인 |
|---|---|---|
| (초기) | 문서 생성. 도메인 수준 뼈대만 반영, 클래스 수준 구조도는 idioms-onboarding 역주행 및 다음 설계 세션에서 채운다 | - |
| 2026-07-23 | Collection(B/C) 클래스 수준 구조도 추가 (idioms-onboarding 역주행, 구현 완료분 기준) | - |
| 2026-07-24 | Save·재화(A) 섹션 추가 — **시각 브리핑 형식 기준 예시**(구조 지도+시퀀스+원리 카드+파일 지도). 이후 모든 브리핑·recap은 이 형식을 따른다 | - |
| 2026-07-24 | **F-17/F-18 생산 UI 배선 설계 추가 (⬜ 승인 대기)** — 도감 갤러리에 행 생산상태·수확·진행바·완성보상·골드 HUD를 매니저 API에 연결. 신규 노드는 `:::new` 표식 | ⬜ |
| 2026-07-24 | **F-17/F-18 생산 UI 배선 구현 완료 (Phase 2)** — RowView 상태칩·수확, Controller 폴링, ProgressView/CompletionRewardView 신규. 매니저·세이브·재화 계약 불변(순수 소비). 손 배선 인계 `COLLECTION_UI_WIRING.md` | ✅ |

## 도메인 수준 구조 (OUTGAME_ROADMAP 기준)

```mermaid
flowchart TD
    subgraph GRP_A["A. 기반 인프라"]
        SAVE["세이브 스토어"]
        TIME["시각 단일 창구"]
        GOLD["재화 서비스<br/>단일 골드"]
    end
    subgraph GRP_B["B. 마스터·소유"]
        CAT["카드 마스터 창구<br/>CardCatalog 읽기"]
        OWN["카드 소유권"]
    end
    subgraph GRP_C["C. 도감 생산"]
        ROW["행 파생 모델"]
        PROD["생산 상태머신<br/>경과시간 순수함수"]
    end
    D["D. 전투 보상 브리지 (완료)<br/>RewardService"]
    subgraph GRP_E["E. 카드팩"]
        PACK["팩 정의·구매·드로우"]
    end
    subgraph GRP_F["F. 아웃게임 UI"]
        HUD["골드 HUD"]
        GAL["도감 갤러리"]
        SHOP["상점"]
    end

    SAVE --> GOLD
    SAVE --> OWN
    TIME --> PROD
    CAT --> ROW
    OWN --> ROW
    ROW --> PROD
    PROD -->|수확| GOLD
    D -->|지급| GOLD
    GOLD -->|차감| PACK
    PACK -->|부여| OWN
    GOLD --> HUD
    ROW --> GAL
    PACK --> SHOP
```

## 클래스 수준 구조 (도메인별)

<!-- 각 도메인 설계 승인 시 이 아래에 클래스도·데이터 흐름 mermaid를 추가한다.
     기존 승인분과 신규분을 구분 표시할 것. -->

### Collection 도메인 (B. 마스터·소유 + C. 도감 생산) — 구현 완료

각 노드 옆 `[관용구 #n]`은 `docs/IDIOMS.md` 항목 번호.

```mermaid
flowchart TD
    subgraph boot["부트 배선 (MainMenuInitializer.Awake, 순서 중요)"]
        INIT["MainMenuInitializer<br/>allCards 주입"]
    end

    subgraph master["B. 마스터·소유"]
        CAT["CardCatalog<br/>[#1 static창구][#3 안정키]<br/>KeyOf = SO 파일명"]
        OWN["OwnershipManager<br/>[#2 Init/Save][#9 스냅샷]<br/>s_owned 집합"]
    end

    subgraph derive["C-행 파생 (읽기전용)"]
        CFG["CollectionLayoutConfig (SO)<br/>[#1 fallback]<br/>행 def + 전역 튜닝"]
        ROWS["CatalogRows<br/>[#1 static+fallback]<br/>행 파생·완성 판정 캐시"]
        ROW["CatalogRow<br/>[#9 get-only]<br/>행 1개 불변 뷰"]
    end

    subgraph prod["C-생산 (순수 시간)"]
        CLOCK["GameClock<br/>[#4 시각 창구]"]
        PROD["CollectionProductionManager<br/>[#2 Init/Save][#5 Resolve][#7 0덮금지]<br/>s_progress 진행도"]
        INFO["RowProductionInfo /<br/>CompletionRewardInfo<br/>[#6 UI 스냅샷]"]
    end

    subgraph save["세이브 슬롯"]
        SD["CollectionSaveData<br/>OwnershipSaveData<br/>[#3 rowKey]"]
    end

    CUR["CurrencyManager<br/>[#2 단일 골드]"]

    subgraph ui["F. UI (도감 갤러리)"]
        GAL["CollectionGalleryController"]
        RV["CollectionRowView"]
        CV["CollectionCardView"]
    end

    INIT -->|SetSource| CAT
    INIT -->|Init| OWN
    INIT -->|Init| PROD
    CAT --> OWN
    CFG -->|SetLayout| ROWS
    CAT --> ROWS
    ROWS --> ROW
    OWN -->|IsOwned 실시간| ROWS
    ROW --> PROD
    CLOCK --> PROD
    PROD -->|"Harvest 후 Earn"| CUR
    OWN <--> SD
    PROD <--> SD
    PROD --> INFO
    ROWS --> GAL
    GAL --> RV --> CV
    OWN -->|OnOwnershipChanged| GAL
```

**핵심 흐름 2줄**
- 소유: 부트가 `CardCatalog.SetSource → OwnershipManager.Init` → 갤러리가 `CatalogRows.Rows`를 그리고 `IsOwned`로 잠금 표시 → `Grant/Revoke` 시 `OnOwnershipChanged`로 재바인딩.
- 생산: 완성 행을 조회하면 `Resolve`가 `GameClock.Since(lastSettle)`로 경과분 누적(cap 클램프) → `Harvest`가 정수분을 `CurrencyManager.Earn`, 소수 보존 후 즉시 영속.

---

### F-17/F-18 도감 생산 UI 배선 (F. UI) — ✅ 구현 완료 (손 배선 인계 대기)

> 목표: 이미 완비된 `CollectionProductionManager` API(GetInfo/Harvest/HarvestAll/ClaimCompletionReward/OnChanged)를
> 도감 갤러리 UI에 연결. 매니저·세이브·재화 계약은 **변경 없음**(소비만 추가). `:::new` = 이번 신규/확장.

```mermaid
flowchart TD
    subgraph mgr["생산/재화 매니저 (기존, 변경 없음)"]
        PROD["CollectionProductionManager<br/>GetInfo · Harvest · OnChanged"]
        CUR["CurrencyManager<br/>Earn · OnCurrencyChanged"]
        OWN["OwnershipManager<br/>OwnedCount · OnOwnershipChanged"]
        CAT["CardCatalog.Count"]
    end

    subgraph ui["F. 도감 갤러리 UI"]
        GAL["CollectionGalleryController<br/>+ 폴링 틱 · OnChanged 구독"]:::new
        RV["CollectionRowView<br/>+ 상태칩·누적·수확버튼"]:::new
        CV["CollectionCardView (기존)"]
        PV["CollectionProgressView<br/>헤더 n/전체 진행바"]:::new
        CR["CollectionCompletionRewardView<br/>푸터 완성보상 버튼"]:::new
        HUD["GoldHud (기존 코드,<br/>씬 부착만)"]:::new
    end

    GAL -->|"틱마다 RefreshProduction"| RV
    RV -->|"GetInfo(rowKey) 폴링"| PROD
    RV -->|"수확버튼 → Harvest(rowKey)"| PROD
    PROD -->|"OnChanged"| GAL
    GAL --> CV
    PV -->|"OwnedCount / Count"| OWN
    PV --> CAT
    OWN -->|"OnOwnershipChanged"| PV
    CR -->|"GetCompletionRewardInfo / Claim"| PROD
    PROD -->|"OnChanged"| CR
    OWN -->|"OnOwnershipChanged"| CR
    CUR -->|"OnCurrencyChanged"| HUD

    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
```

**설계 요지**
- 생산 누적은 시간 함수라 `OnChanged`가 안 뜬다 → 컨트롤러가 **0.5s 폴링 틱**으로 열린 동안 각 행 `RefreshProduction()` 갱신. 수확/완성수령/소유변경은 `OnChanged`/`OnOwnershipChanged`로 즉시 갱신.
- 행 상태 4종 표시: 잠김(수확버튼 off) / 생산중(누적 표시) / 수확가능(버튼 on) / 상한(만땅). 상태는 `GetInfo`의 `State`+`CanHarvest` 조합.
- 매니저/세이브/재화 계약 **불변** — UI는 순수 소비자(경계 준수).

**흐름 시퀀스 — 생산 폴링 + 수확 (Phase 2 구현)**

```mermaid
sequenceDiagram
    participant U as 유저
    participant GAL as GalleryController
    participant RV as RowView
    participant PROD as ProductionManager
    participant CUR as CurrencyManager
    participant HUD as GoldHud

    loop 0.5s 폴링(열린 동안)
        GAL->>RV: RefreshProduction()
        RV->>PROD: GetInfo(rowKey)
        PROD-->>RV: RowProductionInfo(State/Accumulated/CanHarvest)
        Note over RV: 상태칩·누적텍스트·버튼 interactable 갱신
    end
    U->>RV: 수확 버튼 클릭
    RV->>PROD: Harvest(rowKey)
    PROD->>CUR: Earn(RewardType, 정수분)
    PROD->>PROD: Save + CurrencyManager.Save
    PROD-->>GAL: OnChanged
    GAL->>RV: 전 행 RefreshProduction()
    CUR-->>HUD: OnCurrencyChanged → 골드 갱신
```

> 근거: 코드 실물 확인(2026-07-24). `CollectionRowView.cs`(상태칩·수확), `CollectionGalleryController.cs`(Update 폴링+OnChanged), `CollectionProgressView.cs`/`CollectionCompletionRewardView.cs`(신규), 손 배선 `COLLECTION_UI_WIRING.md`.



> 근거: `OutGame/Save/**`, `OutGame/Currency/CurrencyManager.cs` 실물 확인(2026-07-24).

#### 구조 지도 — 이게 전체 어디에 붙어 있나

```mermaid
flowchart TB
    subgraph L3["3.Manager — 유일한 진입점"]
        DSM["DataSaveManager<br/>static · Load / Save / Data<br/>손상 시 corrupt 키에 백업"]
    end
    subgraph L2["2.Domain — 세이브 값 객체(순수 데이터)"]
        USD["UserSaveData<br/>version=1 · 도메인 조립 지점"]
        CUR["CurrencySaveData"]
        COLS["CollectionSaveData<br/>rows: rowKey / ticks / accumulated"]
        ETC["OwnershipSaveData · DeckSaveData"]
    end
    subgraph L1["1.Repository — 저장 매체(교체 가능)"]
        IR["IRepository"]
        JFR["JsonFileRepository<br/>persistentDataPath/Save/*.json"]
        PPR["PlayerPrefsRepository"]
    end
    CM["CurrencyManager<br/>static · 장부 메모리 캐시<br/>Earn / Spend / OnCurrencyChanged"]

    DSM -->|"JsonUtility 직렬화"| USD
    USD --- CUR
    USD --- COLS
    USD --- ETC
    DSM -->|"키 outgame_save"| IR
    IR -. 구현 .- JFR
    IR -. 구현 .- PPR
    CM -->|"Init: 읽기 / Save: flush"| CUR
```

#### 흐름 시퀀스 — 부트 로드와 재화 캐싱

```mermaid
sequenceDiagram
    participant Boot as 부트 ExecutionOrder -100
    participant DSM as DataSaveManager
    participant Repo as IRepository
    participant CM as CurrencyManager
    Boot->>DSM: Load()
    DSM->>Repo: Load("outgame_save")
    Repo-->>DSM: json
    alt 파싱 실패(손상된 세이브)
        DSM->>Repo: Save("outgame_save_corrupt", 원본)
        Note over DSM: 기본값으로 시작 — 진행도 0 덮어쓰기 금지 규약
    end
    Boot->>CM: Init()
    CM->>DSM: Data.currency.gold 읽어 메모리 캐시
    Note over CM: 이후 Earn/Spend는 메모리에서만 변경.<br/>CM.Save() 호출 시점에 세이브 슬롯 flush → DSM.Save()
```

#### 원리 카드 — 왜 이렇게 생겼나

- **3층 분리(Repository/Domain/Manager)**: 저장 매체 교체가 한 줄(`SetRepository`), 스키마는 값 객체로 고립, 진입점은 하나. — `Save/3.Manager/DataSaveManager.cs`
- **재화 캐싱+flush**: Earn/Spend마다 파일 IO를 안 하려고 메모리 장부로 운영, `CurrencyManager.Save()` 시점에만 영속. **트레이드오프: Save() 누락 시 앱 종료로 장부 유실** → 지급/차감 직후 Save() 호출이 규약. — `Currency/CurrencyManager.cs`
- **CollectionRowProgress 필드 3개의 이유**: `rowKey`는 문자열(인덱스 금지 — 카드 추가돼도 진행도 안 밀림) / `lastSettleUtcTicks`는 long(JsonUtility가 DateTime 직렬화 불가) / `accumulated`는 double(수확 시 정수분만 지급, 소수 잔여 보존). — `Save/2.Domain/CollectionSaveData.cs`

**수정 가능성 높은 지점**: 새 재화 추가 = `ECurrencyType` + `CurrencySaveData` 필드 + `CurrencyManager.Init/Save` 매핑 3곳 동시 수정 / 새 세이브 항목 = `UserSaveData`에 값 객체 필드 추가만(리네임·삭제 금지).

#### 파일 지도 — 다이어그램에서 코드로

| 클래스 | 파일 |
|---|---|
| DataSaveManager | `OutGame/Save/3.Manager/DataSaveManager.cs` |
| IRepository · JsonFileRepository · PlayerPrefsRepository | `OutGame/Save/1.Repository/` |
| UserSaveData 외 값 객체 4종 | `OutGame/Save/2.Domain/` |
| CurrencyManager · ECurrencyType | `OutGame/Currency/` |
