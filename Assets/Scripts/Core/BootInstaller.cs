using UnityEngine;

// 전역 부트 프리팹의 루트. 사본이 LoadingScene·LobbyScene 둘이라 먼저 깬 쪽이 부트를 선점한다
// (정상 경로는 로딩 씬이, 로비 단독 Play는 로비 사본이 맡는다).
// 세이브 로드·재화 캐싱 등 씬 오브젝트가 필요 없는 부트는 GameManager가 앱 시작 시 먼저 처리한다.
[DefaultExecutionOrder(-200)]
public class BootInstaller : MonoBehaviour
{
    // 카드 목록은 CardRegistry(SO)가 단일 진실원. 씬에 사본을 두면 카드 추가 시 한쪽만 갱신된다.
    [SerializeField] CardRegistry cardRegistry;
    // 도감 레이아웃/생산 튜닝 SO. 미배선(null)이면 CatalogRows가 CardCatalog 3장씩 청크 fallback.
    [SerializeField] CollectionLayoutConfig collectionLayout;
    // 도감 테마 SO. 미배선(null)이면 CollectionThemes가 빈 목록(테마는 저작물이라 자동 생성 fallback이 없다).
    [SerializeField] CollectionThemeConfig collectionThemes;
    // 튜토리얼 스텝 시퀀스 SO. 로딩 씬이 첫 목적지를 판정하려면 부트 시점에 주입돼 있어야 한다.
    [SerializeField] OutgameTutorialData tutorialData;
    // 트리거 발화 튜토리얼 목록 SO(탭 첫 진입 등). 미배선(null)이면 트리거는 조용히 발화하지 않는다.
    [SerializeField] TriggeredTutorialData triggeredTutorialData;
    // 덱 대표 이미지 후보 SO. 미배선(null)이면 신규 덱이 이미지 키를 못 받고 표시가 첫 카드 아트로 떨어진다.
    [SerializeField] DeckImageCatalog deckImageCatalog;
    // 신규 유저에게 기본 지급할 스타터덱(CardPackData의 pool 6장을 고정 순서로 쓴다). 미배선(null)이면 지급을 건너뛴다.
    [SerializeField] CardPackData starterDeck;
    // 카드 강화·진화 튜닝 SO. 미배선(null)이면 CardGrowthManager가 코드 기본식·기본 게이트로 동작한다.
    [SerializeField] CardGrowthConfig growthConfig;

    static bool s_booted;

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

        // 카드 마스터 단일 창구 주입 — 도감·소유권·덱 등 아웃게임 소비자가 안정 키로 조회.
        ContentProfileConfig t_profile = ContentProfileConfig.Active;
        var t_availableCards = new System.Collections.Generic.List<CardData>(
            cardRegistry.Available(t_profile.IncludeTestCards));
        CardCatalog.SetSource(t_availableCards);

        // 도감 행 레이아웃/생산 튜닝 주입 — 카탈로그 카드를 참조하므로 SetSource 이후. null이면 청크 fallback.
        CatalogRows.SetLayout(collectionLayout);

        // 도감 테마 주입 — 테마는 lazy 빌드라 첫 Themes 접근 전에만 꽂히면 된다(빌드가 CardCatalog.KeyOf를 읽는다).
        CollectionThemes.SetSource(collectionThemes);

        // 소유권 캐싱·최초 기본 지급 — CardCatalog 주입 이후여야 한다(기본 지급 fallback이 카탈로그를 읽음).
        OwnershipManager.Init();
        // Live 카탈로그 밖의 레거시 소유 키가 정리된 뒤 튜토리얼 완료 여부를 판정한다.
        OutgameTutorialProgress.Init();

        // 도감 방치 생산 캐싱 — 세이브(DataSaveManager.Load)만 읽으므로 순서 무관하나
        // 행 완성 판정(OwnershipManager)·행 해석(CatalogRows)을 lazy로 쓰므로 소유권 Init 뒤에 둔다.
        CollectionProductionManager.Init();

        // 카드 성장 캐싱 — 세이브(DataSaveManager.Load)만 읽어 순서 무관하나, 곡선 조회가 Config를 쓰므로 주입이 먼저다.
        CardGrowthManager.SetConfig(growthConfig);
        CardGrowthManager.Init();

        // 전투에 성장값을 흘리는 유일한 배선. Battle이 OutGame을 참조하지 않도록 값 생산자를 부트가 꽂는다
        // (GameInitializer.GrowthProvider 주석이 지정한 자리). 캐시가 준비된 Init 뒤여야 첫 전투부터 반영된다.
        GameInitializer.GrowthProvider = CardGrowthManager.GrowthOf;

        // 덱 복원은 세이브의 카드 키를 CardData로 재수화하므로, 카드 마스터 목록을 먼저 넘겨야 한다.
        // 이 호출이 없으면 세이브의 덱 카드가 복원되지 않고 슬롯이 무효가 된다.
        DeckSaveManager.SetCardRegistry(t_availableCards);
        DeckSaveManager.LoadFromSave(t_profile.RunMode == EContentRunMode.Live);

        // 덱 대표 이미지 후보 주입 — 신규 덱 저장 시 여기서 키를 뽑는다.
        DeckImages.SetSource(deckImageCatalog);

        // 덱이 하나도 없는 신규 유저에게 스타터덱 지급(카드 소유권 포함).
        // 소유권 캐시·덱 로드 이후여야 하고, 대표 이미지 키를 뽑으므로 DeckImages 주입보다도 뒤에 온다.
        // 튜토리얼 첫실행 판정은 GameManager.Boot(BeforeSceneLoad)에서 이미 끝났으므로 여기 지급이 스킵을 유발하지 않는다.
        StarterDeck.GrantIfNoDeck(starterDeck);

        // 주입은 멱등 — 씬 브리지가 같은 에셋을 다시 넣어도 조기 return한다.
        OutgameTutorialRunner.EnsureData(tutorialData);
        TriggeredTutorialRunner.EnsureData(triggeredTutorialData);
    }
}
