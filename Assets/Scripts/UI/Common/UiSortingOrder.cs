using UnityEngine;
using UnityEngine.UI;

/// <summary>화면의 위아래를 정하는 층 표. UI 정렬 순서의 단일 진실원이다.
///
/// <b>재지 않고 적어 둔다.</b> 예전에는 "지금 떠 있는 캔버스 중 최댓값 + 1"로 재서 올라탔다.
/// 재는 방식은 무대 밖의 상시 캔버스까지 후보로 끌어들인다 — 항상 켜져 있는 UIPoolManager의 빈 컨테이너(<see cref="Pool"/>)
/// 때문에 카드 상세가 401까지 뛰었고, 그 위에 서야 할 해금 안내(<see cref="Intro"/>, 150)가 상세 뒤에 묻혔다.
/// 예외를 하나씩 빼는 식(튜토리얼 게이트)으로는 다음에 생길 상시 캔버스를 막지 못한다.
/// "누가 누구 위인가"는 화면을 보고 정하는 저작 결정이라, 런타임에 재는 값이 아니라 여기 적힌 값이다.
///
/// 값 사이를 띄워 두는 이유: 사이에 낄 층이 생겨도 아래위를 통째로 밀지 않게.
///
/// <b>승격은 <see cref="LiftNested"/> 한 문으로만 한다.</b> 표가 진실원이라면 그 표를 쓰는 문도 하나여야 한다 —
/// 같은 승격 코드가 클래스마다 복제되던 시절, 복제본 셋 중 둘이 대입 순서를 거꾸로 써서 층이 0으로 앉았고
/// 화면이 통째로 안 보이는데 에러는 한 줄도 안 났다.
/// 예외는 <b>이전 값을 저장했다가 되돌려야 하는</b> 넷뿐이다(자기 bookkeeping이 있어 이 문으로 안 접힌다):
/// AlbumPageOverlayView · RankHud · SettingsPanel · OutgameTutorialGateUI.Promote.
///
/// /// ⚠ 프리팹에 저작된 Canvas.sortingOrder도 이 표를 따른다. 각 항목의 괄호가 그 값을 들고 있는 자리다 —
/// 고칠 때는 두 곳을 함께 고친다(코드에서 찍는 층은 <see cref="Stamp"/>가 표를 이긴다).</summary>
public static class UiSortingOrder
{
    /// <summary>로비 캔버스와 그 안의 모든 탭. 아래 층들은 전부 이 위에 뜬다.</summary>
    public const int Lobby = 0;

    /// <summary>카드팩 개봉 화면(PackOpenOverlay.prefab).</summary>
    public const int PackOpen = 100;

    /// <summary>획득 결과 오버레이(CardRewardOverlay·CardSetRewardOverlay·PackRewardOverlay.prefab).</summary>
    public const int Reward = 120;

    /// <summary>개봉·보상 위에서 여는 카드 상세. 로비에서 열 때는 로비 캔버스 안(<see cref="Lobby"/>)에 그대로 있고,
    /// 다른 화면 위에서 열 때만 이 층으로 올라탄다(CardDetailOverlayView.LiftAbove).</summary>
    public const int CardDetailLifted = 130;

    /// <summary>전면에서 개념을 가르치는 안내(UnlockIntroOverlay.prefab)와 승급 연출(RankPromoteOverlay.prefab).
    /// 상세를 무대로 쓰는 층이라 <see cref="CardDetailLifted"/>보다 반드시 위다.</summary>
    public const int Intro = 150;

    /// <summary>전투 튜토리얼 안내(TutorialOverlay.prefab).</summary>
    public const int BattleTutorial = 200;

    /// <summary>아웃게임 튜토리얼 게이트(OutgameTutorialGate.prefab). 아래 화면을 <b>가리키는</b> 층이라
    /// 가리킬 대상(상세·안내)보다 항상 위에 있어야 한다.</summary>
    public const int TutorialGate = 350;

    /// <summary>풀에서 여는 로비 오버레이(KeywordGrowthOverlay 등). 담기는 자리인 <see cref="Pool"/>(400)는
    /// 튜토리얼 게이트(<see cref="TutorialGate"/>)보다 위라, <b>안내가 가리켜야 하는 무대</b>는 이 층으로 내려앉는다
    /// — 자기 Canvas + overrideSorting으로 컨테이너에서 떨어져 나온다.
    /// 컨테이너 값을 내리는 것은 답이 아니다: 딤에 묻히면 안 되는 실패 팝업까지 함께 내려간다.</summary>
    public const int PooledOverlay = 300;

    /// <summary>덱 편집의 드래그 고스트(DragLayer). 끌고 있는 카드는 손가락을 따라다니므로 무엇에도 가리면 안 된다 —
    /// 특히 편집 화면이 <see cref="PooledOverlay"/>로 내려앉은 뒤에는 튜토리얼 게이트 딤(<see cref="TutorialGate"/>)
    /// 밑에서 끌리게 된다. 그래서 고스트만 게이트 위로 따로 올린다.</summary>
    public const int DragGhost = 360;

