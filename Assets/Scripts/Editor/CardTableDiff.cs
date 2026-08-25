using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

// 스펙시트 ↔ 카드 SO **대조**. 값을 밀어 넣지 않고 어긋난 곳만 찾아 보고한다.
// (클래스 요약은 CardTableTool.cs 쪽에 있다 — partial이라 문서 주석을 한 곳에만 둔다.)
//
// **왜 필요한가**: ContentRunModeEditor.Applied는 "마지막에 어느 표를 실었는가"를 적어 둔 도장일 뿐이다.
// 표를 적용한 뒤 인스펙터에서 maxHp를 손으로 고치면 도장은 그대로인 채 값만 갈린다.
// 빌드가 실제로 싣는 건 SO이므로, 나가기 전에 표와 SO를 **값 단위로** 맞춰 본다.
//
// **판정 규칙을 다시 적지 않는다**: 카드를 메모리 복제한 뒤 가져오기와 똑같은 ApplyRow를 태우고
// 원본과 비교한다. 그래서 "빈 칸은 기존 값 유지" 같은 규칙이 대조 쪽에서 따로 낡을 일이 없다.
//
// 결과는 **경고**다 — 빌드를 막지 않는다. 표에 없는 축(아트·패시브·보이스)은 애초에 대조 대상이 아니고,
// 의도적으로 SO만 손댄 상태로 빌드를 뽑는 일도 있기 때문이다. 막는 것은 프로필 검증의 몫이다.
public static partial class CardTableTool
{
    /// <summary>표와 카드 SO의 차이를 사람이 읽을 줄로 돌려준다(빈 목록 = 일치).
    /// 표를 못 읽으면 _error에 사유가 담기고 반환값은 null이다.
    ///
    /// 입력이 파일이 아니라 행 목록인 이유는 <see cref="ImportRows"/>와 같다 — 대조가 보는 표와
    /// 적용이 쓰는 표가 **같은 것**이라야 "대조는 통과했는데 적용하면 값이 달라지는" 상태가 안 생긴다.</summary>
    public static List<string> DiffRows(List<List<string>> _rows, out string _error)
    {
        _error = null;

        List<List<string>> t_rows = _rows;
        if (t_rows == null || t_rows.Count < 2)
        {
            _error = "표에 헤더 말고 데이터 행이 없다.";
            return null;
        }

        var t_header = new Dictionary<string, int>();
        for (int i = 0; i < t_rows[0].Count; i++)
        {
            string t_key = t_rows[0][i].Trim();
            if (!string.IsNullOrEmpty(t_key) && !t_header.ContainsKey(t_key)) t_header[t_key] = i;
        }
        if (!t_header.ContainsKey("name"))
        {
            _error = "'name' 열이 없다. 헤더 행을 확인할 것.";
            return null;
        }

        var t_cards = new Dictionary<string, CardData>();
        foreach (CardData c in AllCards()) t_cards[c.name] = c;

        Dictionary<string, ScriptableObject> t_synergies = AllSynergies();

        var t_drift  = new List<string>();
        var t_inTable = new HashSet<string>();

        for (int r = 1; r < t_rows.Count; r++)
        {
            List<string> t_row = t_rows[r];
            string t_name = NormalizeCardAssetName(Cell(t_row, t_header, "name").Trim());
            if (string.IsNullOrEmpty(t_name)) continue;   // 빈 행(Excel이 흔히 남긴다)

            if (!t_inTable.Add(t_name))
            {
                t_drift.Add($"{t_name}: 표에 같은 이름 행이 둘 이상 — 뒷 행이 이긴다");
                continue;
            }

            if (!t_cards.TryGetValue(t_name, out CardData t_card))
            {
                t_drift.Add($"{t_name}: 표에만 있고 카드 에셋이 없다 — 표를 적용하면 새로 생긴다");
                continue;
            }

            DiffCard(t_card, t_row, t_header, t_synergies, t_name, t_drift);
        }

        foreach (var t_pair in t_cards)
            if (!t_inTable.Contains(t_pair.Key))
                t_drift.Add($"{t_pair.Key}: 카드 에셋에만 있고 표에 행이 없다 — 표가 이 카드를 모른다");

        return t_drift;
    }

