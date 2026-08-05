using System.Collections.Generic;
using UnityEngine;

// 카드 마스터의 읽기 전용 단일 창구
public static class CardCatalog
{
    static readonly List<CardData> s_all = new List<CardData>();
    static readonly IReadOnlyList<CardData> s_allReadonly = s_all.AsReadOnly();
    
    static readonly Dictionary<string, CardData> s_byKey = new Dictionary<string, CardData>();

    public static bool IsReady { get; private set; }

    public static IReadOnlyList<CardData> All => s_allReadonly;

    public static int Count => s_all.Count;

    // 부트 주입 — 내부 인덱스 재구성
    public static void SetSource(IEnumerable<CardData> _cards)
    {
        s_all.Clear();
        s_byKey.Clear();

        if (_cards != null)
        {
            foreach (var t_card in _cards)
            {
                if (t_card == null) continue;

                var t_key = KeyOf(t_card);
                if (string.IsNullOrEmpty(t_key))
                {
                    Debug.LogWarning($"[CardCatalog] 카드 '{t_card}'의 키가 비어 제외한다.");
                    continue;
                }
                if (s_byKey.ContainsKey(t_key))
                {
                    Debug.LogWarning($"[CardCatalog] 중복 카드 키 '{t_key}' — 첫 항목만 유지한다.");
                    continue;
                }

                s_byKey.Add(t_key, t_card);
                s_all.Add(t_card);
            }
        }

        IsReady = true;
    }

    // 안정 키 산출의 유일한 지점(키 = SO 에셋 이름, displayName 아님)
    public static string KeyOf(CardData _card) => _card != null ? _card.name : null;

    // 키로 카드 조회 — 없으면 null
    public static CardData Get(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return null;

        return s_byKey.TryGetValue(_key, out var t_card) ? t_card : null;
    }

    public static bool Contains(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return false;

        return s_byKey.ContainsKey(_key);
    }

    public static bool TryGet(string _key, out CardData _card)
    {
        _card = Get(_key);
        return _card != null;
    }
}
