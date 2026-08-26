using UnityEngine;
using UnityEngine.Serialization;

// 전역 부트 프리팹의 루트. 사본이 LoadingScene·LobbyScene 둘이라 먼저 깬 쪽이 부트를 선점한다
// (정상 경로는 로딩 씬이, 로비 단독 Play는 로비 사본이 맡는다).
// 세이브 로드·재화 캐싱 등 씬 오브젝트가 필요 없는 부트는 GameManager가 앱 시작 시 먼저 처리한다.
[DefaultExecutionOrder(-200)]
public class BootInstaller : MonoBehaviour
{
    // 카드 목록은 CardRegistry(SO)가 단일 진실원. 씬에 사본을 두면 카드 추가 시 한쪽만 갱신된다.
    [SerializeField] CardRegistry cardRegistry;
    // 카드 앨범(신규 도감) SO. 미배선(null)이면 CardAlbum이 빈 앨범(앨범도 저작물이라 자동 생성 fallback이 없다).
    [SerializeField] CardAlbumConfig albumConfig;
    // 재화 아이콘·표시명 표 SO. 미배선(null)이면 아이콘은 프리팹 그림 그대로, 이름은 코드 기본값으로 떨어진다.
    [SerializeField] CurrencyLook currencyLook;
    // 튜토리얼 스텝 시퀀스 SO. 로딩 씬이 첫 목적지를 판정하려면 부트 시점에 주입돼 있어야 한다.
    [SerializeField] OutgameTutorialData tutorialData;
    // 트리거 발화 튜토리얼 목록 SO(탭 첫 진입 등). 미배선(null)이면 트리거는 조용히 발화하지 않는다.
    [SerializeField] TriggeredTutorialData triggeredTutorialData;
    // 덱 대표 이미지 후보 SO. 미배선(null)이면 신규 덱이 이미지 키를 못 받고 표시가 첫 카드 아트로 떨어진다.
    [SerializeField] DeckImageCatalog deckImageCatalog;
    // 신규 유저에게 기본 지급할 스타터덱(CardPackData의 pool 6장을 고정 순서로 쓴다). 미배선(null)이면 지급을 건너뛴다.
    [SerializeField] CardPackData starterDeck;
    // 보상 토너먼트 경로 SO. 미배선(null)이면 정점이 0개라 토너먼트 진입이 열리지 않는다.
    [SerializeField] TournamentConfig tournamentConfig;
    // 카드 강화·진화 튜닝 SO. 미배선(null)이면 CardGrowthManager가 코드 기본식·기본 게이트로 동작한다.
    [SerializeField] CardGrowthConfig growthConfig;
    // 키워드 전역 강화 설정. 미배선 시 코드 기본값으로 동작한다.
    [SerializeField] KeywordGrowthConfig keywordGrowthConfig;
    // 프로필 아바타·프레임 표 SO. 미배선(null)이면 아바타·프레임 그림이 전부 프리팹 저작값 그대로 남는다.
    [SerializeField] ProfileConfig profileConfig;
    [FormerlySerializedAs("runtimeUiPrefabs")]
    [SerializeField] SyncUiPrefabCatalog syncUiPrefabs;

    static bool s_booted;
    static bool s_saveDependentInstalled;