    /// <summary>한 장 대조. 복제본에 가져오기와 같은 <see cref="ApplyRow"/>를 태워 "표대로라면 어떤 값이 되는가"를
    /// 만들고 원본과 견준다. 가져오기가 내는 경고(모르는 키워드·없는 시너지)도 그대로 보고 대상이다.</summary>
    static void DiffCard(CardData _card, List<string> _row, Dictionary<string, int> _header,
                         Dictionary<string, ScriptableObject> _synergies, string _name, List<string> _drift)
    {
        // id는 ApplyRow가 아니라 예약대장(ApplyId)이 다루는 축이라 여기서 직접 견준다.
        if (_header.ContainsKey("id"))
        {
            int t_id = ParseInt(Cell(_row, _header, "id"), 0);
            if (t_id > 0 && t_id != _card.id)
                _drift.Add(Line(_name, "id", t_id.ToString(CultureInfo.InvariantCulture),
                                _card.id.ToString(CultureInfo.InvariantCulture)));
        }

        var t_expected = ScriptableObject.Instantiate(_card);
        try
        {
            var t_warnings = new List<string>();
            ApplyRow(t_expected, _row, _header, _synergies, _name, t_warnings);

            foreach (string w in t_warnings) _drift.Add(w);

            Compare(_drift, _name, "displayName", _card.displayName, t_expected.displayName);
            Compare(_drift, _name, "channel", _card.channel.ToString(), t_expected.channel.ToString());
            Compare(_drift, _name, "grade", _card.grade.ToString(), t_expected.grade.ToString());
            Compare(_drift, _name, "maxHp", _card.maxHp, t_expected.maxHp);
            Compare(_drift, _name, "keywords", KeywordsToText(_card.keywords), KeywordsToText(t_expected.keywords));
            Compare(_drift, _name, "keywordUnlockLevel", _card.keywordUnlockLevel, t_expected.keywordUnlockLevel);
            Compare(_drift, _name, "defaultEvolutionStage", _card.defaultEvolutionStage, t_expected.defaultEvolutionStage);
            Compare(_drift, _name, "cardExplain", _card.cardExplain, t_expected.cardExplain);
            Compare(_drift, _name, "synergies", SynergiesToText(_card.synergies), SynergiesToText(t_expected.synergies));

            if (!SameCurve(_card.hpGainByLevel, t_expected.hpGainByLevel))
                _drift.Add(Line(_name, "hp2~hp4", CurveToText(t_expected.hpGainByLevel), CurveToText(_card.hpGainByLevel)));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(t_expected);
        }
    }

    static void Compare(List<string> _drift, string _name, string _field, int _asset, int _expected)
    {
        if (_asset != _expected)
            _drift.Add(Line(_name, _field, _expected.ToString(CultureInfo.InvariantCulture),
                            _asset.ToString(CultureInfo.InvariantCulture)));
    }

    static void Compare(List<string> _drift, string _name, string _field, string _asset, string _expected)
    {
        if (!string.Equals(_asset ?? "", _expected ?? "", StringComparison.Ordinal))
            _drift.Add(Line(_name, _field, _expected, _asset));
    }

    static string Line(string _name, string _field, string _table, string _asset)
        => $"{_name}.{_field}: 표 '{Short(_table)}' ≠ 에셋 '{Short(_asset)}'";

    // 설명문처럼 긴 값이 그대로 흘러나오면 목록을 못 읽는다 — 어느 카드의 어느 열인지만 보이면 된다.
    static string Short(string _value)
    {
        string t_v = (_value ?? "").Replace("\n", " ").Replace("\r", " ");
        return t_v.Length <= 40 ? t_v : t_v.Substring(0, 40) + "…";
    }

    /// <summary>비어 있음(= CardGrowthConfig 전역식)과 전부 0은 다른 뜻이라 길이를 접기 전에 먼저 가른다.</summary>
    static bool SameCurve(int[] _a, int[] _b)
    {
        bool t_emptyA = _a == null || _a.Length == 0;
        bool t_emptyB = _b == null || _b.Length == 0;
        if (t_emptyA || t_emptyB) return t_emptyA && t_emptyB;

        for (int t_level = CardData.MinHpCurveLevel; t_level <= CardData.MaxHpCurveLevel; t_level++)
            if (CurveAt(_a, t_level) != CurveAt(_b, t_level)) return false;
        return true;
    }

    static int CurveAt(int[] _curve, int _level)
        => _curve != null && _level < _curve.Length ? _curve[_level] : 0;

    static string CurveToText(int[] _curve)
    {
        if (_curve == null || _curve.Length == 0) return "(전역식)";

        var t_parts = new List<string>();
        for (int t_level = CardData.MinHpCurveLevel; t_level <= CardData.MaxHpCurveLevel; t_level++)
            t_parts.Add(CurveAt(_curve, t_level).ToString(CultureInfo.InvariantCulture));
        return string.Join("/", t_parts);
    }

    /// <summary>대조 결과를 창·로그가 같은 모양으로 쓰도록 한 곳에서 접는다. 줄이 많으면 앞부분만 보인다 —
    /// 표를 통째로 갈아 끼운 직후엔 수백 줄이 나오는데, 그걸 다 뿌리면 오히려 아무도 안 읽는다.</summary>
    public static string DriftSummary(List<string> _drift, int _maxLines = 20)
    {
        if (_drift == null || _drift.Count == 0) return "";

        var t_sb = new System.Text.StringBuilder();
        t_sb.Append($"표와 카드 에셋이 어긋난 곳 {_drift.Count}건");
        int t_shown = Mathf.Min(_maxLines, _drift.Count);
        for (int i = 0; i < t_shown; i++) t_sb.Append("\n· ").Append(_drift[i]);
        if (t_shown < _drift.Count) t_sb.Append($"\n… 그 밖에 {_drift.Count - t_shown}건");
        return t_sb.ToString();
    }
}
