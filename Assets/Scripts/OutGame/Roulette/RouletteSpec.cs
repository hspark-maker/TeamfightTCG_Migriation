using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Roulette·RouletteSlot 표를 읽어 룰렛 런타임 설정을 만든다. 판정 규칙은 서버 rouletteDraw 와 같은 것을 쓴다 —
/// 클라가 다르게 읽으면 판에 그려진 상품과 실제 지급이 갈린다.</summary>
public static class RouletteSpec
{
    /// <summary>지금 화면이 쓰는 판 키. 표에 이 판이 없으면 초기화를 접는다.</summary>
    public const string DEFAULT_ROULETTE_ID = "roulette_default";

    // 지금은 재화 상품 하나뿐이다. 다른 값이 실린 칸 행은 버린다(서버 REWARD_TYPE_CURRENCY 와 같다).
    const string REWARD_TYPE_CURRENCY = "Currency";

    static RouletteConfig s_runtime;

    /// <summary>필수 판이 표에 서는지 검사한다. 실패하면 초기화가 복구 화면으로 간다.</summary>
    public static bool TryValidateRequired(out string _error) => TryReadBoard(DEFAULT_ROULETTE_ID, out _, out _error);

    /// <summary>저작 SO 사본에 표 값을 덮어 런타임 설정을 만든다. SO에서 오는 것은 표현 축뿐이다.</summary>
    public static bool TryBuildRuntime(RouletteConfig _authored, out RouletteConfig _runtime, out string _error)
    {
        _runtime = null;
        if (_authored == null)
        {
            _error = "RouletteConfig 저작 원본이 배선되지 않았다.";
            return false;
        }
        if (!TryReadBoard(DEFAULT_ROULETTE_ID, out ParsedBoard t_board, out _error)) return false;

        // 초기화 재시도가 여러 번 돌아도 사본이 쌓이지 않게 직전 것을 버린다.
        if (s_runtime != null) UnityEngine.Object.Destroy(s_runtime);

        RouletteConfig t_runtime = UnityEngine.Object.Instantiate(_authored);
        t_runtime.name = _authored.name + " (ServerSpec)";
        t_runtime.hideFlags = HideFlags.DontSave;

        if (!t_runtime.TrySetBoardSpec(t_board.RouletteId, t_board.DisplayName, t_board.PriceType, t_board.Price, t_board.Slots))
        {
            UnityEngine.Object.Destroy(t_runtime);
            _error = $"룰렛 판 '{t_board.RouletteId}'의 표 값을 사본에 옮기지 못했다.";
            return false;
        }

        s_runtime = t_runtime;
        _runtime = t_runtime;
        return true;
    }

