using System;
using System.Collections.Generic;
using TeamfightTCG.BattleCore;

/// <summary>
/// 기록된 전투 명령을 Unity 없이 재생한다. 규칙은 기존 BattleCore 타입에 위임하고,
/// 이 클래스는 라이브 BattleLoop의 턴 순서와 명령 검증만 소유한다.
/// 전용 서버/테스트 프로세스에서 호출하는 계약이며 BattleRuleBridge는 설치하지 않는다.
/// </summary>
public static class BattleReplay
{
    const byte CunningFlag = 1 << 0;
    const byte DerivedFlag = 1 << 1;
    const int MaxCommands = 1024;

    public static BattleReplayResult Run(BattleReplayInput _input)
    {
        if (_input == null) return BattleReplayResult.Fail("input_missing");
        if (_input.Decks == null || _input.Decks.Count != 2)
            return BattleReplayResult.Fail("decks_invalid");
        if (_input.CommandLog == null)
            return BattleReplayResult.Fail("command_log_missing");
        if (_input.CommandLog.Length % BattleCommand.RecordSize != 0)
            return BattleReplayResult.Fail("command_log_size");
        if (_input.CommandLog.Length / BattleCommand.RecordSize > MaxCommands)
            return BattleReplayResult.Fail("command_log_too_long");
        if (!SynergyRuleProvider.TryGetCurrent(out _))
            return BattleReplayResult.Fail("synergy_rule_provider_missing");

        try
        {
            // 재생은 동시 요청을 받는 서비스에서도 돈다 — 전역 시드를 쓰면 요청끼리 스트림을 덮어쓴다.
            MatchRandom.SeedScoped(_input.Seed);
            BattleReplayDeck t_deck0 = FindDeck(_input.Decks, 0);
            BattleReplayDeck t_deck1 = FindDeck(_input.Decks, 1);
            if (t_deck0 == null || t_deck1 == null)
                return BattleReplayResult.Fail("deck_owner_missing");

            if (!TryBuildField(t_deck0, out BattleFieldState t_field0, out string t_reason))
                return BattleReplayResult.Fail(t_reason);
            if (!TryBuildField(t_deck1, out BattleFieldState t_field1, out t_reason))
                return BattleReplayResult.Fail(t_reason);

            using (BattleEventStream.CaptureScope t_eventCapture = BattleEventStream.BeginCapture())
            {
            var t_fields = new[] { t_field0, t_field1 };
            var t_attacksByOwner = new[] { 0, 0 };
            int t_turns = 0;
            int t_firstOwner = MatchRandom.Range(2);
            int t_activeTurn = 0;
            int t_activeOwner = t_firstOwner;
            bool t_turnStarted = false;
            bool t_expectedDerived = false;
            int t_expectedDerivedTarget = -1;
            var t_checkpoints = new List<BattleReplayCheckpoint>();

            for (int t_offset = 0; t_offset < _input.CommandLog.Length; t_offset += BattleCommand.RecordSize)
            {
                if (!BattleCommand.TryRead(_input.CommandLog, t_offset, out BattleCommand t_command))
                    return BattleReplayResult.Fail("command_decode_failed", t_firstOwner, t_fields, t_checkpoints);
                if (t_command.Seq != t_offset / BattleCommand.RecordSize)
                    return BattleReplayResult.Fail("command_sequence_mismatch", t_firstOwner, t_fields, t_checkpoints);
                if (t_command.ActorOwner > 1)
                    return BattleReplayResult.Fail("actor_owner_invalid", t_firstOwner, t_fields, t_checkpoints);

                if (t_command.Kind == BattleCommandKind.Mulligan)
                {
                    if (t_turnStarted || t_command.ActorOwner != 1 - t_firstOwner)
                        return BattleReplayResult.Fail("illegal_mulligan", t_firstOwner, t_fields, t_checkpoints);
                    if (t_command.A >= 0 && !TryMulligan(t_fields[t_command.ActorOwner], t_command.A))
                        return BattleReplayResult.Fail("illegal_mulligan_slot", t_firstOwner, t_fields, t_checkpoints);
                    continue;
                }

                if (t_command.Kind == BattleCommandKind.Surrender)
                    return BattleReplayResult.Success(1 - t_command.ActorOwner, false, t_firstOwner,
                        t_fields, t_checkpoints,
                        BattleReplayStats.Capture(t_eventCapture.Events, t_attacksByOwner, t_turns));
                if (t_command.Kind == BattleCommandKind.AiTakeover)
                    return BattleReplayResult.Fail("ai_takeover_not_authoritative", t_firstOwner, t_fields, t_checkpoints);
                if (t_command.Kind != BattleCommandKind.Attack)
                    return BattleReplayResult.Fail("unsupported_command", t_firstOwner, t_fields, t_checkpoints);

                bool t_derived = (t_command.Flags & DerivedFlag) != 0;
                if (!t_derived)
                {
                    if (t_expectedDerived)
                        return BattleReplayResult.Fail("missing_derived_attack", t_firstOwner, t_fields, t_checkpoints);
                    if (!t_turnStarted)
                    {
                        t_activeTurn = 1;
                        t_turnStarted = true;
                    }
                    else
                    {
                        t_activeOwner = 1 - t_activeOwner;
                        if (t_activeOwner == t_firstOwner) t_activeTurn++;
                    }

                    if (t_command.Turn != t_activeTurn || t_command.ActorOwner != t_activeOwner)
                        return BattleReplayResult.Fail("turn_order_mismatch", t_firstOwner, t_fields, t_checkpoints);
                    BeginTurn(t_fields[t_activeOwner]);
                }
                else
                {
                    if (!t_expectedDerived || t_command.Turn != t_activeTurn || t_command.ActorOwner != t_activeOwner)
                        return BattleReplayResult.Fail("derived_attack_mismatch", t_firstOwner, t_fields, t_checkpoints);
                    if (t_command.B != t_expectedDerivedTarget)
                        return BattleReplayResult.Fail("derived_target_mismatch", t_firstOwner, t_fields, t_checkpoints);
                }

                t_attacksByOwner[t_command.ActorOwner]++;
                if (!t_derived) t_turns++;

                BattleFieldState t_own = t_fields[t_command.ActorOwner];
                BattleFieldState t_enemy = t_fields[1 - t_command.ActorOwner];
                CardInstance t_attacker = t_own.GetSlot(t_command.A);
                CardInstance t_defender = t_enemy.GetSlot(t_command.B);
                if (t_attacker == null || t_defender == null || !t_attacker.IsAlive || !t_defender.IsAlive)
                    return BattleReplayResult.Fail("illegal_attack_slot", t_firstOwner, t_fields, t_checkpoints);
                if (!t_derived && !BattleRules.CanAttack(t_attacker, t_defender, t_enemy.GetActiveCards(), null))
                    return BattleReplayResult.Fail("taunt_target_required", t_firstOwner, t_fields, t_checkpoints);

                CardInstance t_splash = t_attacker.HasKeyword(CardKeyword.Peerless)
                    ? AttackProcessor.PreSelectSplash(t_defender.slotIndex, t_enemy)
                    : null;
                SynergyRuleTriggers.BeforeAttack(new BeforeAttackCtx(t_attacker, t_defender, t_own, t_enemy));

                // 교활 스왑은 **로그 값이 진실원**이다 — 라이브 AttackProcessor 도 `_forceCunningSwap ?? CanSwapWithWaiting`
                // 로 와이어 값을 우선한다(멀티 미러 보장). 골든은 멀티 캡처라 기록된 플래그가 미러 쪽에서
                // 와이어로 온 값일 수 있고, 그때 로컬 재계산과 다를 수 있다. 재계산해서 대조하면
                // 재생기가 게임보다 엄격해져 정상 판을 거절한다.
                bool t_shouldSwap = (t_command.Flags & CunningFlag) != 0;
                // 다만 키워드가 없는데 스왑이 기록됐다면 그건 로그 손상이다 — 그것만 막는다.
                if (t_shouldSwap && !t_attacker.HasKeyword(CardKeyword.Cunning))
                    return BattleReplayResult.Fail("cunning_flag_mismatch", t_firstOwner, t_fields, t_checkpoints);

                AttackResult t_attack = AttackProcessor.Execute(
                    t_attacker, t_defender, t_own, t_enemy, t_splash, t_shouldSwap, t_derived);

                if (t_attacker.IsAlive)
                {
                    SynergyRuleTriggers.AfterAttack(new AfterAttackCtx(
                        t_attacker, t_defender, t_own, t_enemy,
                        t_attack.damageDealt, t_attack.defenderKilled));
                }

                // 라이브 TurnContext.FillSlots와 같은 고정 순서. 공격자 기준으로 뒤집으면
                // owner1 턴에서 두 필드의 Entered/BoardChanged 발화 순서가 달라진다.
                FillEmptySlots(t_field0);
                FillEmptySlots(t_field1);

                t_expectedDerivedTarget = -1;
                t_expectedDerived = ExecutionRule.TryPickNext(
                    in t_attack, t_attacker, t_enemy, out CardInstance t_target);
                if (t_expectedDerived)
                {
                    t_expectedDerivedTarget = t_target.slotIndex;
                }

                if (!t_expectedDerived)
                {
                    EndTurn(t_own, t_enemy);
                    t_checkpoints.Add(new BattleReplayCheckpoint(
                        t_activeTurn, t_activeOwner,
                        BattleStateHash.Compute(t_field0, t_field1), MatchRandom.DrawCount));
                }

                if (Remaining(t_field0) == 0 || Remaining(t_field1) == 0) break;
            }

            int t_remaining0 = Remaining(t_field0);
            int t_remaining1 = Remaining(t_field1);
            if (t_remaining0 > 0 && t_remaining1 > 0)
                return BattleReplayResult.Fail("command_log_incomplete", t_firstOwner, t_fields, t_checkpoints);

            bool t_draw = t_remaining0 == 0 && t_remaining1 == 0;
            int t_winner = t_draw ? -1 : t_remaining0 > 0 ? 0 : 1;
            return BattleReplayResult.Success(t_winner, t_draw, t_firstOwner, t_fields, t_checkpoints,
                BattleReplayStats.Capture(t_eventCapture.Events, t_attacksByOwner, t_turns));
            }
        }
        catch (Exception _exception)
        {
            return BattleReplayResult.Fail("internal_error:" + _exception.GetType().Name);
        }
    }

