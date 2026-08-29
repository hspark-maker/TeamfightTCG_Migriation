using System;
using System.Collections.Generic;
using TeamfightTCG.BattleCore;

/// <summary>
/// 전투 필드의 가변 데이터만 소유한다. Unity 수명과 시너지 훅은 <see cref="BattleField"/>가 담당한다.
/// 아직 CardInstance가 게임 어셈블리에 있으므로 BattleCore 이전 전의 중간 경계다.
/// </summary>
public sealed class BattleFieldState
{
    public const int SlotCount = BattleState.SlotCount;

    // private 이다 — internal 로 열면 어셈블리 어디서든 Slots[i] = null 로 훅(Placed/Entered/BoardChanged)을
    // 건너뛰고 보드를 바꿀 수 있다. 외부는 GetSlot/SetSlot 같은 명시적 진입점만 쓴다.
    CardInstance[] Slots { get; set; } = new CardInstance[SlotCount];
    Queue<CardInstance> WaitingQueue { get; set; } = new Queue<CardInstance>();
    Func<int, CardGrowth> GrowthProvider { get; set; }

    readonly List<int> fallenCards = new List<int>();

    public int OwnerIndex { get; private set; }
    public int WaitingCount => WaitingQueue.Count;
    public int FlowStack { get; private set; }
    public IReadOnlyList<int> FallenCards => this.fallenCards;
    public bool IsEmpty => !HasAnyCard();

    public void Reset(int _ownerIndex, Func<int, CardGrowth> _growthProvider)
    {
        OwnerIndex = _ownerIndex;
        GrowthProvider = _growthProvider;
        Slots = new CardInstance[SlotCount];
        WaitingQueue.Clear();
        this.fallenCards.Clear();
        FlowStack = 0;
    }

    public CardGrowth GrowthOf(int _cardId)
        => GrowthProvider != null ? GrowthProvider(_cardId) : default;

    public void SetSlot(int _slotIndex, CardInstance _card) => Slots[_slotIndex] = _card;
    public void Enqueue(CardInstance _card) => WaitingQueue.Enqueue(_card);
    public CardInstance Dequeue() => WaitingQueue.Dequeue();
    public CardInstance PeekWaiting() => WaitingQueue.Peek();
    public void ReplaceWaiting(IEnumerable<CardInstance> _cards)
        => WaitingQueue = new Queue<CardInstance>(_cards);

    public bool TryFillSlot(int _slotIndex, out CardInstance _card, out bool _cunningReturn)
    {
        _card = null;
        _cunningReturn = false;
        if (_slotIndex < 0 || _slotIndex >= SlotCount || Slots[_slotIndex] != null || WaitingQueue.Count == 0)
            return false;
        _card = WaitingQueue.Dequeue();
        PlaceIncoming(_slotIndex, _card, out _cunningReturn);
        return true;
    }

    public bool TryBeginSwapWithWaiting(CardInstance _outgoing, out CardInstance _incoming,
        out bool _cunningReturn)
    {
        _incoming = null;
        _cunningReturn = false;
        if (WaitingQueue.Count == 0) return false;
        int t_slot = _outgoing?.slotIndex ?? -1;
        if (t_slot < 0 || t_slot >= SlotCount) return false;

        _incoming = WaitingQueue.Dequeue();
        PlaceIncoming(t_slot, _incoming, out _cunningReturn);
        return true;
    }

    public void CompleteSwapWithWaiting(CardInstance _outgoing)
    {
        _outgoing.returnedFromField = true;
        _outgoing.slotIndex = -1;
        _outgoing.isRevealed = false;
        WaitingQueue.Enqueue(_outgoing);
    }

    public CardInstance MulliganSwap(int _slotIndex, int _deckIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= SlotCount || WaitingQueue.Count == 0) return null;
        CardInstance t_out = Slots[_slotIndex];
        if (t_out == null) return null;

