# 아웃게임 구조도 (STRUCTURE)

> 사용자의 설계 승인과 구조 파악의 기준 문서.
> 도메인 설계 확정 시, 구조 변경 시마다 갱신한다 (CLAUDE.md 아웃게임 운영 정책).
> 갱신 주체: outgame-engineer 또는 메인. 근거 없는 노드 금지 — 실제 파일이 있거나 승인된 설계여야 한다.

## 갱신 이력

| 날짜 | 변경 | 승인 |
|---|---|---|
| 2026-07-28 | **카드팩 개봉 3D → 2D uGUI 전환 (🔵 진행중 — 코드 주석·문서 완료, 씬 재구성은 사용자)** — `CardPack.unity`에서 팩만 3D(절차적 메시 + URP Lit)로 남아 있었는데, **머티리얼 3종에 텍스처가 하나도 없어**(전 슬롯 `fileID: 0`) 3D가 만들어내는 건 단색 실루엣 + 라이팅뿐이었다. 그 하나 때문에 씬이 Perspective 카메라·Directional Light·콜라이더·스카이박스 ambient를 끌고 다닌다 → 팩을 UI Image로 옮겨 렌더 계층을 하나로 합친다. **목표는 동작 동등성 유지** — 룩·타이밍·입력 규칙을 그대로 두고 구현 매체만 바꾼다(`PACK_FEEL_PLAN.md`의 게임필 개선은 이 결정으로 **폐기**). **코드 로직 변경 0** — 조사 결과 3D 의존이 이미 거의 없었다: `PackCardStack`·`PackResultGrid`·`PackCardView`는 순수 uGUI(0건), `PackTearHandle`은 `Input.mousePosition` 폴링이라 `OnMouse*`·콜라이더·`Camera` 참조가 애초에 없고, `PackRevealView`는 `Transform`/`GameObject` 인터페이스뿐이라 RectTransform에 그대로 동작한다. `CardPack.prefab:38`의 `BoxCollider`는 아무도 안 보는 **vestigial**(코드베이스 전수 grep에서 `Physics`/`Collider`/`OnMouse` 0건). 그래서 산출물은 **주석·Tooltip 정리 + 씬 배선 스펙 + 문서 개정**뿐이다. 목표 노드: `UICanvas > BG`(Image, `shakeTarget`) · `PackRoot`(빈 컨테이너) `> Pack`(Image + `PackTearHandle`) `> Seal`(Image) — **세 Image 전부 `raycastTarget` OFF**(안 그러면 `PackTearHandle.IsPointerOverUI()` 가드에 막혀 팩 위에서 시작한 드래그로 개봉이 안 된다). 환산 기준 **270.2 참조px/world**. 함정 2개를 문서화: ① `PackRevealView`가 매 `BeginOpen`마다 `packRoot.localScale`을 1로 스탬프하므로 **팩 크기를 `localScale`로 주면 안 된다**(`sizeDelta`로) ② `shakeTarget`에 팩을 걸면 셰이크 시작 직후 `SetActive(false)`라 no-op → **배경**에 건다(오늘 화면에서 실제로 흔들리던 것도 BG 하나였다). 단위 혼재(`tearDistance`=디바이스px ↔ `flickThreshold`=참조px)는 **알려진 편차로 기록만**(동등성 원칙과 충돌). 상세·씬 배선 체크리스트의 진실원 = [`docs/PACK_OPEN_DIRECTION.md`](../PACK_OPEN_DIRECTION.md) | ✅ |
| (초기) | 문서 생성. 도메인 수준 뼈대만 반영, 클래스 수준 구조도는 idioms-onboarding 역주행 및 다음 설계 세션에서 채운다 | - |
| 2026-07-28 | **카드 비주얼 통일 (✅ 코드+프리팹+검수+컴파일 완료, Play 검증 대기)** — 인게임 `CardView.prefab`은 월드스페이스 SpriteRenderer(루트 Transform·BoxCollider2D 입력), 로비는 ScreenSpaceOverlay uGUI라 **프리팹 복사가 불가** → 비주얼 레이어만 uGUI로 미러링. `CollectionCardView` → **`CardVisualView`**(`Scripts/UI/Common/`, **.meta 동반 이동으로 guid `6f92e16a…` 보존** → 씬·프리팹 배선 무손실), `Bind(CardData,bool)` 시그니처 불변. 신규 `CardKeywordIconView`/`CardSynergyBadgeView` + 프리팹 `UI/Common/CardKeywordIcon`·`CardSynergyBadge`. `Card.prefab`에 `Frame`/`HpPanel`/`KeywordIconRoot`/`SynergyBadgeRoot` 추가(**697줄 추가·삭제 0**, 기존 노드 보존) → 이 한 곳이 도감 그리드·도감 생산행·덱편집 타일·드래그 고스트 4곳의 공유 원본. **표시 규칙 단일 진실원 `Assets/Scripts/Card/CardVisualRules.cs`** 신설(키워드 순회 순서·시너지 dedupe·`활성 우선→requiredCount 내림차순` 정렬·`MaxSynergyBadges=3`)을 `CardView`/`CardVisualView`가 공유 — 이전엔 아웃게임이 정렬 **전** 상한을 적용해 배지 순서가 갈라질 수 있었다(현 에셋 6장은 이미 내림차순이라 화면 변화 0). 아트 소스 `fullImage`→**`battleImage`**(40장 전수 보유, 폴백 0). 범위 밖: `DeckEditSlot`(편성 6칸)·`PackCard`·`DeckCard` | ✅ |
| 2026-07-28 | **카드 비주얼 통일 2차 — 남은 3종 (✅ 코드+프리팹+컴파일 완료, Play 검증 대기)** — 1차에서 범위 밖이던 3종을 같은 진실원에 붙였다. **`DeckEditSlot`/`PackCard`**: `Background`에 `CardVisualView`를 부착하고 `Card.prefab`과 같은 노드 세트를 추가 → `DeckEditSlotView`/`PackCardView`는 `portrait`/`nameText` 직렬화 필드를 버리고 `cardVisual.Bind(card, true)` 한 줄로 위임(슬롯 상태·강조 축만 각자 담당). **`Frame`과 `EmptyMark`는 `Background` 밖(루트)에 둔다** — `Bind(null)`이 `Background`를 통째로 끄므로 안에 있으면 빈 칸에서 테두리·빈칸표시까지 사라진다. 같은 이유로 두 프리팹의 `CardVisualView.frame`은 **의도적 미배선**(프레임은 카드별 데이터가 아니라 슬롯 크롬). **`DeckCard`**는 카드가 아니라 "덱 한 칸"(번호·배너·덱이름)이라 HP/키워드/시너지를 얹지 않고 `Tile`에 `Frame`만 씌워 크롬만 맞춤 + `Tile` 흰색→어두운색, `Number` 검정→금색(대비 확보). 아트 폴백은 `CardVisualRules.PickCardArt`로 이관(`deckPreview` 우선은 목적 전용 필드라 호출부 `DeckListController`에 남김). 부수 보정: `PackCard`의 NEW 리본이 우상단 HP와 겹쳐 좌상단으로 이동, `DeckEditSlot` 루트 금색→어두운색(빈 칸/채운 칸 바탕 일치). **미해결(이번 변경 이전부터)**: `DeckCard.prefab`의 `NamePill`/`Banner` Image가 `enabled=false`이고, 프리팹 저작 495×700 vs 씬 셀 480×780 불일치로 세로 80px 여백이 생겨 덱 이름이 타일에서 떨어져 보인다 | ✅ |
| 2026-07-23 | Collection(B/C) 클래스 수준 구조도 추가 (idioms-onboarding 역주행, 구현 완료분 기준) | - |
| 2026-07-24 | Save·재화(A) 섹션 추가 — **시각 브리핑 형식 기준 예시**(구조 지도+시퀀스+원리 카드+파일 지도). 이후 모든 브리핑·recap은 이 형식을 따른다 | - |
| 2026-07-24 | **F-17/F-18 생산 UI 배선 설계 추가 (⬜ 승인 대기)** — 도감 갤러리에 행 생산상태·수확·진행바·완성보상·골드 HUD를 매니저 API에 연결. 신규 노드는 `:::new` 표식 | ⬜ |
| 2026-07-24 | **F-17/F-18 생산 UI 배선 구현 완료 (Phase 2)** — RowView 상태칩·수확, Controller 폴링, ProgressView/CompletionRewardView 신규. 매니저·세이브·재화 계약 불변(순수 소비). 손 배선 인계 `COLLECTION_UI_WIRING.md` | ✅ |
| 2026-07-24 | **씬/프리팹 실배선 완료 (`CollectionTest.unity`)** — RowView `stateLabel`/`harvestButton`/신규 `productionFill`(Fill을 Filled로 전환·진행바 구동)+Status 활성화, 씬 `CompletionRewardView`(BottomBar)·`GoldHud` 부착. `EnsureBoot`에 Load/CurrencyInit 보강. 에디터 검증: 5h경과 fill 0→0.208, 수확 gold 0→50, 에러 0 | ✅ |
| 2026-07-24 | **생산 진행바 = 단위 사이클 + 행별 전용 뷰로 정리** — 진행바 의미를 `누적/상한`→`다음 1단위까지 사이클 진행률`(`CycleProgress01`)로. `CollectionProgressView`를 소유 집계 바에서 **행별 생산 진행바**로 재작성(행 키 `Bind`), RowView는 원시 `productionFill` 제거 후 이 뷰에 위임. 프리팹 `Status/Progress`에 ProgressView 부착. 저장·수확 계약 불변 | ✅ |
| 2026-07-24 | **E. 카드팩 경제 클래스 구조 추가 (⬜ 설계 승인 대기)** — `CardPackData`/`CardShop`(SO)·`CardPackOpener`(static)·`OpenedPack`(값). E는 자체 세이브 없는 **A(재화)·B(소유) 오케스트레이터**. 구매=즉시개봉(팩 재고 없음)·**팩별 지정 풀 드로우**·**중복 시 소액 골드 환급**. 신규 노드 `:::new` | ⬜ |
| 2026-07-24 | **E. 카드팩 경제 구현 완료 (E-14/15/16)** — `OutGame/CardPack/`에 4파일 생성. 설계와 일치. API 확정: 배선 `SetShop(CardShop)`, 결과 `OpenedPack`(class)+`DrawnCard`(readonly struct)+`EPackOpenResult`(enum: Success/PackNotFound/InsufficientGold/EmptyPool/SpendFailed). 로컬 `System.Random`. 재화·소유 계약 순수 소비(불변). SO 에셋 생성·상점 배선은 사용자/메인 검증 | ✅ |
| 2026-07-24 | **tcg-reviewer 심화 재검 반영 (2건 수정)** — ① **무료팩 허용**: 공유 재화 API `CurrencyManager.Spend` 계약 변경(0 이하 거부→음수만 거부, 0은 잔액변경 없이 성공). price=0 팩 구매 가능해짐(사용자 지시). Spend 사용처는 CardPackOpener 1곳뿐 → 회귀 없음. ② **null 풀 방어**: 드로우 시 null 카드 항목 `continue` 스킵(환급 오지급 차단). #3 저장순서(유저 유리, 재화 유실 없음)는 프로토타입 무시·기록만. 컴파일 에러 0 | ✅ |
| 2026-07-28 | **첫 실행 로비 은폐 — `LoadingCover` 도입 (✅ 코드+씬배선+정적검증, Play 검증 대기)** — `AutoBattle` 직행으로 바꾼 뒤에도 **로비가 눈에 보이는** 문제. 원인은 한 프레임 깜빡임이 아니라 **동기 `SceneManager.LoadScene`이 호출 즉시가 아니라 다음 프레임 `EarlyUpdate.UpdatePreloading`에서 언로드·로드를 돌리기 때문** — 프레임 N이 present된 뒤 로드가 끝날 때까지 그 화면(로비)이 유지된다. 해결: 신규 `Assets/Scripts/UI/Common/LoadingCoverView.cs` + `LobbyScene` 루트 `LoadingCover`(Canvas **order 1000** = `UIPoolManager` 400·`GateUI` 300 위, Scaler 1080×1920 = `LobbyCanvas`와 동일 좌표계, `GraphicRaycaster`+`CanvasGroup`, **씬에 활성 상태로 저작**). 자식은 `Backdrop`(단색 불투명 — Layer Lab `Background`는 `preserveAspect=0` 데모 아트라 불투명·비율 보장을 아트와 분리) + `LoadingArt`(`LoadingBar_common.prefab` 인스턴스를 **Completely 언팩** — 서드파티 데모라 패키지 갱신이 씬 노드를 흔들거나 우리 수정이 오버라이드로 쌓이면 Revert 사고). **해제 방식이 설계의 핵심**: `Start` 코루틴이 `yield return null` 1프레임 뒤 페이드아웃하는데, 코루틴 재개(`ScriptRunBehaviourUpdate`)가 `UpdatePreloading`보다 **뒤**라 ① 씬을 떠나면 커버가 파괴돼 코루틴이 재개되지 않고 켜진 채 전환되고 ② 머무르면 정상 해제된다 — **분기 코드 0줄**. 러너/브리지에 "씬 전환 걸었음" static 플래그를 두는 대안은 폐기(모든 전환 호출부가 세팅해야 하는 계약이 생기고 누락 하나가 영구 검은 화면; 자동 해제는 어긋나도 최악이 "원래 증상 복귀"). 진행바·스피너는 **정지 장식**(동기 로드가 메인스레드를 막는 동안 트위닝하면 임의 값에서 얼어붙어 오히려 프리즈로 읽힌다 — 원본 프리팹에 Animator가 없는 것이 곧 정답), `Slider` 고정폭 2003.5는 1080 기준 좌우 여백으로 교정, `Text_Value`는 `MalgunGothic_TMP`로 교체(Quicksand SDF엔 한글 글리프 없음). 공개 API 없음(`Show`/`Hold`/`SetProgress`는 호출부가 없어 데드코드가 된다). **`OutgameTutorialRunner`·`OutgameTutorialBridge`·`MainMenuInitializer`·`GameManager` 전부 무수정** — 코드 변경은 신규 1파일. ⚠️ 커버가 항상 활성이라 **전투·개봉 씬에서 로비 복귀 시에도** 1프레임+페이드 동안 뜬다(로비 진입마다 도는 `MainMenuInitializer.Awake` 동기 초기화 히칭을 함께 가린다 — 의도) | ✅ |
| 2026-07-28 | **G-TUT 온보딩 진입 단축 — 첫 전투 직행 (✅ 코드+에셋+컴파일 검증)** — 사용자 결정: 첫 실행의 "자동 팩 구매 → 3D 개봉 → [획득] → 로비 [플레이]" 4단계 앞머리를 걷어내고 **부트 직후 곧장 첫 튜토리얼 전투**로 보낸다. `EStepKind`에 `AutoBattle = 5` 추가(SO int 직렬화 규칙대로 **끝에만** 추가) + 러너에 `EnterAutoBattle`(`TutorialConfig.Begin(scenario)` → `LoadScene("BattleScene")`, 상수 `BattleScene` 신설). `AutoPurchase`와 같은 **커밋 선행** 불변식 — 씬을 떠나면 롤백 지점이 없어 커밋을 미루면 전투 중 강제종료가 이 스텝을 무한 반복시킨다(구매와 달리 되돌릴 부작용이 없어 롤백 분기 자체가 없다). 첫 전투의 양 덱은 `TutorialConfig`가 고정 주입하므로(`GameInitializer.InitializeSinglePlayerFields`) **저장 덱이 0인 신규 유저도 그대로 진입**한다 → `LobbyMatchLauncher`의 유효덱 가드는 무수정(이 경로를 타지 않음). 첫 카드 소유는 전투 1 종료 후 기존 구매 사이클이 담당. 에셋은 14→**11스텝**(0=`AutoBattle`, 1~5·6~10 구매 사이클 2회). ⚠️ 진행 중이던 세이브는 인덱스가 3칸 밀리므로 `OwnershipDebugTool` 리셋 필요 | ✅ |
| 2026-07-27 | **PKG-BOOT 통합 부트 배선 (✅ 구현+검수 완료)** — `MainMenuInitializer`에 `[SerializeField] CardShop cardShop` + `CardPackOpener.SetShop(cardShop)` 1줄 추가. `Load`/`CurrencyInit`은 `GameManager.Boot()`가 이미 소유 → 중복 미추가(부트 2계층 확정). null→빈 상점 fallback으로 PKG-TUNE 에셋과 독립 진행. 계약 동결 표 "통합 부트 순서" 🧊 동결. tcg-reviewer 검수: 발견 0(경계·이중진실원·계약 무결). 에셋 배선·Play 통합검증은 사용자 재량 | ✅ |
| 2026-07-24 | **F-19 카드팩 개봉 루프 (✅ 구현+검수 완료)** — 스코프 최종 축소: 상점 목록·독립 씬·MainMenu 진입 전부 제외. **카드팩 클릭→개봉 연출+카드 순차 등장→획득** 루프만. `PackOpeningView`(연출 오케스트레이션·옵션 EnsureBoot)+`RevealCardView`(카드 등장 애니·신규/중복 뱃지) 2클래스. 단일 팩 버튼(고정 packId), 팝업 풀링 미사용(씬 상주 뷰), DOTween 연출. 순수 뷰(세이브 없음), E `CardPackOpener` 소비. **tcg-reviewer 검수: 계약·경계·이중처리 무결, 코루틴 안전성 2건(좀비 잠금·트윈 미정리) 수정**(try/finally+OnDisable StopAllCoroutines/DOKill, RevealCardView OnDestroy DOKill). 컴파일 0. 연출·배선은 Play 검증 대기 | ✅ |
| 2026-07-24 | **F-19 개봉 인터랙션 재구현 (스택→드래그 넘김→2×3 그리드)** — `PackOpeningView`를 순차 등장 코루틴에서 **상태머신(Idle/Stacking/Grid)** 으로 재작성. `RevealCardView`에 월드 드래그(`OnMouseDown/Drag/Up`+`ScreenToWorldPoint`)·`SetDraggable`/`SetSortingOrder`/`FadeIn/Out`/`MoveTo`·`SetSwipeCallback` 추가(스케일인 Reveal→데이터 바인딩만). 넘긴 카드는 파괴 안 하고 그리드 재사용. 재진입=상태가드, `OnDisable`에서 카드 트윈 `KillAllTweens`. 계약(CardPackOpener/OpenedPack/CurrencyManager/OwnershipManager) 불변·순수 뷰. 컴파일·드래그/연출은 메인/사용자 검증 대기 | ✅ |
| 2026-07-24 | **tcg-reviewer 검수 + nit 3건 수정** — critical/warn 0(이중처리·계약·경계·재클릭·좀비트윈 무결). 개선: ① Grid enum 데드상태→그리드 연출 완료까지 Grid 유지 후 Idle(`CoGridToIdle`) ② 드래그 중 `SetDraggable(false)` 시 홈으로 즉시 리셋(잔상 방지) ③ 복귀 중 재드래그 드리프트→홈 슬롯(`m_homeLocalPos`) 기준 고정. OnDisable 잔상 2건은 의도(다음 개봉 ClearSpawned 정리)로 유지. 컴파일 0 | ✅ |
| 2026-07-27 | **PKG-TUNE 튜닝 SO 부트 배선 (✅ 코드+검수 완료)** — 미배선이던 두 SO 부트 주입을 코드로 실현: `RewardService.SetConfig`→`DataLibrary.InitializeSingleton`(GameTiming 선례 복제, `[SerializeField] BattleReward`), `CatalogRows.SetLayout`→`MainMenuInitializer.Awake`(SetSource 직후, `[SerializeField] CollectionLayoutConfig`). 보상 공식은 현행 유지(승패 무관 `Max(cards×perCard, minGold)`, 필드 확장 없음 — 사용자 결정). `BattleReward` Tooltip 오타만 수정. CardShop은 PKG-BOOT 배선분 재사용. 미배선(null) 시 기존 fallback 보존(null-safe). tcg-reviewer: critical/warn 0(경계·이중진실원·계약·결정론 무결). 에셋 생성·인스펙터 할당·Play E2E는 사용자 인계 | ✅ |
| 2026-07-24 | **구매 흐름 디버그 E2E 검증 통과 (Unity_RunCommand, 인메모리 격리)** — 6케이스: 잔액부족(InsufficientGold·차감0)/성공신규(−50+환급, 소유부여)/중복환급(−50+10×중복)/무료팩(price0 Success=Spend수정검증)/팩없음(PackNotFound·차감0)/실SO진단. 차감·환급 산술 정확, 격리로 실 세이브 미오염 확인. **미결(사용자 조치): 실제 `NormalPack.Pool` 비어있음(poolCount=0→EmptyPool), packId='0'** — 에디터에서 Pool 카드 채우고 packId 의미있는 값 권장 | ✅ |
| 2026-07-27 | **G. 신규 유저 온보딩 도메인 구조 추가 (⬜ 설계 승인 대기)** — 첫실행(소유==0) 감지 → 스타터팩(price0·6장) 개봉(F-19 뷰 재사용) → 튜토리얼 전투(TutorialConfig) 직행. 신규: `BootScene`+`BootRouter`(index0 라우팅), `FirstTimeOnboardingController`. 계약변경 2건(GrantDefaults 미지급·부트 앞단 라우팅)은 🔴 선행. G도 자체 세이브 없음(OwnedCount 판정). 신규 노드 `:::new` | ⬜ |
| 2026-07-27 | **G-27 PKG-FIRSTBATTLE 온보딩 컨트롤러 (✅ 코드, 컴파일 검증 대기)** — 신규 `Assets/Scripts/UI/Onboarding/FirstTimeOnboardingController.cs`(MonoBehaviour, 온보딩 씬 상주): `[SerializeField] PackOpeningView packView` + `TutorialScenarioData scenario` + `battleSceneName="BattleScene"`. `OnEnable`에서 `packView.OnOpenComplete += 핸들러` 구독 / `OnDisable`에서 해제(누수·중복 구독 방지). 핸들러는 `TutorialConfig.Begin(scenario)` → `SceneManager.LoadScene("BattleScene")` — **`TutorialSetupUI.StartBattle` 호출 패턴 그대로 미러링**(중복 진실원 없음, 동일 API). 중복 전이 차단 `m_transitioned` 가드로 콜백 2회 와도 LoadScene 1회. 개봉 시작은 `PackOpeningView` 자체 팩버튼(순수 뷰)에 위임 — 컨트롤러는 완료만 수신(자동시작 없음, 온보딩 흐름 자연스러움 + 진실원 단일화). 아웃게임 상태(소유·재화) 무접촉 — Grant/차감/Save는 `TryPurchase`가 이미 원자 처리. `Network/` 무수정. **온보딩 씬(.unity) 신설·배선(packView/scenario 할당·Build Settings 등록·`BootRouter.onboardingScene` 지정)은 에디터(사용자) 인계**. Unity 컴파일·개봉→전투 E2E 검증 대기 | ✅(코드) |
| 2026-07-27 | **G-24 부트 라우터 (✅ 코드, 컴파일 검증 대기)** — 신규 `Assets/Scripts/Core/BootRouter.cs`(MonoBehaviour, BootScene 상주): `Start()`에서 첫실행 판정 → 라우팅. 판정은 `OwnedCount`(Init 필요)가 아니라 신규 `OwnershipManager.HasAnyOwnedSaved()`(세이브 슬롯 직접 조회, read-only)로 — BootScene엔 `MainMenuInitializer`가 없어 소유 캐시가 미초기화일 수 있으므로 **씬 config 주입 순서와 무관**하게 안정. 슬롯 매핑(`ownership.ownedCardKeys`)은 여전히 `OwnershipManager` 단일 창구 내부에만. 목적지는 `[SerializeField] lobbyScene="LobbyScene"` / `onboardingScene=""`(후속 PKG-FIRSTBATTLE 배선; **미설정 시 LobbyScene fallback + 경고** → 미존재 씬 크래시 방지). `GameManager`·`Battle/`·`Network/` 무수정. **BootScene(.unity) 신설·빌드 index0 교체는 에디터(사용자) 인계**. Unity 컴파일·부트 E2E 검증 대기 | ✅(코드) |
| 2026-07-27 | **G-23 소유 기본지급 제거 (✅ 구현, 컴파일 검증 대기)** — `OutGame/Collection/OwnershipManager.cs`: `Init()`의 `GrantDefaults()` 호출 제거 + 메서드 **삭제**(no-op 존치 아님; 전체지급 경로는 `OwnershipDebugTool`로 일원화·중복 제거). `OwnershipSaveData.defaultsGranted`는 하위호환 위해 **존치**(미사용 주석화). 공개 API 시그니처 불변, 기존 소유 세이브 보존(읽기만·자동 Save 없음). 회귀 점검: 도감 갤러리 전부 잠김·NRE 없음, 덱빌더는 소유필터 미구현이라 무관. **Unity 컴파일·신규/기존 유저 부트 E2E는 에디터(사용자) 검증 대기** | ✅(코드) |
| 2026-07-27 | **G 진입 = BootScene 제거 + 로비 첫실행 리다이렉트 (✅ 코드, 컴파일 대기)** — 사용자 결정: 별도 `BootScene`/`BootRouter` 폐기. 앱은 기존대로 `LobbyScene`(index0) 직행하고, 로비 상주 신규 `UI/Lobby/LobbyFirstRunRedirect`가 `Start`(MainMenuInitializer.Awake[-100] 주입 후)에서 `HasAnyOwnedSaved()==false`면 `TryPurchase(starterPackId)`→`PackHandoff.Set(opened, "BattleScene", true)`→`LoadScene("PackTest")`, 실패 시 로비 유지. 상점→팩 전환과 동일 경로를 첫실행이 자동으로 탄다. Build Settings에 BootScene 불필요(PackTest/BattleScene만 등록). 잔여 엣지(획득 전 종료 시 튜토리얼 스킵)는 현행 유지 | ✅(코드) |
| 2026-07-27 | **G 카드팩 오픈 3D 뜯기 재구성 (✅ 코드+검수, 씬/컴파일 대기)** — 사용자 결정: `PackOpeningView`(팩버튼 구매) 폐기, **구매를 뷰 밖으로**(상점/`BootRouter`가 `TryPurchase` 후 static 캐리어 `PackHandoff`(Pack·NextScene·StartTutorial)로 전달), 앞단을 **3D `CardPack.prefab` 가로 드래그 봉인뜯기**로 교체. 신규 4: `PackHandoff`(OutGame/CardPack), `PackTearOpenView`(뜯기→스택→그리드, 구 꼬리 이식), `PackTearHandle`(SealStrip 월드드래그), `PackAcquireController`(캐리어 소비·획득버튼·목적지 이동). `BootRouter` 첫시작 분기가 스타터 구매→캐리어→PackTest(실패 시 로비 fallback). 목적지·튜토리얼이 캐리어에 실려 **첫시작 재판정 불필요 → `FirstStartBattleRedirect`·`PackOpenSceneController` 폐기**. 삭제 3파일 GUID 전수검색 참조 0. `RevealCardView` 재사용. tcg-reviewer 검수 진행. **씬 배선(CardPack에 PackTearHandle·MainCamera 태그)·Unity 컴파일은 사용자** | ✅(코드) |
| 2026-07-27 | **G-25/26/27 온보딩 흐름 (✅ 코드+검수, 씬/컴파일 대기) — 흐름 재설계 반영** [※ 아래 항목은 위 3D 뜯기 재구성으로 대체됨] — 사용자 결정: 공용 팩 오픈 씬 `PackTest.unity` 재사용(상점 개봉 = 첫시작 온보딩 공유), 개봉 후 **[획득] 버튼** 클릭 게이트, **일반=로비 고정 / 첫시작=튜토리얼 전투** 분기. 구현: `PackOpeningView.OnOpenComplete`(빈개봉도 발화=데드락 방어), 신규 `UI/Shop/PackOpenSceneController`(획득버튼 게이트·[획득]→`DestinationScene`(기본 로비)·`BeforeLeave` 훅, **Battle 미참조 순수 UI**), 신규 `UI/Onboarding/FirstStartBattleRedirect`(`[RequireComponent]`, 첫시작만 `HasAnyOwnedSaved()`로 판정해 목적지→배틀+`TutorialConfig.Begin`, scenario null이면 로비 fallback). 첫시작 판정은 **개봉 전 Start**에 캡처. tcg-reviewer 2라운드: 경계·타이밍·중복전이 무결, 미배선 소프트락 warn 2건 수정(경고 로그·scenario null 로비 fallback). ~~FirstTimeOnboardingController~~ 폐기(2컴포넌트로 분리). 씬 배선·Unity 컴파일은 사용자 인계 | ✅(코드) |
| 2026-07-27 | **G-28 개봉 연출 단순화 (✅ 코드+검수+씬배선+컴파일 완료, Play 검증 대기)** — 사용자 결정: 드래그 뜯기·카드별 스와이프 스택 전면 폐기. **3D 팩 클릭 1회 → 팩 숨김 → UI 패널(CanvasGroup) fade in → fade 완료 후 3열 GridLayoutGroup 배치 → [획득] → 덱 슬롯0 저장 → 목적지 씬**. 삭제 4: `PackTearOpenView`·`PackTearHandle`·`RevealCardView`·`RevealCardView.prefab`(GUID 잔존 참조 0). 신규 2: `PackRevealView`(Idle→PackShown→Revealing→Done), `PackClickHandle`(OnMouseUpAsButton·Arm/Disarm). 카드 표시는 도감 타일 `CollectionCardView` 재사용(표시 진실원 1개), 3열 좌표 계산은 GridLayoutGroup에 이임. **공유 계약 추가: `DeckSaveManager.SaveSlotToFile(index, deck)`** — 기존 `SaveToFile`은 6슬롯을 통째로 flush해서 `LoadFromFile` 미경유 씬(개봉 씬)에서 다른 덱을 지우므로, 단일 슬롯만 반영하는 비파괴 API로 교체. tcg-reviewer critical 2건(파괴적 전슬롯 flush) → 수정, warn(`m_left` 소프트록·DOTween OnComplete 생존·scenario 미배선) → 수정. 씬 배선 완료(UICanvas/RevealPanel/CardGrid/AcquireButton/EventSystem 신설, CardStackPos·SealStrip 제거, Missing 0·미배선 0). 레이아웃 수치 검증: 420×560 셀 3열×2행, 획득 버튼 비겹침. Play E2E는 사용자 | ✅ |
| 2026-07-27 | **아웃게임 첫시작 튜토리얼 P1~P4 (✅ 코드+검수+컴파일 에러 0, 씬 배선·SO 저작 대기)** — 신규 서브트리 `OutGame/Tutorial/` 6파일: 진행도 단일 창구 `OutgameTutorialProgress`(static), 스텝 해석·실행 `OutgameTutorialRunner`(static), 저작 데이터 `OutgameTutorialData`(SO, `EStepKind{AutoPurchase,WaitClick,BattleEntry}`+`List<Step>`), 타깃 앵커 3종(`EOutgameTutorialAnchor`·`TutorialAnchorRegistry`·`TutorialAnchor`). 신규 `UI/Tutorial/` 2파일: 4패널 딤 강제 게이트 `OutgameTutorialGateUI`(코드 빌드 캔버스 sortingOrder 300), 씬 수명 ↔ static 러너 연결 `OutgameTutorialBridge`(씬당 1개 — LobbyScene·CardPack). **세이브 확장**: `2.Domain/TutorialSaveData.cs` 신규(`outgameStepIndex`/`outgameCompleted`/`migrationChecked`) + `UserSaveData`에 `tutorial` 슬롯 1개 — **`VERSION`은 1 유지**(필드 추가만 = 하위호환, 구 세이브는 노드 없이 기본값). `GameManager.Boot()`에 `OutgameTutorialProgress.Init()` 1줄(Load 직후·CurrencyInit 앞). **`UI/Lobby/LobbyFirstRunRedirect.cs` 삭제** — 첫실행 스타터팩 자동 구매→캐리어→CardPack 전환이 스텝 0 `AutoPurchase`로 흡수됨(존치 시 "소유 0" 판정과 "stepIndex 0" 판정이 같은 사건에 반응해 **이중 구매**). 첫실행 판정 창구가 `OwnershipManager.HasAnyOwnedSaved()` 대리에서 `OutgameTutorialProgress`로 단일화되고, `HasAnyOwnedSaved()`는 **레거시 세이브 마이그레이션 1회 판정 전용**으로 축소(주석만 정정). `LobbyTabController.Tab`에 `tutorialAnchor` 필드 1개 + `Awake` 등록(탭 버튼이 Layer Lab 프리팹 내부 stripped Button이라 컴포넌트 직접 부착 회피). `OwnershipDebugTool`에 진행도 리셋 2종. P5·P6(Pack 탭 앵커 배선·11스텝 저작)은 **아웃게임 레이아웃 확정까지 의도적 보류** — 코드가 레이아웃을 모르게 설계돼(게이트=임의 RectTransform, 타깃=enum 앵커) 보류분은 씬 배선+SO 저작만으로 완료된다 | ✅ |
| 2026-07-27 | **H. 랭크(표시용 티어 진행도) 도메인 설계 승인 (⬜ 구현 대기 — SAVE 게이트만 코드 적용)** — `outgame-design-session` 결과. 스코프를 **표시용**(보상·난이도·매칭 무영향)으로 좁혀 서버 권위 전제를 제거. 신규 4파일(`RankSaveData`·`RankConfig`·`RankManager`·`RankHud`) + 기존 3파일 소규모 수정(`UserSaveData` 1줄·`DataLibrary` 2줄·`TurnRunner.CaptureResult` 2줄). 핵심: **티어 = `points` 순수 파생**(도달티어 별도 저장 금지) · **강등 없음 = 가감 시 하한 클램프** · **캐시 없이 슬롯 직접 읽기 → `GameManager.Boot()` 무수정**. 의도적 제거 3건(`OnRankChanged`·진행률 필드·반환값). 신규 노드 `:::new` | ✅ 설계 |
| 2026-07-27 | **F-20 도감 전체완성 1회성 보상 폐기 → 푸터 일괄수령으로 교체 (✅ 코드+배선, 컴파일·배선 검증 완료)** — 사용자 결정: 완성보상 기능 전면 제거. 삭제: `CollectionProductionManager`의 `s_completionRewardClaimed`·`IsCompletionRewardClaimed`·`CompletionRewardType/Amount`·`CanClaimCompletionReward`·`GetCompletionRewardInfo`·`ClaimCompletionReward`·`CompletionRewardInfo` struct, `CatalogRows.IsAllComplete`/`CompletionRewardType/Amount`, `CollectionLayoutConfig.completionRewardType/Amount`(+`.asset` 키 2줄), `CollectionSaveData.completionRewardClaimed`(**VERSION 1 유지** — 구 세이브의 남은 키는 JsonUtility가 무시, 진행도 무영향). 추가: **`GetTotalHarvestable() → long`**(전 행 수확가능 정수 합계) — 신규 private `HarvestableOf(CatalogRow)`를 `HarvestCore`와 공유해 **표시값 == 실제 지급 총량**(이중 진실원 없음). 뷰 컴포넌트는 **삭제**(`CollectionCompletionRewardView.cs` 제거) — 사용자 판단으로 푸터 로직을 `CollectionGalleryController`가 흡수했다(`harvestAllButton` 필드 1개 + 기존 폴링 틱·`OnChanged`에 편승, 별도 폴링 타이머·구독 중복 제거). 버튼 라벨은 프리팹 저작값 `일괄 수령` 고정(수량 표기 없음), `GetTotalHarvestable() >= 1`일 때만 `interactable`. 배선 3곳(`CollectionScreen.prefab`·`Canvas.prefab`·`CollectionTest.unity`)은 에디터 API로 이관(구 뷰 컴포넌트 제거 + 컨트롤러에 버튼 연결). 검증: 컴파일 성공, 3곳 모두 버튼 바인딩·라벨 정상·Missing Script 0 | ✅ |
| 2026-07-27 | **CardShop(SO) 폐기 — 구매처가 팩 SO·환급액 직접 소유 (✅ 코드+컴파일 완료, 씬 재배선 대기)** — 사용자 결정: 상점 목록·환급 전역값을 쥐던 `CardShop` SO(+`CardShop.cs`·`CardShop.asset`) 및 `MainMenuInitializer.SetShop` 주입 제거. `CardPackOpener`는 **무상태 파사드**가 되고(`s_shop`/`Shop`/`SetShop`/`Packs`/`GetPack` 삭제), API를 `TryPurchase(string packId)`→**`TryPurchase(CardPackData pack, long refundGold)`**로 교체 — 대상 팩 SO와 중복 환급액을 호출부가 직접 넘긴다. 구매처 2곳이 인스펙터로 소유: `PackShowcaseController`(`packData`+`duplicateRefundGold`), `LobbyFirstRunRedirect`(`starterPackId` string→`starterPack` SO+`duplicateRefundGold`). 재화·소유 계약 순수 소비(불변), 로컬 랜덤·null 풀 방어·환급 로직 무변경. Unity 컴파일 에러 0. **씬 재배선(LobbyScene의 FirstRunRedirect `starterPack`·쇼케이스 버튼 `packData` 할당)은 사용자 — 구 string 필드가 SO 참조로 바뀌어 재할당 필요** | ✅(코드) |
| 2026-07-27 | **H-33 랭크 티어 달성 보상 편입 — ✅ 코드 완료·검수 통과(씬 배선 대기)** — 표시용 진행도였던 랭크에 **보상 엔드포인트**를 붙였다. 신규 4파일(`OutGame/Rank/RankRewardManager.cs` + `UI/Rank/` 3개), 수정 3파일(`RankSaveData.claimedCount` · `RankTier.rewardGold` · `DataLibrary` 1줄). **`RankManager` 무수정**(동결 계약 무접촉) · **`UserSaveData` VERSION 1 유지**(필드 추가만). 핵심: 수령 상태 = **단조 증가 커서**(강등이 없어 수령 집합이 항상 프리픽스 → 정수 1개로 접힘, 기본값 0 = 미수령이라 센티널 불요) · 보상량은 **티어와 같은 원소**(`RankTier.rewardGold`)라 인덱스 드리프트 구조적 차단 · 상태 판정에서 **`Claimed` 최우선**(`RankManager.ResetForDebug` 후 재수령 차단) · 영속은 **`CurrencyManager.Save()` 하나**(중간 상태 디스크 기록 방지). 패널은 사용자 결정으로 **씬 직접 저작**(`PooledUIBase` 아님). tcg-reviewer 계약 위반 0. 상세는 아래 H-33 절 | ✅ |

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
        INFO["RowProductionInfo<br/>[#6 UI 스냅샷]"]
    end

    subgraph save["세이브 슬롯"]
        SD["CollectionSaveData<br/>OwnershipSaveData<br/>[#3 rowKey]"]
    end

    CUR["CurrencyManager<br/>[#2 단일 골드]"]
    OPENER["CardPackOpener (E)<br/>TryPurchase(pack, refund) · 무상태"]:::new

    subgraph ui["F. UI (도감 갤러리)"]
        GAL["CollectionGalleryController"]
        RV["CollectionRowView"]
        CV["CardVisualView"]
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
    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
