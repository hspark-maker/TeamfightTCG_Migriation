using System.Collections.Generic;
using UnityEngine;

// 카드 마스터의 읽기 전용 단일 창구
public static class CardCatalog
{
    static readonly List<CardData> s_all = new List<CardData>();
    static readonly IReadOnlyList<CardData> s_allReadonly = s_all.AsReadOnly();

    // 카드 식별의 단일 축 — 세이브·도감 행·덱이 전부 이 번호를 쓴다.
    static readonly Dictionary<int, CardData> s_byId = new Dictionary<int, CardData>();

    // 구 세이브(에셋 이름 키) 이관 전용 역인덱스. 평상시 조회에 쓰지 마라 —
    // 이름은 리네임으로 갈리는 축이고, 그걸 끊으려고 번호를 도입했다.
    static readonly Dictionary<string, int> s_legacyNameToId = new Dictionary<string, int>();

    public static bool IsReady { get; private set; }

    public static IReadOnlyList<CardData> All => s_allReadonly;

    public static int Count => s_all.Count;

    // 부트 주입 — 내부 인덱스 재구성
    public static void SetSource(IEnumerable<CardData> _cards)
    {
        s_all.Clear();
        s_byId.Clear();
        s_legacyNameToId.Clear();

        if (_cards != null)
        {
            foreach (var t_card in _cards)
            {
                if (t_card == null) continue;

                int t_id = IdOf(t_card);
                if (t_id <= 0)
                {
                    Debug.LogError($"[CardCatalog] 카드 '{t_card.name}'에 번호가 없어 제외한다. 카드 표(Excel) 가져오기로 번호를 부여할 것.");
                    continue;
                }
                if (s_byId.ContainsKey(t_id))
                {
                    Debug.LogError($"[CardCatalog] 카드 번호 {t_id} 중복 — '{s_byId[t_id].name}' 유지, '{t_card.name}' 제외. 표에서 번호를 고칠 것.");
                    continue;
                }

                s_byId.Add(t_id, t_card);
                s_all.Add(t_card);

                // 이름 충돌은 이관 정확도만 떨어뜨린다(구 세이브가 어느 쪽인지 모름) — 첫 항목만 잡고 경고.
                if (!string.IsNullOrEmpty(t_card.name) && !s_legacyNameToId.ContainsKey(t_card.name))
                    s_legacyNameToId.Add(t_card.name, t_id);
            }
        }

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

    /// <summary>구 세이브의 에셋 이름 키를 번호로 옮긴다. **세이브 이관 코드에서만 부를 것.**
    /// 카탈로그에 없는 이름(삭제·리네임된 카드)이면 0 — 호출부가 그 항목을 버려야 한다.</summary>
    public static int LegacyIdOfName(string _name)
    {
        if (string.IsNullOrEmpty(_name)) return 0;

        return s_legacyNameToId.TryGetValue(_name, out int t_id) ? t_id : 0;
    }
}
