using System.Collections.Generic;
using UnityEngine;

// 카드 마스터의 읽기 전용 단일 창구. 부트에서 카드 목록을 주입받아 안정 문자열 키로 조회한다.
// 안정 키 = CardData.name(SO 에셋 파일명). 덱·소유권 세이브 키와 정합. displayName은 키로 쓰지 않는다.
// TODO  : 지금은 카드 데이터 자체가 SO고 그 이름을 키로 쓰는데, 나중에 CardData에 id추가하고 그걸 키로 쓰면 이거 없어도 될듯
public static class CardCatalog
{
    // 주입 순서 보존용 원본 목록.
    static readonly List<CardData> s_all = new List<CardData>();
    // 외부 변조 차단용 읽기 전용 래퍼(s_all 위에 한 번만 씌움).
    static readonly IReadOnlyList<CardData> s_allReadonly = s_all.AsReadOnly();
    // 키 → 카드 역인덱스. 같은 키 중복 시 첫 항목만 유지.
    static readonly Dictionary<string, CardData> s_byKey = new Dictionary<string, CardData>();

    public static bool IsReady { get; private set; }

    public static IReadOnlyList<CardData> All => s_allReadonly;

    public static int Count => s_all.Count;

    // 부트 주입. 내부 인덱스를 재구성한다(재호출 시 기존 내용 교체).
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
                    // 빈 키는 조회 불가 유령 항목이 되므로 제외.
                    Debug.LogWarning($"[CardCatalog] 카드 '{t_card}'의 키가 비어 제외한다.");
                    continue;
                }
                if (s_byKey.ContainsKey(t_key))
                {
                    // 같은 name 두 장 — 첫 항목 유지, 중복은 스킵.
                    Debug.LogWarning($"[CardCatalog] 중복 카드 키 '{t_key}' — 첫 항목만 유지한다.");
                    continue;
                }

                s_byKey.Add(t_key, t_card);
                s_all.Add(t_card);
            }
        }

        IsReady = true;
    }

    // 안정 키 산출의 유일한 지점. 나중에 CardData에 명시적 id가 생기면 여기만 고치면 된다.
    public static string KeyOf(CardData _card) => _card != null ? _card.name : null;

    // 없으면 null. 미초기화·null·빈 키는 예외 없이 null.
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
