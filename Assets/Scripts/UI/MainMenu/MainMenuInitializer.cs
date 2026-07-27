using UnityEngine;

[DefaultExecutionOrder(-100)]
public class MainMenuInitializer : MonoBehaviour
{
    [SerializeField] AudioClip mainMenuBgm;
    // 카드 목록은 CardRegistry(SO)가 단일 진실원. 씬에 사본을 두면 카드 추가 시 한쪽만 갱신된다.
    [SerializeField] CardRegistry cardRegistry;
    // 도감 레이아웃/생산 튜닝 SO. 미배선(null)이면 CatalogRows가 CardCatalog 3장씩 청크 fallback.
    [SerializeField] CollectionLayoutConfig collectionLayout;

    void Awake()
    {

        // 카드 마스터 단일 창구 주입 — 도감·소유권·덱 등 아웃게임 소비자가 안정 키로 조회.
        // 세이브 로드·재화 캐싱 등 씬 무관한 전역 부트는 GameManager가 앱 시작 시 처리한다.
        CardCatalog.SetSource(cardRegistry.All);

        // 도감 행 레이아웃/생산 튜닝 주입 — 카탈로그 카드를 참조하므로 SetSource 이후. null이면 청크 fallback.
        CatalogRows.SetLayout(collectionLayout);

        // 소유권 캐싱·최초 기본 지급 — CardCatalog 주입 이후여야 한다(기본 지급 fallback이 카탈로그를 읽음).
        OwnershipManager.Init();

        // 도감 방치 생산 캐싱 — 세이브(DataSaveManager.Load)만 읽으므로 순서 무관하나
        // 행 완성 판정(OwnershipManager)·행 해석(CatalogRows)을 lazy로 쓰므로 소유권 Init 뒤에 둔다.
        CollectionProductionManager.Init();

        // 덱 복원은 카드 이름 키를 CardData로 재수화하므로, 카드 마스터 목록을 먼저 넘겨야 한다.
        // 이 호출이 없으면 세이브의 덱 카드가 복원되지 않고 슬롯이 무효가 된다.
        DeckSaveManager.SetCardRegistry(cardRegistry.All);
        DeckSaveManager.LoadFromFile();
    }

    void Start() => SoundManager.Instance?.PlayBGM(mainMenuBgm);
}
