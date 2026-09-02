using UnityEngine;

/// <summary>해금 데모가 숫자를 내는 유일한 창구. 전부 표시 전용이라 모델(CardInstance)의 체력은 바뀌지 않는다.</summary>
public static class DemoHpDisplay
{
    /// <summary>회복을 보여주기 전에 표기를 그만큼 낮추고, 실제로 낮춘 양을 돌려준다.</summary>
    // 만피에서 회복하면 숫자가 한 칸도 안 움직인다. 0까지 떨어뜨리지 않는 것은 죽은 것으로 읽히기 때문이다.
    public static int WoundDisplay(CardView _view, int _amount)
    {
        if (_view == null || _view.BoundCard == null) return 0;

        CardInstance t_card  = _view.BoundCard;
        int          t_wound = Mathf.Clamp(_amount, 1, Mathf.Max(1, t_card.hp - 1));

        _view.OverrideHpDisplay(t_card.hp - t_wound, t_card.bonusHp);
        return t_wound;
    }

    /// <summary>추가 생명력 "+N"만 표기에 얹는다(체력은 모델 값 그대로).</summary>
    public static void ShowBonusHp(CardView _view, int _amount)
    {
        if (_view == null || _view.BoundCard == null || _amount <= 0) return;
        _view.OverrideHpDisplay(_view.BoundCard.hp, _view.BoundCard.bonusHp + _amount);
    }

    /// <summary>표기를 모델 값으로 되돌린다(유예해 둔 회복 표기도 함께 푼다).</summary>
    public static void SnapHpDisplay(CardView _view)
    {
        if (_view == null || _view.BoundCard == null) return;
        _view.OverrideHpDisplay(_view.BoundCard.hp, _view.BoundCard.bonusHp);
    }

    /// <summary>비늘 감쇄를 반영한 피격 표기.</summary>
    // 감쇄 식과 하한의 진실원은 CardInstance라 값만 얹고 PreviewAfterDamage에 맡긴다 — 여기서 다시 빼면 규칙이 두 곳으로 갈린다.
    // ApplySynergy는 필드 가산이라 Emit을 타지 않고 이 카드는 매 바퀴 새로 세워지지만, 이중 적용을 막으려 ClearSynergy를 먼저 건다.
    public static void ShowReducedHit(CardView _view, CardView _attacker, int _reduction)
    {
        if (_view == null || _view.BoundCard == null || _attacker == null || _attacker.BoundCard == null) return;
        if (_reduction <= 0) return;

        CardInstance t_card = _view.BoundCard;
        t_card.ClearSynergy();
        t_card.ApplySynergy(0, CardKeyword.None, _reduction);

        // 공격력이 체력에서 나오므로 실제 값을 쓰면 한 방에 0으로 표기된다 — "얼마면 죽는가"는 규칙(WouldDieFrom)에게만 묻는다.
        int t_raw = _attacker.BoundCard.AttackDamage();
        while (t_raw > 0 && t_card.WouldDieFrom(t_raw)) t_raw--;
        if (t_raw <= 0) return;

        (int t_hp, int t_bonusHp) = t_card.PreviewAfterDamage(t_raw);

        _view.OverrideHpDisplay(t_hp, t_bonusHp);
    }
}
