# 기능 지도 (자체 코드 `Assets/Scripts/`)

경로는 `Assets/Scripts/` 기준. 줄번호 없음 — 타입·메서드 이름으로 `rg` 해서 확인하고 진행한다.
자체 코드 규모는 아래 자동 생성 블록을 기준으로 본다. `Photon/`, `AmplifyShaderEditor/`, `PurchasedAssets/`, `Plugins/`, `GUIPackCartoon/` 는 서드파티라 여기 없다.
전체 파일 목록만 필요하면 Glob 또는 rg --files로 현재 상태를 조회한다.

<!-- orch:feature-map-sync:start -->
<!-- orch:source files=433 public-types=527 unmapped-public-types=0 auto-draft-types=3 -->
<!-- orch:emptied-bullets=0 -->
<!-- orch:feature-map-sync:end -->

## 전투 — 턴·공격 순서 (`Battle/`, 53파일 8,129줄)

- 턴 루프: `TurnRunner` · `TurnBase` · `PlayerTurn` · `EnemyTurn` (`EnemyAi`) · `TurnContext` · `TurnState` (`InputGesture`) · `TurnEvents` · `TurnThinkTimer`
- 공격 실행: `AttackProcessor` · `AttackSequence` · `AttackFlow` (BeforeAttack / Attacked / AfterAttack 훅) · `Battle/Attack/AttackPreview` · `Battle/Attack/AttackResult`
- 훅 컨텍스트 (`BattleTimings` 안 struct): `BeforeAttackCtx` · `AttackedCtx` · `DamageDealtCtx` · `DeathCtx` · `SwapOutCtx` · `AfterAttackCtx` · `TurnCtx` · `BoardCtx` · `SpawnCtx` · `DeckCtx`
- 효과 훅 베이스: `BattleEffect` (`BattleEffect.OnLethal` / `BattleEffect.OnRemoved`) — 시너지·패시브가 여기 붙는다
- 보드·대상: `BattleField` · `TargetFilter` · `BattleResultBeat` · 공격 튜닝 `NormalTuning` · `PeerlessTuning`
- 규칙·판정: `BattleRules` · `ExecutionRule` · `BattleOverForecast` · `BattleFinisher` · `BattleCleanup`
- AI 카드 레벨 배선: `GameInitializer.EnemyGrowthProvider` · `GameInitializer.BaseGrowthProvider` · `GameInitializer.GrowthAtLevelProvider` 를 `Core/BootInstaller` 가 주입한다 — 실제 레벨은 `RankManager.AiCardLevelOf` / `RankConfig.AiCardLevelAt`, 스탯은 `CardGrowthManager.GrowthAtLevel` · `CardGrowth.BaseLevel`
- 초기화·셔플: `GameInitializer` · `ShufflePolicy` · `MulliganPhase` · `MatchSeeding` · `MatchRandom` · `DeckConfig` · `AIDeckConfig` (`DeckEntry`)
- 타이밍: `Battle/Timing/BattleTimingConfig` · `Battle/Timing/GameTiming` · `BattleTimings`
- 사망·처형 연출: 길이 단일 지점 `Battle/Timing/BattleTimingConfig.DeathDuration` (내부 박자는 전부 이 값 안에서 끝난다) · 애니 `UI/Battle/CardAnimator.PlayDeathAnim` · 시퀀스 `AttackSequence.PlayVictimDeaths` · 처형 `ExecutionRule` · `ExecutionVfx` · 사운드 `Audio/SoundManager.PlayDeath` · `SoundManager.PlayDeathVoice` · 미리보기 플래그 `UI/Battle/BattleUxFlags.DeathPreview` · 사망 트리거 `SynergyTriggers.Lethal` · `SynergyTriggers.Removed`
- 연출: `BattleIntro` · `BattleVfx` · `BattleVfxId` · `CardAppearVfx` · `CardAppearSequence` · `HitImpact` · `HealVfx` · `ExecutionVfx` · `Battle/Cinematic/CardCinematicRules` (`CinemaAttackStyle`) · 에너지 구체 돌진 `AttackSequence.EnergyOrbDash` (프리팹은 `BattleVfxLibrary` 의 `BattleVfxId.CinemaEnergyOrb`, 등장 연출 `CardAppearVfx` 와 구체를 공유 — `CardData.cinemaAttackStyle` 한 축으로 묶여 따로 못 끈다) · 재생 `UI/Cinematic/CardCinematicPlayer`

