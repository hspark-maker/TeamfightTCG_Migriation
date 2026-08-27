using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>덱 셔플에 어떤 난수를 쓸지 호출부가 정하는 정책. 필드 안에서 모드 플래그를 읽지 않는다.
/// - None : 셔플 안 함. 리스트 순서 = 등장 순서(튜토리얼 저작 순서 보존).
/// - Match: <see cref="MatchRandom"/>(시드 고정 결정론). 호출 전에 시드가 걸려 있어야 한다.
/// - Local: <see cref="UnityEngine.Random"/>. 결과를 와이어로 broadcast하는 멀티 전용 —
///          멀티는 시드 합의(commit-reveal)가 필드 Initialize **뒤**라 Match를 쓸 수 없다.
///          대신 MatchRandom 스트림을 건드리지 않으므로 소비 순서가 어긋나지 않는다.</summary>
public enum ShufflePolicy
{
    None,
    Match,
    Local
}

public class BattleField : MonoBehaviour
{
    public const int SLOT_COUNT = 3;

    CardInstance[] slots = new CardInstance[SLOT_COUNT];
    Queue<CardInstance> waitingQueue = new Queue<CardInstance>();
    int ownerIndex;

    HealerEffect healerEffect;

    // 카드 영구 성장값 조회원. 인덱스 배열이 아니라 델리게이트인 이유는 셔플이 이 클래스 안에서 일어나기 때문 —
    // 카드로 조회하면 셔플 순서와 무관하게 맞는다. null이면 성장 미적용(= 기존 동작).
    System.Func<int, CardGrowth> growthOf;

    // 이번 판에 확정 사망한 카드(슬롯에서 빠진 순서). 필드는 시체를 남기지 않으므로 —
    // RemoveCard가 슬롯을 null로 만들고 나면 그 CardInstance를 아무도 붙잡지 않는다 —
    // 빠지는 그 자리에서 적어두지 않으면 판이 끝난 뒤에는 "무엇을 잃었는가"를 복원할 방법이 없다.
    readonly List<int> fallenCards = new List<int>();

    public int OwnerIndex => this.ownerIndex;
    public int WaitingCount => this.waitingQueue.Count;
    public bool IsEmpty => !HasAnyCard();

    /// <summary>이번 판에 잃은 카드(사망 순서). 결과 화면이 "몇 장 중 몇 장을 지켰는가"를 그리는 출처다.
    /// <b>소비자는 플레이어 필드 하나뿐이다</b> — 상대 필드에도 똑같이 쌓이지만 읽는 곳이 없다
    /// (원격 미러는 상대 전사 목록의 정본이 아니다. 필요해지면 그때 경로를 확인하고 열 것).</summary>
    public IReadOnlyList<int> FallenCards => this.fallenCards;

    /// <summary>흐름 시너지 스택. 흐름 카드가 런타임 등장(NotifyEntered)할 때마다 +1, 카드 flowBonus 재동기의 기준값.
    /// Initialize/InitializeFromRemote에서 0 리셋. 초기배치는 미발화 → 0부터 런타임 등장으로만 성장. 전투 중 파생.</summary>
    public int FlowStack { get; private set; }

    /// <summary>이 덱으로 산출된 시너지 상태. 배틀 시작 시 1회 확정, 전투 중 불변. UI(SynergyPanelUI) 참조용.</summary>
    public SynergyState Synergy { get; private set; }

    void OnDestroy() => this.healerEffect?.Unsubscribe();