```

> **부트 2계층(PKG-BOOT 확정)**: 씬무관 전역(`DataSaveManager.Load`·`CurrencyManager.Init`)은 `GameManager.Boot()`(BeforeSceneLoad)가, 씬 카드목록 의존(`SetSource`·`OwnershipManager.Init`·`ProductionManager.Init`)은 `MainMenuInitializer.Awake()`(ExecutionOrder -100)가 담당. `CardPackOpener`는 무상태 파사드가 돼 부트 주입이 없다 — 대상 팩 SO·환급액은 구매처(버튼/첫실행)가 직접 소유해 `TryPurchase`에 넘긴다. `EnsureBoot`(테스트 씬)는 `CardCatalog.IsReady` 가드로 통합 시 no-op.

**핵심 흐름 2줄**
- 소유: 부트가 `CardCatalog.SetSource → OwnershipManager.Init` → 갤러리가 `CatalogRows.Rows`를 그리고 `IsOwned`로 잠금 표시 → `Grant/Revoke` 시 `OnOwnershipChanged`로 재바인딩.
- 생산: 완성 행을 조회하면 `Resolve`가 `GameClock.Since(lastSettle)`로 경과분 누적(cap 클램프) → `Harvest`가 정수분을 `CurrencyManager.Earn`, 소수 보존 후 즉시 영속.

---

### F-17/F-18 도감 생산 UI 배선 (F. UI) — ✅ 씬/프리팹 배선 완료 (CollectionTest.unity)

> 목표: 이미 완비된 `CollectionProductionManager` API(GetInfo/Harvest/HarvestAll/GetTotalHarvestable/OnChanged)를
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
        GAL["CollectionGalleryController<br/>+ 폴링 틱 · OnChanged 구독<br/>+ 푸터 일괄수령 버튼"]:::new
        RV["CollectionRowView<br/>+ 상태칩·수확버튼 · 진행바 위임"]:::new
        CV["CardVisualView (구 CollectionCardView)"]
        PV["CollectionProgressView<br/>행별 생산 사이클 진행바"]:::new
        HUD["GoldHud (기존 코드,<br/>씬 부착만)"]:::new
    end

    GAL -->|"틱마다 RefreshProduction"| RV
    RV -->|"GetInfo(rowKey) 폴링"| PROD
    RV -->|"수확버튼 → Harvest(rowKey)"| PROD
    PROD -->|"OnChanged"| GAL
    GAL --> CV
    RV -->|"Bind(rowKey)·틱마다 Refresh"| PV
    PV -->|"GetInfo(rowKey).CycleProgress01"| PROD
    GAL -->|"GetTotalHarvestable 폴링 / HarvestAll"| PROD
    CUR -->|"OnCurrencyChanged"| HUD

    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
```

