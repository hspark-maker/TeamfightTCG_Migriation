using System;
using System.Collections.Generic;
using UnityEngine;

// 룰렛 판의 칸 하나. 판에 그려진 쐐기 1개와 1:1로 대응한다.
// 값의 진실원은 RouletteSlot 스펙시트다 — 초기화가 표를 읽어 이 칸들을 통째로 만들어 끼운다.
[Serializable]
public struct RouletteSlotDef
{
    public ERewardType rewardType;
    public string rewardId;
    public bool IsPack => rewardType == ERewardType.Pack;
    [Tooltip("이 칸에 당첨됐을 때 주는 재화입니다. 값의 진실원은 RouletteSlot 스펙시트의 rewardId 열이고, " +
             "여기 저작값은 게임이 켜질 때 표가 덮습니다.\n\n" +
             "룰렛 티켓은 넣을 수 없습니다. 티켓으로 돌려 티켓이 나오면 회전이 스스로를 재생산해 " +
             "무한 회전이 되기 때문입니다. 티켓으로 저작한 칸은 표를 읽는 단계에서 버려지고, " +
             "그 자리가 비어 판이 8칸을 못 채우면 룰렛이 아예 열리지 않습니다.")]
    public ECurrencyType currency;

    [Tooltip("당첨 시 주는 수량입니다. 진실원은 RouletteSlot 시트의 amount 열입니다. 1 이상이어야 합니다 — 0 이하인 행은 버려집니다.")]
    public long amount;

    [Tooltip("뽑기 가중치입니다. 값이 클수록 자주 나옵니다. 확률은 (이 칸 가중치 / 전체 칸 가중치 합)입니다. " +
             "진실원은 RouletteSlot 시트의 weight 열입니다.\n\n" +
             "0 이하로 두면 1로 봅니다. 즉 이 칸을 못 나오게 막는 수단이 아닙니다 — " +
             "빼고 싶은 칸은 다른 상품으로 갈아 끼우세요.")]
    public int weight;

    // 저작 0을 1로 보는 규약의 단일 지점 — 추첨과 검증이 같은 값을 봐야 확률 표시가 실제와 갈리지 않는다.
    public int EffectiveWeight => weight > 0 ? weight : 1;

    // 추첨 후보 자격. 티켓 칸은 설정 단계에서 이미 거부되므로 여기서는 수량만 본다.
    public bool IsDrawable => amount > 0;
}

// 룰렛 판 한 벌의 표현 축이자 런타임 그릇. 값의 진실원은 Roulette·RouletteSlot 스펙시트이고,
// 초기화(RouletteSpec.TryBuildRuntime)가 이 애셋의 사본에 표 값을 덮어 쓴다 — 여기 저작값은 그때까지의 자리표시다.
// 값을 바꾸려면 시트를 고치고 릴리스 매니저로 서버 표를 올린다.
[CreateAssetMenu(fileName = "RouletteConfig", menuName = "Card Battle/Roulette Config")]
public class RouletteConfig : ScriptableObject
{
    // 판 아트가 8쐐기로 그려져 있어 칸 수는 저작이 아니라 아트의 제약이다.
    public const int SLOT_COUNT = 8;

    [Tooltip("서버 spinRoulette 에 보낼 판 이름입니다. 값의 진실원은 Roulette 시트의 rouletteId 열이고 " +
             "게임이 켜질 때 표 값이 여기를 덮습니다. 한 번 정하면 바꾸지 마세요 — 서버 요청이 이 키로 판을 찾습니다.")]
    [SerializeField] string rouletteId = "roulette_default";

    [Tooltip("화면 제목에 쓸 이름입니다. 진실원은 Roulette 시트의 displayName 열입니다(시트가 비어 있을 때만 이 저작값이 남습니다).")]
    [SerializeField] string displayName = "행운의 룰렛";

    [Tooltip("1회 회전 비용으로 낼 재화입니다. 진실원은 Roulette 시트의 priceType 열입니다. " +
             "실제 차감은 서버가 판정합니다 — 이 값은 누른 순간 화면에 미리 비출 표시용 사본입니다.")]
    [SerializeField] ECurrencyType priceType = ECurrencyType.RouletteTicket;

    [Tooltip("1회 회전 비용입니다. 진실원은 Roulette 시트의 price 열이고 표가 이 값을 덮습니다 — 실제로 빠지는 양은 언제나 서버가 정합니다.")]
    [SerializeField] long price = 1;

    [Tooltip("판의 칸 목록입니다. 순서가 곧 판 위의 자리입니다 — 0번이 12시(맨 위)이고 시계방향으로 1, 2, 3...입니다.\n" +
             "여기서 저작하지 마세요. 진실원은 RouletteSlot 시트이고, 게임이 켜질 때 그 표의 slotIndex 순서로 8칸이 통째로 갈아 끼워집니다.\n" +
             "표가 8칸을 못 채우면 판 그림과 어긋나므로 룰렛이 아예 열리지 않습니다.")]
    [SerializeField] List<RouletteSlotDef> slots = new List<RouletteSlotDef>();