## 시너지 (`Battle/Synergy/`, 15파일 995줄)

- 한글 시너지명 ↔ 코드 이름: 덩치=Bulk · 돌보미=Caretaker · 포식자=Predator · 흐름=Flow · 유산=Legacy · 수호자=Guardian · 비늘=Scale · 낙인=Brand · 언데드=Undead. 데이터는 Assets/SO/Synergies/Data_Synergy_\*.asset (지도 범위 밖), 스탯형(덩치·비늘)은 `StatSynergyEffect` 가 처리한다
- 적용 지점: `SynergyApplier.ApplyAll` — 공격 계산에는 `CardInstance.AttackDamage` / `CardInstance.ApplySynergy` 경유로 반영
- 트리거: `SynergyTriggers` (DamageDealt / Attacked / Lethal / SwappedOut)
- 해석·진행도: `SynergyResolver` · `SynergyProgress` · `ActiveSynergy` · `SynergyText`
- 개별 효과: `StatSynergyEffect` · `FlowSynergyEffect` · `BrandSynergyEffect` · `GuardianSynergyEffect` · `CaretakerSynergyEffect` · `PredatorSynergyEffect` · `UndeadSynergyEffect` · `LegacySynergyEffect` (왕관 스택 연출 `LegacyCrownVfx` · 배선 `LegacySynergyVfxConfig`) (공통 `SynergyEffect`)
- 엠블럼·연출: `SynergyEmblemSpec` · `SynergyEmblemVfx` · `SynergyEmblemEntry` · `SynergyVfx` · `PopEmblem` · `RiseAndShakeEmblem` · `DropAndShineEmblem` · `StackUpEmblem` · `PrefabEmblem` · `ParticleEmblem` · `JointGap`
- Vfx 설정·라이브러리: `SynergyVfxConfig` · `FlowSynergyVfxConfig` · `SwarmSynergyVfxConfig` · `EmblemOnlySynergyVfxConfig` · `BattleVfxLibrary` · `VfxEntry` · `VfxHandle` · `VfxStrengthScaler` · `CunningVfx` · `SwarmVfx`
- 회복: `HealerEffect` · `HealVfx` — 트레일 잔상 `BattleTimingConfig.HealTrailLinger`
- 상태 보관: `SynergyState` · `SynergyPreview`
- UI: `UI/Battle/FieldSynergyPanel` · `UI/Battle/SynergyIconView` · `UI/Battle/SynergyBadgeView` · `UI/Keyword/SynergyIconStrip` · `SynergyIconButton` · `UI/Keyword/SynergyExplainPopupUI` (`SynergyExplainData`) · 키워드쪽 `KeywordExplainPopupUI` (`KeywordExplainData` · `KeywordExplainItem`) · `KeywordIconButton`

## 카드 (`Card/`, 18파일)

- 런타임 인스턴스: `CardInstance` (스탯·데미지 계산의 단일 지점)
- 한글 키워드 ↔ `CardKeyword` 값: 원거리=Ranged · 무쌍=Peerless · 처형=Execution · 도발=Taunt · 교활=Cunning · 표식=Mark · 힐러=Healer · 무적=Invincible · 추가생명력=BonusHp
- 키워드·패시브 기반: `CardKeyword` · `CardPassive` · `KeywordIconConfig` (`KeywordIcon` · `Entry`)
- 등급: `ECardGrade`
- 데이터 원본: `CardData` (`CardData.MaxEvolutionStage` · `CardData.arts` · `CardData.defaultEvolutionStage`) · `CardArtSet` · 주소 기반 로드 `CardArtCache` · `CardSpec` · 진화 단계는 `CardGrowth.EvolutionStage`
- 공격 이펙트 데이터: `AttackEffect` 안 `ParticleEntry` · `ParticleTiming` · `ParticleSpawnTarget` · `ProjectileData`
- 시너지 데이터: `SynergyData` · `SynergyTier` · `SynergyEmblemScope`
- 성장·표시 규칙: `CardGrowth` · `CardVisualRules` (`GlowSpec`) · `AttackEffect` · `ECardChannel` · `SynergyEmblemTiming`