**설계 요지**
- 생산 누적은 시간 함수라 `OnChanged`가 안 뜬다 → 컨트롤러가 **0.5s 폴링 틱**으로 열린 동안 각 행 `RefreshProduction()` 갱신. 푸터 일괄수령 뷰는 소유자(컨트롤러)가 없어 **자체 0.5s 폴링**. 수확/소유변경은 `OnChanged`/`OnOwnershipChanged`로 즉시 갱신.
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

> 근거: 코드 실물 확인(2026-07-27). `CollectionRowView.cs`(상태칩·진행바·수확), `CollectionGalleryController.cs`(Update 폴링+OnChanged+푸터 일괄수령), `CollectionProgressView.cs`.

**실제 배선 결과 (2026-07-24, `Assets/Scenes/CollectionTest.unity` + `CollectionRow.prefab`)**

| 컴포넌트 | 부착 위치 | 연결된 필드 → 대상 |
|---|---|---|
| `CollectionGalleryController` | 씬/프리팹 `CollectionScreen` | `content`→`Gallery/Viewport/Content`, `rowPrefab`→CollectionRow.prefab, `fallbackAllCards`→카드 9장, `harvestAllButton`→`BottomBar`의 일괄수령 Button |
| `CollectionRowView` | `CollectionRow.prefab`(루트) | `cardsContainer`→`Cards`, `cardPrefab`→Card.prefab, `stateLabel`→`Status/StateChip/T`, `harvestButton`→`Status/Action`, `progressView`→`Status/Progress`(ProgressView) (`amountText`는 미배선=옵션) |
| `CollectionProgressView` | `CollectionRow.prefab`의 `Status/Progress` | `fillImage`→`Status/Progress/BarBg/Fill` (`progressText`는 미배선=옵션). **행별 생산 사이클 진행바** |
| `CardVisualView` (구 `CollectionCardView`, `Scripts/UI/Common/`) | `Card.prefab` | `portrait`/`nameText`/`lockOverlay` (기존) + `frame`/`hpPanel`/`hpText`/`bonusHpText`/`keywordIconRoot`/`synergyBadgeRoot`/`keywordIconPrefab`/`synergyBadgePrefab`/`keywordIconConfig` (카드 비주얼 통일) |
| `GoldHud` | 씬 `TopBar/GoldHud` | `goldText`→`GoldHud/Gold` |

**생산 진행바 = 행별 사이클 진행바 (`CollectionProgressView`)**
- **소유 집계 바가 아니다.** 완성된 각 행이 개별 생산하는 재화의 **"다음 1단위까지의 사이클 진행률"** 을 행마다 표시한다. `CollectionProgressView`가 행 키(`Bind(rowKey)`)로 `GetInfo(rowKey)`를 조회해 `Status/Progress/BarBg/Fill`(Filled 가로)을 구동: 생산중=`CycleProgress01`(소수부) / 만땅=1 / 잠김=0. RowView는 진행바를 이 뷰에 **위임**(`Bind`+매 폴링 틱 `Refresh`)하며 원시 Image를 직접 만지지 않는다(단일 책임).
- `RowProductionInfo.CycleProgress01`(파생 프로퍼티 = `AccumulatedRaw - Accumulated`, 소수부): 한 사이클(재화 1단위)이 차면 바가 0으로 리셋되고 누적 정수가 +1(실시간 채움→완료→누적↑). 사이클 길이 = productionCycleSeconds(기본 15초/단위). **저장 스키마·수확 로직 불변**(표시/파생만). `Status` 컨테이너는 프리팹 기본 비활성이라 **활성화**함.
- `CollectionGalleryController.EnsureBoot()`(독립 씬 전용, `CardCatalog.IsReady`면 no-op): `DataSaveManager.Load()` + `CurrencyManager.Init()` 추가 → 테스트 씬에서도 저장된 골드·진행도 로드. 실제 통합 부트에선 IsReady 가드로 미실행.

> **푸터 일괄수령**: 전용 뷰를 두지 않고 `CollectionGalleryController`가 겸한다 — 필요한 게 폴링 틱과 `OnChanged` 구독뿐인데 둘 다 컨트롤러에 이미 있어, 별도 컴포넌트는 같은 배선의 중복이었다. 버튼은 항상 노출하고 `GetTotalHarvestable() >= 1`일 때만 `interactable`(오브젝트 토글 없음 → 자기비활성 버그 소지 자체가 사라졌다). 라벨은 프리팹 저작값 고정(수량 표기 없음). 클릭 시 `HarvestAll()` 1회로 여러 행·여러 재화를 지급하고 영속·통지는 1회로 묶인다.

**배선 검증(에디터 인메모리 세이브, 실 저장 미오염)**: fallback 3행 전량 소유→완성, 행0 빌드→시간점프. RowView→ProgressView 위임으로 바 구동: +3분 `사이클 0.5·바 0.5` → 롤오버 시 `누적 +1·바 0` → +24h `Capped·바 1`. 수확 `earned·gold` 반영, 콘솔 에러 0.



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

---

### E. 카드팩 경제 도메인 — ✅ 구현 완료 (2026-07-24, `OutGame/CardPack/`)

> 목표 루프 3단계: `골드 → 카드팩 구매 → 신규 카드셋 획득 → 덱 강화`.
> 핵심 성질: **E는 자체 영속 상태가 없다.** 골드 차감은 `CurrencyManager`가, 카드 소유는 `OwnershipManager`가 이미 영속하므로
> E는 둘을 잇는 **오케스트레이터**일 뿐. **구매=즉시 개봉**(미개봉 팩 재고 없음), 팩 정의는 SO(에디터 데이터), 결과는 소유권에 위임. `:::new` = 이번 신규.

```mermaid
flowchart TD
    subgraph shop["상점 진입 (F-19, 후속)"]
        SHOP["상점 UI"]
    end
    subgraph def["E-14 정의 (SO, 에디터 데이터)"]
        DEF["CardPackData (SO)<br/>packId·price·drawCount·pool(지정 카드셋)"]:::new
    end
    subgraph svc["E-15 구매·드로우"]
        SVC["CardPackOpener<br/>[#1 무상태 static]<br/>TryPurchase(pack, refund) · 로컬 랜덤"]:::new
    end
    RES["OpenedPack / DrawnCard<br/>[#6 UI 스냅샷] card · isNew · refund"]:::new

    CUR["CurrencyManager<br/>Spend · Earn · Save (기존)"]
    OWN["OwnershipManager<br/>Grant (기존)"]
    KEY["CardCatalog.KeyOf<br/>안정 키 규약 (기존)"]

    SHOP -->|"TryPurchase(pack, refund)"| SVC
    SHOP -.->|"대상 팩 SO 참조"| DEF
    SVC -->|"Spend(Gold, price)"| CUR
    SVC -->|"DEF.pool 지정셋 균등 드로우"| DEF
    SVC -->|"KeyOf(card)"| KEY
    SVC -->|"Grant(key) 루프 → isNew"| OWN
    SVC -->|"중복이면 Earn(Gold, refund)"| CUR
    SVC -->|"결과 조립"| RES
    RES -->|"신규/중복·환급 연출"| SHOP

    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
```

**흐름 시퀀스 — 팩 구매·개봉 (지정 풀 + 중복 환급)**

```mermaid
sequenceDiagram
    participant U as 유저
    participant SHOP as 상점UI
    participant SVC as CardPackOpener
    participant CUR as CurrencyManager
    participant OWN as OwnershipManager

    U->>SHOP: 팩 구매 클릭
    SHOP->>SVC: TryPurchase(pack, refund)
    SVC->>CUR: CanAfford(Gold, price)?
    alt 잔액 부족
        SVC-->>SHOP: 실패(구매 불가, 차감 없음)
    else 충분
        SVC->>CUR: Spend(Gold, price)
        loop drawCount 회 (DEF.pool에서 균등 드로우)
            SVC->>OWN: Grant(KeyOf(card)) → isNew
            alt 중복(isNew=false)
                SVC->>CUR: Earn(Gold, duplicateRefundGold)
            end
        end
        SVC->>CUR: Save() (즉시 영속)
        SVC-->>SHOP: OpenedPack(카드 + isNew + refund)
        SHOP->>U: 개봉 연출(신규/중복·환급 표시)
    end
```

**설계 요지 (원리 카드)**
- **오케스트레이터라 세이브 섹션 없음**: E는 `Spend`·`Earn`(재화)·`Grant`(소유)만 호출하고 모두 이미 영속. E 자체 세이브를 만들면 이중 진실원. 트레이드오프: 구매 이력·pity 카운터가 필요해지면 그때 세이브 섹션 추가.
- **isNew는 Grant 시점에만 안다**: `Grant`는 신규면 true, 이미 소유면 false 반환. 개봉 후엔 전 카드가 `IsOwned=true`라 UI가 사후 판정 불가 → **`OpenedPack`는 생략 불가**(신규 여부·환급의 유일 진실원).
- **팩별 지정 풀**: 드로우 대상은 `CardData` 전체가 아니라 `CardPackData.pool`(에디터 큐레이션). 이는 마스터 목록 복제가 아닌 **부분집합 참조**라 4번째 목록 드리프트 아님. 키는 여전히 `CardCatalog.KeyOf`(단일 규약)로 산출.
- **중복 = 소액 골드 환급**: 장별 `Grant` 반환이 false면 `CurrencyManager.Earn(Gold, refundGold)`. 환급액은 `TryPurchase`의 인자로 구매처(버튼/첫실행)가 직접 넘긴다(상점 SO 전역값 폐기). Spend/Earn을 한 트랜잭션으로 처리 후 `Save()` 1회.
- **로컬 랜덤(비결정론 무방)**: 아웃게임 최초 랜덤. `Battle/MatchRandom` 재사용 금지(경계), 서비스 내부 `System.Random` 인스턴스.
- **상점 SO(CardShop) 폐기**: 진열 팩 목록·환급 전역값을 쥐던 `CardShop` SO와 `SetShop` 주입을 제거. `CardPackOpener`는 무상태 파사드가 되고, 대상 팩 SO·환급액은 각 구매처 뷰가 인스펙터로 소유해 `TryPurchase(pack, refund)`에 직접 넘긴다(진열=뷰 책임).
- **수정 가능성 높은 지점**: 팩 가격·드로우 수·구성 = `CardPackData` SO(코드 미수정) / 환급액 = 구매처 뷰의 `duplicateRefundGold` 필드 / 등급 가중치가 필요해지면 `DEF.pool`을 가중 목록으로 확장.

