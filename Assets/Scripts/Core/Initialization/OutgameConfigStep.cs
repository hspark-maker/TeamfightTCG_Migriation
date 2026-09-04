using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

// 아웃게임 SO를 전역 static에 꽂는 단일 창구. 조회가 전부 lazy라 각 화면이 처음 그려지기 전에만 서면 된다.
// 미배선(null)이면 각 static이 코드 기본값으로 동작한다 — 예외가 아니라 조용한 기본값이라는 점이 함정이다.
public sealed class OutgameConfigStep : MainInitializer
{
    [FormerlySerializedAs("runtimeUiPrefabs")]
    [SerializeField] SyncUiPrefabCatalog syncUiPrefabs;
    // 카드 앨범(신규 도감) 스킨 SO. 구조·표시 텍스트는 스펙시트가 정하고, 여기선 테마 그림 4종만 온다.
    [SerializeField] CardAlbumConfig albumConfig;
    // 재화 아이콘·표시명 표 SO. 미배선이면 아이콘은 프리팹 그림 그대로, 이름은 코드 기본값으로 떨어진다.
    [SerializeField] CurrencyLook currencyLook;
    // 모험 경로 SO. 미배선이면 정점이 0개라 모험 진입이 열리지 않는다.
    [SerializeField] AdventureConfig adventureConfig;
    // 프로필 아바타·프레임 표 SO. 미배선이면 아바타·프레임 그림이 전부 프리팹 저작값 그대로 남는다.
    [SerializeField] ProfileConfig profileConfig;
    // 덱 대표 이미지 후보 SO. 미배선이면 신규 덱이 이미지 키를 못 받고 표시가 첫 카드 아트로 떨어진다.
    [SerializeField] DeckImageCatalog deckImageCatalog;
    // 룰렛 판 저작 SO. 미배선이거나 저작에 결함이 있으면 룰렛이 열리지 않는다(로비 버튼 미표시) — 나머지 기능은 정상이다.
    [SerializeField] RouletteConfig rouletteConfig;

    public override UniTask Initialize(InitializationContext _context)
    {
        SyncUiPrefabs.SetSource(syncUiPrefabs);

        // 앨범 구조는 여기서 즉시 조립된다 — 스펙시트(AlbumThemeInfo·AlbumEntry)를 읽으므로 SpecSource 뒤에 서야 한다.
        CardAlbum.SetSource(albumConfig);
        CurrencyLook.SetActive(currencyLook);
        if (!AdventureNodeSpec.TryBuildRuntime(adventureConfig, out AdventureConfig t_runtimeAdventure,
                out string t_adventureError))
        {
            if (AdventureNodeSpec.UpdateRequired)
            {
                Debug.LogError($"[OutgameConfigStep] {t_adventureError}");
                GameInitialization.MarkUpdateRequired();
                Destroy(_context.Root);
                _context.Abort();
            }
            else
            {
                FailToRecovery(_context, new System.InvalidOperationException(t_adventureError));
            }
            return UniTask.CompletedTask;
        }
        AdventureProgress.SetConfig(t_runtimeAdventure);
        ProfileManager.SetConfig(profileConfig);

        // 곁가지 컨텐츠라 실패해도 초기화를 세우지 않는다 — 결함은 SetConfig 안의 Validate가 LogError로 드러낸다.
        RouletteManager.SetConfig(rouletteConfig);

        // 신규 덱 저장 시 여기서 대표 이미지 키를 뽑는다.
        DeckImages.SetSource(deckImageCatalog);
        return UniTask.CompletedTask;
    }
}
