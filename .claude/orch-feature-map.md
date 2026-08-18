# 기능 지도 (자체 코드 `Assets/Scripts/`)

경로는 `Assets/Scripts/` 기준. 줄번호 없음 — 타입·메서드 이름으로 `rg` 해서 확인하고 진행한다.
자체 코드 규모는 아래 자동 생성 블록을 기준으로 본다. `Photon/`, `AmplifyShaderEditor/`, `PurchasedAssets/`, `Plugins/`, `GUIPackCartoon/` 는 서드파티라 여기 없다.
전체 파일 목록만 필요하면 Glob 또는 rg --files로 현재 상태를 조회한다.

<!-- orch:feature-map-sync:start -->
<!-- orch:source files=394 public-types=483 unmapped-public-types=170 -->
<!-- orch:emptied-bullets=1 -->
<!-- orch:missing-dir OutGame/Collection/ since=2026-08-14 -->
<!-- orch:feature-map-sync:end -->

## 전투 — 턴·공격 순서 (`Battle/`, 53파일 8,129줄)

- 턴 루프: `TurnRunner` · `TurnBase` · `PlayerTurn` · `EnemyTurn` · `TurnContext` · `TurnState` (`InputGesture`) · `TurnEvents` · `TurnThinkTimer`
- 공격 실행: `AttackProcessor` · `AttackSequence` · `AttackFlow` (BeforeAttack / Attacked / AfterAttack 훅) · `Battle/Attack/AttackPreview` · `Battle/Attack/AttackResult`
- 훅 컨텍스트 (`BattleTimings` 안 struct): `BeforeAttackCtx` · `AttackedCtx` · `DamageDealtCtx` · `DeathCtx` · `SwapOutCtx` · `AfterAttackCtx` · `TurnCtx` · `BoardCtx` · `SpawnCtx` · `DeckCtx`
- 효과 훅 베이스: `BattleEffect` (`BattleEffect.OnLethal` / `BattleEffect.OnRemoved`) — 시너지·패시브가 여기 붙는다
- 보드·대상: `BattleField` · `TargetFilter` · `BattleResultBeat` · 공격 튜닝 `NormalTuning` · `PeerlessTuning`
- 규칙·판정: `BattleRules` · `ExecutionRule` · `BattleOverForecast` · `BattleFinisher` · `BattleCleanup`
- 초기화·셔플: `GameInitializer` · `ShufflePolicy` · `MulliganPhase` · `MatchSeeding` · `MatchRandom` · `DeckConfig` · `AIDeckConfig` (`DeckEntry`)
- 타이밍: `Battle/Timing/BattleTimingConfig` · `Battle/Timing/GameTiming` · `BattleTimings`
- 사망·처형 연출: 길이 단일 지점 `Battle/Timing/BattleTimingConfig.DeathDuration` (내부 박자는 전부 이 값 안에서 끝난다) · 애니 `UI/Battle/CardAnimator.PlayDeathAnim` · 시퀀스 `AttackSequence.PlayVictimDeaths` · 처형 `ExecutionRule` · `ExecutionVfx` · 사운드 `Audio/SoundManager.PlayDeath` · `SoundManager.PlayDeathVoice` · 미리보기 플래그 `UI/Battle/BattleUxFlags.DeathPreview` · 사망 트리거 `SynergyTriggers.Lethal` · `SynergyTriggers.Removed`
- 연출: `BattleIntro` · `BattleVfx` · `BattleVfxId` · `CardAppearVfx` · `CardAppearSequence` · `HitImpact` · `HealVfx` · `ExecutionVfx` · `Battle/Cinematic/CardCinematicRules` (`CinemaAttackStyle`) · 재생 `UI/Cinematic/CardCinematicPlayer`

## 시너지 (`Battle/Synergy/`, 15파일 995줄)

