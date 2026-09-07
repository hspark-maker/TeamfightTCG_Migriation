using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>덱 셔플에 어떤 난수를 쓸지 호출부가 정하는 정책. 필드 안에서 모드 플래그를 읽지 않는다.
/// - None : 셔플 안 함. 리스트 순서 = 등장 순서(튜토리얼 저작 순서 보존).
/// - Match: <see cref="MatchRandom"/>(시드 고정 결정론). 호출 전에 시드가 걸려 있어야 한다.
/// - DerivedMatch: 매치 시드에서 소유자별 독립 스트림을 파생한다. 공용 MatchRandom 소비 순서에 영향을 주지 않는다.</summary>
public enum ShufflePolicy
{
    None,
    Match,
    DerivedMatch,
}

public class BattleField : MonoBehaviour
{
    // 슬롯 수의 진실원은 코어다 — 서버 재시뮬이 같은 값을 써야 하는데 코어는 이 어셈블리를 참조할 수 없다.
    public const int SLOT_COUNT = TeamfightTCG.BattleCore.BattleState.SlotCount;

    readonly BattleFieldState state = new BattleFieldState();

    HealerEffect healerEffect;

    /// <summary>규칙이 보는 필드 상태. 이 클래스는 씬 수명·연출 어댑터일 뿐이고,
    /// 슬롯·대기열·성장 조회·전사 기록·시너지 훅 발화는 전부 상태 객체가 소유한다.</summary>
    public BattleFieldState State => this.state;
    public int OwnerIndex => this.state.OwnerIndex;
    public int WaitingCount => this.state.WaitingCount;
    public bool IsEmpty => this.state.IsEmpty;

    /// <summary>이번 판에 잃은 카드(사망 순서). 결과 화면이 "몇 장 중 몇 장을 지켰는가"를 그리는 출처다.
    /// <b>소비자는 플레이어 필드 하나뿐이다</b> — 상대 필드에도 똑같이 쌓이지만 읽는 곳이 없다
    /// (원격 미러는 상대 전사 목록의 정본이 아니다. 필요해지면 그때 경로를 확인하고 열 것).</summary>
    public IReadOnlyList<int> FallenCards => this.state.FallenCards;

    /// <summary>흐름 시너지 스택. 흐름 카드가 런타임 등장(NotifyEntered)할 때마다 +1, 카드 flowBonus 재동기의 기준값.
    /// Initialize/InitializeFromRemote에서 0 리셋. 초기배치는 미발화 → 0부터 런타임 등장으로만 성장. 전투 중 파생.</summary>
    public int FlowStack => this.state.FlowStack;

    /// <summary>이 덱으로 산출된 시너지 상태. 배틀 시작 시 1회 확정, 전투 중 불변. UI(SynergyPanelUI) 참조용.</summary>
    public SynergyState Synergy => this.state.Synergy;

    void OnDestroy() => this.healerEffect?.Unsubscribe();