    public string RouletteId => rouletteId;
    public string DisplayName => displayName;
    public ECurrencyType PriceType => priceType;
    public long Price => price;

    public IReadOnlyList<RouletteSlotDef> Slots => slots != null ? (IReadOnlyList<RouletteSlotDef>)slots : Array.Empty<RouletteSlotDef>();
    public int SlotCount => slots != null ? slots.Count : 0;

    public bool TryGetSlot(int _index, out RouletteSlotDef _slot)
    {
        if (slots == null || _index < 0 || _index >= slots.Count)
        {
            _slot = default;
            return false;
        }

        _slot = slots[_index];
        return true;
    }

    /// <summary>저작 검사. 결함 수를 돌려주고, 0이 아니면 이 설정은 통째로 쓰이지 않는다.
    /// 순수 함수라 에디터(OnValidate)와 런타임 주입(SetConfig)이 같은 판정을 쓴다 —
    /// OnValidate는 빌드 런타임에 돌지 않아 이것이 없으면 잘못 저작된 애셋이 그대로 배포된다.</summary>
    public int Validate(List<string> _faults, List<string> _warnings)
    {
        int t_faultCount = 0;

        // 서버 요청 키다. 비면 spinRoulette 이 invalid-argument 로 돌려보내 회전이 통째로 막힌다.
        if (string.IsNullOrEmpty(rouletteId))
            AddFault(_faults, ref t_faultCount, "rouletteId가 비어 있습니다 — 서버에 보낼 판 이름이라 반드시 채워야 합니다.");

        int t_count = SlotCount;
        if (t_count != SLOT_COUNT)
        {
            AddFault(_faults, ref t_faultCount, $"칸이 {t_count}개입니다 — 판 그림이 {SLOT_COUNT}쐐기라 정확히 {SLOT_COUNT}개여야 합니다.");
        }

        int t_drawableCount = 0;

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            RouletteSlotDef t_slot = slots[t_i];

            // 티켓 칸은 그 칸만 버리지 않는다 — 7칸 판이 되면 저작자가 자기 실수를 화면에서 못 본다.
            if (!t_slot.IsPack && t_slot.currency == ECurrencyType.RouletteTicket)
                AddFault(_faults, ref t_faultCount, $"{t_i}번 칸이 룰렛 티켓입니다 — 회전이 스스로를 재생산합니다.");
            if (t_slot.IsPack && (string.IsNullOrWhiteSpace(t_slot.rewardId) || t_slot.amount > 100))
                AddFault(_faults, ref t_faultCount, $"{t_i}번 칸의 팩 상품이 유효하지 않습니다.");

            if (t_slot.amount <= 0)
                AddFault(_faults, ref t_faultCount, $"{t_i}번 칸의 수량이 {t_slot.amount}입니다 — 1 이상이어야 합니다.");

            if (t_slot.weight <= 0)
                AddWarning(_warnings, $"{t_i}번 칸의 가중치가 {t_slot.weight}입니다 — 1로 봅니다.");

            if (t_slot.IsDrawable) t_drawableCount++;
        }

        if (t_drawableCount <= 0)
            AddFault(_faults, ref t_faultCount, "뽑을 수 있는 칸이 하나도 없습니다.");

        return t_faultCount;
    }

    /// <summary>스펙시트로 만든 런타임 사본에만 판 값을 주입한다. 칸 수가 맞지 않으면 아무것도 쓰지 않고 거절한다.</summary>
    internal bool TrySetBoardSpec(string _rouletteId, string _displayName, ECurrencyType _priceType, long _price,
                                  IReadOnlyList<RouletteSlotDef> _slots)
    {
        if (string.IsNullOrEmpty(_rouletteId) || _slots == null || _slots.Count != SLOT_COUNT) return false;

        rouletteId = _rouletteId;
        // 표시 이름만 시트가 비어도 저작값으로 버틴다 — 판정에 쓰이지 않아 화면이 빈 제목이 되는 쪽이 더 나쁘다.
        if (!string.IsNullOrEmpty(_displayName)) displayName = _displayName;
        priceType = _priceType;
        price = _price;
        slots = new List<RouletteSlotDef>(_slots);
        return true;
    }

    // 기획자가 애셋을 만지는 즉시 콘솔에 말한다. 빌드 런타임 검사는 RouletteManager.SetConfig가 따로 돈다.
    void OnValidate()
    {
        var t_faults = new List<string>();
        var t_warnings = new List<string>();

        int t_faultCount = Validate(t_faults, t_warnings);

        for (int t_i = 0; t_i < t_warnings.Count; t_i++) Debug.LogWarning($"[RouletteConfig:{name}] {t_warnings[t_i]}", this);

        if (t_faultCount <= 0) return;

        for (int t_i = 0; t_i < t_faults.Count; t_i++) Debug.LogError($"[RouletteConfig:{name}] {t_faults[t_i]}", this);
    }

    static void AddFault(List<string> _faults, ref int _faultCount, string _message)
    {
        _faultCount++;
        _faults?.Add(_message);
    }

    static void AddWarning(List<string> _warnings, string _message)
    {
        _warnings?.Add(_message);
    }
}