| 클래스 | 파일 | 태스크 |
|---|---|---|
| `CardPackData` (SO) | `OutGame/CardPack/CardPackData.cs` — `packId·displayName·price·drawCount·pool(List<CardData>)` | E-14 |
| `CardPackOpener` (static, 무상태) | `OutGame/CardPack/CardPackOpener.cs` — `TryPurchase(CardPackData, long refund)` | E-15 |
| `OpenedPack` · `DrawnCard` (값) | `OutGame/CardPack/OpenedPack.cs` — `card · isNew · refund` | E-16 |

---



> ~~상점 목록(ShopController/ShopPackTileView)·독립 씬·MainMenu 통합 진입~~ — **이번 스코프 전부 제외, 후속.**

---

### G. 신규 유저 온보딩 도메인 — ⬜ 설계 승인 대기 (2026-07-27)

> 목표 루프의 **진입부**. 신규 유저가 로비를 보기 전에 스타터 카드 6장을 얻고 첫 전투(튜토리얼)를 경험한다.
> **G는 자체 영속 상태가 없다** — 카드 획득·영속은 E(`CardPackOpener`→`Grant`), 첫 전투는 Battle(`TutorialConfig`)이 이미 소유. G는 **첫실행 진입 + 개봉 씬 오케스트레이션**만 신설한다(별도 BootScene 없음). `:::new` = 이번 신규.
>
> **⚠️ 2026-07-27 갱신 — 첫실행 진입부는 아래 `G-TUT` 절로 대체됐다.** `LobbyFirstRunRedirect`는 **삭제**됐고 그 역할(스타터팩 자동 구매→캐리어→CardPack 전환)은 튜토리얼 스텝 0(`AutoPurchase`)이 수행한다. 첫실행 판정도 `HasAnyOwnedSaved()==false` 대리에서 **영속 진행도(`OutgameTutorialProgress`)** 로 단일화됐다(`HasAnyOwnedSaved()`는 레거시 마이그레이션 1회 판정 전용으로 축소). 아래 구조 지도·시퀀스의 `LobbyFirstRunRedirect` 경로는 **이력 보존용**이며, 캐리어(`PackHandoff`)→`CardPack` 씬 이후 구간만 현재 실물과 같다.

#### 구조 지도 — 구매(뷰 밖) → 캐리어 → PackTest 3D 뜯기 [^pack2d]

[^pack2d]: 제목의 "3D 뜯기"·"PackTest"는 **이력 보존용 표기**다. 개봉 씬은 `CardPack.unity`로 개명됐고(G-28), 팩은 2026-07-28 **2D uGUI로 전환**돼 3D는 남아 있지 않다. 현행 개봉 화면의 진실원 = [`docs/PACK_OPEN_DIRECTION.md`](../PACK_OPEN_DIRECTION.md) + 코드.

```mermaid
flowchart TD
    subgraph boot["부트 (BootScene 없음 — 앱은 LobbyScene[index0] 직행)"]
        GM["GameManager.Boot()<br/>[BeforeSceneLoad]<br/>Load · CurrencyInit"]
        MMI["LobbyScene 진입<br/>MainMenuInitializer.Awake 주입"]
    end

    subgraph buy["구매처 (뷰 밖, TryPurchase는 여기서만)"]
        SHOP["상점 (향후) — 일반 구매<br/>버튼 → TryPurchase"]
        FIRST["~~LobbyFirstRunRedirect~~ 삭제(2026-07-27)<br/>→ 튜토리얼 스텝0 AutoPurchase가 대체<br/>(OutgameTutorialRunner, G-TUT 절)"]:::dead
    end

    HAND["PackHandoff (static 캐리어)<br/>Pack · NextScene · StartTutorial"]:::new

    subgraph scene["PackTest 씬 (공용 개봉 씬)"]
        CTRL["PackAcquireController<br/>Consume→BeginOpen · 획득버튼 · 목적지 이동"]:::new
        VIEW["PackTearOpenView<br/>PackShown→Tearing→Stacking→Grid · OnOpenComplete"]:::new
        HDL["PackTearHandle (CardPack.prefab)<br/>가로 드래그로 SealStrip 뜯기"]:::new
        CARD["RevealCardView (재사용)<br/>카드 1장 넘김 드래그"]
    end

    OPENER["CardPackOpener.TryPurchase<br/>Grant→Save (E, 원자 영속)"]
    OWN["OwnershipManager<br/>HasAnyOwnedSaved · GrantDefaults 삭제"]:::chg
    TUT["TutorialConfig.Begin (Battle)"]
    LOBBY["LobbyScene"]
    BATTLE["BattleScene<br/>튜토리얼 전투"]

    GM --> MMI
    MMI --> FIRST
    FIRST -->|"HasAnyOwnedSaved() → 로비 유지"| LOBBY
    SHOP --> OPENER
    FIRST -->|"!HasAnyOwnedSaved()"| OPENER
    OPENER --> OWN
    FIRST -->|"Set(opened, Battle, tutorial:true)"| HAND
    SHOP -->|"Set(opened, Lobby, tutorial:false)"| HAND
    FIRST -.->|"LoadScene(PackTest)"| scene
    HAND -->|"Consume"| CTRL
    CTRL -->|"BeginOpen(opened)"| VIEW
    VIEW --> HDL
    VIEW --> CARD
    CTRL -->|"[획득] · StartTutorial이면"| TUT
    CTRL -->|"[획득] → NextScene"| LOBBY
    CTRL -->|"[획득] → NextScene"| BATTLE
    BATTLE -->|"종료 GameResultPopup"| LOBBY

    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
    classDef chg fill:#7a5b16,stroke:#f2c14e,color:#fff;
    classDef dead fill:#5a1f1f,stroke:#e57373,color:#fff;
```

#### 흐름 시퀀스 — 첫 부팅 (로비 진입 → 즉시 리다이렉트 → 3D 뜯기 → [획득] → 튜토리얼 전투) [^pack2d]

```mermaid
sequenceDiagram
    participant FR as LobbyFirstRunRedirect
    participant OP as CardPackOpener
    participant H as PackHandoff
    participant C as PackAcquireController
    participant V as PackTearOpenView
    participant HD as PackTearHandle
    participant TUT as TutorialConfig

    Note over FR: 앱은 LobbyScene(index0) 직행 · MainMenuInitializer.Awake 주입 후 FR.Start
    FR->>FR: HasAnyOwnedSaved()? (세이브 직접 조회)
    alt 소유 있음 (기존 유저)
        FR->>FR: 아무것도 안 함 → 로비 그대로
    else 소유 없음 (첫시작)
        FR->>OP: TryPurchase(starterPack SO, refund)
        OP-->>FR: OpenedPack(Success, 소유·차감 영속)
        FR->>H: Set(opened, "BattleScene", startTutorial:true)
        FR->>FR: LoadScene("PackTest")
        C->>H: HasPending? → Consume()
        C->>V: BeginOpen(opened) [팩 모델 등장]
        V->>HD: ArmTear() + SetTearCallback
        Note over HD: 유저 가로 드래그 > tearThreshold → SealStrip 슬라이드
        HD-->>V: onTorn() [Tearing→Stacking]
        Note over V,C: 스택 → 카드 넘김 → 2×3 그리드
        V-->>C: OnOpenComplete → [획득] 버튼 노출
        Note over C: 유저 [획득] 클릭
        C->>TUT: (StartTutorial) Begin(scenario)
        C->>C: LoadScene(NextScene="BattleScene")
        Note over C: 일반 구매였다면 캐리어 NextScene=LobbyScene, StartTutorial=false<br/>전투 종료 후 GameResultPopup가 LobbyScene 복귀(소유>0 → 이후 로비 직행)
    end
```

#### 원리 카드 — 왜 이렇게 생겼나

- **구매를 뷰 밖으로, 목적지를 구매한 쪽으로**: `TryPurchase`(소유·차감 원자 영속)와 "획득 후 어디로/튜토리얼 여부"를 상점·`LobbyFirstRunRedirect`가 쥐고 `PackHandoff`에 실어 넘긴다. PackTest는 **첫시작 재판정 없이 캐리어 값으로만 분기** → 별도 리다이렉트 레이어(구 FirstStartBattleRedirect)가 소멸(브레인 1개 = `PackAcquireController`).
- **BootScene 없음(사용자 결정)**: 앱은 기존대로 `LobbyScene`(index 0)으로 직행하고, 로비 상주 컴포넌트가 첫실행만 갈라 즉시 개봉 씬으로 전환한다 — 상점→팩 전환과 동일 경로를 첫실행이 자동으로 탄다. `TryPurchase`는 무상태라 부트 주입 불요 — 대상 팩 SO를 호출부가 직접 소유. **(2026-07-27 갱신)** 그 로비 상주 컴포넌트는 `LobbyFirstRunRedirect`에서 `OutgameTutorialBridge`(스텝 0 `AutoPurchase`)로 대체됐고, 판정 순서도 `MainMenuInitializer.Awake`[-100] 이후 브리지 `Start`가 아니라 **`GameManager.Boot()`가 로드한 진행도**를 읽는 방식이 됐다 → 소유 캐시 준비 여부와 무관.
- **검증된 꼬리 재사용, 리스크는 앞단에 국한**: 스택→넘김→그리드·`OnOpenComplete`는 구 `PackOpeningView`의 상태머신 가드·`OnDisable` 트윈 정리까지 무변경 이식. 새로 만든 건 앞단(3D 팩 등장 + 가로 드래그 뜯기 `PackTearHandle`)뿐.
- ~~**입력은 월드 방식 통일**: 팩(3D BoxCollider)·카드(2D BoxCollider2D)가 같은 `Camera.main` + `OnMouse*` 패턴. 트레이드오프: 씬 카메라 `MainCamera` 태그가 인터랙션 전체를 좌우~~ → **2026-07-28 정정: 이 서술은 그 시점의 `PackTearOpenView`/구 `PackTearHandle` 기준이며 현재 코드에는 해당 사항이 없다.** 지금의 `PackTearHandle`은 `Update()`에서 `Input.mousePosition`을 폴링하고 콜라이더·`Camera`를 참조하지 않는다 → **`MainCamera` 태그 의존 없음.** 실제 함정은 `EventSystem.IsPointerOverGameObject()` 가드로, **팩·배경 Image의 `raycastTarget`이 켜져 있으면 개봉이 막힌다**(갱신 이력 2026-07-28 · [`PACK_OPEN_DIRECTION.md`](../PACK_OPEN_DIRECTION.md) §1).
- **[획득] 버튼 게이트**: 개봉(그리드 배열) 완료 = `PackTearOpenView.OnOpenComplete` → 컨트롤러가 획득 버튼 노출. 유저가 능동적으로 [획득]을 눌러야 전이.
- **소유==0 신호의 함의**: `GrantDefaults` 전체지급을 **끈 것이 전제**(G-23). 판정은 세이브 직접 조회 `HasAnyOwnedSaved()`(G-24)로 씬 config 순서 무관. **(2026-07-27 갱신)** 첫실행 판정 자체는 진행도(`OutgameTutorialProgress`)로 옮겨졌고 `HasAnyOwnedSaved()`는 레거시 마이그레이션 1회 판정에만 쓰인다 — "소유 0"을 첫실행의 **대리 신호로 쓰던 구조가 소멸**(구매 직후 종료 시 온보딩 영구 스킵 구멍의 원인).

**수정 가능성 높은 지점**: 뜯기 판정·연출 `PackTearHandle.cs`(`tearThreshold`/슬라이드) / 첫시작 라우팅 값 → **`OutgameTutorial.asset` 스텝 0**(`pack`/`duplicateRefundGold`/`nextScene`, 코드 미수정) / 캐리어 계약 `PackHandoff.cs`(NextScene·StartTutorial) / 튜토리얼 시나리오 → **스텝 SO의 `BattleEntry.scenario`**(`PackAcquireController.scenario`는 캐리어 `StartTutorial`이 항상 false라 미사용) / 튜토리얼 보상 여부 `TurnRunner.CaptureResult`(선택 가드).

#### 파일 지도 — 다이어그램에서 코드로

| 클래스/에셋 | 파일 | 태스크 |
|---|---|---|
| ~~`LobbyFirstRunRedirect`~~ (로비 상주·첫시작 구매→캐리어→PackTest) | **폐기**(2026-07-27, 파일 삭제) — 스텝 0 `AutoPurchase`(`OutgameTutorialRunner`)가 흡수. 존치 시 "소유 0"·"stepIndex 0" 이중 판정으로 이중 구매 | G-24 → G-TUT |
| ~~BootScene / BootRouter~~ | **폐기** — BootScene 없이 LobbyScene(index0) 직행 + 로비 리다이렉트 | — |
| `PackHandoff` (static 캐리어) | 신규 `Assets/Scripts/OutGame/CardPack/PackHandoff.cs` | G-27 ✅ |
| ~~`PackTearOpenView`~~ → `PackRevealView` | `Assets/Scripts/UI/Shop/PackRevealView.cs` | G-28 ✅ (아래 절) |
| ~~`PackClickHandle`~~ → `PackTearHandle` | `Assets/Scripts/UI/Shop/PackTearHandle.cs` — **(2026-07-27) G-29에서 뜯기 제스처 복귀**, `PackClickHandle.cs` 삭제 | G-28 → G-29 ✅ |
| `PackCardStack` / `PackCardView` (더미 스와이프 · 개봉 카드 표시) | 신규 `Assets/Scripts/UI/Shop/PackCardStack.cs` · `PackCardView.cs` | G-29 ✅ |
| `PackAcquireController` (캐리어 소비·획득버튼·덱 저장·목적지 이동) | 신규 `Assets/Scripts/UI/Shop/PackAcquireController.cs` | G-27 ✅ / G-28 덱저장 |
| ~~`RevealCardView`~~ | ~~**폐기**(G-28) — 결과 표시는 `CollectionCardView` 재사용~~ → **(2026-07-27) 개봉 전용 `PackCardView`로 재분리**(G-29). 도감 타일은 잠금 표현이 필요하고 개봉 카드는 신규/중복 강조가 필요해 요구가 갈렸다 | G-28 → G-29 |
| `DeckSaveManager.SaveSlotToFile` (공유 계약 **추가**) | `Assets/Scripts/Battle/DeckSaveManager.cs` — 단일 슬롯만 비파괴 저장(전슬롯 flush 금지) | G-28 ✅ |
| `OwnershipManager` (GrantDefaults 삭제 + HasAnyOwnedSaved) | `Assets/Scripts/OutGame/Collection/OwnershipManager.cs` | G-23/24 ✅ |
| ~~3D 팩 모델 = `Assets/Assets/Prefabs/CardPack.prefab` (BoxCollider)~~ | **폐기**(2026-07-28 2D 전환) — 팩은 씬 UI 노드(`UICanvas > PackRoot > Pack`(Image + `PackTearHandle`) `> Seal`)로 대체. 프리팹·메시 2종·머티리얼 3종은 색 A/B 대조용으로 Play 검증 전까지만 보존(참조는 `CardPack.unity` 하나뿐, guid 전수 검증 완료). `BoxCollider`는 원래부터 참조 0인 vestigial이었다 | G-28 → G-29 → **폐기** |
| 개봉 전용 카드 = `Assets/Assets/Prefabs/UI/PackUI/PackCard.prefab` | 860×1204 · `Glow`/`NewBadge`/`RefundBadge` | G-29 ✅ |
| 공용 팩 오픈 씬 = `Assets/Scenes/CardPack.unity` | `PackOpenDirector`(`PackRevealView`+`PackAcquireController`+브리지) · `UICanvas`(Overlay) > **`BG`**(Image, `shakeTarget`) · **`PackRoot`**(빈 컨테이너) > **`Pack`**(Image + `PackTearHandle`) > **`Seal`**(Image) · `RevealPanel`(CanvasGroup) > `SkipButton`·`AcquireButton`·`SummaryGroup`·`ResultGrid` · `StackInput`(+`PackCardStack`) > `CardLayer` > `StackAnchor` · `RemainingText` · `TearHint`. **(2026-07-28 2D 전환)** `CardPack` 프리팹 인스턴스·`Directional Light`·BG SpriteRenderer 제거, `Main Camera`는 AudioListener 숙주 겸 백버퍼 클리어용으로만 유지(ClearFlags=Solid Color, Culling Mask=Nothing) — **`MainCamera` 태그 의존 없음**. `DiscardArea`는 실재한 적 없다(문서 오기, 2026-07-28 정정) | G-28 → G-29 → **2D 전환 🔵** |
| `StarterPack.asset` (CardPackData) | 신규 에셋 (사용자) — price0·drawCount6·pool=기본6장 | G-25 |
| ~~`PackOpeningView`·`PackOpenSceneController`·`FirstStartBattleRedirect`~~ | **폐기**(구매 분리·캐리어 도입으로 대체) | — |
| (선택) 튜토리얼 보상 가드 | `Assets/Scripts/Battle/TurnRunner.cs` 또는 `Reward/RewardService.cs` | G-보상분기 |

---

### G-28 — 개봉 연출 단순화 (클릭 1회 → 패널 fade → 3열 그리드)

> **(2026-07-27) 이 단순화는 되돌려졌다 → 아래 G-29.** 아래 내용은 그 시점의 구조 기록으로 남긴다.

#### 구조 위치 (변경분만)

