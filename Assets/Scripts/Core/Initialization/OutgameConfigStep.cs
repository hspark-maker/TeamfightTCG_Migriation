using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

// 아웃게임 SO를 전역 static에 꽂는 단일 창구. 조회가 전부 lazy라 각 화면이 처음 그려지기 전에만 서면 된다.
// 미배선(null)이면 각 static이 코드 기본값으로 동작한다 — 예외가 아니라 조용한 기본값이라는 점이 함정이다.
public sealed class OutgameConfigStep : MainInitializer
{
    [FormerlySerializedAs("runtimeUiPrefabs")]
    [SerializeField] SyncUiPrefabCatalog syncUiPrefabs;
    // 카드 앨범(신규 도감) SO. 미배선이면 CardAlbum이 빈 앨범(앨범도 저작물이라 자동 생성 fallback이 없다).
    [SerializeField] CardAlbumConfig albumConfig;
    // 재화 아이콘·표시명 표 SO. 미배선이면 아이콘은 프리팹 그림 그대로, 이름은 코드 기본값으로 떨어진다.
    [SerializeField] CurrencyLook currencyLook;
    // 보상 토너먼트 경로 SO. 미배선이면 정점이 0개라 토너먼트 진입이 열리지 않는다.
    [SerializeField] TournamentConfig tournamentConfig;
    // 프로필 아바타·프레임 표 SO. 미배선이면 아바타·프레임 그림이 전부 프리팹 저작값 그대로 남는다.
    [SerializeField] ProfileConfig profileConfig;
    // 카드 강화·진화 튜닝 SO. 미배선이면 CardGrowthManager가 코드 기본식·기본 게이트로 동작한다.
    [SerializeField] CardGrowthConfig growthConfig;
    // 키워드 전역 강화 설정. 미배선이면 코드 기본값으로 동작한다.
    [SerializeField] KeywordGrowthConfig keywordGrowthConfig;
    // 덱 대표 이미지 후보 SO. 미배선이면 신규 덱이 이미지 키를 못 받고 표시가 첫 카드 아트로 떨어진다.
    [SerializeField] DeckImageCatalog deckImageCatalog;

    public override UniTask Initialize(InitializationContext _context)
    {
        SyncUiPrefabs.SetSource(syncUiPrefabs);

        // 앨범은 lazy 빌드라 첫 Themes 접근 전에만 꽂히면 된다(빌드가 CardCatalog의 카드 번호를 읽는다).
        CardAlbum.SetSource(albumConfig);
        CurrencyLook.SetActive(currencyLook);
        TournamentProgress.SetConfig(tournamentConfig);
        ProfileManager.SetConfig(profileConfig);

        // 성장 곡선 조회가 Config를 쓰므로 주입이 캐싱(Init)보다 먼저다.
        KeywordGrowthManager.SetConfig(keywordGrowthConfig);
        CardGrowthManager.SetConfig(growthConfig);

        // 신규 덱 저장 시 여기서 대표 이미지 키를 뽑는다.
        DeckImages.SetSource(deckImageCatalog);
        return UniTask.CompletedTask;
    }
}
