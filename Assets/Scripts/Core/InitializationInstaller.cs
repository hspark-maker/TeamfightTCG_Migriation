using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

// 이관 중인 초기화 잔여분. 실행 주체는 InitializationRunner이고 여기는 LegacyInstallerStep이 부른다 —
// 스텝을 하나씩 떼어낼수록 이 파일이 얇아지고, 다 떼면 사라진다.
// 세이브 로드·재화 캐싱 등 씬 오브젝트가 필요 없는 초기화는 GameManager가 앱 시작 시 먼저 처리한다.
public class InitializationInstaller : MonoBehaviour
{
    // 카드 목록은 SpecData가 단일 진실원이며 CardCatalog가 부팅 시 구성한다.
    [SerializeField] SynergyRegistry synergyRegistry;
    // 카드 앨범(신규 도감) SO. 미배선(null)이면 CardAlbum이 빈 앨범(앨범도 저작물이라 자동 생성 fallback이 없다).
    [SerializeField] CardAlbumConfig albumConfig;
    // 재화 아이콘·표시명 표 SO. 미배선(null)이면 아이콘은 프리팹 그림 그대로, 이름은 코드 기본값으로 떨어진다.
    [SerializeField] CurrencyLook currencyLook;
    // 튜토리얼 스텝 시퀀스 SO. 로딩 씬이 첫 목적지를 판정하려면 초기화 시점에 주입돼 있어야 한다.
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
    static bool s_initialized;
    static bool s_saveDependentInstalled;