- 적용 지점: `SynergyApplier.ApplyAll` — 공격 계산에는 `CardInstance.AttackDamage` / `CardInstance.ApplySynergy` 경유로 반영
- 트리거: `SynergyTriggers` (DamageDealt / Attacked / Lethal / SwappedOut)
- 해석·진행도: `SynergyResolver` · `SynergyProgress` · `ActiveSynergy` · `SynergyText`
- 개별 효과: `StatSynergyEffect` · `FlowSynergyEffect` · `SwarmSynergyEffect` · `RampartSynergyEffect` · `CaretakerSynergyEffect` · `CleanerSynergyEffect` · `UndeadSynergyEffect` · `LegacySynergyEffect` (공통 `SynergyEffect`)
- 엠블럼·연출: `SynergyEmblemSpec` · `SynergyEmblemVfx` · `SynergyEmblemEntry` · `SynergyVfx` · `PopEmblem` · `RiseAndShakeEmblem` · `DropAndShineEmblem` · `StackUpEmblem` · `PrefabEmblem` · `JointGap`
- Vfx 설정·라이브러리: `SynergyVfxConfig` · `FlowSynergyVfxConfig` · `SwarmSynergyVfxConfig` · `EmblemOnlySynergyVfxConfig` · `BattleVfxLibrary` · `VfxEntry` · `VfxHandle` · `VfxStrengthScaler` · `CunningVfx` · `SwarmVfx`
- 회복: `HealerEffect` · `HealVfx` — 트레일 잔상 `BattleTimingConfig.HealTrailLinger`
- 상태 보관: `SynergyState` · `SynergyPreview`
- UI: `UI/Battle/FieldSynergyPanel` · `UI/Battle/SynergyIconView` · `UI/Battle/SynergyBadgeView` · `UI/Keyword/SynergyIconStrip` · `SynergyIconButton` · `UI/Keyword/SynergyExplainPopupUI` (`SynergyExplainData`) · 키워드쪽 `KeywordExplainPopupUI` (`KeywordExplainData` · `KeywordExplainItem`) · `KeywordIconButton`

## 카드 (`Card/`, 18파일)

- 런타임 인스턴스: `CardInstance` (스탯·데미지 계산의 단일 지점)
- 키워드·패시브: `CardKeyword` · `CardPassive` · `KeywordIconConfig` (`KeywordIcon` · `Entry`) · `Card/Passives/` 의 챔피언별 패시브 9종 (`AatroxPassive`, `FizzPassive`, `GwenPassive`, `KindredPassive`, `MaokaiPassive`, `OrnnPassive`, `PoppyPassive`, `RammusPassive`, `TeemoPassive`)
- 데이터 원본: `CardData` (`CardData.MaxEvolutionStage` · `CardData.evolvedArts` · `CardData.defaultEvolutionStage`) · `CardArtSet` · 진화 단계는 `CardGrowth.EvolutionStage`
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
- 도메인: `OutGame/Save/2.Domain/UserSaveData` (루트) 아래 `CurrencySaveData` · `OwnershipSaveData` · `DeckSaveData` (`DeckSlotSaveData`) · `CardGrowthSaveData` (`CardGrowthEntry`) · `KeywordGrowthSaveData` (`KeywordGrowthEntry`) · `RankSaveData` · `AlbumRewardSaveData` · `TutorialSaveData`
- 매니저: `OutGame/Save/3.Manager/DataSaveManager` (`DataSaveManager.Load` / `DataSaveManager.Save` / `DataSaveManager.Data`) — 각 기능 매니저가 여기로 flush 한다

## 재화·보상 (`OutGame/Currency/`, `OutGame/Reward/`, `Utils/`)

- 재화: `CurrencyManager.Init` / `CurrencyManager.Save` · `ECurrencyType` · `CurrencyGainBucket` (`CurrencyGain`) · `RewardLine`
- 전투 보상: `BattleReward` · `BattleRewardHandoff`
- 지급 서비스: `Utils/RewardService`
- UI: `UI/HUD/CurrencyHud` · `UI/Common/CurrencyGainEffectPlayer` · `UI/Common/CurrencyRewardSlotView` · `UI/Common/RewardClaimPopup` · `UI/Common/CoinBurstEffect`

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
- 도감 UI: `UI/Album/AlbumPageOverlayView` · `AlbumPageFlipView` · `AlbumTabController` · `AlbumChestView` · `AlbumRewardClaimFlow` · `UI/Album/Insert/AlbumInsertSession` · `AlbumInsertCardDragger` · `AlbumInsertPlan` · `AlbumInsertQueue` · `AlbumInsertStep` · `AlbumInsertHintView` · `AlbumInsertMask` · `AlbumSleeveView` · `UI/Album/AlbumCardSlotView` · `AlbumGaugeView`

## 덱 (`OutGame/Deck/`, `UI/Deck/`, `UI/MainMenu/`)

