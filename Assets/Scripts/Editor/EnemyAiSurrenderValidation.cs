using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>실제 치사 예측기로 항복/계속전투 분기와 상태 불변을 검사한다. 에셋·서버 변경 없음.</summary>
public static class EnemyAiSurrenderValidation
{
    [MenuItem("Tools/Battle/Validate AI Surrender")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Run outside play mode.");
        // 테스트 전용 규칙은 호출 흐름 안에서만 설치하고 원래 AsyncLocal 값을 자동 복원한다.
        ExecutionContext.Run(ExecutionContext.Capture(), _ => RunCore(), null);
    }

    static void RunCore()
    {
        SynergyRuleProvider.InstallScoped(new TestProvider());
        int t_cases = 0;
        Check("last card loses every attack", Field(1, Card(1, 2)), Field(0, Card(0, 10)), true, ref t_cases);
        Check("can win", Field(1, Card(1, 10)), Field(0, Card(0, 2)), false, ref t_cases);
        Check("simultaneous wipe is draw", Field(1, Card(1, 5)), Field(0, Card(0, 5)), false, ref t_cases);
        Check("ranged avoids counter", Field(1, Card(1, 2, CardKeyword.Ranged)), Field(0, Card(0, 10)), false, ref t_cases);
        Check("marked target avoids counter", Field(1, Card(1, 2)), Field(0, Card(0, 10, CardKeyword.Mark)), false, ref t_cases);
        Check("immortal can revive", Field(1, Card(1, 2, CardKeyword.Immortal)), Field(0, Card(0, 10)), false, ref t_cases);
        Check("invincible can survive", Field(1, Card(1, 2, CardKeyword.Invincible)), Field(0, Card(0, 10)), false, ref t_cases);
        Check("one safe target prevents surrender", Field(1, Card(1, 2)), Field(0, Card(0, 10), Card(0, 1)), false, ref t_cases);
        Check("taunt restricts safe target", Field(1, Card(1, 2)), Field(0, Card(0, 10, CardKeyword.Taunt), Card(0, 1)), true, ref t_cases);
        Check("multiple survivors continue", Field(1, Card(1, 2), Card(1, 2)), Field(0, Card(0, 10)), false, ref t_cases);
        Check("last healer cannot heal itself", Field(1, Card(1, 2, CardKeyword.Healer)), Field(0, Card(0, 10)), true, ref t_cases);
        Check("peerless loses all splash outcomes", Field(1, Card(1, 2, CardKeyword.Peerless)),
            Field(0, Card(0, 10), Card(0, 10), Card(0, 10)), true, ref t_cases);
        Check("peerless simultaneous wipe continues", Field(1, Card(1, 4, CardKeyword.Peerless)),
            Field(0, Card(0, 4), Card(0, 2)), false, ref t_cases);

        BattleFieldState t_ai = Field(1, Card(1, 2));
        t_ai.Enqueue(Card(1, 1));
        Check("waiting card can recover", t_ai, Field(0, Card(0, 10)), false, ref t_cases);
        foreach (SynergyEffect t_effect in new SynergyEffect[] { new BrandSynergyEffect(), new LegacySynergyEffect(), new UnknownEffect() })
        {
            t_ai = Field(1, Card(1, 2));
            t_ai.SetSynergy(new SynergyState(new[] { new ActiveSynergy { Tier = new SynergyTier { effects = new[] { t_effect } } } }));
            Check("uncertain effect " + t_effect.GetType().Name, t_ai, Field(0, Card(0, 10)), false, ref t_cases);
        }

        var t_loop = new BattleLoop(new TurnRuleContext(), 1);
        bool t_beforeCalled = false;
        EBattleLoopEnd t_end = t_loop.Run(
            _ => throw new InvalidOperationException("Attack must not execute after surrender."),
            () => false, null, null, null,
            _ => { t_beforeCalled = true; return true; }).GetAwaiter().GetResult();
        // 필드도 없는 컨텍스트에서 성공해야 BeginTurn보다 먼저 중단됐다는 증거다.
        Require(t_beforeCalled && t_end == EBattleLoopEnd.Forced, "surrender boundary must precede BeginTurn");
        t_cases++;
        Debug.Log($"[EnemyAiSurrenderValidation] PASS {t_cases} cases, including unchanged board/RNG and pre-turn boundary.");
    }

    static void Check(string _name, BattleFieldState _ai, BattleFieldState _opponent, bool _expected, ref int _cases)
    {
        ulong t_hash = BattleStateHash.Compute(_ai, _opponent);
        int t_draws = MatchRandom.DrawCount;
        Require(EnemyAiSurrender.ShouldSurrender(_ai, _opponent) == _expected, _name);
        Require(BattleStateHash.Compute(_ai, _opponent) == t_hash, _name + ": board changed");
        Require(MatchRandom.DrawCount == t_draws, _name + ": RNG consumed");
        _cases++;
    }

    static CardInstance Card(int _owner, int _hp, CardKeyword _keywords = CardKeyword.None)
        => new CardInstance(1, _owner) { hp = _hp, unlockedKeywords = _keywords };

    static BattleFieldState Field(int _owner, params CardInstance[] _cards)
    {
        var t_field = new BattleFieldState();
        t_field.Reset(_owner, null);
        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i].slotIndex = i;
            t_field.SetSlot(i, _cards[i]);
        }
        return t_field;
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException("[EnemyAiSurrenderValidation] " + _message);
    }

    sealed class UnknownEffect : SynergyEffect { }

    sealed class TestProvider : ISynergyRuleProvider
    {
        readonly CardSpec spec = new CardSpec(1, "AiSurrenderTest", "AI surrender test", default, 10,
            CardKeyword.None, 0, 0, 0, 0, 0, "", default, Array.Empty<string>());
        public bool ContainsCard(int _cardId) => _cardId == 1;
        public CardSpec SpecOf(int _cardId) => this.spec;
        public IReadOnlyList<string> SynergyIdsOf(int _cardId) => Array.Empty<string>();
        public IReadOnlyList<SynergyTier> TiersOf(string _synergyId) => Array.Empty<SynergyTier>();
    }
}