    static BattleReplayDeck FindDeck(IReadOnlyList<BattleReplayDeck> _decks, int _ownerIndex)
    {
        for (int i = 0; i < _decks.Count; i++)
            if (_decks[i]?.OwnerIndex == _ownerIndex) return _decks[i];
        return null;
    }

    static bool TryBuildField(BattleReplayDeck _deck, out BattleFieldState _field, out string _reason)
    {
        _field = null;
        _reason = null;
        if (_deck.Cards == null || _deck.Cards.Count == 0)
        {
            _reason = $"deck_empty:owner{_deck.OwnerIndex}";
            return false;
        }

        var t_ordered = new List<BattleReplayCard>(_deck.Cards);
        if (_deck.BoardOrder != null)
        {
            if (_deck.BoardOrder.Count == 0)
            {
                _reason = $"board_order_empty:owner{_deck.OwnerIndex}";
                return false;
            }
            if (_deck.BoardOrder.Count != t_ordered.Count)
            {
                _reason = $"board_order_size:owner{_deck.OwnerIndex}";
                return false;
            }

            var t_pool = new List<BattleReplayCard>(t_ordered);
            t_ordered.Clear();
            for (int i = 0; i < _deck.BoardOrder.Count; i++)
            {
                int t_index = t_pool.FindIndex(_card => _card.CardId == _deck.BoardOrder[i]);
                if (t_index < 0)
                {
                    _reason = $"board_order_card:owner{_deck.OwnerIndex}:{_deck.BoardOrder[i]}";
                    return false;
                }
                t_ordered.Add(t_pool[t_index]);
                t_pool.RemoveAt(t_index);
            }
        }
        else
        {
            MatchRandom.DerivedStream t_shuffle = MatchRandom.DeriveDeckStream(_deck.OwnerIndex);
            for (int i = t_ordered.Count - 1; i > 0; i--)
            {
                int t_swap = t_shuffle.Range(i + 1);
                (t_ordered[i], t_ordered[t_swap]) = (t_ordered[t_swap], t_ordered[i]);
            }
        }

        var t_growthById = new Dictionary<int, CardGrowth>();
        for (int i = 0; i < _deck.Cards.Count; i++)
            t_growthById[_deck.Cards[i].CardId] = _deck.Cards[i].Growth;

        _field = new BattleFieldState();
        _field.Reset(_deck.OwnerIndex, _cardId => t_growthById.TryGetValue(_cardId, out CardGrowth t_growth)
            ? t_growth : default);

        var t_instances = new List<CardInstance>(t_ordered.Count);
        for (int i = 0; i < t_ordered.Count; i++)
        {
            BattleReplayCard t_snapshot = t_ordered[i];
            var t_card = new CardInstance(t_snapshot.CardId, _deck.OwnerIndex, t_snapshot.Growth);
            t_instances.Add(t_card);
            if (i < BattleFieldState.SlotCount)
            {
                t_card.slotIndex = i;
                t_card.isRevealed = true;
                t_card.wasEverRevealed = true;
                _field.SetSlot(i, t_card);
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible);
            }
            else
            {
                _field.Enqueue(t_card);
            }
        }

