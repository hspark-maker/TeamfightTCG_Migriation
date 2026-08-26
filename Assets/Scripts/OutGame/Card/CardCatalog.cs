using System.Collections.Generic;
using System;
using UnityEngine;

// 카드 마스터의 읽기 전용 단일 창구
public static class CardCatalog
{
    static readonly List<CardData> s_all = new List<CardData>();
    static readonly IReadOnlyList<CardData> s_allReadonly = s_all.AsReadOnly();
    static readonly List<CardSpec> s_allSpecs = new List<CardSpec>();
    static readonly IReadOnlyList<CardSpec> s_allSpecsReadonly = s_allSpecs.AsReadOnly();

    // 카드 식별의 단일 축 — 세이브·도감 행·덱이 전부 이 번호를 쓴다.
    static readonly Dictionary<int, CardData> s_byId = new Dictionary<int, CardData>();
    static readonly Dictionary<int, CardSpec> s_specById = new Dictionary<int, CardSpec>();

    // 구 세이브(에셋 이름 키) 이관 전용 역인덱스. 평상시 조회에 쓰지 마라 —
    // 이름은 리네임으로 갈리는 축이고, 그걸 끊으려고 번호를 도입했다.
    static readonly Dictionary<string, int> s_legacyNameToId = new Dictionary<string, int>();

    public static bool IsReady { get; private set; }

    public static IReadOnlyList<CardData> All => s_allReadonly;
    public static IReadOnlyList<CardSpec> AllSpecs => s_allSpecsReadonly;

    public static int Count => s_all.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_all.Clear();
        s_allSpecs.Clear();
        s_byId.Clear();
        s_specById.Clear();
        s_legacyNameToId.Clear();
        IsReady = false;
    }

    // 부트 주입 — 내부 인덱스 재구성
    public static void SetSource(IEnumerable<CardData> _cards, EContentRunMode _mode, bool _includeTestCards)
    {
        IsReady = false;
        var t_assets = new Dictionary<int, CardData>();
        var t_legacyNames = new Dictionary<string, int>();
        var t_assetOrder = new List<int>();

        if (_cards != null)
        {
            foreach (var t_card in _cards)
            {
                if (t_card == null) continue;

                int t_id = IdOf(t_card);
                if (t_id <= 0)
                    throw new InvalidOperationException($"[CardCatalog] 카드 '{t_card.name}'에 유효한 ID가 없다.");
                if (t_assets.ContainsKey(t_id))
                    throw new InvalidOperationException($"[CardCatalog] 카드 번호 {t_id} 중복: '{t_assets[t_id].name}', '{t_card.name}'.");

                t_assets.Add(t_id, t_card);
                t_assetOrder.Add(t_id);

                // 이름 충돌은 이관 정확도만 떨어뜨린다(구 세이브가 어느 쪽인지 모름) — 첫 항목만 잡고 경고.
                if (!string.IsNullOrEmpty(t_card.name) && !t_legacyNames.ContainsKey(t_card.name))
                    t_legacyNames.Add(t_card.name, t_id);
            }
        }

        Dictionary<int, CardSpec> t_specs = CardSpec.Load(_mode);
        foreach (KeyValuePair<int, CardSpec> t_pair in t_specs)
        {
            if (!t_assets.TryGetValue(t_pair.Key, out CardData t_asset))
                throw new InvalidOperationException($"[CardCatalog] 표 ID {t_pair.Key}({t_pair.Value.AssetName})에 대응하는 CardData가 없다.");
            if (!string.Equals(t_asset.name, t_pair.Value.AssetName, StringComparison.Ordinal))
                throw new InvalidOperationException($"[CardCatalog] ID {t_pair.Key} 이름 불일치: SO='{t_asset.name}', 표='{t_pair.Value.AssetName}'.");
        }
        foreach (KeyValuePair<int, CardData> t_pair in t_assets)
            if (!t_specs.ContainsKey(t_pair.Key))
                throw new InvalidOperationException($"[CardCatalog] CardData ID {t_pair.Key}('{t_pair.Value.name}')가 {_mode} 카드 표에 없다.");

        s_all.Clear();
        s_allSpecs.Clear();
        s_byId.Clear();
        s_specById.Clear();
        s_legacyNameToId.Clear();
        foreach (int t_id in t_assetOrder)
        {
            CardData t_asset = t_assets[t_id];
            CardSpec t_spec = t_specs[t_id];
            s_specById.Add(t_id, t_spec);
            if (_includeTestCards || t_spec.Channel == ECardChannel.Live)
            {
                s_byId.Add(t_id, t_asset);
                s_all.Add(t_asset);
                s_allSpecs.Add(t_spec);
            }
        }
        foreach (KeyValuePair<string, int> t_pair in t_legacyNames) s_legacyNameToId.Add(t_pair.Key, t_pair.Value);
        IsReady = true;
    }

    // 카드 식별 번호 산출의 유일한 지점(0 이하 = 미부여)
    public static int IdOf(CardData _card) => _card != null ? _card.id : 0;

    // 번호로 카드 조회 — 없거나 미부여면 null
    public static CardData Get(int _id)
    {
        if (_id <= 0) return null;

        return s_byId.TryGetValue(_id, out var t_card) ? t_card : null;
    }

    public static bool Contains(int _id) => _id > 0 && s_byId.ContainsKey(_id);

    public static bool TryGet(int _id, out CardData _card)
    {
        _card = Get(_id);
        return _card != null;
    }

    public static bool TryGetSpec(CardData _card, out CardSpec _spec)
        => TryGetSpec(IdOf(_card), out _spec);

    public static bool TryGetSpec(int _id, out CardSpec _spec)
    {
        _spec = null;
        return IsReady && _id > 0 && s_specById.TryGetValue(_id, out _spec);
    }

    public static CardSpec RequireSpec(CardData _card)
        => RequireSpec(IdOf(_card));

    public static CardSpec RequireSpec(int _id)
    {
        if (!IsReady) throw new InvalidOperationException("[CardCatalog] 초기화 전에 CardSpec을 조회했다.");
        if (_id <= 0 || !s_specById.TryGetValue(_id, out CardSpec t_spec))
            throw new InvalidOperationException($"[CardCatalog] 카드 ID {_id}의 CardSpec이 없다.");
        return t_spec;
    }

    /// <summary>구 세이브의 에셋 이름 키를 번호로 옮긴다. **세이브 이관 코드에서만 부를 것.**
    /// 카탈로그에 없는 이름(삭제·리네임된 카드)이면 0 — 호출부가 그 항목을 버려야 한다.</summary>
    public static int LegacyIdOfName(string _name)
    {
        if (string.IsNullOrEmpty(_name)) return 0;

        return s_legacyNameToId.TryGetValue(_name, out int t_id) ? t_id : 0;
    }
}
