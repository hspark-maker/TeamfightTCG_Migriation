using System;
using UnityEngine;
using A = EOutgameTutorialAnchor;
using F = EOutgameFeature;

/// <summary>앵커 하나가 "어디에 있고 · 무엇에 잠기고 · 어디서 등록되는가"를 답하는 저작 검증용 단일 테이블.
///
/// 이 세 가지는 지금 프리팹 YAML(TutorialAnchor 컴포넌트의 key)과 코드 등록부(TutorialAnchorRegistry.Register),
/// 그리고 잠금 부착부(FeatureLockView.Attach)에 흩어져 있어 저작자가 볼 방법이 없다. 그래서 아직 잠긴 기능의
/// 앵커를 누르라고 저작하는 실수가 컴파일도 통과하고, 런타임에 게이트가 영영 안 켜지는 것으로만 드러난다.
///
/// 런타임은 이 표를 읽지 않는다 — 에디터 검증기 전용이다. 앵커를 추가하면 EOutgameTutorialAnchor 끝에 값을 더하고
/// 이 테이블 끝에 그 행을 더한다. 빠뜨리면 아래 static 생성자가 부팅 때 소리내어 잡는다.</summary>
public readonly struct TutorialAnchorMeta
{
    public readonly EOutgameTutorialAnchor Anchor;  // 자기 행의 앵커(테이블 정렬 검증용 — 아래 static 생성자)
    public readonly EOutgameFeature        Gate;    // 이 앵커를 누르려면 열려 있어야 하는 기능(None = 잠금과 무관)
    public readonly string                 Screen;  // 표시용 화면 이름(예: "로비/팩 탭")
    public readonly string                 Source;  // 등록처 — 프리팹 경로 또는 "파일.cs:라인". 비면 미등록.

    public TutorialAnchorMeta(EOutgameTutorialAnchor _anchor, EOutgameFeature _gate, string _screen, string _source)
    {
        Anchor = _anchor;
        Gate   = _gate;
        Screen = _screen;
        Source = _source;
    }

    /// <summary>등록처가 확인된 앵커인가(비면 어느 프리팹·코드도 이 키를 등록하지 않는다 = 안내가 뜰 자리가 없다).</summary>
    public bool IsRegistered => !string.IsNullOrEmpty(Source);

    /// <summary>앵커의 메타. 테이블에 없는 값은 "잠금 무관 · 화면 미상 · 미등록"으로 본다.
    /// 여기 닿는 것은 아래 static 생성자가 이미 오류로 잡은 뒤다.</summary>
    public static TutorialAnchorMeta Of(EOutgameTutorialAnchor _anchor)
    {
        int t_index = (int)_anchor;

        return t_index >= 0 && t_index < s_table.Length
            ? s_table[t_index]
            : new TutorialAnchorMeta(_anchor, F.None, "(알 수 없음)", null);
    }

    // 인덱스 = (int)EOutgameTutorialAnchor. 첫 칸의 앵커 이름이 그 계약을 눈에 보이게 하고, static 생성자가 검증한다.
    // Gate는 전부 소스 대조로 채웠다 — 확인되지 않은 것은 추측하지 않고 None으로 둔다(틀린 Gate는 멀쩡한 저작을 오류로 찍는다).
    static readonly TutorialAnchorMeta[] s_table =
    {
        // 0
        new(A.None,                       F.None,               "(없음)",               null),

        // 1  Tab_Match.prefab:381(key: 1)과 같은 GameObject(1969415885228295404)에 FeatureLockView feature: 6(LobbyPlay) — Tab_Match.prefab:394
        new(A.LobbyPlayButton,            F.LobbyPlay,          "로비/배틀 탭",          "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Match.prefab"),

        // 2  LobbyCanvas.prefab:1929(tutorialAnchor: 2) / :1931(unlockFeature: 2) — LobbyTabController.Tab이 쌍으로 저작
        new(A.LobbyPackTab,               F.LobbyPackTab,       "로비/탭바",            "UI/Lobby/LobbyTabBarView.cs:67"),

        // 3  Tab_Pack.prefab:2064(key: 3)의 GameObject(8874862289494966950) = PackShowcaseController.buyButton(Tab_Pack.prefab:1502),
        //    같은 GameObject에 FeatureLockView feature: 7(PackBuy) — Tab_Pack.prefab:2077 / UI/Shop/PackShowcaseController.cs:75
        new(A.PackBuyButton,              F.PackBuy,            "로비/뽑기 탭",          "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Pack.prefab"),

        // 4  PackOpenOverlay.prefab:3668(key: 4), GameObject 이름 AcquireButton. 이 오브젝트에 붙는 잠금 없음
        //    (PackAcquireController가 EOutgameFeature를 쓰는 곳은 retryButton 하나뿐 — PackAcquireController.cs:257)
        new(A.PackAcquireButton,          F.None,               "팩 개봉 오버레이",       "Assets/Assets/Prefabs/UI/LobbyUI/PackUI/PackOpenOverlay.prefab"),

        // 5  LobbyCanvas.prefab:1941(tutorialAnchor: 5) / :1943(unlockFeature: 4 = LobbyDeckTab)
        new(A.LobbyDeckTab,               F.LobbyDeckTab,       "로비/탭바",            "UI/Lobby/LobbyTabBarView.cs:67"),

        // 6  LobbyCanvas.prefab:1935(tutorialAnchor: 6) / :1937(unlockFeature: 0) — 배틀 탭은 잠그지 않는 것으로 저작돼 있다
        new(A.LobbyMatchTab,              F.None,               "로비/탭바",            "UI/Lobby/LobbyTabBarView.cs:67"),

        // 7  UI/Deck/DeckListController.cs:94가 FeatureLockView.Attach(DeckCreate), :96이 같은 t_create에 앵커 등록 — 같은 GameObject
        new(A.DeckCreateSlot,             F.DeckCreate,         "로비/덱 탭(덱 목록)",    "UI/Deck/DeckSlotView.cs:115"),

        // 8  DeckEditPanel.prefab:1800(key: 8), GameObject 이름 CollectionArea. 이 오브젝트에 붙는 잠금 없음(화면 진입만 탭에 걸린다)
        new(A.DeckCollectionArea,         F.None,               "덱 편집",              "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Deck/DeckEditPanel.prefab"),

        // 9  DeckEditPanel.prefab:3170(key: 9), GameObject 이름 DeckArea. 잠금 없음
        new(A.DeckSlotArea,               F.None,               "덱 편집",              "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Deck/DeckEditPanel.prefab"),

        // 10 DeckEditPanel.prefab:2424(key: 10)의 GameObject(5057038299696546900) = DeckEditController.autoEquipButton(DeckEditPanel.prefab:2980)
        //    → UI/Deck/DeckEditController.cs:94가 그 GameObject에 FeatureLockView.Attach(DeckAutoEquip)
        new(A.DeckAutoEquipButton,        F.DeckAutoEquip,      "덱 편집",              "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Deck/DeckEditPanel.prefab"),

        // 11 DeckEditPanel.prefab:3116(key: 11) / MatchDeckEditPanel.prefab:479(key: 11, Btn_MatchBack) — 두 화면이 키를 공유. 잠금 없음
        new(A.DeckEditBackButton,         F.None,               "덱 편집",              "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Deck/DeckEditPanel.prefab"),

        // 12 MatchDeckRoot.prefab:1646(key: 12). UI/Match/ 전체에 EOutgameFeature 사용 0건 → 잠금 없음
        new(A.MatchDeckEditButton,        F.None,               "매치 덱 화면",          "Assets/Assets/Prefabs/UI/MatchUI/MatchDeckRoot.prefab"),

        // 13 MatchDeckRoot.prefab:1698(key: 13). 잠금 없음
        new(A.MatchDeckBattleButton,      F.None,               "매치 덱 화면",          "Assets/Assets/Prefabs/UI/MatchUI/MatchDeckRoot.prefab"),

        // 14 DeckEditPanel.prefab:2696(key: 14), GameObject 이름 Btn_UnequipAll. 잠금 없음
        new(A.DeckUnequipAllButton,       F.None,               "덱 편집",              "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Deck/DeckEditPanel.prefab"),

        // 15 MatchDeckPanel.prefab:2577(key: 15, MySection) / MatchDeckRoot.prefab:1628. 잠금 없음
        new(A.MatchDeckMySection,         F.None,               "매치 덱 화면",          "Assets/Assets/Prefabs/UI/MatchUI/MatchDeckPanel.prefab"),

        // 16 MatchDeckPanel.prefab:200(key: 16, EnemySection) / MatchDeckRoot.prefab:1675. 잠금 없음
        new(A.MatchDeckEnemySection,      F.None,               "매치 덱 화면",          "Assets/Assets/Prefabs/UI/MatchUI/MatchDeckPanel.prefab"),

        // 17 폐기 — 가로 덱 리스트가 사라져 이 키를 등록하는 곳이 없다. 저작이 남아 있으면 검증기가 "미등록"으로 잡는다
        new(A.MatchDeckTutorialDeck,      F.None,               "매치 덱 화면(덱 목록)",   null),

        // 18 LobbyCanvas.prefab:1947(tutorialAnchor: 18) / :1949(unlockFeature: 5 = LobbyCollectionTab)
        new(A.LobbyCollectionTab,         F.LobbyCollectionTab, "로비/탭바",            "UI/Lobby/LobbyTabBarView.cs:67"),

        // 19 UI/Album/AlbumThemeCellView.cs:81. UI/Album/ 전체에 EOutgameFeature 사용 0건 → 잠금 없음(진입만 도감 탭에 걸린다)
        new(A.AlbumThemeCell,             F.None,               "로비/도감 탭",          "UI/Album/AlbumThemeCellView.cs:81"),

        // 20 UI/Album/AlbumCardSlotView.cs:56. 잠금 없음
        new(A.AlbumCardSlot,              F.None,               "도감 페이지 오버레이",    "UI/Album/AlbumCardSlotView.cs:56"),

        // 21 UI/CardDetail/CardDetailOverlayView.cs:1522가 지금 서 있는 성장 버튼에 등록하고,
        //    :446/:447이 그 강화·진화 버튼 GameObject에 FeatureLockView.Attach(CardEnhance) — 같은 오브젝트
        new(A.CardDetailEnhanceButton,    F.CardEnhance,        "카드 상세 오버레이",      "UI/CardDetail/CardDetailOverlayView.cs:1522"),

        // 22 UI/Growth/KeywordGrowthCellView.cs:77. UI/Growth/ 전체에 EOutgameFeature 사용 0건 → 잠금 없음
        new(A.KeywordGrowthCell,          F.None,               "키워드 강화 패널",       "UI/Growth/KeywordGrowthCellView.cs:77"),

        // 23 UI/Growth/KeywordGrowthPanel.cs:335(패널이 열려 있는 동안만). 잠금 없음
        new(A.KeywordGrowthUpgradeButton, F.None,               "키워드 강화 패널",       "UI/Growth/KeywordGrowthPanel.cs:335"),

        // 24 Tab_Match.prefab의 TournamentBtn(2563422757509691373)에 key: 24.
        //    잠금은 프리팹 저작이 아니라 UI/Lobby/LobbyMatchTabPanel.cs의 Awake가 FeatureLockView.Attach(Tournament)로 건다
        new(A.TournamentButton,           F.Tournament,         "로비/배틀 탭",          "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Match.prefab"),

        // 25 UI/Deck/DeckEditCollectionGrid.cs가 anchorCard로 지목된 카드 타일에만 등록(해제는 Clear). 잠금 없음
        new(A.DeckEditCollectionCard,     F.None,               "덱 편집(컬렉션 격자)",    "UI/Deck/DeckEditCollectionGrid.cs"),
    };

    // 이 구조의 조용한 실패 두 가지를 이 창을 처음 열 때 소리내어 잡는다(에디터 어셈블리라 부팅이 아니다).
    //  (1) 앵커만 늘리고 행을 안 늘림 → 그 앵커가 폴백으로 떨어져 검증기가 "미등록"으로 오판한다.
    //  (2) 앵커를 중간에 끼워 넣고 행은 끝에 붙임 → 개수는 맞는데 삽입 지점 이후가 통째로 한 칸씩 밀린다.
    //      행마다 자기 앵커를 싣고 인덱스와 대조하는 것이 (2)를 잡는 유일한 방법이다.
    // 못 잡는 실패가 하나 남는다: Gate 값 자체의 오기. 그건 곧장 "앵커 잠김" 오탐이 되므로,
    // FeatureLockView.Attach 호출부(현재 8곳)나 LobbyTabController.tabs의 짝을 옮기면 이 표도 함께 고쳐라.
    static TutorialAnchorMeta()
    {
        int t_anchors = Enum.GetValues(typeof(EOutgameTutorialAnchor)).Length;
        if (t_anchors != s_table.Length)
            Debug.LogError($"[TutorialAnchorMeta] 앵커 {t_anchors}개 / 테이블 {s_table.Length}행 — 새 앵커의 행을 테이블에 추가하세요.");

        for (int t_i = 0; t_i < s_table.Length; t_i++)
        {
            if (s_table[t_i].Anchor == (EOutgameTutorialAnchor)t_i) continue;

            Debug.LogError($"[TutorialAnchorMeta] 테이블 {t_i}번 행이 {s_table[t_i].Anchor}입니다 — 행 순서가 앵커 순서와 어긋났습니다(그 뒤 전부가 밀립니다).");
        }
    }
}