        var t_synergyCards = new List<int>();
        for (int i = 0; i < t_instances.Count; i++)
            if (t_instances[i].synergyEnabled) t_synergyCards.Add(t_instances[i].cardId);
        _field.SetSynergy(SynergyResolver.Resolve(t_synergyCards));
        SynergyApplier.ApplyAll(_field.Synergy, t_instances);
        for (int i = 0; i < BattleFieldState.SlotCount; i++)
        {
            CardInstance t_card = _field.GetSlot(i);
            if (t_card != null) _field.NotifyPlaced(t_card);
        }
        _field.NotifyBoardChanged();
        return true;
    }

    static bool TryMulligan(BattleFieldState _field, int _slot)
    {
        if (_slot < 0 || _slot >= BattleFieldState.SlotCount
            || _field.GetSlot(_slot) == null || _field.WaitingCount == 0) return false;
        int t_index = MatchRandom.Range(_field.WaitingCount);
        CardInstance t_incoming = _field.MulliganSwap(_slot, t_index);
        if (t_incoming == null) return false;
        _field.NotifyPlaced(t_incoming);
        t_incoming.justSpawned = t_incoming.HasKeyword(CardKeyword.Invincible);
        _field.NotifyBoardChanged();
        return true;
    }

    static void BeginTurn(BattleFieldState _field)
    {
        List<CardInstance> t_cards = _field.GetActiveCards();
        var t_healers = new List<CardInstance>();
        for (int i = 0; i < t_cards.Count; i++)
            if (t_cards[i].HasKeyword(CardKeyword.Healer)) t_healers.Add(t_cards[i]);

        for (int i = 0; i < t_healers.Count; i++)
        {
            bool t_fired = false;
            for (int j = 0; j < t_cards.Count; j++)
            {
                if (!ReferenceEquals(t_cards[j], t_healers[i]))
                    t_fired |= t_cards[j].Heal(1, _showEffect: false, _allowOverheal: true) > 0;
            }
            if (t_fired)
                BattleEventStream.Emit(new BattleEvent(BattleEventKind.KeywordFired,
                    t_healers[i].ownerIndex, t_healers[i].slotIndex, (int)CardKeyword.Healer));
        }

        for (int i = 0; i < t_cards.Count; i++)
        {
            CardInstance t_card = t_cards[i];
            if (t_card.justSpawned)
            {
                t_card.justSpawned = false;
                continue;
            }
            SynergyRuleTriggers.TurnBegan(new TurnCtx(t_card, _field));
        }
    }

    static void EndTurn(BattleFieldState _active, BattleFieldState _opposite)
    {
        List<CardInstance> t_activeCards = _active.GetActiveCards();
        for (int i = 0; i < t_activeCards.Count; i++)
            SynergyRuleTriggers.TurnEnded(new TurnCtx(t_activeCards[i], _active));
        foreach (CardInstance t_card in _opposite.GetActiveCards()) t_card.ClearShield();
        foreach (CardInstance t_card in _opposite.GetWaitingCards()) t_card.ClearShield();
    }

    static void FillEmptySlots(BattleFieldState _field)
    {
        for (int i = 0; i < BattleFieldState.SlotCount; i++)
        {
            if (!_field.TryFillSlot(i, out CardInstance t_card, out bool t_cunningReturn)) continue;
            _field.NotifyEntered(t_card);
            t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible) || t_cunningReturn;
        }
    }

    static int Remaining(BattleFieldState _field) => _field.ActiveCount + _field.WaitingCount;
}