- 저장·구성: `DeckSaveManager` · `StarterDeck` · `DeckPower`
- 편집 UI: `UI/Deck/DeckEditController` · `DeckEditDragController` · `DeckEditCollectionGrid` · `DeckEditSlotView` · `DeckListController` · `DeckTabController` · `DeckEditCardTile` · `DeckSlotView` · 이미지 `DeckImageCatalog` · `DeckImages` · `UI/DeckSelectPopup`
- 빌더: `UI/MainMenu/DeckBuilderUI` · `DeckSynergyStrip` · `DeckGroup` · `MainMenuManager` · `GameReadyPanel` · `SynergyCountIcon` · `SynergyTooltip` · `SceneTransitionVideo`

## 성장·강화 (`OutGame/Growth/`, `UI/Growth/`)

- 로직: `CardGrowthManager` · `CardGrowthConfig` (`GrowthLevelStep` · `GrowthStep`) · `KeywordGrowthManager` · `KeywordGrowthConfig` · `EEnhanceOutcome` · `EnhanceResult`
- 연출: `UI/Growth/CardEvolveRitualView` · `CardEnhanceRitualView` · `CardGrowthRitualView` · `EnhanceResultPanelView` · `EnhanceRitualHandoff` · `KeywordGrowthPanel` · `CardEvolveEmblems` · `CardEvolveRays` · `CardEnhanceEmbers` · `CardEnhanceHalo` · `CardEnhanceShading` · `EnhanceResultLine` · `KeywordGrowthCellView`

## 랭크·매치메이킹 (`OutGame/Rank/`, `OutGame/Match/`)

- 랭크 데이터: `ERankGrade` · `RankGradeConfig` · `RankTier` · `RankInfo` · `RankApplyResult` · `RankRewardDef` · `ERankRewardState` · `RankRewardInfo`
- 랭크: `RankManager` · `RankConfig` · `RankRewardManager` · `RankResultHandoff` · UI `UI/HUD/RankHud` · `UI/Rank/RankRewardPanel` · `RankRewardRowView` · `RankRewardAlertDot` · `UI/HUD/RankProgressArc` · `RankProgressGauge` · `RankPromoteEffect`
- 매칭: `IMatchmaker` · `FakeMatchmaker` · `MatchProfile` · `MatchOpponent` · `OpponentProfilePool` · `MatchOpponentHandoff`
- 매칭 UI: `UI/Match/MatchmakingShell` · `MatchDeckShell` · `MatchDeckStripController` · `MatchDeckPanelView` · `MatchProfileView` · 연출 `MatchDeckIntroFx` · `MatchHandoffFx` · `MatchmakingFx` · `MatchHandoffTargets` · `UI/MainMenu/RandomMatchPanel` · `MultiplayerLobbyPanel`

## 튜토리얼 (`OutGame/Tutorial/`, `UI/Tutorial/`)

- 실행: `OutgameTutorialRunner` · `TriggeredTutorialRunner` · `OutGame/Tutorial/Steps/TutorialStepExecutor` · `TutorialStepDef` · `EOutgameTutorialAction` · `EOutgameTutorialCompletion` · `EOutgameTutorialFailure` · `EOutgameTutorialStepResult` · `OutgameTutorialStepContext`
- 진행·잠금: `OutgameTutorialProgress` · `OutgameFeatureLock` · `EOutgameFeature` · `ITutorialProgressSink` · `OutgameTutorialRewind`
- 앵커·데이터: `TutorialAnchorRegistry` · `TutorialAnchor` · `EOutgameTutorialAnchor` · `OutgameTutorialData` · `OutgameTutorialChapter` · `OutgameTutorialGuide` · `EOutgameTutorialTrigger` · `TriggeredTutorialData` (`TriggeredTutorialEntry`)
- UI: `UI/Tutorial/OutgameTutorialGateUI` · `OutgameTutorialBridge` · `TriggeredTutorialBridge` · `UI/TutorialOverlayUI` · `UI/TutorialSetupUI` · `UI/TutorialUIStyle` · `UI/Tutorial/TutorialAlertDot` · `UI/Input/TutorialTapCatcher`
- 전투 내 튜토리얼: `Battle/TutorialConfig` · `TutorialScenarioData` (`StepKind` · `BannerAnchor` · `CardFocusSide` · `ScriptedAttack`) · `TutorialStepGate` (`Side`)

## 전투 UI (`UI/Battle/`, 29파일 6,965줄)