## 멀티플레이 (`Network/`, 6파일 1,137줄)

- 세션·제어: `NetworkSession` · `NetworkGameController` · `CardRegistry`
- 턴 동기화: `MultiplayerTurnRunner` · `MultiplayerPlayerTurn` · `MultiplayerOpponentTurn`
- 메시지 처리: `NetworkGameController.HandleMessage` — `NetworkSession` 이 Photon 콜백을 여기로 넘긴다
- 서드파티 Photon Fusion 은 `Assets/Photon/` (지도 범위 밖)

## 아웃게임 세이브 (`OutGame/Save/`, 14파일)

3계층 구조다.

- 저장소: `OutGame/Save/1.Repository/IRepository` · `JsonFileRepository` · `PlayerPrefsRepository`
- 도메인: `OutGame/Save/2.Domain/UserSaveData` (루트) 아래 `CurrencySaveData` · `OwnershipSaveData` · `DeckSaveData` (`DeckSlotSaveData`) · `CardGrowthSaveData` (`CardGrowthEntry`) · `KeywordGrowthSaveData` · `RankSaveData` · `AlbumRewardSaveData` · `TutorialSaveData`
- 매니저: `OutGame/Save/3.Manager/DataSaveManager` (`DataSaveManager.Load` / `DataSaveManager.Save` / `DataSaveManager.Data`) — 각 기능 매니저가 여기로 flush 한다

## 재화·보상 (`OutGame/Currency/`, `OutGame/Reward/`, `Utils/`)

- 재화: `CurrencyManager.Init` / `CurrencyManager.Save` · `ECurrencyType` · `CurrencyGainBucket` (`CurrencyGain`) · `RewardLine`
- 전투 보상: `BattleReward` · `BattleRewardHandoff`
- 지급 서비스: `Utils/RewardService`
- UI: `UI/HUD/CurrencyHud` · `ContextCurrencySlot` · `CurrencyLook` · `UI/Common/CurrencyGainEffectPlayer` · `UI/Common/CurrencyRewardSlotView` · `UI/Common/RewardClaimPopup` · `UI/Common/CoinBurstEffect`

## 카드팩 (`OutGame/CardPack/`, `UI/Shop/`)

- 추첨 로직: `CardPackOpener.TryPurchase` / `CardPackOpener.PickWeighted` · `CardPackData.ResolvePool` · `PackOdds` (`PackOddsEntry`) · `PackSpec` · `EPackOpenResult` · `WeightedCard` · `RankPackPool` · `DrawnCard` · `OpenedPack`
- 결과 전달: `PackHandoff` · `CardPackRewardHandoff`
- 연출 UI: `UI/Shop/PackRevealView` (`PackRevealView.BeginOpen` 이후 Entering→Swipe→Shifting→Tearing→Pulling→Flicking→Summary 상태 진행) · `PackCardView` · `PackCardStack` · `PackResultGrid`
- 진입·제어: `UI/Shop/PackAcquireController` · `PackOpenOverlay` · `PackShowcaseController` · `PackCarouselView` · `PackCarouselDotsView` · `PackOddsPopup` (`PackOddsData` · `PackOddsRow`) · `PackStandaloneBoot`
- 연출 소품: `PackTearSkin` · `PackTearHandle` · `PackShellRig` · `PackIdleMotion` · `PackScreenFlash` · `PackSpecularSweep` · `PackPurchaseImpact`

## 컬렉션·소유·도감 (`OutGame/Card/`, `OutGame/Album/`)

