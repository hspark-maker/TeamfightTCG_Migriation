using TeamfightTCG.BattleCore;
using UnityEngine;

/// <summary>순수 BattleEvent를 현재 Unity 카드 뷰에 투영한다.</summary>
public static class BattleEventPresenter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Install()
    {
        BattleEventStream.Published -= OnPublished;
        BattleEventStream.Published += OnPublished;
    }

    // Action<BattleEvent>는 선택적 인자를 가진 Present에 직접 붙지 않는다(CS0123).
    // 캡처 밖에서 흘러온 이벤트는 뷰를 지정할 수 없으므로 슬롯 조회에만 의존한다.
    static void OnPublished(BattleEvent _event) => Present(_event, null);

    public static void Present(BattleEvent _event) => Present(_event, null);

    public static void Present(BattleEvent _event, CardView _preferredView)
    {
        CardView t_view = _preferredView ?? BattleBoardView.GetView(_event.OwnerIndex, _event.SlotIndex);
        if (t_view == null) return;

        switch (_event.Kind)
        {
            case BattleEventKind.Heal:
                if ((_event.Flags & BattleEventFlags.Deferred) != 0) t_view.DeferHpDisplay(_event.Value);
                else t_view.PlayHealEffect(_event.Value);
                break;
            case BattleEventKind.ShieldChanged:
                t_view.SetShieldVisible((_event.Flags & BattleEventFlags.Visible) != 0);
                break;
            case BattleEventKind.ShieldBroken:
                t_view.PlayShieldBreakEffect();
                break;
        }
    }
}