public sealed class BattleReplayInput
{
    public ulong Seed { get; set; }
    public IReadOnlyList<BattleReplayDeck> Decks { get; set; }
    public byte[] CommandLog { get; set; }
}

public sealed class BattleReplayDeck
{
    public int OwnerIndex { get; set; }
    public IReadOnlyList<BattleReplayCard> Cards { get; set; }
    public IReadOnlyList<int> BoardOrder { get; set; }
}

public readonly struct BattleReplayCard
{
    public int CardId { get; }
    public CardGrowth Growth { get; }

    public BattleReplayCard(int _cardId, CardGrowth _growth)
    {
        CardId = _cardId;
        Growth = _growth;
    }
}

public readonly struct BattleReplayCheckpoint
{
    public int Turn { get; }
    public int ActingOwner { get; }
    public ulong StateHash { get; }
    public int DrawCount { get; }

    public BattleReplayCheckpoint(int _turn, int _actingOwner, ulong _stateHash, int _drawCount)
    {
        Turn = _turn;
        ActingOwner = _actingOwner;
        StateHash = _stateHash;
        DrawCount = _drawCount;
    }
}

public sealed class BattleReplayResult
{
    public bool Ok { get; private set; }
    public string Reason { get; private set; }
    public int FirstOwner { get; private set; }
    public int WinnerOwner { get; private set; }
    public bool Draw { get; private set; }
    public int[] Remaining { get; private set; } = new[] { 0, 0 };
    /// <summary>각 owner가 상대 필드에서 파괴한 카드 수. 미션 집계는 이 서버 재생 결과만 사용한다.</summary>
    public int[] DestroyedByOwner { get; private set; } = new[] { 0, 0 };
    public ulong FinalStateHash { get; private set; }
    public int DrawCount { get; private set; }
    public IReadOnlyList<BattleReplayCheckpoint> Checkpoints { get; private set; }
        = Array.Empty<BattleReplayCheckpoint>();
    public BattleReplayStats Stats { get; private set; }

