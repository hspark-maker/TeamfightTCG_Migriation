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

    /// <summary>흐름 시너지 스택. 흐름 카드가 런타임 등장(NotifyEntered)할 때마다 +1, 카드 flowBonus 재동기의 기준값.
    /// Initialize/InitializeFromRemote에서 0 리셋. 초기배치는 미발화 → 0부터 런타임 등장으로만 성장. 전투 중 파생.</summary>
    public int FlowStack { get; private set; }

    /// <summary>이 덱으로 산출된 시너지 상태. 배틀 시작 시 1회 확정, 전투 중 불변. UI(SynergyPanelUI) 참조용.</summary>
    public SynergyState Synergy { get; private set; }

    void OnDestroy() => this.healerEffect?.Unsubscribe();

    public void Initialize(List<CardData> _deckData, int _ownerIndex)
    {
        this.ownerIndex = _ownerIndex;
        this.slots = new CardInstance[SLOT_COUNT];
        this.waitingQueue.Clear();
        this.FlowStack = 0;
        this.Synergy   = null;   // 인스턴스 재사용(리매치) 시 이전 판 스냅샷으로 Placed가 발화하지 않게
        this.healerEffect?.Unsubscribe();
        this.healerEffect = new HealerEffect(this);

        List<CardData> t_shuffled = new List<CardData>(_deckData);
        if (!TutorialConfig.IsActive)   // 튜토리얼: 무셔플 = 리스트 순서가 곧 등장 순서(결정론)
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
                NotifyPlaced(t_card);   // [Placed] 오프닝 배치 — 등장(Entered) 아님
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
                NotifyEntered(t_card);   // [Entered] 런타임 등장(패시브+시너지). justSpawned 판정 전 — 패시브가 무적 부여 가능.
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
        NotifyEntered(t_next);   // [Entered] 런타임 등장(패시브+시너지).
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
        NotifyBoardChanged();   // 보드 구성 변화 → 라이브 카운트 파생 상태 재동기(성벽 등)
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
        this.FlowStack = 0;
        this.Synergy   = null;   // 인스턴스 재사용(리매치) 시 이전 판 스냅샷으로 Placed가 발화하지 않게
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
                NotifyPlaced(t_card);   // [Placed] 오프닝 배치 — 등장(Entered) 아님
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible);
            }
            else
            {
                this.waitingQueue.Enqueue(t_card);
            }
        }
    }

    /// <summary>
    /// RPC로 받은 상대 스폰을 원격 미러 대기 큐에서 꺼내 슬롯에 배치.
    /// 소유 클라 FillEmptySlots와 100% 동형 — fresh 재파생/시너지 재적용을 하지 않고 미러 인스턴스를
    /// 그대로 배치한다. bonusHp(덩치) 같은 stateful 값을 원격에서 재부여하면 divergence가 나므로,
    /// 이미 ApplyDeckSynergy로 시너지를 받고(초기 대기) / SwapWithWaiting으로 savedBonusHp=base가 저장된
    /// (Cunning 복귀) 미러 인스턴스를 재사용해 소유 클라와 값이 정확히 일치하게 한다.
    /// </summary>
    public CardInstance PlaceCardDirectly(int _slot, CardData _data)
    {
        if (_data == null || _slot < 0 || _slot >= SLOT_COUNT) return null;

        // 미러 대기 인스턴스 dequeue (소유 클라 FillEmptySlots 소비와 lockstep: SwapWithWaiting은 양측이
        // 동일 순서로 큐를 변형, FillEmptySlots dequeue를 이 dequeue가 미러). cardId(참조 동일성)로 정합 검증.
        CardInstance t_card;
        if (this.waitingQueue.Count > 0 && this.waitingQueue.Peek().data == _data)
        {
            t_card = this.waitingQueue.Dequeue();
        }
        else
        {
            // desync 방어(정상 lockstep에선 도달 안 함): fresh 폴백 + 확정 필드 시너지 재적용.
            Debug.LogWarning($"[BattleField] PlaceCardDirectly 미러 큐 불일치 → fresh 폴백 " +
                             $"(slot={_slot}, card={_data.name}, waiting={this.waitingQueue.Count})");
            t_card = new CardInstance(_data, this.ownerIndex);
            if (this.Synergy != null)
                SynergyApplier.ApplyAll(this.Synergy, new[] { t_card });
        }

        // FillEmptySlots와 동형: savedHp/savedBonusHp 복원 + 슬롯 세팅 + justSpawned 판정.
        bool t_cunningReturn = t_card.savedHp >= 0 && t_card.HasKeyword(CardKeyword.Cunning);
        if (t_card.savedHp >= 0)
        {
            t_card.hp          = t_card.savedHp;
            t_card.bonusHp     = t_card.savedBonusHp;
            t_card.savedHp     = -1;
            t_card.savedBonusHp = -1;
        }
        t_card.slotIndex       = _slot;
        t_card.isRevealed      = true;
        t_card.wasEverRevealed = true;
        this.slots[_slot] = t_card;
        NotifyEntered(t_card);   // [Entered] 런타임 등장. 원격 미러도 소유 클라와 동형 발화.
        t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible) || t_cunningReturn;
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
        // [DeckResolved] 패시브 몫. **DeckResolved만 synergy→passive 역순인데 구조적 제약이다** —
        // ApplyAll 안에 ClearSynergy가 있어서 패시브를 먼저 돌리면 패시브가 넣은 정적 스탯이 지워진다.
        // (BattleTimings ◆ 참조.) ctx.state = 확정 스냅샷, ctx.synergy = null. 동기 void.
        foreach (var t_card in t_cards)
            t_card.data.passive?.OnDeckResolved(new DeckCtx(t_card, this.Synergy));

        // [Placed] 시너지 몫. Initialize 시점엔 this.Synergy가 아직 null이라 거기선 발화가 불가능하다
        // (패시브 Placed만 Initialize에서 발화 — justSpawned 판정에 무적 부여를 반영해야 하므로).
        // 그래서 시너지 Placed는 스냅샷이 확정된 여기서 슬롯 카드에 대해 발화한다.
        for (int i = 0; i < SLOT_COUNT; i++)
            if (this.slots[i] != null)
                SynergyTriggers.Placed(new SpawnCtx(this.slots[i], this));

        NotifyBoardChanged();   // 오프닝 배치분에도 라이브 카운트 파생 상태를 깔아준다(Entered는 오프닝에 미발화)
    }

    // [BoardChanged] 필드의 라이브 카드 구성이 바뀐 직후. 발화점은 이 클래스 안 3곳뿐이다:
    // ApplyDeckSynergy(배치 확정) / NotifyEntered(등장) / RemoveCard(제거).
    // "필드의 X 수만큼" 류 효과가 파생 상태를 재동기하는 지점 — 동기 완결, RNG 미소비.
    // 패시브는 라이브 카드마다(self=그 카드), 시너지는 필드당 1회(self=null) — 순서는 패시브 → 시너지.
    void NotifyBoardChanged()
    {
        // IsAlive 게이트 필수 — RemoveDead 루프 중간에 불리면 아직 제거 안 된 시체가 슬롯에 남아 있다.
        // 시너지 쪽(RampartSynergyEffect)도 IsAlive로 거르므로 "라이브"의 정의를 양쪽 일치시킨다.
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            CardInstance t_c = this.slots[i];
            if (t_c == null || !t_c.IsAlive) continue;
            t_c.data.passive?.OnBoardChanged(new BoardCtx(this, t_c));
        }
        SynergyTriggers.BoardChanged(new BoardCtx(this));
    }

    // [Placed] 오프닝 배치 확정 공통 후처리(패시브 → 시너지 순서 고정). 런타임 등장(Entered)과 혼동 금지.
    // 호출 위치는 justSpawned 판정 '전'이어야 한다(효과가 무적을 부여하는 경우를 판정에 반영).
    void NotifyPlaced(CardInstance _card)
    {
        var t_ctx = new SpawnCtx(_card, this);
        _card.data.passive?.OnPlaced(t_ctx).Forget();
        SynergyTriggers.Placed(t_ctx);
    }

    /// <summary>흐름: 스택 +1. 스택 권위는 BattleField 소유(FlowSynergyEffect가 런타임 스폰 시 호출). 순수 산술.</summary>
    public void AddFlowStack() => this.FlowStack++;

    // [Entered] 런타임 등장 공통 후처리(패시브 → 시너지 순서 고정).
    // Placed(오프닝 배치)와 혼동 금지 — 오프닝은 시너지 미발화가 의도(등장=런타임 스폰만).
    // 호출 위치는 justSpawned 판정 '전'이어야 한다(패시브가 무적을 부여하는 경우를 판정에 반영).
    void NotifyEntered(CardInstance _card)
    {
        var t_ctx = new SpawnCtx(_card, this);
        _card.data.passive?.OnEntered(t_ctx).Forget();
        SynergyTriggers.Entered(t_ctx);
        NotifyBoardChanged();   // 등장으로 라이브 구성이 바뀜 → 파생 상태 재동기(성벽 등)
    }

    /// <summary>튜토리얼 확정승: 슬롯+대기 카드의 현재 체력을 _hp 이하로 낮춤(공격력=체력이라 적이 약해짐).
    /// bonusHp 제거. data.maxHp(공유 에셋)는 건드리지 않음 — 인스턴스 hp만.</summary>
    public void OverrideAllHp(int _hp)
    {
        if (_hp <= 0) return;
        foreach (CardInstance t_c in GetActiveCards()) { t_c.hp = Mathf.Min(t_c.hp, _hp); t_c.bonusHp = 0; }
        foreach (CardInstance t_c in this.waitingQueue)  { t_c.hp = Mathf.Min(t_c.hp, _hp); t_c.bonusHp = 0; }
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
