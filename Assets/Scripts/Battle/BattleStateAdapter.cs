using System;
using System.Collections.Generic;
using TeamfightTCG.BattleCore;

/// <summary>현재 Unity 전투 상태를 헤드리스 코어 DTO로 복사하는 <b>단방향</b> 어댑터.
///
/// <para><b>아직 전투 경로에 연결돼 있지 않다</b>(호출부 0건). 재시뮬 리졸버를 붙일 때 쓸 계약 초안이며,
/// 지금 형태로는 <b>턴 진행 상태가 빠져 있다</b> — 현재 공격자·수비자, 입력 허용 여부,
/// 진영별 활성 시너지 집합, 처형·파생 공격 대기열이 없다. 붙이기 전에 그 목록부터 채워야 한다.</para>
///
/// <para>역방향(코어 → Unity) 쓰기 경로는 만들지 않는다. 두 곳에서 상태가 따로 변하면
/// 어느 쪽이 진실원인지 사라지고 divergence 원인을 추적할 수 없게 된다.</para></summary>
public static class BattleStateAdapter
{
    public static BattleState Capture(BattleField _first, BattleField _second, int _firstOwner)
    {
        var t_state = new BattleState
        {
            Seed = MatchRandom.InitialSeed,
            RandomDrawCount = MatchRandom.DrawCount,
            Turn = TurnRunner.TurnCount,
            FirstOwner = _firstOwner,
        };
        CaptureField(_first, t_state);
        CaptureField(_second, t_state);
        return t_state;
    }

    static void CaptureField(BattleField _field, BattleState _state)
    {
        if (_field == null) return;
        BattleFieldState t_field = _field.State;
        if (t_field.OwnerIndex < 0 || t_field.OwnerIndex >= BattleState.SideCount) return;
        int t_owner = t_field.OwnerIndex;
        for (int t_slot = 0; t_slot < BattleState.SlotCount; t_slot++)
        {
            CardInstance t_card = t_field.GetSlot(t_slot);
            _state.Slots[t_owner][t_slot] = t_card == null ? null : ToCore(t_card);
        }

        var t_waiting = new List<BattleCardState>();
        foreach (CardInstance t_card in t_field.GetWaitingCards())
            if (t_card != null) t_waiting.Add(ToCore(t_card));
        _state.Waiting[t_owner] = t_waiting.ToArray();

        var t_fallen = new int[t_field.FallenCards.Count];
        for (int t_i = 0; t_i < t_fallen.Length; t_i++)
            t_fallen[t_i] = t_field.FallenCards[t_i];
        _state.FallenCardIds[t_owner] = t_fallen;
        _state.FlowStacks[t_owner] = t_field.FlowStack;
    }

    static BattleCardState ToCore(CardInstance _card) => new BattleCardState
    {
        CardId = _card.cardId,
        OwnerIndex = _card.ownerIndex,
        SlotIndex = _card.slotIndex,
        BaseMaxHp = _card.spec.MaxHp,
        Hp = _card.hp,
        MaxHp = _card.maxHp,
        BonusHp = _card.bonusHp,
        AttackCount = _card.attackCount,
        FlowBonus = _card.flowBonus,
        LegacyStack = _card.legacyStack,
        SynergyDamageReduction = _card.synergyDmgReduction,
        EvolutionStage = _card.evolutionStage,
        UnlockedKeywords = _card.unlockedKeywords,
        RuntimeKeywords = _card.runtimeKeywords,
        SynergyKeywords = _card.synergyKeywords,
        SynergyEnabled = _card.synergyEnabled,
        ReviveUsed = _card.reviveUsed,
        HasShield = _card.hasShield,
        ReturnedFromField = _card.returnedFromField,
        JustSpawned = _card.justSpawned,
        IsRevealed = _card.isRevealed,
        WasEverRevealed = _card.wasEverRevealed,
        CinematicShown = _card.cinematicShown,
        CinemaAttackUsed = _card.cinemaAttackUsed,
    };
}
