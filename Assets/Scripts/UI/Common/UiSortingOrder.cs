using UnityEngine;

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
/// ⚠ 프리팹에 저작된 Canvas.sortingOrder도 이 표를 따른다. 각 항목의 괄호가 그 값을 들고 있는 자리다 —
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

    /// <summary>UIPoolManager의 UI 컨테이너(Boot.prefab). 무대가 아니라 풀린 UI가 담기는 자리라
    /// 비어 있어도 항상 켜져 있다 — 이 표가 생긴 이유다.</summary>
    public const int Pool = 400;

    /// <summary>보상 수령 팝업(RewardClaimPopup.prefab).</summary>
    public const int RewardClaim = 410;

    /// <summary>설정 화면(SettingUI.prefab·SettingsPanel).</summary>
    public const int Setting = 900;

    /// <summary>씬을 갈아 끼우는 커튼(SceneCurtain.prefab).</summary>
    public const int Curtain = 950;

    /// <summary>로딩 커버(LoadingCover.prefab).</summary>
    public const int LoadingCover = 1000;

    /// <summary>화면 전체를 덮는 번쩍임(ScreenFlash). 그 밑에서 화면을 갈아치우는 것이 목적이라 무엇보다 위다.</summary>
    public const int ScreenFlash = 32000;

    /// <summary>_canvas의 층을 이 표의 값으로 못 박는다. 캔버스가 없으면 아무 일도 하지 않는다 —
    /// 프리팹 저작값이 표와 갈려도 코드가 이기게 해서, 읽을 곳을 한 군데로 남긴다.</summary>
    public static void Stamp(Canvas _canvas, int _order)
    {
        if (_canvas == null) return;

        _canvas.sortingOrder = _order;
    }
}
