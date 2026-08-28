using System;
using Cysharp.Threading.Tasks;

public enum EBattleLoopEnd : byte
{
    Forced = 0,
    PlayerWon = 1,
    PlayerLost = 2,

    /// <summary>양쪽 보드가 같은 순간에 비었다. 이걸 따로 두지 않으면 두 클라가 각자
    /// "내가 이겼다"로 판정해(각자 enemyField 를 먼저 본다) 서버에서 winner_conflict 로 튕긴다.</summary>
    Draw = 3,
}

/// <summary>
/// 턴 교대와 규칙 훅 순서만 소유한다. Unity 연출은 executeTurn 콜백 바깥에서 알지 못한다.
/// </summary>
public sealed class BattleLoop
{
    readonly TurnRuleContext rules;
    readonly int startOwner;
    int currentOwner;

    public int TurnCount { get; private set; } = 1;
    public IAiTakeoverContinuable ActiveTurn { get; set; }

    public BattleLoop(TurnRuleContext _rules, int _startOwner)
    {
        rules = _rules ?? throw new ArgumentNullException(nameof(_rules));
        startOwner = _startOwner;
        currentOwner = _startOwner;
    }

    /// <summary>
    /// 첫 행동 owner를 확정한다. 튜토리얼은 스크립트 순서를 위해 RNG를 소비하지 않는다.
    /// 서버 재시뮬레이터도 tutorial 여부를 동일하게 알아야 이후 RNG draw가 일치한다.
    /// </summary>
    public static int DecideFirstOwner(bool _tutorialActive)
        => _tutorialActive ? 0 : MatchRandom.Range(2);

    public async UniTask<EBattleLoopEnd> Run(
        Func<int, UniTask> _executeTurn,
        Func<bool> _forcedEnd,
        Action<int> _afterTurnResolved,
        Action _beforeContinue,
        Action<int> _turnCountChanged)
    {
        if (_executeTurn == null) throw new ArgumentNullException(nameof(_executeTurn));

        while (true)
        {
            BattleField t_field = FieldOf(currentOwner);
            await BeginTurn(t_field);
            await _executeTurn(currentOwner);
            EndTurn(t_field);

            _afterTurnResolved?.Invoke(currentOwner);

            if (_forcedEnd != null && _forcedEnd()) return EBattleLoopEnd.Forced;
            // 동시 전멸을 **먼저** 본다. 개별 판정을 앞에 두면 양 클라가 서로 다른 답을 낸다.
            bool t_enemyEmpty = rules.enemyField.IsEmpty;
            bool t_mineEmpty = rules.playerField.IsEmpty;
            if (t_enemyEmpty && t_mineEmpty) return EBattleLoopEnd.Draw;
            if (t_enemyEmpty) return EBattleLoopEnd.PlayerWon;
            if (t_mineEmpty) return EBattleLoopEnd.PlayerLost;

            _beforeContinue?.Invoke();

            if (currentOwner == 1 - startOwner)
            {
                TurnCount++;
                _turnCountChanged?.Invoke(TurnCount);
                TurnEvents.RaiseTurnCountChanged(TurnCount);
            }

            currentOwner = 1 - currentOwner;
        }
    }

    BattleField FieldOf(int _ownerIndex)
        => rules.playerField.OwnerIndex == _ownerIndex ? rules.playerField : rules.enemyField;

    static async UniTask BeginTurn(BattleField _field)
    {
        TurnEvents.RaiseTurnStarted(_field);
        foreach (CardInstance t_card in _field.GetActiveCards())
        {
            if (t_card.justSpawned)
            {
                t_card.justSpawned = false;
                continue;
            }
            await SynergyTriggers.TurnBegan(new TurnCtx(t_card, _field));
        }
    }

    void EndTurn(BattleField _field)
    {
        foreach (CardInstance t_card in _field.GetActiveCards())
            SynergyTriggers.TurnEnded(new TurnCtx(t_card, _field));

        BattleField t_opposite = ReferenceEquals(_field, rules.playerField)
            ? rules.enemyField : rules.playerField;
        foreach (CardInstance t_card in t_opposite.GetActiveCards()) t_card.ClearShield();
        foreach (CardInstance t_card in t_opposite.GetWaitingCards()) t_card.ClearShield();
    }
}
