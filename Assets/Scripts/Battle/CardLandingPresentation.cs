using System;
using System.Collections.Generic;

/// <summary>등장 카드가 **슬롯에 내려앉은 뒤**에 보여야 할 표시를 그 카드 앞으로 붙잡아 둔다.
///
/// 규칙(BattleField.NotifyEntered)은 뷰가 덱에서 나와 날아오기 **전에** 끝난다. 그래서 등장 효과의 표시를
/// 규칙 자리에서 바로 내면 카드가 아직 화면 중앙을 날고 있는 동안 엠블럼·숫자가 슬롯에서 터진다.
/// 착지 시점을 아는 것은 뷰뿐이므로(<see cref="CardView.PlayDealToSlot"/>), 표시만 여기 맡겨 두고
/// 그 카드가 앉을 때 <see cref="Flush"/>가 푼다.
///
/// <see cref="BattlePresentationQueue"/>와 축이 다르다 — 저건 "공격 캡처 중이라 접촉 프레임까지" 미루고,
/// 여기는 "그 카드가 자리에 앉을 때까지" 미룬다. 같은 표시가 둘 다 필요하면 착지 쪽이 항상 뒤다.
///
/// **순수 표시만 담는다.** 상태·RNG를 건드리는 코드를 넣으면 결정론이 연출 타이밍에 다시 묶인다.</summary>
public static class CardLandingPresentation
{
    // 카드 한 장에 여러 효과가 붙을 수 있다(돌보미 + 다른 등장 효과) — 예약 순서대로 이어 붙인다.
    static readonly Dictionary<CardInstance, Action> s_pending = new Dictionary<CardInstance, Action>();

    /// <summary>이 카드가 슬롯에 앉을 때 재생할 표시를 예약한다. 카드가 null이면 즉시 재생 —
    /// 기다릴 대상이 없는데 붙잡아 두면 표시가 영영 안 나온다.</summary>
    public static void Enqueue(CardInstance _card, Action _present)
    {
        if (_present == null) return;
        if (_card == null) { _present(); return; }

        s_pending[_card] = s_pending.TryGetValue(_card, out Action t_prev)
            ? t_prev + _present
            : _present;
    }

    /// <summary>그 카드 몫을 재생하고 비운다. 착지 지점(CardView)이 부른다 — 예약이 없으면 무동작.
    /// 하나가 던져도 나머지는 재생한다(표시 하나 때문에 등장 연출이 끊기면 안 된다).</summary>
    public static void Flush(CardInstance _card)
    {
        if (_card == null || !s_pending.TryGetValue(_card, out Action t_present)) return;
        s_pending.Remove(_card);

        foreach (Delegate t_one in t_present.GetInvocationList())
        {
            try { ((Action)t_one)(); }
            catch (Exception t_exception) { UnityEngine.Debug.LogException(t_exception); }
        }
    }

    /// <summary>전투 정리용. 착지를 못 만난 예약이 다음 판으로 새지 않게 한다.</summary>
    public static void Clear() => s_pending.Clear();
}