    /// <summary>_growthOf = 카드 영구 성장값 공급자(생략/ null이면 성장 미적용).
    /// 싱글·튜토리얼은 로컬 공급자를, 멀티는 양쪽이 교환해 확정한 성장 스냅샷 공급자를 사용한다.</summary>
    public void Initialize(List<int> _deckData, int _ownerIndex, ShufflePolicy _shuffle,
        System.Func<int, CardGrowth> _growthOf = null)
    {
        this.ownerIndex = _ownerIndex;
        this.growthOf = _growthOf;
        this.slots = new CardInstance[SLOT_COUNT];
        this.waitingQueue.Clear();
        this.fallenCards.Clear(); // 리매치로 인스턴스를 재사용하면 직전 판의 전사자가 결과 화면에 얹힌다
        this.FlowStack = 0;
        this.Synergy = null; // 인스턴스 재사용(리매치) 시 이전 판 스냅샷으로 Placed가 발화하지 않게
        this.healerEffect?.Unsubscribe();
        this.healerEffect = new HealerEffect(this);

        // 빈 칸(null)은 여기서 걷어낸다. 덱 출처가 여럿이고(세이브·AI 에셋·튜토리얼 시나리오)
        // 에셋 참조가 끊기면 그 칸이 null로 들어오는데, 그대로 CardInstance를 만들면 NRE로 초기화가
        // 통째로 죽어 "아무것도 안 나오는 전투"가 된다. 한 장 빠진 채로라도 전투는 열려야 한다.
        List<int> t_shuffled = Compact(_deckData, _ownerIndex);
        Shuffle(t_shuffled, _shuffle);

        for (int i = 0; i < t_shuffled.Count; i++)
        {
            var t_card = new CardInstance(t_shuffled[i], this.ownerIndex, GrowthOf(t_shuffled[i]));
            if (i < SLOT_COUNT)
            {
                t_card.isRevealed = true;
                t_card.wasEverRevealed = true;
                t_card.slotIndex = i;
                this.slots[i] = t_card;
                NotifyPlaced(t_card); // [Placed] 오프닝 배치 — 등장(Entered) 아님
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible);
            }
            else
            {
                this.waitingQueue.Enqueue(t_card);
            }
        }
    }

    // 덱 리스트에서 끊긴 참조를 제거한 사본. 빠진 칸은 로그로 남긴다 — 조용히 지우면
    // "덱이 6장인데 5장만 나온다"를 아무도 못 찾는다(에셋 참조가 끊긴 시나리오·AI 덱을 잡는 단서).
    static List<int> Compact(List<int> _deckData, int _ownerIndex)
    {
        var t_cards = new List<int>(_deckData != null ? _deckData.Count : 0);
        if (_deckData == null)
        {
            Debug.LogError($"[BattleField] owner {_ownerIndex} 덱이 null — 빈 필드로 시작한다.");
            return t_cards;
        }

        int t_missing = 0;
        for (int i = 0; i < _deckData.Count; i++)
        {
            if (_deckData[i] <= 0 || !CardCatalog.Contains(_deckData[i]))
            {
                t_missing++;
                continue;
            }

            t_cards.Add(_deckData[i]);
        }

        if (t_missing > 0)
            Debug.LogError(
                $"[BattleField] owner {_ownerIndex} 덱에 빈 칸 {t_missing}개(카드 에셋 참조 끊김) — 그 칸을 빼고 {t_cards.Count}장으로 시작한다. 덱 에셋을 고칠 것.");

        return t_cards;
    }

    // 성장값 조회 단일 지점(미주입이면 default = 미적용). 카드 생성 경로가 늘어도 여기만 통과시킨다.
    CardGrowth GrowthOf(int _cardId) => this.growthOf != null ? this.growthOf(_cardId) : default;

    // 빈 슬롯에 대기 카드 순서대로 배치. 채운 카드 목록 반환.
    public List<CardInstance> FillEmptySlots()
    {
        var t_placed = new List<CardInstance>();
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (this.slots[i] == null && this.waitingQueue.Count > 0)
            {
                var t_card = this.waitingQueue.Dequeue();
                bool t_cunningReturn = t_card.returnedFromField && t_card.HasKeyword(CardKeyword.Cunning);
                t_card.returnedFromField = false;
                t_card.isRevealed = true;
                t_card.wasEverRevealed = true;
                t_card.slotIndex = i;
                this.slots[i] = t_card;
                NotifyEntered(t_card); // [Entered] 런타임 등장(패시브+시너지). justSpawned 판정 전 — 패시브가 무적 부여 가능.
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
        bool t_cunningReturn = t_next.returnedFromField && t_next.HasKeyword(CardKeyword.Cunning);
        t_next.returnedFromField = false;
        t_next.isRevealed = true;
        t_next.wasEverRevealed = true;
        t_next.slotIndex = t_slot;
        this.slots[t_slot] = t_next;
        NotifyEntered(t_next); // [Entered] 런타임 등장(패시브+시너지).
        t_next.justSpawned = t_next.HasKeyword(CardKeyword.Invincible) || t_cunningReturn;

        // 재등장 턴의 TurnBegan 스킵 판정용. 현재 hp/bonusHp는 인스턴스에 그대로 유지한다.
        _card.returnedFromField = true;
        _card.slotIndex = -1;
        _card.isRevealed = false;
        this.waitingQueue.Enqueue(_card);
        return t_next;
    }

    /// <summary>후공 어드밴티지 멀리건: _slotIndex 슬롯 카드를 대기열의 _deckIndex 카드와 교환.
    /// 전투 시작 1회. 스왑-인 카드는 오프닝 배치와 동일한 [Placed] 경로(런타임 등장 [Entered] 아님) —
    /// [Entered]로 발화하면 돌보미/흐름 등 런타임 스폰 보너스가 이 카드에만 붙어 나머지 오프닝 카드와
    /// 비대칭이 생긴다. 시너지는 ApplyDeckSynergy에서 대기 카드까지 이미 적용됨 → 재적용 금지(bonusHp 이중가산).
    /// _deckIndex는 호출부가 MatchRandom으로 산출(결정론, 멀티 확장 대비). 반환: 새로 슬롯에 들어온 카드(실패 시 null).</summary>
    public CardInstance MulliganSwap(int _slotIndex, int _deckIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= SLOT_COUNT) return null;
        if (this.waitingQueue.Count == 0) return null;
        CardInstance t_out = this.slots[_slotIndex];
        if (t_out == null) return null;

        // 대기열에서 _deckIndex 카드 추출. Queue는 임의 위치 제거가 없어 리스트(FIFO 순서) 경유로 재구성 — 양측 동일 알고리즘이라 결정론.
        int t_idx = Mathf.Clamp(_deckIndex, 0, this.waitingQueue.Count - 1);
        var t_list = new List<CardInstance>(this.waitingQueue);
        CardInstance t_in = t_list[t_idx];
        t_list.RemoveAt(t_idx);

        // 스왑-인 배치(오프닝 슬롯 카드와 동형). 시너지 재적용 없음.
        t_in.isRevealed = true;
        t_in.wasEverRevealed = true;
        t_in.slotIndex = _slotIndex;
        this.slots[_slotIndex] = t_in;

        // 스왑-아웃 카드 → 대기열 뒤로. 전투 시작 전 멀리건이므로 교활 복귀 플래그는 설정하지 않는다.
        t_out.slotIndex = -1;
        t_out.isRevealed = false;
        t_list.Add(t_out);

        this.waitingQueue = new Queue<CardInstance>(t_list);

        NotifyPlaced(t_in); // [Placed] 오프닝 배치 경로(패시브 OnPlaced + 시너지 Placed). Initialize+ApplyDeckSynergy가 슬롯 카드에 준 것과 동형.
        t_in.justSpawned = t_in.HasKeyword(CardKeyword.Invincible);
        NotifyBoardChanged(); // 보드 구성 변화 → 라이브 카운트 파생 재동기(Placed는 미발화라 명시 호출).
        return t_in;
    }

    public bool CanSwapWithWaiting(CardInstance _card)
    {
        if (this.waitingQueue.Count == 0) return false;
        int t_slot = _card?.slotIndex ?? -1;
        return t_slot >= 0 && t_slot < SLOT_COUNT;
    }

    /// <summary>슬롯 하나를 비운다. <b>사망 정리 전용</b>이라 여기서 전사 기록을 남긴다 —
    /// 유일한 호출자가 <c>AttackProcessor.RemoveDead</c>의 부활 게이트를 통과한 시체이고,
    /// 교활 되돌림·스왑·멀리건은 슬롯을 <b>덮어쓰므로</b> 이 경로를 타지 않는다.
    /// 사망 외 용도로 이걸 부르기 시작하면 결과 화면의 전사 목록이 조용히 오염된다.</summary>
    public void RemoveCard(int _slotIndex)
    {
        CardInstance t_card = this.slots[_slotIndex];
        if (t_card != null)
        {
            t_card.slotIndex = -1;
            this.fallenCards.Add(t_card.cardId);
        }

        this.slots[_slotIndex] = null;
        NotifyBoardChanged(); // 보드 구성 변화 → 라이브 카운트 파생 상태 재동기
    }

    /// <summary>셔플된 카드 ID 배열 반환. 배틀 초기화 후 broadcast용.</summary>
    public int[] GetShuffledIds()
    {
        var t_ids = new System.Collections.Generic.List<int>();
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (this.slots[i] != null)
                t_ids.Add(this.slots[i].cardId);
        }

        foreach (CardInstance t_c in this.waitingQueue)
            t_ids.Add(t_c.cardId);
        return t_ids.ToArray();
    }

    /// <summary>상대방에게 받은 카드 ID와 최종 성장 스냅샷으로 enemyField를 재구성한다.</summary>
    public void InitializeFromRemote(int[] _ids, CardGrowth[] _growth, int _ownerIndex)
    {
        this.ownerIndex = _ownerIndex;
        var t_growthById = new Dictionary<int, CardGrowth>(_ids?.Length ?? 0);
        if (_ids != null && _growth != null)
        {
            for (int i = 0; i < _ids.Length && i < _growth.Length; i++)
                t_growthById[_ids[i]] = _growth[i];
        }
        this.growthOf = _cardId => t_growthById.TryGetValue(_cardId, out CardGrowth t_value)
            ? t_value
            : default;
        this.slots = new CardInstance[SLOT_COUNT];
        this.waitingQueue.Clear();
        this.fallenCards.Clear();
        this.FlowStack = 0;
        this.Synergy = null; // 인스턴스 재사용(리매치) 시 이전 판 스냅샷으로 Placed가 발화하지 않게
        this.healerEffect?.Unsubscribe();
        this.healerEffect = new HealerEffect(this);

        for (int i = 0; i < _ids.Length; i++)
        {
            int t_cardId = _ids[i];
            if (!CardCatalog.Contains(t_cardId)) continue;
            var t_card = new CardInstance(t_cardId, _ownerIndex, GrowthOf(t_cardId));
            if (i < SLOT_COUNT)
            {
                t_card.slotIndex = i;
                t_card.isRevealed = true;
                t_card.wasEverRevealed = true;
                this.slots[i] = t_card;
                NotifyPlaced(t_card); // [Placed] 오프닝 배치 — 등장(Entered) 아님
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
    /// 그대로 배치한다. hp/bonusHp 같은 stateful 값을 원격에서 재부여하면 divergence가 나므로,
    /// 초기 대기 및 교활 복귀 모두 미러 인스턴스를 재사용해 소유 클라와 값이 정확히 일치하게 한다.
    /// </summary>
    public CardInstance PlaceCardDirectly(int _slot, int _cardId)
    {
        if (!CardCatalog.Contains(_cardId) || _slot < 0 || _slot >= SLOT_COUNT) return null;

        // 미러 대기 인스턴스 dequeue (소유 클라 FillEmptySlots 소비와 lockstep: SwapWithWaiting은 양측이
        // 동일 순서로 큐를 변형, FillEmptySlots dequeue를 이 dequeue가 미러). cardId(참조 동일성)로 정합 검증.
        CardInstance t_card;
        if (this.waitingQueue.Count > 0 && this.waitingQueue.Peek().cardId == _cardId)
        {
            t_card = this.waitingQueue.Dequeue();
        }
        else
        {
            // desync 방어(정상 lockstep에선 도달 안 함): fresh 폴백 + 확정 필드 시너지 재적용.
            Debug.LogError($"[BattleField] PlaceCardDirectly 미러 큐 불일치 → fresh 폴백 " +
                             $"(slot={_slot}, cardId={_cardId}, waiting={this.waitingQueue.Count})");
            t_card = new CardInstance(_cardId, this.ownerIndex, GrowthOf(_cardId)); // 성장원도 Initialize와 같은 소스로
            if (this.Synergy != null)
                SynergyApplier.ApplyAll(this.Synergy, new[] { t_card });
        }

        // FillEmptySlots와 동형: 교활 복귀 플래그 소비 + 슬롯 세팅 + justSpawned 판정.
        bool t_cunningReturn = t_card.returnedFromField && t_card.HasKeyword(CardKeyword.Cunning);
        t_card.returnedFromField = false;
        t_card.slotIndex = _slot;
        t_card.isRevealed = true;
        t_card.wasEverRevealed = true;
        this.slots[_slot] = t_card;
        NotifyEntered(t_card); // [Entered] 런타임 등장. 원격 미러도 소유 클라와 동형 발화.
        t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible) || t_cunningReturn;
        return t_card;
    }

    /// <summary>
    /// 덱 시너지를 산출해 이 필드의 모든 카드(슬롯+대기)에 1회 적용. 배틀 시작 시 호출.
    /// 시너지는 덱 확정으로 결정되므로 전투 중 재계산 없음. 산출 결과는 Synergy에 보관.
    /// 멀티 결정론: 양 클라가 동일 카드 ID 덱으로 Resolve → 동일 결과.
    /// </summary>
    public void ApplyDeckSynergy()
    {
        var t_cards = new List<CardInstance>(GetActiveCards());
        t_cards.AddRange(this.waitingQueue);

        // 성장 카드의 시너지는 1차 진화부터 카운트한다. CardData만 넘기기 전에 인스턴스에서
        // 필터해야 Resolver가 성장 계층을 알지 않아도 되고 기존 순수 집계 계약도 유지된다.
        this.Synergy = SynergyResolver.Resolve(
            t_cards.FindAll(c => c.synergyEnabled).ConvertAll(c => c.cardId));
        SynergyApplier.ApplyAll(this.Synergy, t_cards);
        // [DeckResolved] 패시브 몫. **DeckResolved만 synergy→passive 역순인데 구조적 제약이다** —
        // ApplyAll 안에 ClearSynergy가 있어서 패시브를 먼저 돌리면 패시브가 넣은 정적 스탯이 지워진다.
        // (BattleTimings ◆ 참조.) ctx.state = 확정 스냅샷, ctx.synergy = null. 동기 void.
        // [Placed] 시너지 몫. Initialize 시점엔 this.Synergy가 아직 null이라 거기선 발화가 불가능하다
        // (패시브 Placed만 Initialize에서 발화 — justSpawned 판정에 무적 부여를 반영해야 하므로).
        // 그래서 시너지 Placed는 스냅샷이 확정된 여기서 슬롯 카드에 대해 발화한다.
        for (int i = 0; i < SLOT_COUNT; i++)
            if (this.slots[i] != null)
                SynergyTriggers.Placed(new SpawnCtx(this.slots[i], this));

        NotifyBoardChanged(); // 오프닝 배치분에도 라이브 카운트 파생 상태를 깔아준다(Entered는 오프닝에 미발화)
    }

    // [BoardChanged] 필드의 라이브 카드 구성이 바뀐 직후. 발화점은 이 클래스 안 3곳뿐이다:
    // ApplyDeckSynergy(배치 확정) / NotifyEntered(등장) / RemoveCard(제거).
    // "필드의 X 수만큼" 류 효과가 파생 상태를 재동기하는 지점 — 동기 완결, RNG 미소비.
    // 패시브는 라이브 카드마다(self=그 카드), 시너지는 필드당 1회(self=null) — 순서는 패시브 → 시너지.
    void NotifyBoardChanged()
    {
        // IsAlive 게이트 필수 — RemoveDead 루프 중간에 불리면 아직 제거 안 된 시체가 슬롯에 남아 있다.
        // 파생 보드 상태를 쓰는 효과도 IsAlive로 거르므로 "라이브"의 정의를 양쪽 일치시킨다.
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            CardInstance t_c = this.slots[i];
            if (t_c == null || !t_c.IsAlive) continue;
        }

        SynergyTriggers.BoardChanged(new BoardCtx(this));
    }

    // [Placed] 오프닝 배치 확정 공통 후처리(패시브 → 시너지 순서 고정). 런타임 등장(Entered)과 혼동 금지.
    // 호출 위치는 justSpawned 판정 '전'이어야 한다(효과가 무적을 부여하는 경우를 판정에 반영).
    void NotifyPlaced(CardInstance _card)
    {
        var t_ctx = new SpawnCtx(_card, this);
        SynergyTriggers.Placed(t_ctx);
    }

    /// <summary>흐름: 스택 +1. 스택 권위는 BattleField 소유(FlowSynergyEffect가 런타임 스폰 시 호출). 순수 산술.</summary>
    public void AddFlowStack(int _amount) => this.FlowStack += _amount;

    // [Entered] 런타임 등장 공통 후처리(패시브 → 시너지 순서 고정).
    // Placed(오프닝 배치)와 혼동 금지 — 오프닝은 시너지 미발화가 의도(등장=런타임 스폰만).
    // 호출 위치는 justSpawned 판정 '전'이어야 한다(패시브가 무적을 부여하는 경우를 판정에 반영).
    void NotifyEntered(CardInstance _card)
    {
        var t_ctx = new SpawnCtx(_card, this);
        SynergyTriggers.Entered(t_ctx);
        NotifyBoardChanged(); // 등장으로 라이브 구성이 바뀜 → 파생 상태 재동기
    }

    /// <summary>튜토리얼 확정승: 슬롯+대기 카드의 현재 체력을 _hp 이하로 낮춤(공격력=체력이라 적이 약해짐).
    /// bonusHp 제거. data.maxHp(공유 에셋)는 건드리지 않음 — 인스턴스 hp만.</summary>
    public void OverrideAllHp(int _hp)
    {
        if (_hp <= 0) return;
        foreach (CardInstance t_c in GetActiveCards())
        {
            t_c.hp = Mathf.Min(t_c.hp, _hp);
            t_c.bonusHp = 0;
        }

        foreach (CardInstance t_c in this.waitingQueue)
        {
            t_c.hp = Mathf.Min(t_c.hp, _hp);
            t_c.bonusHp = 0;
        }
    }

    /// <summary>슬롯 조회. 범위 밖은 예외가 아니라 null이다 — 이 함수는 와이어에서 온 raw int가
    /// 그대로 흘러드는 경로(수신 공격/스폰 미러)에 노출돼 있어서, 범위를 안 보면 손상·조작 패킷 하나가
    /// 수신 클라를 IndexOutOfRangeException으로 세운다. 호출부는 이미 전부 null 분기를 갖고 있다.</summary>
    public CardInstance GetSlot(int _index)
        => _index >= 0 && _index < SLOT_COUNT ? this.slots[_index] : null;
    public IEnumerable<CardInstance> GetWaitingCards() => this.waitingQueue;

    /// <summary>슬롯을 점유한 카드 수(hp 0으로 아직 정리 전인 카드 포함 — <see cref="GetActiveCards"/>와 같은 집합).
    /// GetActiveCards()는 부를 때마다 리스트를 새로 만든다. 개수만 필요한 판정
    /// (전투 종료 예측의 사전 게이트 등)은 이쪽을 써서 매 공격 할당을 만들지 않는다.</summary>
    public int ActiveCount
    {
        get
        {
            int t_count = 0;
            for (int i = 0; i < SLOT_COUNT; i++)
                if (this.slots[i] != null)
                    t_count++;
            return t_count;
        }
    }

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

    // 이 필드를 공격할 때 유효한 타깃. 필터 규칙(지정 타깃 > 도발 > 전체)은 BattleRules.ValidTargets 단독 —
    // CardView의 강조/거절 안내와 **같은 함수**다(도발만 보고 지정 타깃을 모르는 이중 진실원 제거).
    // 지정 타깃은 TurnState가 해석해서(공격자가 로컬일 때만) 규칙에 인자로 넘긴다 —
    // BattleRules는 전역을 읽지 않는다.
    public List<CardInstance> GetValidTargets(CardInstance _attacker = null)
        => BattleRules.ValidTargets(_attacker, GetActiveCards(), TurnState.ForcedTargetFor(_attacker));

    /// <summary>이 필드를 향한 공격이 규칙상 허용되는가. 턴 로직의 백스톱 진입점.</summary>
    public bool CanAttack(CardInstance _attacker, CardInstance _target)
        => BattleRules.CanAttack(_attacker, _target, GetActiveCards(), TurnState.ForcedTargetFor(_attacker));

    bool HasAnyCard()
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (this.slots[i] != null) return true;
        }

        return this.waitingQueue.Count > 0;
    }

    /// <summary>Fisher-Yates. 난수원은 정책이 정한다 — 필드가 모드 플래그를 읽지 않는다.
    /// Match면 MatchRandom(결정론 스트림)을 소비하므로 **양측 소비 횟수가 같아야 한다**:
    /// 지금 Match를 쓰는 건 싱글/튜토리얼뿐이라 스트림 공유 상대가 없다.</summary>
    static void Shuffle(List<int> _list, ShufflePolicy _policy)
    {
        if (_policy == ShufflePolicy.None) return;

        for (int i = _list.Count - 1; i > 0; i--)
        {
            int t_j = _policy == ShufflePolicy.Match
                ? MatchRandom.Range(i + 1)
                : Random.Range(0, i + 1);
            (_list[i], _list[t_j]) = (_list[t_j], _list[i]);
        }
    }
}
