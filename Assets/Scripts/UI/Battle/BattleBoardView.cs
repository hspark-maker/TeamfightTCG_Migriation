using System.Collections.Generic;

/// <summary>보드에 존재하는 <see cref="CardView"/> 전체를 대상으로 하는 연출 서비스.
/// 카드 한 장의 상태가 아니라 "판 전체"를 다루는 책임(뷰 레지스트리 / 일괄 페이드)만 여기 있다.
///
/// TODO(E4): BattleFieldView.slotViews와 중복 레지스트리. GetView는 O(n) 선형탐색,
/// BattleFieldView.GetSlotView(slotIndex)가 이미 존재.</summary>
public static class BattleBoardView
{
    static readonly List<CardView> views = new List<CardView>();

    /// <summary>등록 순서(=Awake 순서) 그대로인 뷰 목록. 순회 전용 — 등록/해제는 Register/Unregister로만.</summary>
    public static IReadOnlyList<CardView> Views => views;

    // ForcedAttacker 활성 시 나머지 로컬 카드에 적용할 암전 alpha. 일반 전투(처형 재무장)는 0.3,
    // 튜토리얼은 "그 카드 말고 다 검게" 위해 더 낮은 값으로 덮어쓴다(PlayerTurn이 설정).
    public static float ForcedDimAlpha = 0.3f;

    public static void Register(CardView _view)   => views.Add(_view);
    public static void Unregister(CardView _view) => views.Remove(_view);

    public static CardView GetView(CardInstance _card)
    {
        foreach (CardView t_cv in views)
            if (t_cv.BoundCard == _card) return t_cv;
        return null;
    }

    public static CardView GetView(int _ownerIndex, int _slotIndex)
    {
        if (_slotIndex < 0) return null;
        foreach (CardView t_cv in views)
        {
            if (t_cv != null && t_cv.RenderedOwnerIndex == _ownerIndex && t_cv.RenderedSlotIndex == _slotIndex)
                return t_cv;
        }
        return null;
    }

    static bool IsAliveView(CardView _cv)
        => _cv != null && _cv.BoundCard != null && _cv.BoundCard.IsAlive;

    public static void FadeAll(float _alpha)
    {
        foreach (CardView t_cv in views)
        {
            if (!IsAliveView(t_cv)) continue;
            t_cv.FadeView(_alpha, GameTiming.Battle.FadeViewDuration);
        }
    }

    public static void FadeTeam(float _alpha, int _ownerIndex)
    {
        foreach (CardView t_cv in views)
        {
            if (!IsAliveView(t_cv)) continue;
            if (t_cv.BoundCard.ownerIndex == _ownerIndex)
                t_cv.FadeView(_alpha, GameTiming.Battle.FadeViewDuration);
        }
    }

    // 호출부가 뷰를 직접 지목한 경우다 — 생사 게이트를 걸지 않는다.
    // resolve/present 뒤집기 이후 공격 연출이 시작될 때 이번 타격의 피해자는 이미 IsAlive=false다.
    // 여기서 걸러내면 암전(FadeAll)만 먹고 복구를 못 받아 죽는 카드가 어두운 채로 사망 연출을 탄다.
    public static void FadeCards(float _alpha, params CardView[] _cards)
    {
        foreach (CardView t_cv in _cards)
        {
            if (t_cv == null || t_cv.BoundCard == null) continue;
            t_cv.FadeView(_alpha, GameTiming.Battle.FadeViewDuration);
        }
    }

    public static void RestoreAllFades()
    {
        FadeAll(1f);

        // 공격자 지정: 로컬 팀을 암전하고 공격자만 밝게.
        if (TurnState.ForcedAttacker != null)
        {
            FadeTeam(ForcedDimAlpha, TurnState.LocalOwnerIndex);
            CardView t_forced = GetView(TurnState.ForcedAttacker);
            if (t_forced != null) FadeCards(1f, t_forced);
        }

        // 타깃 지정(튜토리얼): 적 팀을 암전하고 지정 타깃만 밝게 — "이 적을 쳐라" 집중 유도.
        if (TurnState.ForcedTarget != null)
        {
            FadeTeam(ForcedDimAlpha, 1 - TurnState.LocalOwnerIndex);
            CardView t_target = GetView(TurnState.ForcedTarget);
            if (t_target != null) FadeCards(1f, t_target);
        }
    }

    /// <summary>전투 종료 리셋. 호출 지점은 CardView.Cleanup() 하나뿐(BattleCleanup.Run이 그걸 부른다).</summary>
    public static void Cleanup()
    {
        ForcedDimAlpha = 0.3f;
        views.Clear();
    }
}