    // 루트 수명 판정은 RootLifecycleStep이 읽는다. 카탈로그 구성이 스텝으로 떨어져 나오면 소유도 같이 옮긴다.
    internal static bool IsInitialized => s_initialized;
    internal static bool IsSaveDependentInstalled => s_saveDependentInstalled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_initialized = false;
        s_saveDependentInstalled = false;
    }

    // 즉시 주입 단계. 실행 주체는 InitializationRunner(LegacyInstallerStep)다 —
    // 이 클래스는 더 이상 Awake/Start 메시지로 스스로 돌지 않는다.
    // 반환값 false는 "이 사본은 여기서 끝"(중복 사본이거나 카탈로그 구성 실패로 루트를 파괴함)이다.
    internal bool InstallImmediate()
    {
        // 루트 수명(중복 사본 정리·DontDestroyOnLoad)과 전투 SO 주입은
        // RootLifecycleStep·BattleConfigStep이 이 스텝보다 먼저 끝낸다.
        SyncUiPrefabs.SetSource(syncUiPrefabs);

        // 카드 마스터 단일 창구 주입 — 도감·소유권·덱 등 아웃게임 소비자가 안정 키로 조회.
        ContentProfileConfig t_profile;
        try
        {
            t_profile = ContentProfileConfig.Active;
            SpecSource.Init();
            CardCatalog.SetSource(synergyRegistry, t_profile.RunMode, t_profile.IncludeTestCards);
        }
        catch (System.Exception t_exception)
        {
            GameInitialization.MarkRecoveryRequired();
            Debug.LogException(t_exception);
            Destroy(gameObject);
            return false;
        }

        s_initialized = true;

        // 카드 아트 선로드. 아트는 이제 CardData가 직접 물지 않고 Addressables로 따로 온다
        // (CardArtCache) — 그리는 코드는 여전히 동기라 화면에 나가기 전에 여기서 채워 둔다.
        // 지금은 전 카드를 받는다: 이관 전과 같은 "언제든 다 그려진다" 동작을 유지하기 위함이다.
        // 범위를 덱·도감 단위로 좁히는 건 그 다음 단계다(좁히는 순간 미스 경고가 진단이 된다).
        StartCoroutine(CardArtCache.Preload(CardCatalog.AllSpecs));

        // UI 프리팹 라벨 로드. 시작을 여기서 거는 이유는 CardArtCache와 같다 — 시작 시점이
        // 컴포넌트 실행 순서에 끌려다니지 않게 초기화 소유자가 명시적으로 건다.
        UiPrefabCache.Preload().Forget();

        // 카드팩 스펙시트 선로드 — 팩 값(가격·장수·드롭)의 진실원. 지연 로드도 되지만 상점 진입 프레임에
        // 파싱이 걸리지 않게 여기서 당긴다. 드롭 조회가 CardCatalog를 읽으므로 SetSource 이후여야 한다.
        PackSpec.Init();

        // 보상 스펙시트 선로드 — 토너먼트·앨범 보상 값의 진실원. 파싱은 SpecSource가 이미 1회 했으므로
        // 여기서 드는 비용은 키 색인뿐이다.
        TournamentSpec.Init();
        AlbumSpec.Init();

        // 카드 앨범 주입 — lazy 빌드라 첫 Themes 접근 전에만 꽂히면 된다(빌드가 CardCatalog의 카드 번호를 읽는다).
        CardAlbum.SetSource(albumConfig);

        // 재화 그림 주입 — 조회는 lazy라 재화 UI가 처음 그려지기 전에만 꽂히면 된다.
        CurrencyLook.SetActive(currencyLook);

        // 토너먼트 경로 주입 — 정점 상태 조회가 이 애셋에서 나온다(미배선이면 정점 0개).
        TournamentProgress.SetConfig(tournamentConfig);

        // 프로필 주입·로드 — 세이브 의존이 없어 순서는 자유다.
        ProfileManager.SetConfig(profileConfig);

        // 소유권 캐싱·최초 기본 지급 — CardCatalog 주입 이후여야 한다(기본 지급 fallback이 카탈로그를 읽음).
        // Live 카탈로그 밖의 레거시 소유 키가 정리된 뒤 튜토리얼 완료 여부를 판정한다.

        // 카드 성장 캐싱 — 세이브(DataSaveManager.Load)만 읽어 순서 무관하나, 곡선 조회가 Config를 쓰므로 주입이 먼저다.
        KeywordGrowthManager.SetConfig(keywordGrowthConfig);
        CardGrowthManager.SetConfig(growthConfig);

        // 전투에 성장값을 흘리는 유일한 배선. Battle이 OutGame을 참조하지 않도록 값 생산자를 초기화가 꽂는다
        // (GameInitializer.GrowthProvider 주석이 지정한 자리). 캐시가 준비된 Init 뒤여야 첫 전투부터 반영된다.
        GameInitializer.GrowthProvider = CardGrowthManager.GrowthOf;
        // Firebase 구현이 먼저 주입되지 않은 개발/오프라인 환경에서만 로컬 세이브를 사용한다.
        // 전투와 네트워크는 IMatchGrowthSource만 보므로 이후 공급자 교체가 와이어 계약을 바꾸지 않는다.
        MatchGrowthSource.SetFallback(new LocalSaveMatchGrowthSource());

        // 표시용 해금 키워드도 같은 성장값에서 나온다. 이걸 안 꽂으면 아직 못 쓰는 키워드가
        // 도감·덱편집·정보창에 그대로 떠서 표시와 규칙이 갈라진다.
        CardVisualRules.UnlockedKeywordProvider = _card => CardGrowthManager.GrowthOf(_card).UnlockedKeywords;
        CardVisualRules.EvolutionStageProvider = _card => CardGrowthManager.GrowthOf(_card).EvolutionStage;

        // 싱글 AI 난이도. 랭크 티어가 정한 레벨을 같은 성장 곡선에 태운다 —
        // 체력뿐 아니라 키워드·시너지 해금까지 플레이어와 동일한 규칙으로 결정된다.
        // 레벨은 전투 시작 시점에 읽어야 한다(초기화 때 굳히면 랭크가 올라도 난이도가 안 따라온다).
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

        // 덱 대표 이미지 후보 주입 — 신규 덱 저장 시 여기서 키를 뽑는다.
        DeckImages.SetSource(deckImageCatalog);

        // 덱이 하나도 없는 신규 유저에게 스타터덱 지급(카드 소유권 포함).
        // 소유권 캐시·덱 로드 이후여야 하고, 대표 이미지 키를 뽑으므로 DeckImages 주입보다도 뒤에 온다.
        // 튜토리얼 첫실행 판정은 GameManager.Initialize(BeforeSceneLoad)에서 이미 끝났으므로 여기 지급이 스킵을 유발하지 않는다.

        // 주입은 멱등 — 씬 브리지가 같은 에셋을 다시 넣어도 조기 return한다.
        OutgameTutorialRunner.EnsureData(tutorialData);
        TriggeredTutorialRunner.EnsureData(triggeredTutorialData);

        // 세이브가 붙잡아 둔 스텝 번호로 좌표를 되찾는다 — 저작이 스텝을 끼워 넣거나 옮겼어도 같은 스텝에 선다.
        // 시퀀스를 읽어야 하므로 EnsureData 뒤, 좌표를 쓰는 아래 둘보다는 반드시 앞이다.

        // 대본 전투가 연 화면 안에서 앱이 닫혔으면 좌표를 그 전투 진입 스텝으로 되감는다(초기화당 1회는 여기가 유일).
        // 디버그 되감기보다 앞이다 — 디버그가 찍은 좌표는 그대로 서야 한다.

        // 디버그 되감기 예약 소비(2단) — 좌표까지의 지급 재생. 시퀀스를 읽어야 하므로 EnsureData 뒤,
        // 덱·소유·카탈로그를 쓰므로 위 배선이 전부 끝난 이 자리다. 예약이 없으면 아무 일도 없다.

        return true;
    }

    // 세이브 게이트·에셋 로드를 기다린 뒤 세이브 의존 매니저를 설치하는 지연 단계.
    // 호출자는 LegacyInstallerStep 하나뿐이다(InstallImmediate가 true를 돌려준 사본에서만).
    internal System.Collections.IEnumerator RunDeferred()
    {
        while (!PlayerSaveCloud.IsGateComplete && !GameInitialization.IsTerminated)
        {
            yield return null;
        }

        if (GameInitialization.IsTerminated)
            yield break;

        if (DataLibrary.instance == null)
        {
            Debug.LogError("[InitializationInstaller] DataLibrary is missing from the initialization hierarchy.");
            GameInitialization.MarkRecoveryRequired();
            yield break;
        }

        GameInitialization.SetState(EGameInitState.LoadingAssets);
        while ((!CardArtCache.IsComplete ||
                (!UiPrefabCache.IsComplete && !UiPrefabCache.HasFailed)) &&
               !GameInitialization.IsTerminated)
        {
            yield return null;
        }

        if (GameInitialization.IsTerminated)
            yield break;

        if (CardArtCache.HasFailed || UiPrefabCache.HasFailed)
        {
            GameInitialization.MarkRecoveryRequired();
            yield break;
        }

        try
        {
            GameInitialization.SetState(EGameInitState.InstallingManagers);
            InstallSaveDependent();
        }
        catch (System.Exception t_exception)
        {
            GameInitialization.MarkRecoveryRequired();
            Debug.LogException(t_exception);
        }
    }

    void InstallSaveDependent()
    {
        if (s_saveDependentInstalled) return;

        // 클라우드 채택이 끝난 뒤여야 한다 — 채택 전에 슬롯을 갈아엎으면 채택이 그대로 덮어써 무효가 된다.
        OutgameTutorialRewind.ApplyWipeIfScheduled();

        // 스타터 지급의 유일한 근거는 "원격 문서가 없다"이다. 오프라인 폴백 세션은 IsFreshAccount가 false다.
        CurrencyManager.Init(PlayerSaveCloud.IsFreshAccount);
        ProfileManager.Init();
        OwnershipManager.Init();
        OutgameTutorialProgress.Init();
        if (OutgameTutorialProgress.IsCompleted) RankManager.TryEnterFirstTier(out _);
        KeywordGrowthManager.Init();
        CardGrowthManager.Init();
        DeckSaveManager.LoadFromSave();
        StarterDeck.GrantIfNoDeck(starterDeck);
        OutgameTutorialRunner.ResolveProgressAnchor();
        OutgameTutorialRunner.RewindToPendingBattleEntry();
        OutgameTutorialRewind.ApplyReplayIfScheduled();

        s_saveDependentInstalled = true;

        // 신규 계정의 첫 문서를 여기서 한 번에 만든다. 이 업로드가 실패하면 원격 문서는 여전히 없으므로
        // 다음 초기화도 신규로 판정되고 지급이 정확히 한 번만 남는다(멱등).
        DataSaveManager.SaveImmediate();
        GameInitialization.MarkReady();
    }

    static int EnemyCardLevelOf(int _cardId)
    {
        if (!TournamentRun.IsActive) return RankManager.AiCardLevelOf(_cardId);

        int t_max = CardGrowthManager.MaxLevel;
        return t_max > 0 && TournamentRun.AiCardLevel > t_max ? t_max : TournamentRun.AiCardLevel;
    }
}