    public static BattleReplayResult Fail(string _reason, int _firstOwner = -1,
        BattleFieldState[] _fields = null, IReadOnlyList<BattleReplayCheckpoint> _checkpoints = null)
    {
        var t_result = new BattleReplayResult
        {
            Ok = false,
            Reason = _reason ?? "unknown",
            FirstOwner = _firstOwner,
            WinnerOwner = -1,
            Checkpoints = _checkpoints ?? Array.Empty<BattleReplayCheckpoint>(),
        };
        t_result.Capture(_fields);
        return t_result;
    }

    public static BattleReplayResult Success(int _winnerOwner, bool _draw, int _firstOwner,
        BattleFieldState[] _fields, IReadOnlyList<BattleReplayCheckpoint> _checkpoints,
        BattleReplayStats _stats = null)
    {
        var t_result = new BattleReplayResult
        {
            Ok = true,
            Reason = string.Empty,
            FirstOwner = _firstOwner,
            WinnerOwner = _winnerOwner,
            Draw = _draw,
            Checkpoints = _checkpoints ?? Array.Empty<BattleReplayCheckpoint>(),
            Stats = _stats,
        };
        t_result.Capture(_fields);
        return t_result;
    }

    void Capture(BattleFieldState[] _fields)
    {
        DrawCount = MatchRandom.DrawCount;
        if (_fields == null || _fields.Length != 2 || _fields[0] == null || _fields[1] == null) return;
        Remaining = new[]
        {
            _fields[0].ActiveCount + _fields[0].WaitingCount,
            _fields[1].ActiveCount + _fields[1].WaitingCount,
        };
        DestroyedByOwner = new[]
        {
            _fields[1].FallenCards.Count,
            _fields[0].FallenCards.Count,
        };
        FinalStateHash = BattleStateHash.Compute(_fields[0], _fields[1]);
    }
}