    /// <summary>UIPoolManager의 UI 컨테이너(Initialize.prefab). 무대가 아니라 풀린 UI가 담기는 자리라
    /// 비어 있어도 항상 켜져 있다 — 이 표가 생긴 이유다.</summary>
    public const int Pool = 400;

    /// <summary>보상 수령 팝업(RewardClaimPopup.prefab).</summary>
    public const int RewardClaim = 410;

    /// <summary>설정 화면(SettingUI.prefab·SettingsPanel).</summary>
    public const int Setting = 900;

    /// <summary>클라우드 세이브 동기화 지연 배너(CloudSyncBanner.prefab). 어떤 화면에서도 보여야 하므로 설정(<see cref="Setting"/>) 위다.
    /// 다만 커튼(<see cref="Curtain"/>)·로딩 커버(<see cref="LoadingCover"/>)보다는 아래에 둔다 —
    /// 화면을 갈아 끼우는 동안은 아무 것도 새면 안 되고, 배너는 갈아 끼운 뒤에도 그대로 떠 있다.</summary>
    public const int CloudSyncBanner = 940;

    /// <summary>씬을 갈아 끼우는 커튼(SceneCurtain.prefab).</summary>
    public const int Curtain = 950;

    /// <summary>로딩 커버(LoadingCover.prefab).</summary>
    public const int LoadingCover = 1000;

    /// <summary>부트 로그인 관문(Popup_LoginEmail). 계정이 정해지기 전에는 부트가 멈춰 있으므로
    /// 로딩 커버(<see cref="LoadingCover"/>) <b>위</b>다 — 아래에 두면 커버에 가려 아무것도 고를 수 없다.</summary>
    public const int SignIn = 1100;

    /// <summary>화면 전체를 덮는 번쩍임(ScreenFlash). 그 밑에서 화면을 갈아치우는 것이 목적이라 무엇보다 위다.</summary>
    public const int ScreenFlash = 32000;

    /// <summary>중첩 캔버스를 이 표의 층으로 끌어올린다(Canvas·GraphicRaycaster가 없으면 붙인다). 올라선 캔버스를 돌려준다.
    ///
    /// <b>대입 순서가 계약이다</b> — overrideSorting이 꺼진 중첩 캔버스는 sortingOrder 대입을 <b>버린다</b>.
    /// 거꾸로 쓰면 값이 0으로 남아 로비(<see cref="Lobby"/>)와 같은 층에 앉고, 그 화면이 통째로 안 보인다(에러 0건).
    ///
    /// GraphicRaycaster를 함께 붙이는 이유: overrideSorting을 켠 중첩 캔버스는 부모의 레이캐스터가 쥔 정렬에서
    /// 떨어져 나온다 — 없으면 눈에는 보이는데 탭이 안 먹는다.</summary>
    public static Canvas LiftNested(GameObject _go, int _order)
    {
        if (_go == null) return null;

        var t_canvas = _go.GetComponent<Canvas>();
        if (t_canvas == null) t_canvas = _go.AddComponent<Canvas>();

        if (_go.GetComponent<GraphicRaycaster>() == null) _go.AddComponent<GraphicRaycaster>();

        t_canvas.overrideSorting = true;   // 이 줄이 먼저다 — 위 주석 참조
        Stamp(t_canvas, _order);

        return t_canvas;
    }

    /// <summary>승격을 되돌려 부모 캔버스의 정렬로 복귀시킨다.
    /// sortingOrder까지 0으로 내리는 이유: Overlay 캔버스의 레이캐스트 우선순위는 overrideSorting이 아니라
    /// sortingOrder가 정해서, 값을 남겨 두면 정렬은 부모를 따르는데 입력만 계속 앞자리를 차지한다.</summary>
    public static void DropNested(Canvas _canvas)
    {
        if (_canvas == null) return;

        // 아직 override가 켜져 있는 동안 0을 찍는다 — 끈 뒤로는 getter가 부모 값을 돌려줘 저장값을 확인할 길이 없다.
        // (LiftNested와 같은 이유로 순서를 고정해 둔다.)
        _canvas.sortingOrder    = 0;
        _canvas.overrideSorting = false;
    }

    /// <summary>_canvas의 층을 이 표의 값으로 못 박는다. 캔버스가 없으면 아무 일도 하지 않는다 —
    /// 프리팹 저작값이 표와 갈려도 코드가 이기게 해서, 읽을 곳을 한 군데로 남긴다.</summary>
    public static void Stamp(Canvas _canvas, int _order)
    {
        if (_canvas == null) return;

        // 조용한 실패를 소리내어 잡는다 — 중첩 캔버스는 overrideSorting이 꺼져 있으면 이 대입을 버린다.
        // 루트 캔버스에 찍는 것은 정상이라(UnlockIntroOverlay) isRootCanvas로 걸러낸다.
        if (_canvas.transform.parent != null && !_canvas.isRootCanvas && !_canvas.overrideSorting)
            Debug.LogWarning($"[UiSortingOrder] '{_canvas.name}'은 중첩 캔버스인데 overrideSorting이 꺼져 있어 층({_order})이 먹지 않는다 — LiftNested를 쓸 것.");

        _canvas.sortingOrder = _order;
    }
}