```mermaid
flowchart TD
    HAND["PackHandoff (static 캐리어)<br/>Pack · NextScene · StartTutorial"]

    subgraph scene["CardPack 씬 (공용 개봉 씬)"]
        CTRL["PackAcquireController<br/>Consume→BeginOpen · 카드 캐시 · [획득]"]:::chg
        VIEW["PackRevealView<br/>Idle→PackShown→Revealing→Done · OnRevealComplete"]:::new
        HDL["PackClickHandle (CardPack.prefab)<br/>클릭 1회(OnMouseUpAsButton)"]:::new
        PANEL["revealPanel (CanvasGroup)<br/>DOFade → cardGrid(GridLayoutGroup 3열)"]:::new
        TILE["CollectionCardView (재사용)<br/>Bind(card, owned:true)"]
        DEAD["~~PackTearHandle · RevealCardView<br/>PackTearOpenView~~ 삭제"]:::dead
    end

    DECK["DeckSaveManager.Save(0,·)+SaveToFile<br/>DeckConfig.Set (Battle, 읽기전용 사용)"]
    LOBBY["LobbyScene"]
    BATTLE["BattleScene"]

    HAND -->|"Consume"| CTRL
    CTRL -->|"BeginOpen(opened)"| VIEW
    VIEW -->|"Arm(onClicked)"| HDL
    VIEW -->|"fade 완료 후 Instantiate"| PANEL
    PANEL --> TILE
    VIEW -.->|"OnRevealComplete"| CTRL
    CTRL -->|"[획득] 1) 덱 슬롯0 저장"| DECK
    CTRL -->|"[획득] 2) NextScene"| LOBBY
    CTRL -->|"[획득] 2) NextScene"| BATTLE

    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
    classDef chg fill:#7a5b16,stroke:#f2c14e,color:#fff;
    classDef dead fill:#5a1f1f,stroke:#e57373,color:#fff;
```

#### 흐름 시퀀스 — 개봉 1회

```mermaid
sequenceDiagram
    participant U as 유저
    participant C as PackAcquireController
    participant V as PackRevealView
    participant H as PackClickHandle
    participant P as revealPanel (CanvasGroup)
    participant D as DeckSaveManager

    C->>C: Consume() → 카드 목록 캐시(m_cards)
    C->>V: BeginOpen(opened)
    V->>V: packRoot 활성 · panel alpha0/입력off · 이전 타일 정리
    V->>H: Arm(OnPackClicked)
    U->>H: 팩 클릭 (OnMouseUpAsButton, 1회 가드)
    H-->>V: OnPackClicked
    V->>V: packRoot SetActive(false)
    V->>P: DOFade(1, panelFadeDuration)
    P-->>V: OnComplete → blocksRaycasts/interactable = true
    V->>V: Cards 순서대로 CollectionCardView.Bind(card, true) (3열은 GridLayoutGroup)
    V-->>C: OnRevealComplete → [획득] 버튼 노출
    U->>C: [획득] 클릭
    C->>D: SaveSlotToFile(0, m_cards) + DeckConfig.Set
    C->>C: (StartTutorial) TutorialConfig.Begin → LoadScene(NextScene)
```

#### 원리 카드

- **연출을 인터랙션 1개로 축약**: 드래그 뜯기·카드별 스와이프 스택은 상태·입력 축이 많아 배선 실패가 곧 소프트락이었다. 클릭 1회 + CanvasGroup fade + `GridLayoutGroup`으로 줄여, 좌표 계산 코드를 레이아웃 컴포넌트에 넘겼다.
- **표시 뷰 단일화**: 결과 카드는 도감 타일 `CollectionCardView`를 그대로 재사용(월드 스프라이트 뷰 폐기) → 카드 표시 진실원이 하나(`displayName`/`fullImage`).
- **데드락 방지 우선**: `packHandle`/`revealPanel`/`cardPrefab`/`cardGrid` 미배선·카드 0장 모두 경고 후 `OnRevealComplete`를 발화 — 획득 버튼이 영구히 숨는 경로를 남기지 않는다.

**수정 가능성 높은 지점**: fade 시간·전이 가드 `PackRevealView.cs`(`panelFadeDuration`, `OnPackClicked`) / 덱 저장 슬롯·정책 `PackAcquireController.SaveOpenedDeck`(슬롯 0 고정).

---

### G-29 — 개봉 연출 재확장 (스와이프 뜯기 → 카드 더미 한 장씩 넘기기)

**의도·단계별 상세·열린 항목의 진실원 = [`docs/PACK_OPEN_DIRECTION.md`](../PACK_OPEN_DIRECTION.md).** 여기엔 구조 노드만 남긴다.

#### 구조 위치 (변경분만)

```mermaid
flowchart TD
    subgraph scene["CardPack 씬"]
        VIEW["PackRevealView<br/>Entering→Tearing→Bursting→Flicking→Summary<br/>스킵 총괄 · OnRevealComplete"]:::chg
        TEAR["PackTearHandle (UICanvas > PackRoot > Pack)<br/>스와이프 진행도 · OnProgress/OnTorn · sealRoot 훅"]:::new
        STACK["PackCardStack (StackInput)<br/>더미 겹침 · 스와이프 넘기기 · 라인업"]:::new
        CARD["PackCardView (PackCard.prefab)<br/>Bind(DrawnCard) · 신규 광채/NEW · 중복 환급"]:::new
    end

    VIEW -->|Arm / OnTorn| TEAR
    VIEW -->|Build · BeginInteraction| STACK
    STACK -->|OnCardRevealed| VIEW
    VIEW -->|PlayRevealAccent| CARD
    STACK -->|OnSkipRequested| VIEW

    classDef new fill:#eaffea,stroke:#3a3
    classDef chg fill:#fff6e0,stroke:#c90
```

#### 원리 카드

- **G-28의 폐기 사유를 구조로 막았다**: 폐기 이유는 연출 품질이 아니라 "배선 실패 = 소프트락"이었다. 이번엔 `tearHandle`·`cardStack`·`packRoot`·`revealPanel` **어느 것이 비어도 경고 후 다음 단계로 진행**해 요약에 도달하고 `OnRevealComplete`가 발화한다. 카드 0장·`Build` 실패도 같다. 다만 **배선 지점 자체는 늘었다**(과거 4 → `cardLayer`/`stackAnchor`/`sealRoot`/`shakeTarget`/`skipButton` 추가).
- **`IsNew`/`Refund`가 처음으로 화면에 나온다**: G-28은 `Bind(card, true)`로 둘 다 버렸다. 희귀도를 도입하지 않기로 한 이상 이 둘이 연출의 유일한 감정 축이다.
- **좌표계 전제**: 카드와 `stackAnchor`가 모두 `cardLayer`의 자식이고 앵커가 같아야 한다(`anchoredPosition`을 직접 보간). `CardLayer`가 `StackInput`의 **자식**인 것도 필수 — 카드가 raycast를 먹어도 드래그가 부모로 버블링돼야 스택이 입력을 받는다.
- **장수 확장 대응**: 팩은 전부 `drawCount=6`. 밀린 카드는 라인업으로 남지 않고 날아가며 파괴되고, 결과는 `PackResultGrid`가 **새 인스턴스를 3열로** 세운다(마지막 행이 덜 차면 그 행만 가운데 정렬) → 장수가 늘어도 행이 늘 뿐이다. ※ ~~`discardArea`/`discardMaxWidth`로 라인업 간격을 자동 축소~~ 는 실재한 적 없는 서술이다(2026-07-28 정정).
- **스킵 입력이 두 경로인 이유**: 넘기기 단계는 `StackInput` 탭, 그 외는 `SkipButton`. **`RevealPanel`의 `CanvasGroup`이 단계를 갈라놓기 때문**이다 — 분출 전까지 `alpha 0` + `blocksRaycasts false`라 그 아래의 `StackInput`은 뜯기 단계에 입력을 받을 수 없다. ~~3D `OnMouse*`와 uGUI 전체화면 캐처가 충돌하기 때문~~ 은 **근거가 소멸한 서술**이다(2026-07-28 정정 — `PackTearHandle`은 `OnMouse*`를 쓰지 않고 2D 전환으로 3D 자체가 사라졌다).
  - ⚠️ 알려진 문제: `SkipButton`이 씬에서 `RevealPanel`의 **자식**이라 같은 `alpha 0`/`blocksRaycasts false`를 뒤집어쓴다 → **Entering·Tearing 동안 스킵 버튼이 보이지도 눌리지도 않는다**(`PackRevealView.cs:52` 주석과 실배선 불일치). 소프트락은 아니며, 2D 씬 재구성 시 캔버스 직속으로 올리는 것이 권장안. 상세 = `PACK_OPEN_DIRECTION.md` §3 Stage 5.

**수정 가능성 높은 지점**: 뜯기 감도 `PackTearHandle`(`tearDistance`/`commitThreshold`/`flickSpeed`) / 넘기기 감도 `PackCardStack`(`flickThreshold`/`flickSpeed`/`dismissDistance`) / 결과 격자 `PackResultGrid`(`cellWidth`/`cellHeight`/`spacing*`/`cardStagger`) / 분출 타이밍 `PackRevealView`(`burstHold`/`panelFadeDuration`). **값 단위는 한 가지가 아니다** — `PACK_OPEN_DIRECTION.md` §7 표를 먼저 볼 것.

**미완**: SFX 전무(`SoundConfig` 슬롯 추가 필요) · `burstEffect` 파티클 미배선 · 봉인 표현은 `Seal` 통째로 밀기(찢김 아님) · **Play 검증 대기**.

---

### G-TUT — 아웃게임 첫시작 튜토리얼 (P1~P4) — ✅ 코드+검수+컴파일 에러 0 (씬 배선·SO 저작 대기)

> 첫실행 온보딩을 "한 번 리다이렉트하고 끝"에서 **영속 진행도 + 스텝 해석 + 강제 안내 게이트** 3축으로 재편한 것.
> `LobbyFirstRunRedirect`를 흡수·삭제하고, 로비↔CardPack↔Battle 왕복과 앱 강제종료를 견디는 진행도를 세이브에 둔다.
> **인게임(전투) 튜토리얼 내부 로직은 범위 밖** — 기존 `TutorialConfig`/`TutorialOverlayUI`를 그대로 쓰고, 아웃게임은 **어느 회차에 어느 시나리오를 넘길지만** 책임진다. `:::new` = 이번 신규.

#### 구조 지도 — 영속(진행도) / 해석(러너) / 수명(브리지) / 표시(게이트) 4층

```mermaid
flowchart TD
    subgraph boot["부트 (BeforeSceneLoad)"]
        GM["GameManager.Boot()<br/>Load → TutorialProgress.Init → CurrencyInit"]:::chg
    end

    subgraph persist["영속 — 세이브만 안다"]
        PRG["OutgameTutorialProgress (static)<br/>IsCompleted · StepIndex · Init<br/>CommitStep · Complete · ResetForDebug"]:::new
        SD["TutorialSaveData (슬롯)<br/>outgameStepIndex · outgameCompleted · migrationChecked"]:::new
    end

    subgraph run["해석 — 스텝 실행 (씬 오브젝트를 모름)"]
        RUN["OutgameTutorialRunner (static)<br/>IsRunning · EnsureData · TryGetCurrentStep<br/>EnterCurrentStep · NotifyStepSatisfied"]:::new
        DATA["OutgameTutorialData (SO)<br/>EStepKind{AutoPurchase,WaitClick,BattleEntry,<br/>WaitPackOpen,WaitPurchase,AutoBattle}<br/>List&lt;Step&gt; (인덱스 = 세이브 진행도)"]:::new
    end

    subgraph scene["씬 레이어 (UI/Tutorial/)"]
        BRG["OutgameTutorialBridge<br/>씬당 1개 (LobbyScene · CardPack)<br/>Awake:EnsureData · Start:EnterCurrentStep"]:::new
        GATE["OutgameTutorialGateUI<br/>4패널 딤 + 구멍 · 배너 · sortingOrder 300<br/>onClick 구독으로 완료 감지"]:::new
    end

    subgraph anchor["타깃 식별 (씬 경로 문자열 금지)"]
        KEY["EOutgameTutorialAnchor (enum, int 직렬화)<br/>LobbyPlayButton · LobbyPackTab<br/>PackBuyButton · PackAcquireButton"]:::new
        REG["TutorialAnchorRegistry (static)<br/>Register/Unregister/TryGet · OnRegistered"]:::new
        ANC["TutorialAnchor (MonoBehaviour)<br/>OnEnable/OnDisable = 등록/해제"]:::new
        TAB["LobbyTabController.Tab.tutorialAnchor<br/>탭 버튼(프리팹 내부 stripped Button) 대리 등록"]:::chg
    end

    OPENER["CardPackOpener.TryPurchase (E)<br/>Grant→Save 원자 영속"]
    HAND["PackHandoff (static 캐리어)"]
    TUT["TutorialConfig.Begin (Battle, 읽기 사용)"]
    BATTLE["BattleScene<br/>브리지 없음 (복귀 시 로비 브리지가 재개)"]
    OWN["OwnershipManager.HasAnyOwnedSaved<br/>레거시 마이그레이션 판정 전용으로 축소"]:::chg
    DEAD["~~LobbyFirstRunRedirect~~ 삭제<br/>= 스텝 0 AutoPurchase"]:::dead

    GM --> PRG
    PRG <--> SD
    PRG -.->|"Init 1회 판정"| OWN
    DEAD -.-> RUN
    BRG -->|"EnsureData(멱등)"| RUN
    RUN --- DATA
    RUN -->|"StepIndex 읽기 · CommitStep"| PRG
    RUN -->|"AutoPurchase"| OPENER
    RUN -->|"Set(opened, nextScene, false)"| HAND
    RUN -->|"BattleEntry · AutoBattle 진입 시"| TUT
    RUN -->|"AutoBattle → LoadScene(BattleScene)"| BATTLE
    BRG -->|"ShowGate(rect, button, msg)"| GATE
    GATE -->|"onClick → onSatisfied"| BRG
    BRG -->|"NotifyStepSatisfied"| RUN
    ANC -->|"Register/Unregister"| REG
    TAB -->|"Awake 등록"| REG
    KEY --- REG
    BRG -->|"TryGet(anchor) · OnRegistered 대기"| REG

    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
    classDef chg fill:#7a5b16,stroke:#f2c14e,color:#fff;
    classDef dead fill:#5a1f1f,stroke:#e57373,color:#fff;
```

#### 흐름 시퀀스 — 스텝 0~2 (부트 → 첫 전투 직행 → 로비 복귀 → 상점 [구매])

```mermaid
sequenceDiagram
    participant GM as GameManager.Boot
    participant PRG as TutorialProgress
    participant BRG as Bridge(씬당 1개)
    participant RUN as Runner
    participant TUT as TutorialConfig
    participant REG as AnchorRegistry
    participant GATE as GateUI

    GM->>PRG: Init() [Load 직후 · 레거시 판정 1회]
    Note over BRG: LobbyScene 진입 · Awake: EnsureData(SO)
    BRG->>RUN: Start → EnterCurrentStep()
    RUN->>PRG: StepIndex=0 → 스텝0(AutoBattle)
    RUN->>PRG: CommitStep(1) + Save  ← 실행보다 커밋이 먼저
    RUN->>TUT: Begin(scenario) [양 덱 고정 주입 = 저장 덱 불필요]
    RUN->>RUN: LoadScene("BattleScene")
    Note over BRG: 로비는 한 프레임만 스쳐간다(딤·배너 없음)
    Note over TUT: 전투 종료 → GameResultPopup가 LobbyScene 복귀<br/>BattleCleanup이 TutorialConfig.End()

    Note over BRG: 로비 브리지 Start → StepIndex=1(WaitClick)
    BRG->>REG: TryGet(LobbyPackTab) → 미등록이면 OnRegistered 대기
    REG-->>BRG: 등록됨(LobbyTabController.Awake 대리 등록)
    BRG->>GATE: ShowGate(rect, button, "상점으로 이동하세요")
    Note over GATE: 4패널 딤이 타깃만 비움 · 원래 onClick은 그대로 실행
    GATE-->>BRG: onSatisfied (1회 가드)
    BRG->>RUN: NotifyStepSatisfied() → CommitStep(2)
    BRG->>RUN: EnterCurrentStep() → WaitPurchase(구매 성공이 완료)
    Note over BRG: 이후 개봉 → [획득] → BattleEntry(PlayBtn) 사이클 2회
```

#### 원리 카드 — 왜 이렇게 생겼나