    internal static bool IsSaveDependentInstalled => s_saveDependentInstalled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_booted = false;
        s_saveDependentInstalled = false;
    }

    void Awake()
    {
        // 두 번째 사본은 자식 매니저가 각자 자폭하기 전에 루트째로 걷어낸다(빈 루트가 씬에 남지 않게).
        if (s_booted)
        {
            Destroy(gameObject);
            return;
        }

        s_booted = true;
        DontDestroyOnLoad(gameObject);
        SyncUiPrefabs.SetSource(syncUiPrefabs);

        // 카드 마스터 단일 창구 주입 — 도감·소유권·덱 등 아웃게임 소비자가 안정 키로 조회.
        ContentProfileConfig t_profile = ContentProfileConfig.Active;
        var t_availableCards = new System.Collections.Generic.List<CardData>(
            cardRegistry.Available(t_profile.IncludeTestCards));
        CardCatalog.SetSource(t_availableCards);

        // 카드팩 스펙시트 선로드 — 팩 값(가격·장수·드롭)의 진실원. 지연 로드도 되지만 상점 진입 프레임에
        // 파싱이 걸리지 않게 여기서 당긴다. 드롭 조회가 CardCatalog를 읽으므로 SetSource 이후여야 한다.
        PackSpec.Init();

        // 보상 스펙시트 선로드 — 토너먼트·앨범 보상 값의 진실원. 파싱은 SpecSource가 이미 1회 했으므로
        // 여기서 드는 비용은 키 색인뿐이다.
        TournamentSpec.Init();
        AlbumSpec.Init();

        // 카드 앨범 주입 — lazy 빌드라 첫 Themes 접근 전에만 꽂히면 된다(빌드가 CardCatalog.IdOf를 읽는다).
        CardAlbum.SetSource(albumConfig);

        // 재화 그림 주입 — 조회는 lazy라 재화 UI가 처음 그려지기 전에만 꽂히면 된다.
        CurrencyLook.SetActive(currencyLook);

        // 토너먼트 경로 주입 — 정점 상태 조회가 이 애셋에서 나온다(미배선이면 정점 0개).
        TournamentProgress.SetConfig(tournamentConfig);

        // 프로필 주입 — Init은 세이브 슬롯을 읽으므로 InstallSaveDependent()로 미뤘다.
        ProfileManager.SetConfig(profileConfig);

        // 소유권 캐싱·최초 기본 지급 — CardCatalog 주입 이후여야 한다(기본 지급 fallback이 카탈로그를 읽음).
        // Live 카탈로그 밖의 레거시 소유 키가 정리된 뒤 튜토리얼 완료 여부를 판정한다.

        // 카드 성장 캐싱 — 세이브(DataSaveManager.Load)만 읽어 순서 무관하나, 곡선 조회가 Config를 쓰므로 주입이 먼저다.
        KeywordGrowthManager.SetConfig(keywordGrowthConfig);
        CardGrowthManager.SetConfig(growthConfig);

        // 전투에 성장값을 흘리는 유일한 배선. Battle이 OutGame을 참조하지 않도록 값 생산자를 부트가 꽂는다
        // (GameInitializer.GrowthProvider 주석이 지정한 자리). 캐시가 준비된 Init 뒤여야 첫 전투부터 반영된다.
        GameInitializer.GrowthProvider = CardGrowthManager.GrowthOf;

        // 표시용 해금 키워드도 같은 성장값에서 나온다. 이걸 안 꽂으면 아직 못 쓰는 키워드가
        // 도감·덱편집·정보창에 그대로 떠서 표시와 규칙이 갈라진다.
        CardVisualRules.UnlockedKeywordProvider = _card => CardGrowthManager.GrowthOf(_card).UnlockedKeywords;
        CardVisualRules.EvolutionStageProvider = _card => CardGrowthManager.GrowthOf(_card).EvolutionStage;

        // 싱글 AI 난이도. 랭크 티어가 정한 레벨을 같은 성장 곡선에 태운다 —
        // 체력뿐 아니라 키워드·시너지 해금까지 플레이어와 동일한 규칙으로 결정된다.
        // 레벨은 전투 시작 시점에 읽어야 한다(부트 때 굳히면 랭크가 올라도 난이도가 안 따라온다).
        // 레벨은 카드마다 다르다(티어 레벨이 기준값).
        // 토너먼트 정점은 난이도가 저작 고정이라 랭크 티어를 타지 않는다. 만렙 클램프를 여기서 다시 거는 이유:
        // 그 클램프가 RankManager.AiCardLevelOf 안에 있어서 이 우회로에는 따라오지 않는다(곡선 밖 레벨은 보너스가 멈춘다).
        GameInitializer.EnemyGrowthProvider = _card => CardGrowthManager.GrowthAtLevel(_card, EnemyCardLevelOf(_card));
        GameInitializer.EnemyTierProvider = () => RankManager.TierIndex;

        // 튜토리얼 전투용 미강화 기준값. 레벨은 바닥 고정이라 체력은 안 오르고 해금 게이트만 산다 —
        // 진행도(GrowthProvider)를 태우면 저작된 킬 수·턴 수가 깨지고, 아예 안 태우면 키워드가 전부 열린다.
        GameInitializer.BaseGrowthProvider = _card => CardGrowthManager.GrowthAtLevel(_card, CardGrowth.BaseLevel);
        GameInitializer.GrowthAtLevelProvider = CardGrowthManager.GrowthAtLevel;

        // 덱 복원은 세이브의 카드 키를 CardData로 재수화하므로, 카드 마스터 목록을 먼저 넘겨야 한다.
        // 이 호출이 없으면 세이브의 덱 카드가 복원되지 않고 슬롯이 무효가 된다.
        DeckSaveManager.SetCardRegistry(t_availableCards);

        // 덱 대표 이미지 후보 주입 — 신규 덱 저장 시 여기서 키를 뽑는다.
        DeckImages.SetSource(deckImageCatalog);

        // 덱이 하나도 없는 신규 유저에게 스타터덱 지급(카드 소유권 포함).
        // 소유권 캐시·덱 로드 이후여야 하고, 대표 이미지 키를 뽑으므로 DeckImages 주입보다도 뒤에 온다.
        // 튜토리얼 첫실행 판정은 GameManager.Boot(BeforeSceneLoad)에서 이미 끝났으므로 여기 지급이 스킵을 유발하지 않는다.

        // 주입은 멱등 — 씬 브리지가 같은 에셋을 다시 넣어도 조기 return한다.
        OutgameTutorialRunner.EnsureData(tutorialData);
        TriggeredTutorialRunner.EnsureData(triggeredTutorialData);

        // 세이브가 붙잡아 둔 스텝 번호로 좌표를 되찾는다 — 저작이 스텝을 끼워 넣거나 옮겼어도 같은 스텝에 선다.
        // 시퀀스를 읽어야 하므로 EnsureData 뒤, 좌표를 쓰는 아래 둘보다는 반드시 앞이다.

        // 대본 전투가 연 화면 안에서 앱이 닫혔으면 좌표를 그 전투 진입 스텝으로 되감는다(부트당 1회는 여기가 유일).
        // 디버그 되감기보다 앞이다 — 디버그가 찍은 좌표는 그대로 서야 한다.

        // 디버그 되감기 예약 소비(2단) — 좌표까지의 지급 재생. 시퀀스를 읽어야 하므로 EnsureData 뒤,
        // 덱·소유·카탈로그를 쓰므로 위 배선이 전부 끝난 이 자리다. 예약이 없으면 아무 일도 없다.
    }

    // 이번 전투에서 적 카드 한 장이 쓸 레벨. 토너먼트면 정점 저작값(만렙 클램프), 아니면 랭크 티어값.
    System.Collections.IEnumerator Start()
    {
        while (!PlayerSaveSync.IsGateComplete &&
               GameManager.BootState != EGameBootState.UpdateRequired &&
               GameManager.BootState != EGameBootState.RecoveryRequired)
        {
            yield return null;
        }

        if (GameManager.BootState == EGameBootState.UpdateRequired ||
            GameManager.BootState == EGameBootState.RecoveryRequired)
            yield break;

        try
        {
            InstallSaveDependent();
        }
        catch (System.Exception t_exception)
        {
            GameManager.MarkRecoveryRequired();
            Debug.LogException(t_exception);
        }
    }

    void InstallSaveDependent()
    {
        if (s_saveDependentInstalled) return;

        OutgameTutorialRewind.ApplyWipeIfScheduled();
        CurrencyManager.Init();
        ProfileManager.Init();
        OwnershipManager.Init();
        OutgameTutorialProgress.Init();
        if (OutgameTutorialProgress.IsCompleted) RankManager.TryEnterFirstTier(out _);
        KeywordGrowthManager.Init();
        CardGrowthManager.Init();
        DeckSaveManager.LoadFromSave(ContentProfileConfig.Active.RunMode == EContentRunMode.Live);
        StarterDeck.GrantIfNoDeck(starterDeck);
        OutgameTutorialRunner.ResolveProgressAnchor();
        OutgameTutorialRunner.RewindToPendingBattleEntry();
        OutgameTutorialRewind.ApplyReplayIfScheduled();

        s_saveDependentInstalled = true;
        GameManager.MarkBootReady();
    }

    static int EnemyCardLevelOf(CardData _card)
    {
        if (!TournamentRun.IsActive) return RankManager.AiCardLevelOf(_card);

        int t_max = CardGrowthManager.MaxLevel;
        return t_max > 0 && TournamentRun.AiCardLevel > t_max ? t_max : TournamentRun.AiCardLevel;
    }
}