- 카탈로그·소유: `CardCatalog` · `OwnershipManager`
- 테마: `AlbumTheme` · `AlbumThemeDef` · `AlbumSection` · UI `UI/Album/AlbumThemeCellView`
- 도감: `CardAlbum` · `AlbumPage` · `AlbumTheme` · `CardAlbumConfig` · `AlbumRewardManager` · `AlbumRewardInfo` · `AlbumPageDef` · `AlbumRewardDef` · `EAlbumRewardState`
- 카드 상세(`UI/CardDetail/`): `CardDetailOverlayView` (최대 UI 파일) · `CardDetailOpenOptions` · 키워드 데모 `KeywordDemoConfig` · `KeywordDemoStage` · 해금 연출 `SectionRevealFx` · `SectionUnlockFx` · `UnlockIntro` · `UnlockIntroOverlay` · `UnlockIntroRow`
- 도감 UI: `UI/Album/AlbumPageOverlayView` · `AlbumPageFlipView` · `AlbumTabController` · `AlbumChestView` · `AlbumRewardClaimFlow` · `UI/Album/Insert/AlbumInsertSession` · `AlbumInsertCardDragger` · `AlbumInsertPlan` · `AlbumInsertQueue` · `AlbumInsertStep` · `AlbumInsertFanfareFx` · `AlbumInsertHintView` · `AlbumInsertMask` · `AlbumSleeveView` · `UI/Album/AlbumCardSlotView` · `AlbumGaugeView`

## 덱 (`OutGame/Deck/`, `UI/Deck/`, `UI/MainMenu/`)

- 저장·구성: `DeckSaveManager` · `StarterDeck` · `DeckPower`
- 편집 UI: `UI/Deck/DeckEditController` · `DeckEditDragController` · `DeckEditCollectionGrid` · `DeckEditSlotView` · `DeckListController` · `DeckTabController` · `DeckEditCardTile` · `DeckSlotView` · 이미지 `DeckImageCatalog` · `DeckImages` · `UI/DeckSelectPopup`
- 빌더: `DeckSynergyStrip` · `MainMenuManager` · `SynergyCountIcon` · `SynergyTooltip` · `SceneTransitionVideo`

## 성장·강화 (`OutGame/Growth/`, `UI/Growth/`)

- 로직: `CardGrowthManager` · `CardGrowthConfig` (`GrowthLevelStep` · `GrowthStep`) · `KeywordGrowthManager` · `KeywordGrowthConfig` · `EEnhanceOutcome` · `EnhanceResult`
- 연출: `UI/Growth/CardEvolveRitualView` · `CardEnhanceRitualView` · `CardGrowthRitualView` · `EnhanceResultPanelView` · `EnhanceRitualHandoff` · `KeywordGrowthPanel` · `CardEvolveEmblems` · `CardEvolveRays` · `CardEnhanceEmbers` · `CardEnhanceHalo` · `CardEnhanceShading` · `EnhanceResultLine` · `KeywordGrowthCellView`

## 랭크·매치메이킹 (`OutGame/Rank/`, `OutGame/Match/`)

- 랭크 데이터: `ERankGrade` · `RankGradeConfig` · `RankTier` · `RankInfo` · `RankApplyResult` · `RankRewardDef` · `ERankRewardState` · `RankRewardInfo`
- 랭크: `RankManager` · `RankConfig` · `RankRewardManager` · `RankResultHandoff` · UI `UI/HUD/RankHud` · `UI/Rank/RankRewardPanel` · `RankRewardRowView` · `UI/Common/LobbyEntryAlertDot` (`EAlertDotTarget`) · `UI/HUD/RankStarGauge` (`Star`) · `RankPromoStandby` · `UI/Lobby/RankPromoteOverlay` · `RankProgressGauge`
- 매칭: `IMatchmaker` · `FakeMatchmaker` · `MatchProfile` · `MatchOpponent` · `OpponentProfilePool` · `MatchOpponentHandoff`
- 매칭 UI: `UI/Match/MatchmakingShell` · `MatchDeckShell` · `MatchDeckStripController` · `MatchDeckPanelView` · `MatchProfileView` · 연출 `MatchDeckIntroFx` · `MatchHandoffFx` · `MatchmakingFx` · `MatchHandoffTargets` · `UI/MainMenu/RandomMatchPanel` · `MultiplayerLobbyPanel`