- **완료는 파생이 아니라 스칼라**: `outgameCompleted`가 `outgameStepIndex`보다 **항상 우선**한다. 완료를 `stepIndex >= steps.Count`로 파생시키면 나중에 스텝을 추가한 순간 **이미 끝낸 유저의 튜토리얼이 되살아난다**. 러너는 `CommitAdvance`에서 한 번만 `Complete()`를 확정한다. — `TutorialSaveData.cs` / `OutgameTutorialRunner.cs`
- **커밋이 실행보다 앞선다**: 자동 스텝(`AutoPurchase`·`AutoBattle`)은 `CommitStep(idx+1)`+`Save` **후에** 실행한다. `AutoPurchase`가 반대 순서면 "구매 직후 앱 종료 → 소유는 생겼는데 진행도는 0"이 되어 온보딩이 영구 스킵되고, `AutoBattle`이 반대 순서면 전투 중 강제종료가 그 스텝을 **영원히 되풀이**한다(씬을 떠난 뒤엔 커밋할 지점이 없다). 롤백 분기는 `AutoPurchase`에만 있다 — 구매는 실패할 수 있고(차감 없이 반환되므로 `CommitStep(idx)`로 원상복구) 씬 로드는 실패하지 않는다. — `OutgameTutorialRunner.EnterAutoPurchase` / `EnterAutoBattle`
- **첫 전투는 저장 덱을 요구하지 않는다(2026-07-28)**: 온보딩 앞머리의 팩 구매·개봉을 걷어내 신규 유저는 첫 전투 시점에 소유 카드도 덱도 0이다. 그래도 성립하는 이유는 `GameInitializer.InitializeSinglePlayerFields`가 `TutorialConfig.IsActive`일 때 **양 덱을 시나리오에서 고정 주입**하기 때문 — `DeckConfig`를 아예 읽지 않는다. 그래서 `LobbyMatchLauncher`의 "유효한 덱이 없습니다" 가드는 **손대지 않았다**: `AutoBattle`은 `PlayBtn`을 거치지 않으므로 그 가드를 통과할 일이 없고, 그 가드를 만나는 2·3회차 `BattleEntry`는 이미 첫 팩을 획득한 뒤다.
- **레거시 판정은 계정당 1회(`migrationChecked` 낙인)**: "소유 있음 + 진행도 0 = 구버전 유저" 판정을 매 부트 반복하면, 러너가 아직 한 번도 돌지 못한 상태(SO 미배선·구매 실패 롤백 — **P5·P6 보류 중인 지금의 실제 상태**)에서 수동 구매한 신규 유저까지 완료 처리된다. `stepIndex == 0` 항은 함께 남긴다 — 없으면 **현재 빌드로 진행 중인 세이브**가 업데이트 첫 부팅에 완료 낙인을 맞는다. `ResetForDebug()`는 낙인을 **true로 유지**(되돌리면 남은 소유 탓에 다음 부트가 곧장 완료 처리해 리셋이 무의미). — `OutgameTutorialProgress.Init`
- **타깃은 씬 경로가 아니라 enum 앵커**: 탭 버튼이 Layer Lab 프리팹 내부라 경로 문자열은 취약하고 오타가 검출되지 않는다. `TutorialAnchor`의 `OnEnable/OnDisable`이 그대로 등록/해제가 되므로 **"타깃이 나타나는 시점"을 감지하는 코드가 0줄** — `AcquireButton`의 `SetActive(true)`가 곧 개봉 완료 시점이고, 탭 콘텐츠 토글이 곧 등록 시점이다. enum은 SO에 int로 직렬화되므로 **새 값은 끝에만 추가**(재배치·삭제 금지).
- **구멍은 4패널(셰이더 아님)**: 타깃 rect 기준 상/하/좌/우 `Image` 4장으로 덮어 타깃만 비운다. 구멍이 물리적 공백이라 클릭 통과가 `ICanvasRaycastFilter` 0줄로 성립한다. 완료는 `onClick` **구독**으로만 감지 → 원래 리스너(`StartAiBattle`·`Select`·`OnBuyPressed`·`OnAcquirePressed`)가 그대로 실행되고, 게이트는 `HideGate`/`Clear`/`OnDestroy`에서 반드시 `RemoveListener`.
- **소프트락 방지 불변식**: 딤이 걸린 채 누를 수 있는 것이 0인 상태를 만들지 않는다. 타깃이 `activeInHierarchy == false`거나 `IsInteractable() == false`면 **딤만 숨기고 게이트는 armed 유지** → 다시 누를 수 있게 되면 자동 복귀(`LateUpdate` 매 프레임 판정이라 게이트가 걸린 *뒤* 잠기는 경로도 커버). 타깃에 `Button`이 없으면 아예 게이트를 걸지 않는다.
- **씬 이름을 보지 않는 재개**: 브리지는 씬 로드마다 `StepIndex`를 읽어 그대로 재개한다. 앵커가 이 씬에 없으면 조용히 대기하고 나중에 등장하면 켜진다. `AutoPurchase`는 커밋이 실행보다 앞서므로 개봉 씬 브리지가 재실행할 수 없다. 클릭 직후 다음 스텝이 `AutoPurchase`면 진입을 넘긴다 — 방금 누른 버튼의 `LoadScene`과 자동 전환이 경합해 목적지가 뒤집히기 때문.
- **SO 주입은 브리지가(부트가 아니라)**: `CardPack.unity`에는 `MainMenuInitializer`가 없다. static은 씬 전환에 살아남지만 **CardPack 단독 Play**(`PackStandaloneBoot` 워크플로)에서 깨진다 → 브리지가 `Awake`에서 멱등 주입(다른 에셋 주입 시도는 경고 후 기존 유지).
- **전투 시작은 클릭 리스너가 아니라 스텝 진입 시**: `TutorialConfig.Begin`을 `BattleEntry` 진입 순간에 호출한다. 클릭 리스너로 하면 `PlayBtn`의 씬 PersistentCall이 런타임 리스너보다 먼저 `LoadScene`을 돌려 순서 의존이 생긴다. `Begin`은 덱/스크립트 큐 세팅뿐이고 `TutorialConfig`는 휘발이라 재진입도 멱등. → **`LobbyMatchLauncher`·`PackShowcaseController`·`PackAcquireController` 무수정.**

- **개봉 씬은 가이드를 끄고 씬 안내로 대체(2026-07-27)**: `CardPack`은 화면에 3D 팩 하나, 요약엔 [획득] 하나뿐이라 강제 게이트가 길잡이가 아니라 **연출을 가리는 잡음**이다. 브리지에 `suppressGuideUI`(씬 스위치)를 두어 그 씬에서만 딤·배너를 끈다 — **스텝 진행은 불변**. 단 완료 감지 경로가 갈린다: `WaitPackOpen`은 원래부터 `OnAnyPackOpened`가 완료를 확정하므로 배너만 빼면 끝이지만, `WaitClick`의 완료는 **게이트가 대신 걸어주던 `onClick` 구독**이라 게이트를 끄면 브리지가 직접 물어야 한다(`HookSilently`/`DetachSilent`, `m_silentDone` 1회 가드 = `GateUI.m_satisfied`와 같은 역할). `GateUI`는 무수정 — "아무것도 안 그리는 모드"를 넣는 대신 소비처에서 끊었다. ⚠️ 그래서 **`AcquireButton`의 `TutorialAnchor`는 억제 모드에서도 필수**다(떼면 스텝 4·9가 영구 정지).
- **뜯기 안내는 튜토리얼이 아니다**: "스와이프하여 오픈"은 `PackRevealView.tearHint`(씬 저작 TMP + `CanvasGroup`)로, **튜토리얼 진행 여부와 무관하게 항상** 뜯기 대기 중 표시되고 뜯김 확정(`EnterBursting`)에 사라진다. 튜토리얼 배너였다면 2회차 이후 구매에서는 안내가 사라진다 — 조작법은 온보딩이 끝나도 필요하므로 씬의 상시 안내로 둔다. ⚠️ 배치는 **`RevealPanel` 바깥**이어야 한다(그 `CanvasGroup`은 분출 전까지 `alpha 0`이라 그 아래 두면 정작 뜯기 단계에 안 보인다).

**수정 가능성 높은 지점**: 스텝 순서·문구·팩·시나리오 = `OutgameTutorial.asset`(코드 미수정) / 새 타깃 = `EOutgameTutorialAnchor` 끝에 값 추가 + 씬에 `TutorialAnchor` 부착 / 게이트 외형 = `OutgameTutorialGateUI.BuildUI`(현재 코드 빌드, 아트 스킨 교체는 이 메서드 내부만) / 개봉 씬·전투 씬 경로 = `OutgameTutorialRunner.PackOpenScene` / `BattleScene` 상수 / 개봉 씬 가이드 on·off = 씬 브리지의 `suppressGuideUI` 체크박스 / 첫 전투를 다시 "로비에서 눌러서 시작"으로 되돌리려면 스텝 0의 kind를 `AutoBattle`→`BattleEntry`(anchor=`LobbyPlayButton`)로 바꾸면 된다(코드 무수정).

#### 파일 지도 — 다이어그램에서 코드로

| 클래스/에셋 | 파일 | 패키지 |
|---|---|---|
| `TutorialSaveData` (세이브 값 객체) | 신규 `Assets/Scripts/OutGame/Save/2.Domain/TutorialSaveData.cs` | P1 ✅ |
| `UserSaveData.tutorial` 슬롯 (VERSION 1 유지) | `Assets/Scripts/OutGame/Save/2.Domain/UserSaveData.cs` — 1줄 추가 | P1 ✅ |
| `OutgameTutorialProgress` (static, 진행도 단일 창구) | 신규 `Assets/Scripts/OutGame/Tutorial/OutgameTutorialProgress.cs` | P1 ✅ |
| `GameManager.Boot()` 진행도 Init | `Assets/Scripts/Core/GameManager.cs` — 1줄(Load 직후·CurrencyInit 앞) | P1 ✅ |
| 튜토리얼 리셋 2종(진행도만 / 소유까지) | `Assets/Scripts/OutGame/Collection/Debug/OwnershipDebugTool.cs` — ContextMenu | P1 ✅ |
| `EOutgameTutorialAnchor` (enum 키) | 신규 `Assets/Scripts/OutGame/Tutorial/EOutgameTutorialAnchor.cs` | P2 ✅ |
| `TutorialAnchorRegistry` (static 등록소 + `OnRegistered`) | 신규 `Assets/Scripts/OutGame/Tutorial/TutorialAnchorRegistry.cs` | P2 ✅ |
| `TutorialAnchor` (MonoBehaviour, 수명주기 등록) | 신규 `Assets/Scripts/OutGame/Tutorial/TutorialAnchor.cs` | P2 ✅ |
| `OutgameTutorialData` (SO, `EStepKind`+`Step`+`steps`) | 신규 `Assets/Scripts/OutGame/Tutorial/OutgameTutorialData.cs` — `Create → Card Battle/Outgame Tutorial` | P2 ✅ |
| `OutgameTutorialGateUI` (4패널 딤 게이트, `Ensure()` 파사드) | 신규 `Assets/Scripts/UI/Tutorial/OutgameTutorialGateUI.cs` | P3 ✅ |
| `OutgameTutorialRunner` (static, 스텝 해석·실행) | 신규 `Assets/Scripts/OutGame/Tutorial/OutgameTutorialRunner.cs` | P4 ✅ |
| `OutgameTutorialBridge` (씬당 1개, 수명 연결) | 신규 `Assets/Scripts/UI/Tutorial/OutgameTutorialBridge.cs` | P4 ✅ |
| `OutgameTutorialBridge.suppressGuideUI` (개봉 씬 가이드 off + 클릭 직접 구독) | `Assets/Scripts/UI/Tutorial/OutgameTutorialBridge.cs` — 필드 1 + `HookSilently`/`OnSilentClicked`/`DetachSilent` | G-HINT ✅ |
| `PackRevealView.tearHint` (뜯기 상시 안내, 튜토리얼 무관) | `Assets/Scripts/UI/Shop/PackRevealView.cs` — 필드 2 + `SetTearHint` | G-HINT ✅ |
| `LobbyTabController.Tab.tutorialAnchor` (탭 버튼 대리 등록) | `Assets/Scripts/UI/Lobby/LobbyTabController.cs` — 필드 1 + `Awake` 3줄 | P4 ✅ |
| `OwnershipManager.HasAnyOwnedSaved` 용도 축소(주석) | `Assets/Scripts/OutGame/Collection/OwnershipManager.cs` — 레거시 마이그레이션 판정 전용 | P4 ✅ |
| ~~`LobbyFirstRunRedirect`~~ | **삭제** — 스텝 0 `AutoPurchase`가 흡수 | P4 ✅ |
| `OutgameTutorialData.EStepKind.AutoBattle` + `Runner.EnterAutoBattle`/`BattleScene` 상수 | `Assets/Scripts/OutGame/Tutorial/OutgameTutorialData.cs` · `OutgameTutorialRunner.cs` | 2026-07-28 ✅ |
| `LoadingCoverView` (첫 프레임 은폐 커버, 자동 해제) + 씬 노드 `LobbyScene/LoadingCover`(order **1000**) | 신규 `Assets/Scripts/UI/Common/LoadingCoverView.cs` · `Assets/Scenes/LobbyScene.unity` | 2026-07-28 ✅ |
| `Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset` (스텝 0~10) | 저작 완료 — 전투 3회 · 구매 사이클 2회 (2026-07-28 앞머리 3스텝 제거) | P6 ✅ |

#### 씬/에셋 인계 (코드 밖 — 사용자)

| 씬/에셋 | 작업 |
|---|---|
| `Assets/Scenes/New/LobbyScene.unity` | 구 `LobbyFirstRunRedirect` GameObject의 **Missing Script 제거** 후 `OutgameTutorialBridge` 부착(`data` ← `OutgameTutorial.asset`) / `MatchContent/PlayBtn`에 `TutorialAnchor`(`key = LobbyPlayButton`) |
| `Assets/Scenes/CardPack.unity` | `AcquireButton`에 `TutorialAnchor`(`key = PackAcquireButton`) / `PackOpenDirector`에 `OutgameTutorialBridge`(같은 에셋) |
| `Assets/Scenes/CardPack.unity` (2026-07-27 추가) | ① `PackOpenDirector`의 브리지 → **`Suppress Guide UI` 체크** ② **`UICanvas` 직속**(`RevealPanel`의 **형제**)에 안내 TMP 신규(예: `TearHint`, "스와이프하여 오픈") — `CanvasGroup` 추가 후 **alpha 0으로 저장**, TMP `Raycast Target` 해제 → `PackRevealView.tearHint`에 배선 ③ `AcquireButton`의 `TutorialAnchor`는 **유지**(억제 모드에서도 브리지가 이 앵커로 클릭을 감지) |
| `Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset` | **11스텝**(2026-07-28 개정). 0=`AutoBattle`(scenario←TutorialScenario, anchor·pack·nextScene 전부 비움) · 1~5·6~10=구매 사이클(`WaitClick`(LobbyPackTab) → `WaitPurchase`(PackBuyButton, pack←NormalPack, refund 10) → `WaitPackOpen` → `WaitClick`(PackAcquireButton) → `BattleEntry`(TutorialScenario2/3, anchor←LobbyPlayButton)) |
| `UIPoolManager` 캔버스 | `sortingOrder` **1 → 400** (`LobbyScene.unity` / `MainMenu.unity`). 게이트 300 > 팝업 상속값 1이라 **실패 팝업이 딤 아래로 묻혀 [확인]을 못 누른다** |

> `nextScene`은 **개봉 후 목적지**이며 `AutoPurchase` 전용이다(개봉 씬 `"CardPack"`·전투 씬 `"BattleScene"`은 러너 상수). `AutoBattle`은 이 필드를 읽지 않는다.
> 개봉 씬 스텝(3·8 `WaitPackOpen`, 4·9 `WaitClick`)의 `guideMessage`는 `suppressGuideUI` 이후 **표시되지 않는다**. 비워도 되고 두어도 무해하다(로비 브리지는 이 스텝들을 만나지 않는다).
> ⚠️ 스텝 인덱스가 곧 세이브 진행도라, 앞머리 3스텝 제거로 **진행 중이던 세이브는 3칸 밀린다** — `OwnershipDebugTool`의 진행도 리셋으로 처음부터 확인할 것.
> 게이트 UI는 코드 빌드라 **신규 프리팹·Addressable 등록이 없다**(`UIPrefab` 라벨 체크 대상 아님).

#### P5·P6 해결 내역

- **앵커 배선 완료**: `LobbyTabController.tabs[1].tutorialAnchor = LobbyPackTab` / `PackShowcaseController.packData` ← `NormalPack.asset` / 그 `buyButton`(BuyBtn_0)에 `TutorialAnchor(PackBuyButton)`.
- **완료 판정이 두 갈래로 갈렸다**: 팩 개봉은 uGUI 게이트를 걸 수 **없고**, 구매는 눌러도 실패할 수 있다.
  - 게이트를 못 거는 이유는 **팩에 `Button`이 없기 때문**이다. `OutgameTutorialGateUI.ShowGate`는 타깃에 `Button`이 없으면 경고 후 게이트를 걸지 않는다(`OutgameTutorialGateUI.cs:57-61`) — 완료 판정을 `onClick` 구독으로만 하는 설계라 버튼이 없으면 "딤은 걸렸는데 완료시킬 방법이 없는" 소프트락이 되기 때문이다. 팩은 그때나 지금이나 `Button`이 아니라 제스처 컴포넌트(`PackTearHandle`)로 조작된다. → `WaitPackOpen`은 딤 없이 배너만 띄우는 `ShowBanner` 경로.
  - ~~"`PackClickHandle`의 물리 클릭이라 … Overlay 딤이 3D를 덮어 강조가 은폐가 된다"~~ 는 **이중으로 낡은 서술**이었다(2026-07-28 정정): ① `PackClickHandle`은 G-29에서 **삭제된 클래스**다(현재 코드베이스에 남은 건 `PackTearHandle.cs:13`의 성격 계승 언급뿐) ② 2D 전환으로 3D 자체가 사라졌다. **결론(`WaitPackOpen` = `ShowBanner` 경로)은 그대로 유효하다** — 근거만 바뀌었다. → kind `WaitPackOpen`(딤 없이 배너만, `GateUI.ShowBanner`)과 `WaitPurchase`(딤만 걸고 클릭은 완료가 아님)를 추가하고, 완료는 **결과 신호**가 확정한다.
  - `PackRevealView.OnAnyPackOpened` / `PackShowcaseController.OnAnyPurchased` (둘 다 static event) → `OutgameTutorialBridge`가 구독. **뷰는 구독자를 모른다** — `TutorialAnchorRegistry.OnRegistered`와 같은 방향의 관용구.
  - `GateUI.ShowGate(_onSatisfied: null)` = 클릭 리스너 미부착(딤 유지). 이것이 `WaitPurchase`의 구현.