    /// <summary>_growthOf = 카드 영구 성장값 공급자(생략/ null이면 성장 미적용).
    /// 싱글·튜토리얼은 로컬 공급자를, 멀티는 양쪽이 교환해 확정한 성장 스냅샷 공급자를 사용한다.</summary>
    public void Initialize(List<int> _deckData, int _ownerIndex, ShufflePolicy _shuffle,
        System.Func<int, CardGrowth> _growthOf = null)
    {
        this.state.Reset(_ownerIndex, _growthOf);
        this.healerEffect?.Unsubscribe();
        this.healerEffect = new HealerEffect(this);

        // 빈 칸(null)은 여기서 걷어낸다. 덱 출처가 여럿이고(세이브·AI 에셋·튜토리얼 시나리오)
        // 에셋 참조가 끊기면 그 칸이 null로 들어오는데, 그대로 CardInstance를 만들면 NRE로 초기화가
        // 통째로 죽어 "아무것도 안 나오는 전투"가 된다. 한 장 빠진 채로라도 전투는 열려야 한다.
        List<int> t_shuffled = Compact(_deckData, _ownerIndex);
        Shuffle(t_shuffled, _shuffle, _ownerIndex);
        // 서버 재시뮬은 이 순서를 시드로 산출할 수 없다(싱글·튜토리얼은 서버가 모르는 로컬 시드다) — 기록해서 제출한다.
        // 초기화 경로가 여럿이라 호출부가 아니라 여기 한 곳에서 잡는다.
        BattleBoardOrder.Capture(_ownerIndex, t_shuffled);

        for (int i = 0; i < t_shuffled.Count; i++)
        {
            var t_card = new CardInstance(t_shuffled[i], this.OwnerIndex, GrowthOf(t_shuffled[i]));
            if (i < SLOT_COUNT)
            {
                t_card.isRevealed = true;
                t_card.wasEverRevealed = true;
                t_card.slotIndex = i;
                this.state.SetSlot(i, t_card);
                this.state.NotifyPlaced(t_card); // [Placed] 오프닝 배치 — 등장(Entered) 아님
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible);
            }
            else
            {
                this.state.Enqueue(t_card);
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
    CardGrowth GrowthOf(int _cardId) => this.state.GrowthOf(_cardId);

    // 빈 슬롯에 대기 카드 순서대로 배치. 채운 카드 목록 반환.
    public List<CardInstance> FillEmptySlots()
    {
        var t_placed = new List<CardInstance>();
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (this.state.TryFillSlot(i, out CardInstance t_card, out bool t_cunningReturn))
            {
                this.state.NotifyEntered(t_card); // [Entered] 런타임 등장 시너지. justSpawned 판정 전에 발화.
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible) || t_cunningReturn;
                t_placed.Add(t_card);
            }
        }

        return t_placed;
    }

    public CardInstance SwapWithWaiting(CardInstance _card)
        => this.state.SwapWithWaiting(_card);

    /// <summary>후공 어드밴티지 멀리건: _slotIndex 슬롯 카드를 대기열의 _deckIndex 카드와 교환.
    /// 전투 시작 1회. 스왑-인 카드는 오프닝 배치와 동일한 [Placed] 경로(런타임 등장 [Entered] 아님) —
    /// [Entered]로 발화하면 돌보미/흐름 등 런타임 스폰 보너스가 이 카드에만 붙어 나머지 오프닝 카드와
    /// 비대칭이 생긴다. 시너지는 ApplyDeckSynergy에서 대기 카드까지 이미 적용됨 → 재적용 금지(bonusHp 이중가산).
    /// _deckIndex는 호출부가 MatchRandom으로 산출(결정론, 멀티 확장 대비). 반환: 새로 슬롯에 들어온 카드(실패 시 null).</summary>
    public CardInstance MulliganSwap(int _slotIndex, int _deckIndex)
    {
        // 교환 자체(대기열 추출 → 슬롯 배치 → 스왑-아웃 대기열 복귀)는 BattleFieldState.MulliganSwap 소유다.
        CardInstance t_in = this.state.MulliganSwap(_slotIndex, _deckIndex);
        if (t_in == null) return null;

        this.state.NotifyPlaced(t_in); // [Placed] 오프닝 배치 경로. Initialize+ApplyDeckSynergy가 슬롯 카드에 준 것과 동형.
        t_in.justSpawned = t_in.HasKeyword(CardKeyword.Invincible);
        this.state.NotifyBoardChanged(); // 보드 구성 변화 → 라이브 카운트 파생 재동기(Placed는 미발화라 명시 호출).
        return t_in;
    }

    public bool CanSwapWithWaiting(CardInstance _card)
    {
        return this.state.CanSwapWithWaiting(_card);
    }

    /// <summary>슬롯 하나를 비운다. <b>사망 정리 전용</b>이라 여기서 전사 기록을 남긴다 —
    /// 유일한 호출자가 <c>AttackProcessor.RemoveDead</c>의 부활 게이트를 통과한 시체이고,
    /// 교활 되돌림·스왑·멀리건은 슬롯을 <b>덮어쓰므로</b> 이 경로를 타지 않는다.
    /// 사망 외 용도로 이걸 부르기 시작하면 결과 화면의 전사 목록이 조용히 오염된다.</summary>
    public void RemoveCard(int _slotIndex)
        => this.state.RemoveCard(_slotIndex);