## 보상 토너먼트 (`OutGame/Tournament/`, `UI/Tournament/`)

- 진행도 단일 창구: `TournamentProgress` (정점 해금 판정 · 클리어 지급 · 낙인, `TournamentProgress.OnChanged` 로 맵 갱신 통지)
- 데이터: `TournamentConfig` · `TournamentNodeDef` · `ETournamentNodeState` · 검증 `TournamentValidator`
- 전투 연결: `Battle/TournamentRun` · 결과 전달 `TournamentResultHandoff`
- 저장: `OutGame/Save/2.Domain/TournamentSaveData`
- UI: `UI/Tournament/TournamentMapOverlayView` · `TournamentNodeView` · `TournamentRewardFlow` · `TournamentReturnFlow`

## 튜토리얼 (`OutGame/Tutorial/`, `UI/Tutorial/`)

- 실행: `OutgameTutorialRunner` · `TriggeredTutorialRunner` · `OutGame/Tutorial/Steps/TutorialStepExecutor` · `TutorialStepDef` · `EOutgameTutorialAction` · `EOutgameTutorialCompletion` · `EOutgameTutorialFailure` · `EOutgameTutorialStepResult` · `OutgameTutorialStepContext`
- 진행·잠금: `OutgameTutorialProgress` · `OutgameFeatureLock` · `EOutgameFeature` · `ITutorialProgressSink` · `OutgameTutorialRewind`
- 앵커·데이터: `TutorialAnchorRegistry` · `TutorialAnchor` · `EOutgameTutorialAnchor` · `OutgameTutorialData` · `OutgameTutorialChapter` · `OutgameTutorialGuide` · `EOutgameTutorialTrigger` · `TriggeredTutorialData` (`TriggeredTutorialEntry`)
- UI: `UI/Tutorial/OutgameTutorialGateUI` · `OutgameTutorialBridge` · `TriggeredTutorialBridge` · `UI/TutorialOverlayUI` · `UI/TutorialSetupUI` · `UI/TutorialUIStyle` · `UI/Tutorial/TutorialAlertDot` · `UI/Input/TutorialTapCatcher`
- 전투 내 튜토리얼: `Battle/TutorialConfig` · `TutorialScenarioData` (`StepKind` · `BannerAnchor` · `CardFocusSide` · `ScriptedAttack`) · `TutorialStepGate` (`Side`)

## 전투 UI (`UI/Battle/`, 29파일 6,965줄)

- 입력·카드: `CardInputController` · `CardView` · `CardAnimator` · `CardFaceFlipper` · `CardDecorView` · `CardArmedVfx` · `WeaponAnimSpec` · `CardFadeAlpha`
- 보드·카메라: `BattleBoardView` · `BattleFieldView` · `BattleCamera` · `BattleCameraFit` · `BattleSelection`
- 턴 표시: `TurnBannerUI` · `TurnTimerUI` · `TurnSideTint` · `ActionPanel` · `CoinFlipUI` · `MulliganOverlayUI`
- 결과·기타: `GameResultPopup` · `DeckPileUI` · `EffectNotifyUI` · `SurvivorGoldFlight` · `BattleUxFlags`
- 감정표현(`UI/Battle/Emote/`): 표시 단일 창구 `EmoteDirector` (플레이어·AI·상대 클라가 모두 여기로) · 목록 `EmoteCatalog` (`EmoteEntry`) · 선택 표 `EmotePickerUI` · 스티커 `EmoteStickerView` · `ScreenDim` · `EDimLayer` · `KeywordFrame` · `WeaponAnimSpec` · `EffectNotifyData`

## 공용 UI·연출 (`UI/Common/`, `UI/UIManager/`, `UI/Lobby/`)