- **결과 기반 커밋 해결**: 구매 실패 시 진행도가 앞서 나가지 않는다.
- **튜토리얼 구매 대상 고정**: `WaitPurchase` 스텝의 `pack`/`duplicateRefundGold`가 상점 진열을 덮어쓴다(`OutgameTutorialRunner.TryGetForcedPack` → `PackShowcaseController.ResolvePack`). `AutoPurchase`와 같은 원리 — 튜토리얼 중 구매 결과가 저작대로 고정된다. 표시명·가격·구매 잠금 판정도 같은 해석을 쓰므로 **표시 가격과 실제 결제가 갈리지 않는다**. 미지정이면 상점 기본 진열(`packData`)로 폴백.
  - 갱신 시점이 둘인 이유: 탭 활성화(`OnEnable`)가 스텝 커밋보다 **먼저** 일어난다(탭 버튼의 `Select` 리스너가 게이트 리스너보다 앞에 등록됨). 그래서 `OutgameTutorialRunner.OnStepChanged`로도 재해석한다.
- **경제 데드락 대응(값 무수정)**: `PackShowcaseController`가 잔액으로 구매 버튼을 잠그고(`CurrencyManager.CanAfford` + `OnCurrencyChanged` 구독), `GateUI.RefreshVisibility`의 기존 소프트락 방어가 딤을 자동으로 걷는다 → 유저가 전투로 골드를 벌고 돌아오면 그 스텝이 그대로 재개된다. Play 실측 후 부족하면 `RewardConfig.minGold` / `NormalPack.price` 조정(코드 무수정).
- **전투 사이 진입 깜빡임 제거**: `BattleEntry` 완료 직후에는 다음 스텝을 이 씬에서 진입시키지 않는다(`PlayBtn`이 이미 `LoadScene`을 걸어 곧 사라질 게이트가 한 프레임 뜬다) — 로비 복귀 시 그 씬의 브리지가 재개한다.

---

### H. 랭크 — 표시용 티어 진행도 — ✅ 코드 전량 완료(H-28~H-32) + 로비 씬 배선 완료 (2026-07-27)

> 목표 루프의 **엔드포인트 표기**를 실물로 세운다. 단 실력 지표가 아니라 **표시용 진행도**(칭호)다.
> **왜 로컬로 가능한가**: 로비 Match 탭은 100% AI전이고(`LobbyMatchLauncher.StartAiBattle` 단일 배선), PvP UI는 런타임에 도달 불가한 `MainMenu.unity`에만 있다. 클라 권위 + RPC 무검증이라 위조 가능하지만, **보상·난이도·매칭에 아무 영향이 없으므로 위조돼도 잃는 게 없다** → 서버 권위가 전제되지 않는다.
> **H는 자체 계약 소비가 거의 없다** — 재화·소유·생산·팩 어느 것도 안 건드리고, 세이브 슬롯 1개와 전투 종료 훅 1줄만 쓴다. `:::new` = 신규, `:::chg` = 기존 파일 소규모 수정.

#### 구조 지도 — 4갈래(영속 / 판정 / 주입 / 표시)

```mermaid
flowchart TD
    subgraph save["세이브 (기존 3층, 슬롯 1개 추가)"]
        DSM["DataSaveManager<br/>Data · Save"]
        USD["UserSaveData<br/>VERSION 1 유지"]
        RSD["RankSaveData<br/>points 단일 필드"]:::new
    end

    subgraph rank["H. 랭크 (신규 OutGame/Rank/)"]
        CFG["RankConfig (SO)<br/>[#1 fallback] 테이블 해석 단일 진실원<br/>grades(등급 5행) · DivisionsPerGrade=4<br/>TierCount · ResolveTierIndex · TryGetTier<br/>필드 초기화자 = 코드 기본 테이블"]:::new
        MGR["RankManager<br/>[#1 static창구] 캐시 없음 · 예외 미발생<br/>Points · GetInfo · ApplyBattleResult(→RankApplyResult)"]:::new
        APPLY["RankApplyResult (readonly struct)<br/>[정산 1회 결과]<br/>Delta · PrevTierIndex · TierIndex<br/>IsTierUp = 티어 상승 여부"]:::new
        INFO["RankInfo (readonly struct)<br/>[#6 UI 스냅샷]<br/>TierIndex · Grade · Division<br/>DisplayName · Badge<br/>Points · NextRequired · IsMaxTier"]:::new
    end

    subgraph battle["Battle (경계 교차, 2줄)"]
        TR["TurnRunner.CaptureResult(bool _won)<br/>resultCaptured = 전투당 1회"]:::chg
        RS["RewardService.GrantBattleReward<br/>(기존 — 반드시 먼저)"]
    end

    DL["DataLibrary<br/>전역 SO 주입 창구<br/>(RewardService.SetConfig 선례)"]:::chg
    HUD["RankHud (UI/HUD)<br/>RankBadge(Image) · RankPower(TMP)<br/>최초 렌더 = Start()"]:::new

    DSM --> USD
    USD --- RSD
    MGR -->|"슬롯 직접 읽기 · 즉시 Save"| DSM
    CFG -->|SetConfig| MGR
    DL -->|"Awake · InitializeSingleton"| CFG
    TR --> RS
    TR -->|"ApplyBattleResult(_won) — 무조건"| MGR
    MGR --> APPLY
    APPLY -->|"CaptureResult가 수신"| TR
    MGR --> INFO
    INFO -->|"Start에서 1회 조회"| HUD

    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
    classDef chg fill:#7a5b16,stroke:#f2c14e,color:#fff;
```

#### 흐름 시퀀스 — 전투 종료 → 로비 배지 갱신

```mermaid
sequenceDiagram
    participant TR as TurnRunner
    participant RS as RewardService
    participant MGR as RankManager
    participant DSM as DataSaveManager
    participant HUD as RankHud (로비)

    Note over TR: 승패 확정 4지점 → CaptureResult(_won)<br/>resultCaptured 가드로 전투당 1회
    TR->>RS: GrantBattleReward(remaining)
    RS-->>TR: 지급액 (골드는 이미 영속 완료)
    Note over TR: 멀티 배제 게이트는 제거됨(프로토 스코프 밖)<br/>싱글·멀티·튜토리얼·부전승 전부 가감
    TR->>MGR: ApplyBattleResult(_won)
    MGR->>MGR: delta = 승? +winPoints : -losePoints
    MGR->>MGR: 하한 = max(가감 "전" 티어 requiredPoints, 0)
    MGR->>MGR: points = max(points + delta, 하한)
    MGR->>DSM: Save() — 씬 왕복을 견디게 즉시 영속
    Note over TR,HUD: BattleCleanup.LoadScene("LobbyScene")
    HUD->>MGR: Start()에서 GetInfo() 1회
    MGR-->>HUD: RankInfo(TierIndex/Badge/Points)
    Note over HUD: badge non-null일 때만 교체(아트 미배선 시 기존 유지)
```

#### 원리 카드 — 왜 이렇게 생겼나

- **티어를 저장하지 않는 이유**: 티어는 `points`의 순수 파생("`requiredPoints <= points`를 만족하는 최대 인덱스, 없으면 0 클램프")이다. 도달 티어를 따로 저장하면 둘이 어긋날 때 어느 쪽이 진실인지 알 수 없다. **트레이드오프: 임계치 테이블을 나중에 상향하면 기존 유저가 소급 강등된다** — "절대 강등 없음"은 본질적으로 *역사적* 불변식이라 포인트만으로는 인코딩되지 않는다. 프로토타입이라 수용하고, SO 툴팁에 **"임계치는 하향만"** 저작 규칙을 남긴다(사용자 결정).
- **강등 없음의 구현**: 별도 상태가 아니라 **가감 시 하한 클램프**다 — `max(points + delta, max(가감 전 티어의 requiredPoints, 0))`. 이중 하한이라 음수도 불가능. 부작용: **갓 승급한 유저는 다음 승리 전까지 패배 비용이 0**(설계된 사각지대, 표시용이라 수용).
- **`Init`이 없는 이유**: 캐시를 두지 않고 세이브 슬롯을 직접 읽는다(`OutgameTutorialProgress` 패턴). 캐시의 유일한 이득인 읽기 성능이 여기선 0인 반면(조회 = 로비 진입당 1회), 캐시의 비용은 **`GameManager.Boot()` 수정 = 통합 부트 순서(동결 계약) 접촉**으로 최대다. `GameManager`가 `BeforeSceneLoad`+`DontDestroyOnLoad`라 **어느 씬에서 Play를 시작하든 `Load()`가 끝나 있다** — 캐시 패턴은 이 보장을 못 준다.
- **보상 뒤에 랭크를 두는 이유**: 랭크가 실패해도 골드는 이미 지급·영속 완료. 또 `RewardService`가 `CurrencyManager.Save()`로 캐시→슬롯 flush를 끝낸 뒤라 랭크의 `DataSaveManager.Save()`가 최신 골드를 쓴다(뒤집으면 낡은 골드 슬롯을 한 번 디스크에 쓰게 됨).
- **`try/catch`를 안 쓰는 이유**: 프로젝트에서 예외를 삼키는 곳은 `DataSaveManager.Load` 하나뿐. 대신 `RankManager`를 **예외를 던질 수 없게** 짠다(config null·빈 `grades`·null 등급 행·범위 밖 인덱스·슬롯 null 전부 폴백 — 등급 재설계 후에는 `RankConfig.ResolveTierIndex`/`TryGetTier`가 이 폴백을 한 곳에서 책임진다) — `RewardService`/`OutgameTutorialProgress`가 쓰는 방식.
- **의도적으로 덜어낸 2건**: ① `OnRankChanged` 이벤트 — 골드와 달리 **랭크는 로비에서 변동할 경로가 0**(전투 씬에서만 변함)이라 `Start`/`OnEnable` 재조회로 충분. 필요해지면 순수 추가라 🟢로 언제든 붙는다. ② 구간 진행률 필드 — 씬에 진행바가 없어 소비처 0.
  - (해제) `ApplyBattleResult` 반환값 — 결과 팝업이 랭크 증감을 연출하게 되면서 소비처가 생겼다. **클램프 뒤 실제 증감**(`저장 후 points - 저장 전 points`)을 돌려주므로 표시액과 저장값이 어긋날 수 없다. `TurnRunner.CaptureResult`가 받아 `GameResultPopup.Show(골드, 랭크델타)`로 넘긴다.
    - (확장, 2026-07-29) 반환 타입이 `long` → **`RankApplyResult`(readonly struct)**로 승격됐다. `Delta`에 더해 `PrevTierIndex`/`TierIndex`를 실어 **승급 여부(`IsTierUp`)를 재조회 없이** 알린다 — 델타만 주면 소비처가 티어를 다시 조회해야 하고, 그 사이 값이 바뀌면 연출과 저장값이 갈린다. 가감 **전** 인덱스는 강등 하한 계산에 이미 쓰던 값을 그대로 재사용해 조회가 늘지 않았다. **정산 후 총 포인트는 담지 않는다** — `RankManager.Points`가 이미 단일 진실원이라 이중 진실원이 된다.
- **수정 가능성 높은 지점**: 티어 수·이름·임계치·승패 포인트 = `RankConfig.asset`(코드 무수정) / 티어 배지 아트 = SO의 `badge` 슬롯 / 집계 제외 규칙(튜토리얼 등) = `CaptureResult`의 조건 1줄.

#### 함정 2개 — 구현 시 반드시

| 함정 | 왜 | 처방 |
|---|---|---|
| `RankHud`에 `GoldHud` 패턴 복제 | `GoldHud`의 `OnEnable` 즉시 렌더가 안전한 건 `CurrencyManager.Init()`이 `BeforeSceneLoad`에서 끝나기 때문. `RankConfig` 주입은 `DataLibrary.Awake`(순서 0)라 `RankHud.OnEnable`이 **먼저 돌 수 있다**(같은 씬, 둘 다 순서 0 = 비결정). 이벤트를 뺐으므로 잘못된 첫 렌더가 그대로 굳는다 | **최초 렌더는 `Start()`**(모든 `Awake` 이후 보장), `OnEnable`은 `m_started` 가드로 탭 재진입만 처리. `DataLibrary`에 `[DefaultExecutionOrder]`를 붙이는 건 전역 부트 계약 접촉이라 금지 |
| `RankConfig.tiers`를 빈 리스트로 방치 | `BattleReward`는 스칼라 필드라 `CreateInstance` fallback에서 기본값이 살지만 **`List<>`는 빈 리스트**가 된다. 게다가 `DataLibrary`는 **BattleScene에 없어서** 에디터에서 전투 씬을 직접 Play하면 **항상** fallback이 탄다 | `tiers`를 **C# 필드 초기화자로 기본 테이블** 채움(필드 초기화자는 `CreateInstance`에서 실행됨). 배지만 null → SO는 "튜닝·아트 주입 전용"으로 강등되고 기능은 코드가 보증 |

#### 씬 배선 (실측, 신규 노드 생성 0) — ✅ 완료

```
LobbyCanvas/LobbyRoot/Content/Tab_Match/MatchContent
├── RankReward  (BasicFrame 프리팹 인스턴스 + Button 추가)  → H-33에서 활성화 + onClick = RankRewardPanel.Open ✅
│   └── RankText (TMP = "랭크보상")   ⚠️ 티어 라벨이 아니라 버튼 캡션 — 건드리지 말 것
└── PlayBtn
    └── RankInfo  (RectTransform만)           ← RankHud 부착 ✅
        ├── RankPower (오벌 프레임 프리팹, 내부 TMP "Text" = "82")  → pointText ✅
        └── RankBadge (Image 230×230, y=+450)                     → badgeImage ✅
```

> 티어명은 **배지 스프라이트로 표현**하기로 결정(사용자) — `RankInfo` 하위에 티어명 TMP를 새로 만들지 않는다.
> `pointText`가 가리키는 TMP는 `RankPower` **프리팹 인스턴스 내부**의 자식(`Text`)이다 — 프리팹 자체를 수정한 게 아니라 씬 인스턴스의 자식을 참조만 한다.

#### 파일 지도 — 다이어그램에서 코드로

| 클래스 | 파일 | 태스크 | 상태 |
|---|---|---|---|
| `RankSaveData` | `OutGame/Save/2.Domain/RankSaveData.cs` (+ `UserSaveData.rank` 슬롯) | H-28 | ✅ 코드 |
| `RankConfig`(SO) · `ERankGrade` · `RankGradeConfig`(저작) · `RankTier`(파생 struct) | `OutGame/Rank/RankConfig.cs` | H-30 | ✅ 코드 (`RankTier.Index`는 **예약 — 읽는 소비처 없음**) |
| `RankManager` · `RankInfo` | `OutGame/Rank/RankManager.cs` | H-29 | ✅ 코드 (`RankInfo.Grade`·`Division`은 **예약 — 읽는 소비처 없음**) |
| 전투 훅 | `Battle/TurnRunner.cs` `CaptureResult` 1줄 | H-31 | ✅ 코드 |
| SO 주입 | `Utils/DataLibrary.cs` 필드1+호출1 | H-30 | ✅ 코드 (`RankConfig.asset`은 사용자 인계) |
| `RankHud` | `UI/HUD/RankHud.cs` | H-32 | ✅ 코드 + 씬 배선 |

#### CORE 구현 결과 (2026-07-27) — 소비자가 알아야 할 것

- **기본 테이블(코드 필드 초기화자)**: **등급 5행 × 4단계 = 20티어**(20행 손저작이 아니라 등급 5행에서 **파생**). `브론즈 1~4(0/25/50/75) → 실버 1~4(100~175) → 골드 1~4(200~275) → 플래티넘 1~4(300~375) → 다이아몬드 1~4(400~475)`, 균등 25포인트 간격. `winPoints=10` · `losePoints=5`. 각 등급 4단계에서 다음 등급 `entryPoints`를 넘기면 **인덱스 연속성으로** 다음 등급 1단계가 된다(브론즈 4 → 실버 1). `RankConfig.asset`이 없어도 이 20티어로 동작한다 → **배지 아트**가 사용자 인계분(등급당 1장 = `RankGradeConfig.badge`, 4단계 공용). `long` 필드에 `[Min]`은 붙이지 않는다(Unity `MinDrawer`가 `intValue`로 잘라낸다 — `BattleReward` 선례).
- **등급 × 단계 재설계(후속)**: 등급이 `ERankGrade` enum으로 승격돼 코드에서 참조 가능해졌고, 승급 규칙이 `RankConfig`의 파생 공식(`tierIndex = gradeIndex * 4 + (division-1)`)으로 명시됐다. 티어 해석은 `RankConfig.ResolveTierIndex` / `TryGetTier` **단일 진실원**에 모였고(두 매니저에 흩어져 있던 `ResolveTierIndex`/`ResolveTierFloor`/`FindNextTier`/`FindTier` 제거), `RankTier`는 저작 class → **파생 `readonly struct`**가 됐다. `DisplayName`은 생성자에서 정규화돼 조회 실패(`RankTier.None`)에도 빈 문자열이 보장된다 — 단 **조회 실패 판정은 `TryGetTier` 반환값으로만** 한다(`None`의 `Index=0`·`Grade=Bronze`는 유효한 브론즈 1과 값으로 구분되지 않는다). 세이브 스키마(`points`/`claimedCount`)와 티어 인덱스 의미는 **불변**.
- **예약 API(읽는 소비처 0, 데드코드 아님)**: `RankInfo.Grade` · `RankInfo.Division` · `RankTier.Index`. 등급을 코드에서 참조 가능하게 만든 것이 이번 재설계의 목적이고, 등급 단위 뱃지/색상 분기·"실버 3" 형태 분해 표시가 붙을 확장 축이다. 제거하지 말 것.
- **최대 티어면 `NextRequired == Points`**(0 아님) — "남은 = `NextRequired - Points`"가 모든 티어에서 성립해 HUD가 `IsMaxTier` 분기 없이 진행률을 계산해도 0 나눗셈이 안 난다.
- **`DisplayName`은 항상 non-null**(미저작·빈 테이블이면 `string.Empty`). **`Badge`는 null 가능** → HUD는 non-null일 때만 스프라이트를 교체한다(아트 미배선 시 씬 기존 이미지 유지).
- **`Config`·`Save()`는 private** — 공개 API는 `Points`/`GetInfo`/`ApplyBattleResult`/`SetConfig`/`ResetForDebug` 5개뿐.
- ⚠️ **이름 겹침**: 로비 씬 노드 `RankInfo`(RectTransform, `RankHud` 부착 지점)와 C# `RankInfo`(struct)는 이름만 같고 무관하다.