        int t_index = BattleMath.Max(0, BattleMath.Min(_deckIndex, WaitingQueue.Count - 1));
        var t_waiting = new List<CardInstance>(WaitingQueue);
        CardInstance t_in = t_waiting[t_index];
        t_waiting.RemoveAt(t_index);

        t_in.isRevealed = true;
        t_in.wasEverRevealed = true;
        t_in.slotIndex = _slotIndex;
        Slots[_slotIndex] = t_in;

        t_out.slotIndex = -1;
        t_out.isRevealed = false;
        t_waiting.Add(t_out);
        WaitingQueue = new Queue<CardInstance>(t_waiting);
        return t_in;
    }

    public bool CanSwapWithWaiting(CardInstance _card)
    {
        if (WaitingQueue.Count == 0) return false;
        int t_slot = _card?.slotIndex ?? -1;
        return t_slot >= 0 && t_slot < SlotCount;
    }

    public bool TryTakeMatchingWaiting(int _cardId, out CardInstance _card)
    {
        if (WaitingQueue.Count > 0 && WaitingQueue.Peek().cardId == _cardId)
        {
            _card = WaitingQueue.Dequeue();
            return true;
        }
        _card = null;
        return false;
    }

    public void PlaceIncoming(int _slotIndex, CardInstance _card, out bool _cunningReturn)
    {
        _cunningReturn = _card.returnedFromField && _card.HasKeyword(CardKeyword.Cunning);
        _card.returnedFromField = false;
        _card.isRevealed = true;
        _card.wasEverRevealed = true;
        _card.slotIndex = _slotIndex;
        Slots[_slotIndex] = _card;
    }

    public CardInstance GetSlot(int _index)
        => _index >= 0 && _index < SlotCount ? Slots[_index] : null;

    public IEnumerable<CardInstance> GetWaitingCards() => WaitingQueue;

    public int ActiveCount
    {
        get
        {
            int t_count = 0;
            for (int t_i = 0; t_i < SlotCount; t_i++)
                if (Slots[t_i] != null)
                    t_count++;
            return t_count;
        }
    }

    public List<CardInstance> GetActiveCards()
    {
        var t_result = new List<CardInstance>();
        for (int t_i = 0; t_i < SlotCount; t_i++)
            if (Slots[t_i] != null)
                t_result.Add(Slots[t_i]);
        return t_result;
    }

    public void RemoveCard(int _slotIndex)
    {
        // 와이어에서 온 raw int 가 여기까지 흘러든다 — 다른 진입점과 같은 규약으로 범위를 먼저 건다.
        if (_slotIndex < 0 || _slotIndex >= SlotCount) return;

        CardInstance t_card = Slots[_slotIndex];
        if (t_card != null)
        {
            t_card.slotIndex = -1;
            this.fallenCards.Add(t_card.cardId);
        }

        Slots[_slotIndex] = null;
    }

    public int[] GetOrderedCardIds()
    {
        var t_ids = new List<int>();
        for (int t_i = 0; t_i < SlotCount; t_i++)
            if (Slots[t_i] != null)
                t_ids.Add(Slots[t_i].cardId);
        foreach (CardInstance t_card in WaitingQueue)
            t_ids.Add(t_card.cardId);
        return t_ids.ToArray();
    }

    public void OverrideAllHp(int _hp)
    {
        if (_hp <= 0) return;
        foreach (CardInstance t_card in GetActiveCards())
        {
            t_card.hp = BattleMath.Min(t_card.hp, _hp);
            t_card.bonusHp = 0;
        }

        foreach (CardInstance t_card in WaitingQueue)
        {
            t_card.hp = BattleMath.Min(t_card.hp, _hp);
            t_card.bonusHp = 0;
        }
    }

    public void AddFlowStack(int _amount) => FlowStack += _amount;

    bool HasAnyCard()
    {
        for (int t_i = 0; t_i < SlotCount; t_i++)
            if (Slots[t_i] != null)
                return true;
        return WaitingQueue.Count > 0;
    }
}