- 공통 뷰: `CardVisualView` · `CardSynergyBadgeView` · `CardKeywordIconView` · `CardPressRelay` · `FeatureLockView` · `AlertDotView`
- 전환·커버: `CurtainView` · `ICurtainSwap` · `LoadingCoverView` · `ScreenFlashCover` · `ScreenDimTint` · `UI/ScreenCoverBackground` · `ScreenFillRect` · `StickerPeelGraphic` · `PopupTransition` · `RetractingPanels` · `SceneLoadSwap` · `ScreenFlash` · `PageRollGraphic` (`RollFace`)
- 이펙트: `RewardRevealFx` · `CardGainFlightEffect` · `UiGainBurst` · `UiConfettiBurst` · `UiLightStreak` · `UiPunch` · `UiCrumble` · `UiAdditive` · `UiGrayscale` (`Toned`) · `UiRectCapture` · `UiConfettiBurst.Settings`
- 정렬 층(무엇이 무엇 위에 뜨는가): `UiSortingOrder` — 프리팹 저작값·코드 상수 모두 이 표를 따른다. 순서를 런타임에 재지 않는다
- 레이아웃: `SafeAreaFitter` · `PopupPlacer` · `GridRatioFitter` · `UniformFitContent` · `CardAutoScale` () · `UI/SettingsPanel`
- 풀·매니저: `UI/UIManager/UIPoolManager` · `PooledCardElement` (`PooledCardElementData`) · `PooledUIBase` (`UIData`) · `UIAnimator` · `IUIController` · `SimpleYNPopup` (`SimpleYNPopupData`) · 상시 오버레이 `SingletonOverlay` · `SingletonOverlayBase` · `RuntimeOverlayPrefabs` · `SyncUiPrefabCatalog` (`ESyncUiPrefab` · `SyncUiPrefabs`)
- 로비: `UI/Lobby/LobbyTabController` · `LobbyTabServices` · `LobbyTabBarView` · `LobbyTabPanel` (`Tab`) · `LobbyMatchTabPanel` · `LobbyOverlayHost` · `LobbyShellBars` (`EShellBars`) · `LobbySettingsButton` · `ScrollingUvBackground` · `TabButtonView` · `LobbyMatchLauncher` · `LobbyGainEffectDirector` · `LobbyRankEffectDirector` · `CardRewardOverlay` · `CardSetRewardOverlay` · `PackRewardOverlay` · `EPromoteKind`
- 입력 제스처: `UI/Input/HorizontalSwipeDetector` · `LongPressDetector` · `SwipeThroughScrollRect` · `SwipeGuide` · `HintArrow`

## 부트·코어·유틸

- 기동: `Core/BootInstaller` · `Core/GameManager` · `EContentRunMode` · `Core/ContentProfileConfig` · `Core/Rendering/ScreenBlurFeature`
- 데이터·풀: `Utils/DataLibrary` · `ObjectPooler` · `ParticlePooler` · `PooledParticle` · `LogUtil` · `CameraUtil` · `KoreanText` · `EdgeShadeSprite` · `ShineBandSprite`
- 사운드: `Audio/SoundManager` · `SoundConfig` · `UIClickSound`
- 디버그: `OutGame/Debug/OutgameDebugOverlay` · `OutgameDebugActions` · `UI/Debug/DebugCurrencyButton` · `UnlockAllCardsButton` · `Test/BattleDebugKill` · `Test/VfxDebugWindow` (`VfxSlot`) · `Test/AttackAnimTester` (`AttackStep`) · `SynergyPreviewKind` · `KeywordPreviewKind`

## 에디터 도구 (`Editor/`, 19파일 3,585줄)

`CardTableTool` · `CardAuthoringWindow` · `CardSpecImporter` · `CardDetailChipBaker` · `ReleaseManagerWindow` · `ContentProfileValidator` · `ContentProfileMenu` · `TutorialAuthoringWindow` · `TutorialStepDefDrawer` · `AttackAnimTesterEditor` · `ContentRunModeEditor` · `FlowWavePrefabBuilder` · `WaveMeshBuilder` · `SafeAreaInstaller` · `UiSpriteAnimationClipCreator` · `UiSpriteAnimationClipWriter`

<!-- orch:auto-draft:start -->
## 미분류 자동 초안 (섹션으로 옮기면 다음 동기화에서 빠집니다)

- `Battle/` — `AttackerTier`
- `Card/` — `GrowthStar`
- `OutGame/Growth/` — `LimitBreakStep`
<!-- orch:auto-draft:end -->
