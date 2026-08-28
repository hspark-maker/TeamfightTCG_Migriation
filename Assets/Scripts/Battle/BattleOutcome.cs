using System.Collections.Generic;

/// <summary>전투 결과의 1회 확정, 보상·랭크 적용, 서버 증거 제출을 소유한다.</summary>
public sealed class BattleOutcome
{
    readonly TurnRuleContext rules;

    public bool IsCaptured { get; private set; }
    public CurrencyGain Reward { get; private set; }
    public long RankDelta { get; private set; }
    public List<int> SurvivorCards { get; private set; }
    public List<int> FallenCards { get; private set; }

    public BattleOutcome(TurnRuleContext _rules)
    {
        rules = _rules;
    }

    public bool TryCapture(bool _won, EMatchEndReason _reason)
    {
        if (IsCaptured) return false;
        IsCaptured = true;

        List<CardInstance> t_active = rules.playerField.GetActiveCards();
        int t_remaining = t_active.Count + rules.playerField.WaitingCount;
        SurvivorCards = CollectSurvivorCards(t_active);
        FallenCards = new List<int>(rules.playerField.FallenCards);

        if (TournamentRun.IsActive)
        {
            if (_won) TournamentProgress.MarkRewardPending(TournamentRun.NodeId);
            TournamentResultHandoff.Set(TournamentRun.NodeId, _won);
            Reward = default;
            RankDelta = 0;
            return true;
        }

        long t_rankPointsBefore = RankManager.Points;
        bool t_serverPayout = DeckConfig.IsMultiplayer;
        Reward = t_serverPayout
            ? RewardService.CalculateReward(_won, t_remaining)
            : RewardService.GrantBattleReward(_won, t_remaining);

        if (!t_serverPayout) BattleRewardHandoff.Set(Reward);

        RankApplyResult t_rank = t_serverPayout
            ? RankManager.PreviewBattleResult(_won, TutorialConfig.IsActive)
            : RankManager.ApplyBattleResult(_won, TutorialConfig.IsActive);
        RankDelta = t_rank.Delta;
        if (!t_serverPayout) RankResultHandoff.Set(t_rank);

        SubmitMatchEvidence(_won, t_remaining, t_rankPointsBefore, _reason);
        return true;
    }

    List<int> CollectSurvivorCards(List<CardInstance> _active)
    {
        var t_cards = new List<int>(_active.Count + rules.playerField.WaitingCount);
        for (int i = 0; i < _active.Count; i++) t_cards.Add(_active[i]?.cardId ?? 0);
        foreach (CardInstance t_card in rules.playerField.GetWaitingCards())
            t_cards.Add(t_card?.cardId ?? 0);
        return t_cards;
    }

    void SubmitMatchEvidence(bool _won, int _remaining, long _rankPointsBefore, EMatchEndReason _reason)
    {
        if (!DeckConfig.IsMultiplayer || _reason == EMatchEndReason.DebugForceWin || DeckConfig.AiTakeover)
            return;

        int t_opponentRemaining = rules.enemyField.GetActiveCards().Count + rules.enemyField.WaitingCount;
        MatchResultSubmission.TryEnqueue(_won, _remaining, t_opponentRemaining, _rankPointsBefore);
    }
}
