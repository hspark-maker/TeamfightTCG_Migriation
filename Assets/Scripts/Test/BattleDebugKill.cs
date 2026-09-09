#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// SROptions에서 호출하는 강제 사망 조작. 멀티에서는 실행하지 않는다.
public static class BattleDebugKill
{
    public static string DescribeSlot(bool _enemy, int _slot)
    {
        BattleFieldView t_view = FindField(_enemy);
        CardInstance t_card = t_view != null ? t_view.Field.GetSlot(_slot) : null;
        return t_card == null ? "빈 슬롯" : $"{Name(t_card)} HP {t_card.hp} (+{t_card.bonusHp})";
    }

    public static void KillSlot(bool _enemy, int _slot)
    {
        if (IsMultiplayer())
        {
            Debug.LogWarning("[BattleDebug] 멀티플레이 중에는 강제 사망을 사용할 수 없습니다.");
            return;
        }
        if (_slot < 0 || _slot >= BattleField.SLOT_COUNT) return;

        BattleFieldView t_view = FindField(_enemy);
        CardInstance t_card = t_view != null ? t_view.Field.GetSlot(_slot) : null;
        if (t_card == null || !t_card.IsAlive)
        {
            Debug.LogWarning("[BattleDebug] 선택한 필드에 살아 있는 카드가 없습니다.");
            return;
        }
        Kill(t_view.Field, t_view, t_card);
    }

    static BattleFieldView FindField(bool _enemy)
    {
        foreach (var t_view in Object.FindObjectsByType<BattleFieldView>(FindObjectsSortMode.None))
        {
            if (t_view.Field == null) continue;
            bool t_enemy = t_view.Field.OwnerIndex != TurnState.LocalOwnerIndex;
            if (t_enemy == _enemy) return t_view;
        }
        return null;
    }

    /// <summary>전투와 같은 순서로 죽인다 — 피해를 체력만큼 넣고 필드 정리를 돌린다.
    /// 추가 체력(덩치)까지 함께 깎아야 한 번에 죽는다. 무적·보호막이 각각 한 타를 삼킬 수 있어 최대 세 번 넣는다.</summary>
    static void Kill(BattleField _field, BattleFieldView _view, CardInstance _card)
    {
        _card.TakeDamage(int.MaxValue);
        if (_card.IsAlive) _card.TakeDamage(int.MaxValue);   // 무적/보호막 1회 소진분
        if (_card.IsAlive) _card.TakeDamage(int.MaxValue);   // 둘 다 있었을 때 남은 1회

        // [Lethal] → [Removed] → 슬롯 제거. 전투와 같은 함수라 훅 순서가 갈라지지 않는다.
        AttackProcessor.RemoveDead(_field.State);

        // 죽인 쪽 필드만 다시 그린다. 회복이 건너편까지 갔더라도(유산) 다음 턴 갱신이 따라잡는다 —
        // 여기서 모든 필드를 훑으면 디버그가 뷰 갱신 규칙의 두 번째 진실원이 된다.
        if (_view != null) _view.Refresh();
    }

    /// <summary>러너가 살아 있으면 멀티다(NetworkSession 없는 씬에서도 안전하게 false).
    /// 판정 기준은 GameInitializer가 모드를 가르는 것과 같은 <c>Runner.IsRunning</c>이다.</summary>
    static bool IsMultiplayer()
        => NetworkSession.Instance != null
        && NetworkSession.Instance.Runner != null
        && NetworkSession.Instance.Runner.IsRunning;

    static string Name(CardInstance _card) => _card != null ? CardCatalog.RequireSpec(_card.cardId).DisplayName : "?";
}
#endif
