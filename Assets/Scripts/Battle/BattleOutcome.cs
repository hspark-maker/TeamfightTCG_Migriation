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

        // 무승부: 승자가 없으므로 랭크를 움직일 근거가 없다. 골드는 패배와 같은 정액분을 준다
        // (판은 끝까지 진행됐으니 무보상은 아니다). _won 은 false 로 흘러 보상 계산이 정액 경로를 탄다.
        bool t_draw = _reason == EMatchEndReason.Draw;

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

        if (t_draw)
        {
            // 랭크는 조회도 적용도 하지 않는다 — 승패 인자를 넘기는 순간 어느 쪽으로든 움직인다.
            RankDelta = 0;
        }
        else
        {
            RankApplyResult t_rank = t_serverPayout
                ? RankManager.PreviewBattleResult(_won, TutorialConfig.IsActive)
                : RankManager.ApplyBattleResult(_won, TutorialConfig.IsActive);
            RankDelta = t_rank.Delta;
            if (!t_serverPayout) RankResultHandoff.Set(t_rank);
        }

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
        // 서버 재시뮬이 대조할 종료 시점 해시. 골든 레코더와 **같은 계산·같은 시점**이어야 한다 —
        // 다르면 규칙이 맞아도 발산으로 보고된다(실제로 그랬다).
        ulong t_endStateHash = BattleStateHash.Compute(rules.playerField, rules.enemyField);
        MatchResultSubmission.TryEnqueue(_won, _remaining, t_opponentRemaining, _rankPointsBefore,
            _reason == EMatchEndReason.Draw, t_endStateHash);
    }
}