#### HUD 구현 결과 (2026-07-27) — 도메인 H 코드 종결

- `RankHud`는 **`RankInfo` 스냅샷의 6필드 중 2개만 소비**한다 — `Badge`(Image 교체) · `Points`(TMP). `DisplayName`은 티어를 배지로 표현하기로 해서 소비처가 없고, `TierIndex`/`NextRequired`/`IsMaxTier`도 미소비(진행바 없음 + `IsMaxTier` 분기는 빈 tiers 오표시 위험).
- **렌더 진입점은 `Render()` 하나**, 호출자는 `Start()`(최초 1회)와 `OnEnable()`(`m_started` 가드 → 탭 재진입만). 이벤트 구독 0이라 `OnDisable`이 없다 — 해제 누락 위험도 구조적으로 없다.
- **배지 진실원은 `RankGradeConfig.badge` 단 하나**(사용자 결정, 등급 재설계 전에는 `RankConfig.tiers[].badge`). HUD에 `Sprite[]` 폴백 배열을 두지 않는다 — 아트 미저작이면 `Badge == null`이라 씬에 배선된 기존 스프라이트가 그대로 남는다.
- 도메인 H는 이걸로 **코드 100% 종결**. 남은 건 전부 에셋/아트: `RankConfig.asset` 저작·배선(✅ 완료), **등급 배지 스프라이트 5장**(등급 단위, 4단계 공용 — L931과 동일 규약). 없어도 20티어 기본 테이블 + 씬 기본 배지로 동작한다.

---

### H-33. 랭크 티어 달성 보상 — ✅ 완료 (2026-07-27, 코드 + 씬 저작)

> 표시용 진행도였던 랭크에 **보상 엔드포인트**를 붙인다. 티어에 도달하면 골드를 **순차로 1회씩** 수령한다.
> **`RankManager`는 무수정** — 보상은 신규 static 창구 `RankRewardManager`로만 흐르고, 달성 판정만 `RankManager.GetInfo()`에 위임한다(동결 계약 무접촉).
> ⚠️ 이 "무수정·동결" 서술은 **H-33 시점 기록**이다. 이후 등급 재설계에서 **동결이 해제**돼 `GetInfo`·`ApplyBattleResult`가 재작성되고 private 헬퍼 3개(`ResolveTierIndex`/`ResolveTierFloor`/`FindNextTier`)가 제거됐다. 공개 API·불변식은 보존됐다(아래 재설계 항목 참조).
> 스코프: 골드 1종(프로젝트 재화가 `Gold` 단일) · 20티어 전부 · 광고 2배 없음.

#### 구조 지도 — 판정/영속/표시

```mermaid
flowchart TD
    subgraph save["세이브 (슬롯 추가 없음, 필드 1개)"]
        RSD["RankSaveData<br/>points (기존)<br/>claimedCount ← 수령 커서"]:::chg
    end

    subgraph rank["랭크 (기존 + 신규)"]
        CFG["RankConfig / RankGradeConfig<br/>rewardGold · rewardGoldPerDivision"]:::chg
        MGR["RankManager<br/>[H-33 시점 무수정 → 등급 재설계에서 동결 해제]<br/>GetInfo → 도달 티어"]
        RMGR["RankRewardManager<br/>[#1 보상 창구] 캐시 없음 · 예외 미발생<br/>GetInfo · CanClaim · Claim · OnChanged"]:::new
        INFO["RankRewardInfo (readonly struct)<br/>TierIndex · DisplayName · Badge<br/>RewardGold · State"]:::new
        HAND["RankUpHandoff<br/>[씬 캐리어] 세이브 없음 · nullable 홀더 1개<br/>Set(RankApplyResult) · TryConsume(1회 소비)"]:::new
    end

    subgraph ui["UI (씬 직접 저작 — PooledUIBase 아님)"]
        PANEL["RankRewardPanel<br/>행 20개 Build · OnChanged 구독"]:::new
        ROW["RankRewardRowView<br/>인덱스만 보유 · 매번 재조회"]:::new
        POP["RankRewardClaimPopup<br/>표시 + 확인 콜백"]:::new
    end

    CUR["CurrencyManager<br/>Earn → Save (내부에서 DataSaveManager.Save)"]

    RMGR -->|"슬롯 직접 읽기"| RSD
    CFG -->|SetConfig| RMGR
    MGR -->|"도달 티어 조회"| RMGR
    RMGR --> INFO
    INFO --> ROW
    PANEL --> ROW
    ROW -->|"클릭 → 인덱스"| PANEL
    PANEL --> POP
    POP -->|"획득 콜백"| PANEL
    PANEL -->|Claim| RMGR
    RMGR -->|"지급 + 영속 1회"| CUR
    RMGR -.->|OnChanged| PANEL
    MGR -.->|"전투 씬: 승급이면 Set"| HAND
    HAND -.->|"로비 Start: TryConsume 1회<br/>→ 패널 자동 오픈 + 도달 행 연출"| PANEL

    classDef new fill:#1f6f3f,stroke:#7CFC9E,color:#fff;
    classDef chg fill:#7a5b16,stroke:#f2c14e,color:#fff;
```

#### 원리 카드 — 왜 이렇게 생겼나

- **수령 상태가 정수 1개인 이유**: 티어는 강등이 없으므로(`ApplyBattleResult`의 하한 클램프) 도달 집합이 항상 `[0..TierIndex]` **프리픽스**다. 수령 집합도 프리픽스로 두면 상태공간이 정수 하나로 접힌다. `bool[20]`이나 키 리스트는 "구멍 뚫린 수령"처럼 **불변식이 허용하지 않는 상태까지 표현**할 수 있어 버그 표면이 넓어지고, 티어 테이블 길이가 바뀌면 마이그레이션이 필요하다.
- **센티널(`-1`) 대신 개수인 이유**: 세이브는 `JsonUtility.FromJson`으로 읽는다. `0`이 곧 "미수령"이면 구 세이브·노드 재작성 어느 경로에서도 "이미 받음"으로 오판할 수 없다. `-1` 초기화자에 의존하면 직렬화기 동작에 안전성이 묶인다.
- **보상량을 등급 행에 넣은 이유**: 별도 SO로 분리하면 두 리스트를 인덱스 정렬 상태로 **사람이** 유지해야 한다(도감이 이미 rowKey 드리프트로 겪은 문제). 임계치와 같은 원소(`RankGradeConfig`)에 두면 어긋남이 구조적으로 불가능하다.
- **`Claimed`를 가장 먼저 판정하는 이유**: `RankManager.ResetForDebug()`는 `points`만 0으로 되돌려 `claimedCount > 도달티어` 구간을 만든다. `Claimable`을 먼저 보면 그 구간에서 **재수령이 뚫린다**.
- **영속을 `CurrencyManager.Save()` 하나로 끝내는 이유**: 그 메서드가 골드를 슬롯에 flush한 뒤 `DataSaveManager.Save()`를 부른다. 앞에 `DataSaveManager.Save()`를 따로 세우면 **"커서만 오르고 골드는 미반영"인 중간 상태가 한 번 디스크에 쓰인다**(그 사이 크래시 = 골드만 유실).
- **행이 스냅샷을 캐싱하지 않는 이유**: `RankRewardRowView`는 티어 인덱스만 들고 `Refresh()`마다 `GetInfo`를 재조회한다. struct를 캐싱하면 수령 후 stale이 되고, 패널이 갱신 때마다 스냅샷을 나눠줘야 해서 결합이 늘어난다.
- **승급 결과를 씬 캐리어로 넘기는 이유 (2026-07-29 추가)**: 정산은 전투 씬에서 끝나는데 보상 패널은 로비에만 있다. 그 사이를 잇는 게 `RankUpHandoff` — `TurnRunner`가 `RankApplyResult`를 통째로 싣고, `RankRewardPanel.Start`가 `TryConsume`으로 1회 꺼내 패널을 자동으로 열고 도달 행을 연출한다(`PackHandoff`의 "1회 소비 후 홀더 비움" 규약 그대로). **자체 세이브 없음** — 포인트는 `ApplyBattleResult`가 이미 즉시 영속했고, 캐리어는 "이번 왕복"의 휘발 컨텍스트만 나른다. bool+int를 나란히 두지 않고 **nullable DTO 홀더 1개**라 "pending 여부"와 "값"이 갈라질 수 없다. 로비 도달 전 연속 전투가 나면 **더 높은 티어가 이기고**(강등·동일 티어는 기존 값 유지), `PrevTierIndex`는 최초 값을 유지한 채 `Delta`를 누적한다 → 캐리어의 의미는 "시작 티어 → 도달 티어, 그 사이 총 증감"이다.
- **패널이 `PooledUIBase`가 아닌 이유**: 사용자 결정으로 씬 직접 저작이다. `UIPoolManager` 캔버스(1440×2960)는 `LobbyCanvas`(1080×1920)와 해상도가 달라 큰 레이아웃에서 좌표계가 어긋난다. 씬 저작이면 Addressable 등록·소팅 오더 문제도 함께 사라지고, 수령 팝업이 패널의 자식이라 항상 그 위에 뜬다.

#### 함정 5개

| 함정 | 왜 | 처방 |
|---|---|---|
| **인스펙터에서 `grades`에 빈 행 추가** | 신규 원소는 `entryPoints=0`·`pointsPerDivision=0`이라 **역순 스캔이 그 행의 4단계를 즉시 매치**한다 → 포인트와 무관하게 전 유저가 최상위 티어로 점프하고, 20행 보상이 통째로 수령 가능해진다(순차 커서가 끝까지 열림) | 저작 규율로만 막는다 — **신규 행은 값을 채운 뒤 저장**. `OnValidate` 검증 가드는 프로토 스코프 밖이라 넣지 않는다 |
| **`grades`에 null 등급 행** | `TierCount`는 null 행도 4티어로 세지만 `TryGetTier`는 그 행에서 `false` → 패널은 행을 만들되 `Claim`이 커서를 못 올려 **그 지점부터 순차 수령이 영구 정지** | 구 `List<RankTier>` 구조와 동일한 성질이라 **회귀 아님**. Unity 직렬화상 null 원소 실현이 어려워 코드 가드 없이 방치(기록만) |
| `rowPrefab`을 Content 안의 목업 행으로 배선 | `Build()`가 Content 자식을 전부 지우면 프리팹 참조가 fake-null이 되어 **다음 Open부터 행 0개** | 원본은 숨기기만 하고 Destroy 제외 + 사본을 `SetActive(true)` (구현 반영됨) |
| ~~`RankConfig.asset`의 `rewardGold`~~ | YAML엔 키가 **없는데** 런타임엔 값이 산다 — Unity가 초기화자로 만든 원소를 재사용하고 없는 필드를 덮어쓰지 않기 때문. 즉 **값이 에셋이 아니라 코드에 묶여 있었다**(전 티어 0골드 표시) | ✅ **해소(등급 재설계 시)**: `grades` 5행을 YAML에 직접 저작하며 `rewardGold`/`rewardGoldPerDivision`를 굳혔다. 코드 필드 초기화자와 값 일치 |
| `RankReward` 활성화만 하고 중복 `RankHud`를 방치 | `Tab_Match`에 붙은 두 번째 `RankHud`의 `badgeImage`가 **`RankReward` 프리팹 내부 Image**를 가리킨다 → 배지 아트를 저작하는 순간 보상 버튼 아이콘이 티어 배지로 덮인다 | 버튼 활성화와 컴포넌트 제거를 **한 세트로** 처리 → ✅ **해소(2026-07-27)**: 활성화와 동시에 `Tab_Match`의 `RankHud` 제거. 정상본(`PlayBtn/RankInfo`) 1개만 잔존 |

#### 씬 저작 (2026-07-27, Unity MCP `Unity_RunCommand`)

`RankRewardOverlay`는 `LobbyCanvas` **직속**(`LobbyRoot` 밖, `DragLayer` 다음 = 최상단 렌더).

```
LobbyCanvas
└── RankRewardOverlay        RankRewardPanel        ← 항상 활성 (OnEnable 구독이 살아야 한다)
    └── Root                                        ← 토글 대상(= root 필드). 저작 기본값 비활성
        ├── Dim              Image(α .72) + Button  → onClick = RankRewardPanel.Close
        ├── Panel            940 × (H−360)
        │   ├── TitleRibbon 560×116 → TitleText "랭크 보상"
        │   ├── CloseButton ⌀104 (onClick 영구배선 없음 — OnEnable이 런타임 등록)
        │   └── Scroll       ScrollRect(vertical)
        │       └── Viewport RectMask2D
        │           └── Content  VerticalLayoutGroup + ContentSizeFitter
        │               └── MockRow_0..2 (디자인타임 목업 — 런타임 Build()가 파괴)
        └── ClaimPopup       RankRewardClaimPopup   ← Root 안 = Root가 꺼지면 구조적으로 못 남는다
            ├── PopupDim     Button → onClick = RankRewardClaimPopup.Hide  (취소 경로)
            └── PopupFrame   760×760 (리본 · 골드 아이콘 · 금액 · [획득])
```

행 프리팹 `RankRewardRow.prefab` (848×208 = 카드 164 + 쉐브론 44):
`RowCard`(연하늘) → `Badge` · `TierName` · `RewardBox`(Button, 골드아이콘+금액) · `Highlight`(9-slice 링, `fillCenter=false` + `pixelsPerUnitMultiplier=4`) · `ClaimedMark` · `LockDim`(raycastTarget ✔ = 미달성 클릭 물리 차단) → `LockIcon`. 형제 순서상 `LockDim`이 최상단.

> **`Highlight`에 `pixelsPerUnitMultiplier`가 필요한 이유**: `BasicFrame_Rectangle01_l`은 원본 92×93에 보더 46/46/46/47이다. 높이 164 카드에 `fillCenter=false`로 깔면 상하 보더 93이 카드 대부분을 먹어 **링이 아니라 노란 판**이 된다. 배율 4로 보더를 ~11px로 줄여야 테두리로 읽힌다.
>
> **상태 아이콘을 보상 박스 안 우측에 두는 이유**: 박스 정중앙에 두면 `x100` 텍스트를 가린다. 박스를 340으로 넓히고 `AmountText`의 `offsetMax`를 −78로 밀어 아이콘 자리를 비웠다.
>
> **캡처 방법**: `LobbyCanvas`는 ScreenSpaceOverlay라 `Unity_Camera_Capture`·`Capture2DScene` 어느 쪽에도 렌더되지 않는다. 검수 스크린샷은 `Root`를 임시 WorldSpace 캔버스에 복제해 찍고 복제본을 지웠다(원본 무접촉).

#### 파일 대응

| 산출 | 경로 | 상태 |
|---|---|---|
| 보상 창구 · `RankRewardInfo` · `ERankRewardState` | `OutGame/Rank/RankRewardManager.cs` | ✅ 신규 |
| 수령 커서 | `OutGame/Save/2.Domain/RankSaveData.cs` (`claimedCount`) | ✅ 필드 추가 |
| 보상 테이블 | `OutGame/Rank/RankConfig.cs` (`RankGradeConfig.rewardGold` + `rewardGoldPerDivision`) | ✅ 필드 추가 |
| SO 주입 | `Utils/DataLibrary.cs` 1줄 | ✅ |
| 패널 · 행 · 수령 팝업 | `UI/Rank/RankRewardPanel.cs` · `RankRewardRowView.cs` · `RankRewardClaimPopup.cs` | ✅ 신규 |
| 승급 씬 캐리어 · `RankApplyResult` | `OutGame/Rank/RankUpHandoff.cs` · `OutGame/Rank/RankManager.cs`(struct 추가 + 반환 타입 승격) | ✅ 신규 (2026-07-29) |
| 씬 노드 · 행 프리팹 | `LobbyScene.unity`(`RankRewardOverlay`) · `Assets/Assets/Prefabs/UI/RankUI/RankRewardRow.prefab` | ✅ 저작 완료 (Addressable 등록 없음 — 풀드 UI가 아니다) |

- ✅ 해소: 수령 팝업 취소 경로(`PopupDim` Button → `Hide()`, 코드 수정 0) · 중복 `RankHud`(제거 완료).
- 미해결(후속 후보): `HasAnyClaimable` 소비처(진입 버튼 알림점 — 갱신할 런타임 스크립트가 없어 이번 스코프 밖) · 스크롤 위치가 행 인덱스 비율 근사(행 높이 미반영) · `RankConfig`가 두 static 캐시에 이중 보관(부트가 둘 다 주입해 현재 실해 없음) · 등급 배지 아트 미저작 → 행 프리팹의 별 아이콘 1종이 전 티어 공통.
  - ✅ 목록에서 제거: `RankConfig.asset`의 `rewardGold` YAML 미저작 — 등급 재설계 때 `grades` 5행을 직접 저작하며 해소(위 함정 표 참조).