public sealed class BattleReplayStats
{
    public int[] AttacksByOwner { get; private set; } = new[] { 0, 0 };
    public int[] DamageDealtByOwner { get; private set; } = new[] { 0, 0 };
    public int[] HealedByOwner { get; private set; } = new[] { 0, 0 };
    public int[] SynergyFiredByOwner { get; private set; } = new[] { 0, 0 };
    public IReadOnlyDictionary<string, int>[] KeywordsByOwner { get; private set; }
        = new IReadOnlyDictionary<string, int>[]
        {
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
        };
    public int Turns { get; private set; }

    public static BattleReplayStats Capture(IReadOnlyList<BattleEvent> _events,
        int[] _attacksByOwner, int _turns)
    {
        var t_damage = new[] { 0, 0 };
        var t_healed = new[] { 0, 0 };
        var t_synergy = new[] { 0, 0 };
        var t_keywords = new[]
        {
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
        };
        if (_events != null)
        {
            for (int i = 0; i < _events.Count; i++)
            {
                BattleEvent t_event = _events[i];
                if (t_event.Kind == BattleEventKind.Damage &&
                    t_event.SourceOwnerIndex >= 0 && t_event.SourceOwnerIndex <= 1)
                    t_damage[t_event.SourceOwnerIndex] += Math.Max(0, t_event.Value);
                else if (t_event.Kind == BattleEventKind.Heal &&
                    t_event.OwnerIndex >= 0 && t_event.OwnerIndex <= 1)
                    t_healed[t_event.OwnerIndex] += Math.Max(0, t_event.Value);
                else if (t_event.Kind == BattleEventKind.SynergyFired &&
                    t_event.OwnerIndex >= 0 && t_event.OwnerIndex <= 1)
                    t_synergy[t_event.OwnerIndex]++;
                else if (t_event.Kind == BattleEventKind.KeywordFired &&
                    t_event.OwnerIndex >= 0 && t_event.OwnerIndex <= 1)
                {
                    string t_keyword = ((CardKeyword)t_event.Value).ToString();
                    t_keywords[t_event.OwnerIndex].TryGetValue(t_keyword, out int t_count);
                    t_keywords[t_event.OwnerIndex][t_keyword] = t_count + 1;
                }
            }
        }
        return new BattleReplayStats
        {
            AttacksByOwner = _attacksByOwner == null ? new[] { 0, 0 } : (int[])_attacksByOwner.Clone(),
            DamageDealtByOwner = t_damage,
            HealedByOwner = t_healed,
            SynergyFiredByOwner = t_synergy,
            KeywordsByOwner = new IReadOnlyDictionary<string, int>[] { t_keywords[0], t_keywords[1] },
            Turns = Math.Max(0, _turns),
        };
    }
}