    /// <summary>셔플된 카드 ID 배열 반환. 배틀 초기화 후 broadcast용.</summary>
    public int[] GetShuffledIds()
    {
        return this.state.GetOrderedCardIds();
    }

    /// <summary>상대방에게 받은 카드 ID와 최종 성장 스냅샷으로 enemyField를 재구성한다.</summary>
    public void InitializeFromRemote(int[] _ids, CardGrowth[] _growth, int _ownerIndex)
    {
        var t_growthById = new Dictionary<int, CardGrowth>(_ids?.Length ?? 0);
        if (_ids != null && _growth != null)
        {
            for (int i = 0; i < _ids.Length && i < _growth.Length; i++)
                t_growthById[_ids[i]] = _growth[i];
        }
        System.Func<int, CardGrowth> t_growthOf = _cardId => t_growthById.TryGetValue(_cardId, out CardGrowth t_value)
            ? t_value
            : default;
        this.state.Reset(_ownerIndex, t_growthOf);
        this.healerEffect?.Unsubscribe();
        this.healerEffect = new HealerEffect(this);

        // 원격 필드도 보드 순서를 기록해야 서버가 재시뮬할 수 있다. 여기는 셔플이 없고
        // 받은 순서가 곧 배치 순서다 — 카탈로그에서 걸러진 카드는 실제로 안 놓이므로 제외한 순서를 남긴다.
        var t_boardOrder = new List<int>(_ids?.Length ?? 0);

        for (int i = 0; i < _ids.Length; i++)
        {
            int t_cardId = _ids[i];
            if (!CardCatalog.Contains(t_cardId)) continue;
            t_boardOrder.Add(t_cardId);
            var t_card = new CardInstance(t_cardId, _ownerIndex, GrowthOf(t_cardId));
            if (i < SLOT_COUNT)
            {
                t_card.slotIndex = i;
                t_card.isRevealed = true;
                t_card.wasEverRevealed = true;
                this.state.SetSlot(i, t_card);
                this.state.NotifyPlaced(t_card); // [Placed] 오프닝 배치 — 등장(Entered) 아님
                t_card.justSpawned = t_card.HasKeyword(CardKeyword.Invincible);
            }
            else
            {
                this.state.Enqueue(t_card);
            }
        }

        BattleBoardOrder.Capture(_ownerIndex, t_boardOrder);
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
        if (!this.state.TryTakeMatchingWaiting(_cardId, out t_card))
        {
            // desync 방어(정상 lockstep에선 도달 안 함): fresh 폴백 + 확정 필드 시너지 재적용.
            Debug.LogError($"[BattleField] PlaceCardDirectly 미러 큐 불일치 → fresh 폴백 " +
                             $"(slot={_slot}, cardId={_cardId}, waiting={this.state.WaitingCount})");
            t_card = new CardInstance(_cardId, this.OwnerIndex, GrowthOf(_cardId)); // 성장원도 Initialize와 같은 소스로
            if (this.Synergy != null)
                SynergyApplier.ApplyAll(this.Synergy, new[] { t_card });
        }

        // FillEmptySlots와 동형: 교활 복귀 플래그 소비 + 슬롯 세팅 + justSpawned 판정.
        this.state.PlaceIncoming(_slot, t_card, out bool t_cunningReturn);
        this.state.NotifyEntered(t_card); // [Entered] 런타임 등장. 원격 미러도 소유 클라와 동형 발화.
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
        t_cards.AddRange(this.state.GetWaitingCards());

        // 성장 카드의 시너지는 1차 진화부터 카운트한다. CardData만 넘기기 전에 인스턴스에서
        // 필터해야 Resolver가 성장 계층을 알지 않아도 되고 기존 순수 집계 계약도 유지된다.
        this.state.SetSynergy(SynergyResolver.Resolve(
            t_cards.FindAll(c => c.synergyEnabled).ConvertAll(c => c.cardId)));
        SynergyApplier.ApplyAll(this.Synergy, t_cards);
        // [Placed]는 시너지 스냅샷이 확정된 여기서 슬롯 카드마다 발화한다.
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            CardInstance t_placed = this.state.GetSlot(i);
            if (t_placed != null) this.state.NotifyPlaced(t_placed);
        }