- 입력·카드: `CardInputController` · `CardView` · `CardAnimator` · `CardFaceFlipper` · `CardDecorView` · `CardWeaponView` · `CardArmedVfx` · `CardFadeAlpha`
- 보드·카메라: `BattleBoardView` · `BattleFieldView` · `BattleCamera` · `BattleCameraFit` · `BattleSelection`
- 턴 표시: `TurnBannerUI` · `TurnTimerUI` · `TurnSideTint` · `ActionPanel` · `CoinFlipUI` · `MulliganOverlayUI`
- 결과·기타: `GameResultPopup` · `DeckPileUI` · `EffectNotifyUI` · `SurvivorGoldFlight` · `BattleUxFlags` · `ScreenDim` · `EDimLayer` · `KeywordFrame` · `WeaponAnimSpec` · `EffectNotifyData`

## 공용 UI·연출 (`UI/Common/`, `UI/UIManager/`, `UI/Lobby/`)

- 공통 뷰: `CardVisualView` · `CardSynergyBadgeView` · `CardKeywordIconView` · `FeatureLockView` · `AlertDotView`
- 전환·커버: `CurtainView` · `ICurtainSwap` · `LoadingCoverView` · `ScreenFlashCover` · `ScreenDimTint` · `PopupTransition` · `RetractingPanels` · `SceneLoadSwap` · `ScreenFlash` · `PageRollGraphic` (`RollFace`)
- 이펙트: `RewardRevealFx` · `CardGainFlightEffect` · `UiGainBurst` · `UiConfettiBurst` · `UiLightStreak` · `UiPunch` · `UiCrumble` · `UiAdditive` · `UiGrayscale` (`Toned`) · `UiRectCapture` · `UiConfettiBurst.Settings`
- 레이아웃: `SafeAreaFitter` · `PopupPlacer` · `GridRatioFitter` · `UniformFitContent` · `CardAutoScale` · `UI/CardElement` (`CardElementMod`) · `UI/SettingsPanel`
- 풀·매니저: `UI/UIManager/UIPoolManager` · `PooledCardElement` (`PooledCardElementData`) · `PooledUIBase` (`UIData`) · `UIAnimator` · `IUIController` · `SimpleYNPopup` (`SimpleYNPopupData`) · 상시 오버레이 `SingletonOverlay` · `SingletonOverlayBase` · `RuntimeOverlayPrefabs` · `SyncUiPrefabCatalog` (`ESyncUiPrefab` · `SyncUiPrefabs`)
- 로비: `UI/Lobby/LobbyTabController` · `LobbyTabBarView` · `LobbyTabPanel` (`Tab`) · `LobbyMatchTabPanel` · `LobbyOverlayHost` · `LobbyShellBars` (`EShellBars`) · `LobbySettingsButton` · `ScrollingUvBackground` · `TabButtonView` · `LobbyMatchLauncher` · `LobbyGainEffectDirector` · `LobbyRankEffectDirector` · `CardRewardOverlay` · `CardSetRewardOverlay`
- 입력 제스처: `UI/Input/HorizontalSwipeDetector` · `LongPressDetector` · `SwipeThroughScrollRect` · `SwipeGuide` · `HintArrow`

## 부트·코어·유틸

- 기동: `Core/BootInstaller` · `Core/GameManager` · `EContentRunMode` · `Core/ContentProfileConfig` · `Core/Rendering/ScreenBlurFeature`
- 데이터·풀: `Utils/DataLibrary` · `ObjectPooler` · `ParticlePooler` · `PooledParticle` · `LogUtil` · `CameraUtil` · `EdgeShadeSprite` · `ShineBandSprite`
- 사운드: `Audio/SoundManager` · `SoundConfig` · `UIClickSound`
- 디버그: `OutGame/Debug/OutgameDebugOverlay` · `OutgameDebugActions` · `UI/Debug/DebugCurrencyButton` · `UnlockAllCardsButton` · `Test/VfxDebugWindow` (`VfxSlot`) · `Test/AttackAnimTester` (`AttackStep`) · `SynergyPreviewKind` · `KeywordPreviewKind`

## 에디터 도구 (`Editor/`, 19파일 3,626줄)

`CardTableTool` · `CardAuthoringWindow` · `CardSpecImporter` · `CardDetailChipBaker` · `ReleaseManagerWindow` · `ContentProfileValidator` · `ContentProfileMenu` · `AIDeckBandValidator` · `OutgameTutorialStepWindow` · `TutorialStepDefDrawer` · `AttackAnimTesterEditor` · `LobbyLayoutAudit` · `LobbyPrefabOverrideAudit` · `AssetNamingMigration` · `ContentRunModeEditor` · `FlowWavePrefabBuilder` · `WaveMeshBuilder` · `FontCleanupTool` · `SafeAreaInstaller`