    static bool TryReadBoard(string _rouletteId, out ParsedBoard _board, out string _error)
    {
        _board = default;
        _error = null;

        IReadOnlyList<Roulette> t_headers = SpecSource.Manager?.Roulette?.All;
        if (t_headers == null || t_headers.Count == 0)
        {
            _error = "Roulette 서버 표가 비어 있다.";
            return false;
        }

        Roulette t_header = null;
        foreach (Roulette t_row in t_headers)
        {
            if (t_row == null || !string.Equals(t_row.rouletteId, _rouletteId, StringComparison.Ordinal)) continue;

            // 같은 키가 두 행이면 id 낮은 쪽이 산다(서버 rouletteSpecReader 와 같은 규약).
            if (t_header == null || t_row.id < t_header.id) t_header = t_row;
        }
        if (t_header == null)
        {
            _error = $"Roulette 표에 판 '{_rouletteId}'가 없다.";
            return false;
        }
        if (!TryReadCurrency(t_header.priceType, out ECurrencyType t_priceType))
        {
            _error = $"Roulette '{_rouletteId}'의 priceType '{t_header.priceType}'을 읽을 수 없다.";
            return false;
        }
        if (t_header.price <= 0)
        {
            _error = $"Roulette '{_rouletteId}'의 price {t_header.price}가 유효하지 않다 — 1 이상이어야 한다.";
            return false;
        }

        IReadOnlyList<RouletteSlot> t_allSlots = SpecSource.Manager?.RouletteSlot?.All;
        if (t_allSlots == null || t_allSlots.Count == 0)
        {
            _error = "RouletteSlot 서버 표가 비어 있다.";
            return false;
        }

        var t_slots = new RouletteSlotDef[RouletteConfig.SLOT_COUNT];
        var t_taken = new bool[RouletteConfig.SLOT_COUNT];
        int t_filled = 0;
        int t_dropped = 0;

        foreach (RouletteSlot t_row in RowsOfBoard(t_allSlots, _rouletteId))
        {
            if (!string.Equals(t_row.rewardType?.Trim(), REWARD_TYPE_CURRENCY, StringComparison.OrdinalIgnoreCase))
            { t_dropped++; continue; }
            if (t_row.amount <= 0) { t_dropped++; continue; }

            // 티켓 상품은 회전이 스스로를 재생산하므로 칸이 될 수 없다.
            if (!TryReadCurrency(t_row.rewardId, out ECurrencyType t_currency) || t_currency == ECurrencyType.RouletteTicket)
            { t_dropped++; continue; }
            if (t_row.slotIndex < 0 || t_row.slotIndex >= RouletteConfig.SLOT_COUNT) { t_dropped++; continue; }

            // 같은 자리를 두 행이 주장하면 id 낮은 쪽이 산다.
            if (t_taken[t_row.slotIndex]) { t_dropped++; continue; }

            t_taken[t_row.slotIndex] = true;
            t_slots[t_row.slotIndex] = new RouletteSlotDef
            {
                currency = t_currency,
                amount = t_row.amount,
                weight = t_row.weight,
            };
            t_filled++;
        }

        // 빈 자리를 남긴 채 열면 판 그림이 없는 상품을 가리킨다 — 칸이 모자라면 판 전체를 접는다.
        if (t_filled != RouletteConfig.SLOT_COUNT)
        {
            _error = $"룰렛 판 '{_rouletteId}'의 칸이 {t_filled}개다 — 판 그림이 {RouletteConfig.SLOT_COUNT}쐐기라 정확히 그 수여야 한다(버린 행 {t_dropped}개).";
            return false;
        }
        if (t_dropped > 0)
            Debug.LogWarning($"[RouletteSpec] Discarded {t_dropped} slot row(s) of board '{_rouletteId}' as authoring defects.");

        _board = new ParsedBoard(t_header.rouletteId, t_header.displayName, t_priceType, t_header.price, t_slots);
        return true;
    }

    static List<RouletteSlot> RowsOfBoard(IReadOnlyList<RouletteSlot> _rows, string _rouletteId)
    {
        var t_rows = new List<RouletteSlot>();
        foreach (RouletteSlot t_row in _rows)
        {
            if (t_row == null || !string.Equals(t_row.rouletteId, _rouletteId, StringComparison.Ordinal)) continue;
            t_rows.Add(t_row);
        }
        t_rows.Sort((a, b) => a.id.CompareTo(b.id));
        return t_rows;
    }

    // 이름을 정확히 대조한다. Gold 폴백을 두지 않는다 — 오타가 조용히 금화가 되면 화면과 서버 지급이 갈린다.
    static bool TryReadCurrency(string _name, out ECurrencyType _currency)
    {
        _currency = default;
        if (string.IsNullOrEmpty(_name)) return false;

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
        {
            var t_candidate = (ECurrencyType)t_i;
            if (!string.Equals(_name, t_candidate.ToString(), StringComparison.Ordinal)) continue;

            _currency = t_candidate;
            return true;
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_runtime = null;

    readonly struct ParsedBoard
    {
        public readonly string RouletteId;
        public readonly string DisplayName;
        public readonly ECurrencyType PriceType;
        public readonly long Price;
        public readonly IReadOnlyList<RouletteSlotDef> Slots;

        public ParsedBoard(string _rouletteId, string _displayName, ECurrencyType _priceType, long _price,
                           IReadOnlyList<RouletteSlotDef> _slots)
        {
            RouletteId = _rouletteId;
            DisplayName = _displayName;
            PriceType = _priceType;
            Price = _price;
            Slots = _slots;
        }
    }
}