        this.state.NotifyBoardChanged(); // 오프닝 배치분에도 라이브 카운트 파생 상태를 깔아준다(Entered는 오프닝에 미발화)
    }

    /// <summary>흐름: 스택 +1. 스택 권위는 BattleField 소유(FlowSynergyEffect가 런타임 스폰 시 호출). 순수 산술.</summary>
    public void AddFlowStack(int _amount) => this.state.AddFlowStack(_amount);

    /// <summary>튜토리얼 확정승: 슬롯+대기 카드의 현재 체력을 _hp 이하로 낮춤(공격력=체력이라 적이 약해짐).
    /// bonusHp 제거. data.maxHp(공유 에셋)는 건드리지 않음 — 인스턴스 hp만.</summary>
    public void OverrideAllHp(int _hp)
    {
        this.state.OverrideAllHp(_hp);
    }

    /// <summary>슬롯 조회. 범위 밖은 예외가 아니라 null이다 — 이 함수는 와이어에서 온 raw int가
    /// 그대로 흘러드는 경로(수신 공격/스폰 미러)에 노출돼 있어서, 범위를 안 보면 손상·조작 패킷 하나가
    /// 수신 클라를 IndexOutOfRangeException으로 세운다. 호출부는 이미 전부 null 분기를 갖고 있다.</summary>
    public CardInstance GetSlot(int _index)
        => this.state.GetSlot(_index);
    public IEnumerable<CardInstance> GetWaitingCards() => this.state.GetWaitingCards();

    /// <summary>슬롯을 점유한 카드 수(hp 0으로 아직 정리 전인 카드 포함 — <see cref="GetActiveCards"/>와 같은 집합).
    /// GetActiveCards()는 부를 때마다 리스트를 새로 만든다. 개수만 필요한 판정
    /// (전투 종료 예측의 사전 게이트 등)은 이쪽을 써서 매 공격 할당을 만들지 않는다.</summary>
    public int ActiveCount
    {
        get => this.state.ActiveCount;
    }

    public List<CardInstance> GetActiveCards()
    {
        return this.state.GetActiveCards();
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

    /// <summary>Fisher-Yates. 난수원은 정책이 정한다 — 필드가 모드 플래그를 읽지 않는다.
    /// Match면 MatchRandom(결정론 스트림)을 소비하므로 **양측 소비 횟수가 같아야 한다**:
    /// 지금 Match를 쓰는 건 싱글/튜토리얼뿐이라 스트림 공유 상대가 없다.</summary>
    static void Shuffle(List<int> _list, ShufflePolicy _policy, int _ownerIndex)
    {
        // 정책을 한 자리에서 남김없이 가른다 — else로 접으면 새 정책이 조용히 남의 분기를 타고
        // 컴파일러가 못 잡는다. DerivedMatch 는 DeckOrder 가 단일 구현을 소유하므로 위임한다.
        switch (_policy)
        {
            case ShufflePolicy.None:
                return;
            case ShufflePolicy.DerivedMatch:
                List<int> t_derivedOrder = DeckOrder.Derive(_list, _ownerIndex);
                _list.Clear();
                _list.AddRange(t_derivedOrder);
                return;
            case ShufflePolicy.Match:
                break;
            default:
                throw new System.InvalidOperationException($"Unhandled ShufflePolicy: {_policy}");
        }

        for (int i = _list.Count - 1; i > 0; i--)
        {
            int t_j = MatchRandom.Range(i + 1);
            (_list[i], _list[t_j]) = (_list[t_j], _list[i]);
        }
    }
}
