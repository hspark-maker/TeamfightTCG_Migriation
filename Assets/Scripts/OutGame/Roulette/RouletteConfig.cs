using System;
using System.Collections.Generic;
using UnityEngine;

// 룰렛 판의 칸 하나. 판에 그려진 쐐기 1개와 1:1로 대응한다.
[Serializable]
public struct RouletteSlotDef
{
    [Tooltip("이 칸에 당첨됐을 때 주는 재화입니다.\n\n" +
             "룰렛 티켓은 여기 넣을 수 없습니다. 티켓으로 돌려 티켓이 나오면 회전이 스스로를 재생산해 " +
             "무한 회전이 되기 때문입니다. 한 칸이라도 티켓으로 저작하면 이 설정 전체가 거부되고 " +
             "로비에서 룰렛 버튼이 아예 뜨지 않습니다(칸 하나만 조용히 빼면 저작 실수를 화면에서 못 보게 됩니다).")]
    public ECurrencyType currency;

    [Tooltip("당첨 시 주는 수량입니다. 1 이상이어야 합니다 — 0 이하는 설정 결함으로 잡힙니다.")]
    public long amount;

    [Tooltip("뽑기 가중치입니다. 값이 클수록 자주 나옵니다. 확률은 (이 칸 가중치 / 전체 칸 가중치 합)입니다.\n\n" +
             "0 이하로 두면 1로 봅니다. 즉 이 칸을 못 나오게 막는 수단이 아닙니다 — " +
             "빼고 싶은 칸은 다른 상품으로 갈아 끼우세요.")]
    public int weight;

    [Tooltip("잭팟 칸 표식입니다. 판 전체에서 정확히 1칸만 켜야 하며, 0칸이거나 2칸 이상이면 설정 결함으로 잡힙니다.\n" +
             "여기 걸리면 화면이 전구 연출을 추가로 재생합니다.")]
    public bool isJackpot;

    // 저작 0을 1로 보는 규약의 단일 지점 — 추첨과 검증이 같은 값을 봐야 확률 표시가 실제와 갈리지 않는다.
    public int EffectiveWeight => weight > 0 ? weight : 1;

    // 추첨 후보 자격. 티켓 칸은 설정 단계에서 이미 거부되므로 여기서는 수량만 본다.
    public bool IsDrawable => amount > 0;
}

// 룰렛 판 한 벌의 저작 표. 2단계에서 스펙 표로 이관되면 이 SO는 표현 축만 남는다.
[CreateAssetMenu(fileName = "RouletteConfig", menuName = "Card Battle/Roulette Config")]
public class RouletteConfig : ScriptableObject
{
    // 판 아트가 8쐐기로 그려져 있어 칸 수는 저작이 아니라 아트의 제약이다.
    public const int SLOT_COUNT = 8;

    [Tooltip("2단계에서 서버 표로 이관할 때 쓸 영구 키입니다. 한 번 정하면 바꾸지 마세요 — " +
             "서버 요청이 이 키로 판을 찾습니다.")]
    [SerializeField] string rouletteId = "roulette_default";

    [Tooltip("화면 제목에 쓸 이름입니다. 표시용이라 언제든 고쳐도 됩니다.")]
    [SerializeField] string displayName = "행운의 룰렛";

    [Tooltip("1회 회전 비용으로 낼 재화입니다. 1단계에서는 표시만 되고 실제로 차감되지 않습니다.")]
    [SerializeField] ECurrencyType priceType = ECurrencyType.RouletteTicket;

    [Tooltip("1회 회전 비용입니다. 1단계에서는 표시만 되고 실제로 차감되지 않습니다.")]
    [SerializeField] long price = 1;

    [Tooltip("판의 칸 목록입니다. 순서가 곧 판 위의 자리입니다 — 0번이 12시(맨 위)이고 시계방향으로 1, 2, 3...입니다.\n" +
             "정확히 8칸이어야 하며, 그중 잭팟은 1칸입니다. 칸을 늘리거나 줄이면 판 그림과 어긋나 설정 전체가 거부됩니다.")]
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

        if (string.IsNullOrEmpty(rouletteId)) AddWarning(_warnings, "rouletteId가 비어 있습니다 — 2단계 표 이관 키이므로 지금 정해 두세요.");

        int t_count = SlotCount;
        if (t_count != SLOT_COUNT)
        {
            AddFault(_faults, ref t_faultCount, $"칸이 {t_count}개입니다 — 판 그림이 {SLOT_COUNT}쐐기라 정확히 {SLOT_COUNT}개여야 합니다.");
        }

        int t_jackpotCount = 0;
        int t_drawableCount = 0;

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            RouletteSlotDef t_slot = slots[t_i];

            // 티켓 칸은 그 칸만 버리지 않는다 — 7칸 판이 되면 저작자가 자기 실수를 화면에서 못 본다.
            if (t_slot.currency == ECurrencyType.RouletteTicket)
                AddFault(_faults, ref t_faultCount, $"{t_i}번 칸이 룰렛 티켓입니다 — 회전이 스스로를 재생산합니다.");

            if (t_slot.amount <= 0)
                AddFault(_faults, ref t_faultCount, $"{t_i}번 칸의 수량이 {t_slot.amount}입니다 — 1 이상이어야 합니다.");

            if (t_slot.weight <= 0)
                AddWarning(_warnings, $"{t_i}번 칸의 가중치가 {t_slot.weight}입니다 — 1로 봅니다.");

            if (t_slot.isJackpot) t_jackpotCount++;
            if (t_slot.IsDrawable) t_drawableCount++;
        }

        if (t_jackpotCount != 1)
            AddFault(_faults, ref t_faultCount, $"잭팟 칸이 {t_jackpotCount}개입니다 — 정확히 1개여야 합니다.");

        if (t_drawableCount <= 0)
            AddFault(_faults, ref t_faultCount, "뽑을 수 있는 칸이 하나도 없습니다.");

        return t_faultCount;
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
