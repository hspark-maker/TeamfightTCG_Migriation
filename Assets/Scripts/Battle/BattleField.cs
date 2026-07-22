using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class BattleField : MonoBehaviour
{
    public const int SLOT_COUNT = 3;

    CardInstance[] slots = new CardInstance[SLOT_COUNT];
    Queue<CardInstance> waitingQueue = new Queue<CardInstance>();
    int ownerIndex;
    HealerEffect healerEffect;

    public int OwnerIndex => this.ownerIndex;
    public int WaitingCount => this.waitingQueue.Count;
    public bool IsEmpty => !HasAnyCard();

    /// <summary>이 덱으로 산출된 시너지 상태. 배틀 시작 시 1회 확정, 전투 중 불변. UI(SynergyPanelUI) 참조용.</summary>
    public SynergyState Synergy { get; private set; }

    void OnDestroy() => this.healerEffect?.Unsubscribe();

    public void Initialize(List<CardData> _deckData, int _ownerIndex)
    {
        this.ownerIndex = _ownerIndex;
        this.slots = new CardInstance[SLOT_COUNT];
        this.waitingQueue.Clear();
        this.healerEffect?.Unsubscribe();
        this.healerEffect = new HealerEffect(this);

        List<CardData> t_shuffled = new List<CardData>(_deckData);
        Shuffle(t_shuffled);

        for (int i = 0; i < t_shuffled.Count; i++)
        {
            var t_card = new CardInstance(t_shuffled[i], this.ownerIndex);
            if (i < SLOT_COUNT)
            {
                t_card.isRevealed = true;
                t_card.wasEverRevealed = true;
                t_card.slotIndex = i;
                this.slots[i] = t_card;
                t_card.data.passive?.OnSpawn(t_card).Forget();
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible);
            }
            else
            {
                this.waitingQueue.Enqueue(t_card);
            }
        }
    }

    // 빈 슬롯에 대기 카드 순서대로 배치. 채운 카드 목록 반환.
    public List<CardInstance> FillEmptySlots()
    {
        var t_placed = new List<CardInstance>();
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (this.slots[i] == null && this.waitingQueue.Count > 0)
            {
                var t_card = this.waitingQueue.Dequeue();
                bool t_cunningReturn = t_card.savedHp >= 0 && t_card.HasKeyword(CardKeyword.Cunning);
                if (t_card.savedHp >= 0)
                {
                    t_card.hp         = t_card.savedHp;
                    t_card.bonusHp    = t_card.savedBonusHp;
                    t_card.savedHp    = -1;
                    t_card.savedBonusHp = -1;
                }
                t_card.isRevealed = true;
                t_card.wasEverRevealed = true;
                t_card.slotIndex = i;
                this.slots[i] = t_card;
                t_card.data.passive?.OnSpawn(t_card).Forget();
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible) || t_cunningReturn;
                t_placed.Add(t_card);
            }
        }
        return t_placed;
    }

    public CardInstance SwapWithWaiting(CardInstance _card)
    {
        if (this.waitingQueue.Count == 0) return null;

        int t_slot = _card.slotIndex;
        if (t_slot < 0 || t_slot >= SLOT_COUNT) return null;

        CardInstance t_next = this.waitingQueue.Dequeue();
        t_next.isRevealed      = true;
        t_next.wasEverRevealed = true;
        t_next.slotIndex       = t_slot;
        this.slots[t_slot]     = t_next;
        t_next.data.passive?.OnSpawn(t_next).Forget();
        t_next.justSpawned = t_next.HasKeyword(CardKeyword.Invincible);

        _card.savedHp      = _card.data.maxHp;
        _card.savedBonusHp = _card.data.bonusHp;
        _card.slotIndex   = -1;
        _card.isRevealed  = false;
        this.waitingQueue.Enqueue(_card);
        return t_next;
    }

    public bool CanSwapWithWaiting(CardInstance _card)
    {
        if (this.waitingQueue.Count == 0) return false;
        int t_slot = _card?.slotIndex ?? -1;
        return t_slot >= 0 && t_slot < SLOT_COUNT;
    }

    public void RemoveCard(int _slotIndex)
    {
        if (this.slots[_slotIndex] != null)
            this.slots[_slotIndex].slotIndex = -1;
        this.slots[_slotIndex] = null;
    }

    /// <summary>셔플된 카드 ID 배열 반환. 배틀 초기화 후 broadcast용.</summary>
    public int[] GetShuffledIds(CardRegistry _registry)
    {
        var t_ids = new System.Collections.Generic.List<int>();
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (this.slots[i] != null)
                t_ids.Add(_registry.GetId(this.slots[i].data));
        }
        foreach (CardInstance t_c in this.waitingQueue)
            t_ids.Add(_registry.GetId(t_c.data));
        return t_ids.ToArray();
    }

    /// <summary>상대방에게 받은 카드 ID 배열로 enemyField 재구성. 셔플 동기화용.</summary>
    public void InitializeFromRemote(int[] _ids, int _ownerIndex, CardRegistry _registry)
    {
        this.ownerIndex = _ownerIndex;
        this.slots = new CardInstance[SLOT_COUNT];
        this.waitingQueue.Clear();
        this.healerEffect?.Unsubscribe();
        this.healerEffect = new HealerEffect(this);

        for (int i = 0; i < _ids.Length; i++)
        {
            CardData t_data = _registry.GetData(_ids[i]);
            if (t_data == null) continue;
            var t_card = new CardInstance(t_data, _ownerIndex);
            if (i < SLOT_COUNT)
            {
                t_card.slotIndex       = i;
                t_card.isRevealed      = true;
                t_card.wasEverRevealed = true;
                this.slots[i] = t_card;
                t_card.data.passive?.OnSpawn(t_card).Forget();
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible);
            }
            else
            {
                this.waitingQueue.Enqueue(t_card);
            }
        }
    }

    /// <summary>RPC로 받은 상대 카드 정보를 직접 슬롯에 배치. 대기 큐를 소비하지 않음.</summary>
    public CardInstance PlaceCardDirectly(int _slot, CardData _data)
    {
        if (_data == null || _slot < 0 || _slot >= SLOT_COUNT) return null;
        var t_card = new CardInstance(_data, this.ownerIndex);
        // 원격 스폰은 새 인스턴스라 시너지가 없음 → 이미 확정된 필드 시너지를 재적용(재계산 아님).
        // 소유 클라의 FillEmptySlots 인스턴스와 스탯·키워드를 일치시켜 멀티 divergence 방지.
        if (this.Synergy != null)
            SynergyApplier.ApplyAll(this.Synergy, new[] { t_card });
        t_card.slotIndex       = _slot;
        t_card.isRevealed      = true;
        t_card.wasEverRevealed = true;
        this.slots[_slot] = t_card;
        t_card.data.passive?.OnSpawn(t_card).Forget();
        t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible) || t_card.HasKeyword(CardKeyword.Cunning);
        // 원격 스폰 수신 시 대기 큐 소비 → IsEmpty / WaitingCount 동기화
        if (this.waitingQueue.Count > 0)
            this.waitingQueue.Dequeue();
        return t_card;
    }

    /// <summary>
    /// 덱 시너지를 산출해 이 필드의 모든 카드(슬롯+대기)에 1회 적용. 배틀 시작 시 호출.
    /// 시너지는 덱 확정으로 결정되므로 전투 중 재계산 없음. 산출 결과는 Synergy에 보관.
    /// 멀티 결정론: 양 클라가 동일 덱(동일 CardData 집합)으로 Resolve → 동일 결과.
    /// </summary>
    public void ApplyDeckSynergy()
    {
        var t_cards = new List<CardInstance>(GetActiveCards());
        t_cards.AddRange(this.waitingQueue);

        this.Synergy = SynergyResolver.Resolve(t_cards.ConvertAll(c => c.data));
        SynergyApplier.ApplyAll(this.Synergy, t_cards);
    }

    public CardInstance GetSlot(int _index) => this.slots[_index];
    public IEnumerable<CardInstance> GetWaitingCards() => this.waitingQueue;

    public List<CardInstance> GetActiveCards()
    {
        var t_result = new List<CardInstance>();
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (this.slots[i] != null)
                t_result.Add(this.slots[i]);
        }
        return t_result;
    }

    // Taunt 카드가 있으면 그것만 반환, 없으면 전체 반환
    public List<CardInstance> GetValidTargets()
    {
        var t_all   = GetActiveCards();
        var t_taunt = t_all.FindAll(c => c.data.HasKeyword(CardKeyword.Taunt));
        return t_taunt.Count > 0 ? t_taunt : t_all;
    }

    bool HasAnyCard()
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (this.slots[i] != null) return true;
        }
        return this.waitingQueue.Count > 0;
    }

    static void Shuffle(List<CardData> _list)
    {
        for (int i = _list.Count - 1; i > 0; i--)
        {
            int t_j = Random.Range(0, i + 1);
            (_list[i], _list[t_j]) = (_list[t_j], _list[i]);
        }
    }
}
