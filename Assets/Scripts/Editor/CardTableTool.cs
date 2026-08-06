using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 카드 표(Excel/CSV) ↔ CardData SO 왕복 도구. Tools > Card Battle > 카드 표(Excel).
///
/// **왜 CSV인가**: .xlsx를 직접 읽으려면 외부 라이브러리(EPPlus/NPOI)를 프로젝트에 들여야 한다.
/// Excel은 "CSV UTF-8"로 저장/열기를 기본 지원하므로, 표는 Excel에서 편집하고 프로젝트는 CSV만 읽는다.
/// 내보낼 때 **UTF-8 BOM**을 붙이는 이유도 이것이다 — BOM이 없으면 Excel이 한글을 깨서 연다.
///
/// **이 도구가 소유하는 열만 덮어쓴다.** 아트·패시브·보이스·attackEffect는 표에 없고 건드리지도 않는다 —
/// 표를 다시 밀어넣어도 인스펙터에서 채운 배선이 날아가지 않는다.
///
/// **삭제·재정렬은 하지 않는다.** CardRegistry는 배열 인덱스가 곧 멀티 와이어 ID다(CardAuthoringWindow와 같은 규약).
/// 표에서 행을 지워도 카드 에셋과 등록은 그대로 남는다.
/// </summary>
public class CardTableTool : EditorWindow
{
    const string DEFAULT_TABLE_PATH = "Assets/SO/CardTable.csv";
    const string DEFAULT_CARD_ROOT  = "Assets/SO/Cards";
    const string REGISTRY_PATH      = "Assets/SO/CardRegistry.asset";

    const string PREF_TABLE = "CardTable.TablePath";
    const string PREF_ROOT  = "CardTable.CardRoot";

    // 열 순서 = 내보내기 순서. 읽을 때는 헤더 이름으로 찾으므로 Excel에서 열을 옮겨도 된다.
    // 표에 없는 축은 인스펙터 전용이다: bonusHp(시너지 덩치 채널), explainKeywords(설명 전용 표시),
    // 강화 키워드(기본 키워드가 강화되는 개념이라 별도 열이 없다).
    static readonly string[] Columns =
    {
        "name", "displayName", "channel", "maxHp",
        "keywords", "keywordUnlockLevel",
        "synergies", "defaultEvolutionStage", "cardExplain",
    };

    string tablePath = DEFAULT_TABLE_PATH;
    string cardRoot  = DEFAULT_CARD_ROOT;

    Vector2 scroll;
    string  lastReport;

    [MenuItem("Tools/Card Battle/카드 표(Excel)")]
    static void Open() => GetWindow<CardTableTool>("카드 표").minSize = new Vector2(460, 420);

    void OnEnable()
    {
        this.tablePath = EditorPrefs.GetString(PREF_TABLE, DEFAULT_TABLE_PATH);
        this.cardRoot  = EditorPrefs.GetString(PREF_ROOT,  DEFAULT_CARD_ROOT);
    }

    void OnDisable()
    {
        EditorPrefs.SetString(PREF_TABLE, this.tablePath);
        EditorPrefs.SetString(PREF_ROOT,  this.cardRoot);
    }

    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 에셋을 만들 수 없다. 플레이를 멈추고 다시 열 것.", MessageType.Warning);
            return;
        }

        this.scroll = EditorGUILayout.BeginScrollView(this.scroll);

        EditorGUILayout.LabelField("표 파일 (CSV UTF-8)", EditorStyles.boldLabel);
        this.tablePath = EditorGUILayout.TextField(this.tablePath);
        EditorGUILayout.LabelField("카드 에셋 생성 위치", EditorStyles.boldLabel);
        this.cardRoot = EditorGUILayout.TextField(this.cardRoot);

        EditorGUILayout.Space(10);
        if (GUILayout.Button("① 카드 → 표 내보내기", GUILayout.Height(30))) Export();

        EditorGUILayout.Space(4);
        GUI.enabled = File.Exists(this.tablePath);
        if (GUILayout.Button("② 표 → 카드 SO 생성/갱신", GUILayout.Height(38))) Import();
        GUI.enabled = true;
        if (!File.Exists(this.tablePath))
            EditorGUILayout.HelpBox("표 파일이 아직 없다. ①로 현재 카드를 뽑아 Excel에서 편집할 것.", MessageType.Info);

        if (!string.IsNullOrEmpty(this.lastReport))
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(this.lastReport, GUILayout.MinHeight(120));
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "열: " + string.Join(", ", Columns) + "\n\n" +
            "· keywords : 키워드 이름을 | 로 나열 (예: Ranged|Peerless). 해금 전에는 없는 것으로 친다.\n" +
            "· keywordUnlockLevel : keywords가 열리는 강화 레벨. 0/빈칸 = 처음부터 열림(기본 키워드 카드).\n" +
            "· synergies : SynergyData 에셋 이름을 | 로 나열\n" +
            "· 진화 레벨(1차/2차)과 레벨별 체력 증가량은 표에 없다 — CardGrowthConfig가 소유한다.\n" +
            "· 표에 없는 열(아트·패시브·보이스)은 건드리지 않는다.\n" +
            "· 행을 지워도 카드는 지워지지 않는다(CardRegistry ID 보존).", MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    // ── 내보내기 ───────────────────────────────────────────────────────────

    void Export() => this.lastReport = ExportTo(this.tablePath);

    /// <summary>표를 파일로 뽑는 실동작. 창 없이도(자동화·검증) 부를 수 있게 static으로 분리한다.</summary>
    public static string ExportTo(string _tablePath)
    {
        List<CardData> t_cards = AllCards();

        var t_sb = new StringBuilder();
        t_sb.AppendLine(string.Join(",", Columns));
        foreach (CardData t_card in t_cards)
            t_sb.AppendLine(string.Join(",", Array.ConvertAll(Columns, c => Escape(ValueOf(t_card, c)))));

        string t_dir = Path.GetDirectoryName(_tablePath);
        if (!string.IsNullOrEmpty(t_dir)) Directory.CreateDirectory(t_dir);

        // BOM 필수 — 없으면 Excel이 한글을 깨서 연다.
        File.WriteAllText(_tablePath, t_sb.ToString(), new UTF8Encoding(true));
        AssetDatabase.Refresh();

        string t_report = $"내보내기 완료: {t_cards.Count}행\n{_tablePath}";
        Debug.Log($"[카드 표] {t_report}");
        return t_report;
    }

    static string ValueOf(CardData _card, string _column)
    {
        switch (_column)
        {
            case "name":                  return _card.name;
            case "displayName":           return _card.displayName;
            case "channel":               return _card.channel.ToString();
            case "maxHp":                 return _card.maxHp.ToString(CultureInfo.InvariantCulture);
            case "keywords":              return KeywordsToText(_card.keywords);
            case "keywordUnlockLevel":    return _card.keywordUnlockLevel.ToString(CultureInfo.InvariantCulture);
            case "synergies":             return SynergiesToText(_card.synergies);
            case "defaultEvolutionStage": return _card.defaultEvolutionStage.ToString(CultureInfo.InvariantCulture);
            case "cardExplain":           return _card.cardExplain;
            default:                      return "";
        }
    }

    // ── 가져오기 ───────────────────────────────────────────────────────────

    void Import()
    {
        this.lastReport = ImportFrom(this.tablePath, this.cardRoot, out string t_error);
        if (t_error != null) EditorUtility.DisplayDialog("실패", t_error, "확인");
    }

    /// <summary>표를 읽어 카드 SO를 생성/갱신하는 실동작. 창 없이도(자동화·검증) 부를 수 있게 static으로 분리한다.
    /// 막힌 경우 _error에 사유가 담기고 반환값은 빈 문자열이다.</summary>
    public static string ImportFrom(string _tablePath, string _cardRoot, out string _error)
    {
        _error = null;

        if (LoadRegistry() == null)
        {
            _error = $"CardRegistry를 못 찾음: {REGISTRY_PATH}";
            return "";
        }
        if (!File.Exists(_tablePath))
        {
            _error = $"표 파일이 없다: {_tablePath}";
            return "";
        }

        List<List<string>> t_rows = ParseCsv(File.ReadAllText(_tablePath, Encoding.UTF8));
        if (t_rows.Count < 2)
        {
            _error = "표에 헤더 말고 데이터 행이 없다.";
            return "";
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
            return "";
        }

        Dictionary<string, CardData> t_existing = new Dictionary<string, CardData>();
        foreach (CardData c in AllCards()) t_existing[c.name] = c;

        Dictionary<string, ScriptableObject> t_synergies = AllSynergies();

        int t_created = 0, t_updated = 0;
        var t_warnings = new List<string>();

        for (int r = 1; r < t_rows.Count; r++)
        {
            List<string> t_row = t_rows[r];
            string t_name = Cell(t_row, t_header, "name").Trim();
            if (string.IsNullOrEmpty(t_name)) continue;   // 빈 행(Excel이 흔히 남긴다)

            if (!t_existing.TryGetValue(t_name, out CardData t_card))
            {
                t_card = CreateCardAsset(_cardRoot, t_name);
                if (t_card == null) { t_warnings.Add($"{t_name}: 에셋 생성 실패"); continue; }
                t_existing[t_name] = t_card;
                t_created++;
            }
            else
            {
                t_updated++;
            }

            ApplyRow(t_card, t_row, t_header, t_synergies, t_name, t_warnings);
            // 이미 존재하지만 Registry에서 빠진 카드도 가져오기 한 번으로 복구한다.
            // AppendToRegistry는 기존 참조면 no-op이며 항상 맨 뒤에만 추가해 와이어 ID를 보존한다.
            AppendToRegistry(t_card);
            EditorUtility.SetDirty(t_card);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var t_sb = new StringBuilder();
        t_sb.AppendLine($"가져오기 완료 — 신규 {t_created}장 / 갱신 {t_updated}장");
        if (t_warnings.Count > 0)
        {
            t_sb.AppendLine($"경고 {t_warnings.Count}건:");
            foreach (string w in t_warnings) t_sb.AppendLine("  · " + w);
        }
        string t_report = t_sb.ToString();
        Debug.Log($"[카드 표] {t_report}");
        return t_report;
    }

    static void ApplyRow(CardData _card, List<string> _row, Dictionary<string, int> _header,
                         Dictionary<string, ScriptableObject> _synergies, string _name, List<string> _warnings)
    {
        if (_header.ContainsKey("displayName"))
        {
            string t_display = Cell(_row, _header, "displayName");
            _card.displayName = string.IsNullOrWhiteSpace(t_display) ? _name : t_display;
        }
        if (_header.ContainsKey("channel"))
        {
            string t_channel = Cell(_row, _header, "channel");
            if (Enum.TryParse(t_channel, true, out ECardChannel t_parsed)) _card.channel = t_parsed;
            else _warnings.Add($"{_name}.channel: 알 수 없는 채널 '{t_channel}' — 기존 값 유지");
        }
        if (_header.ContainsKey("maxHp")) _card.maxHp = ParseInt(Cell(_row, _header, "maxHp"), _card.maxHp);

        if (_header.ContainsKey("keywords"))
            _card.keywords = ParseKeywords(Cell(_row, _header, "keywords"), _name, "keywords", _warnings);
        if (_header.ContainsKey("keywordUnlockLevel"))
        {
            // 음수는 0(처음부터 열림)으로 접는다 — 빈 칸과 같은 뜻이 되게.
            int t_level = ParseInt(Cell(_row, _header, "keywordUnlockLevel"), _card.keywordUnlockLevel);
            _card.keywordUnlockLevel = t_level < 0 ? 0 : t_level;
        }

        if (_header.ContainsKey("defaultEvolutionStage"))
        {
            int t_stage = ParseInt(Cell(_row, _header, "defaultEvolutionStage"), _card.defaultEvolutionStage);
            _card.defaultEvolutionStage = Mathf.Clamp(t_stage, 0, CardData.MaxEvolutionStage);
        }

        if (_header.ContainsKey("cardExplain")) _card.cardExplain = Cell(_row, _header, "cardExplain");

        if (_header.ContainsKey("synergies"))
            _card.synergies = ParseSynergies(Cell(_row, _header, "synergies"), _synergies, _name, _warnings);
    }

    static CardData CreateCardAsset(string _cardRoot, string _name)
    {
        if (_name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (!EnsureFolder(_cardRoot)) return null;

        string t_dir = $"{_cardRoot}/{_name}";
        if (!AssetDatabase.IsValidFolder(t_dir)) AssetDatabase.CreateFolder(_cardRoot, _name);

        var t_card = CreateInstance<CardData>();
        t_card.displayName = _name;
        AssetDatabase.CreateAsset(t_card, $"{t_dir}/{_name}.asset");
        AppendToRegistry(t_card);
        return t_card;
    }

    // ── 값 변환 ────────────────────────────────────────────────────────────

    static string KeywordsToText(CardKeyword _kw)
    {
        if (_kw == CardKeyword.None) return "";

        var t_parts = new List<string>();
        foreach (CardKeyword t_flag in Enum.GetValues(typeof(CardKeyword)))
        {
            if (t_flag == CardKeyword.None) continue;
            if ((_kw & t_flag) == t_flag) t_parts.Add(t_flag.ToString());
        }
        return string.Join("|", t_parts);
    }

    static CardKeyword ParseKeywords(string _text, string _card, string _column, List<string> _warnings)
    {
        CardKeyword t_kw = CardKeyword.None;
        if (string.IsNullOrWhiteSpace(_text)) return t_kw;

        foreach (string t_raw in _text.Split('|'))
        {
            string t_token = t_raw.Trim();
            if (t_token.Length == 0) continue;

            if (Enum.TryParse(t_token, true, out CardKeyword t_one)) t_kw |= t_one;
            else _warnings.Add($"{_card}.{_column}: 모르는 키워드 '{t_token}' — 무시");
        }
        return t_kw;
    }

    static string SynergiesToText(SynergyData[] _synergies)
    {
        if (_synergies == null || _synergies.Length == 0) return "";

        var t_parts = new List<string>();
        foreach (SynergyData s in _synergies)
            if (s != null && !t_parts.Contains(s.name)) t_parts.Add(s.name);
        return string.Join("|", t_parts);
    }

    static SynergyData[] ParseSynergies(string _text, Dictionary<string, ScriptableObject> _known,
                                        string _card, List<string> _warnings)
    {
        var t_list = new List<SynergyData>();
        if (string.IsNullOrWhiteSpace(_text)) return t_list.ToArray();

        foreach (string t_raw in _text.Split('|'))
        {
            string t_token = t_raw.Trim();
            if (t_token.Length == 0) continue;

            if (_known.TryGetValue(t_token, out ScriptableObject t_so) && t_so is SynergyData t_syn)
            {
                if (!t_list.Contains(t_syn)) t_list.Add(t_syn);   // 중복은 소비측에서 1회 취급 — 미리 정리
            }
            else
            {
                _warnings.Add($"{_card}.synergies: SynergyData '{t_token}' 없음 — 무시");
            }
        }
        return t_list.ToArray();
    }

    static int ParseInt(string _text, int _fallback)
        => int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t_v) ? t_v : _fallback;

    // ── CSV ────────────────────────────────────────────────────────────────

    static string Escape(string _value)
    {
        string t_v = _value ?? "";
        if (t_v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return t_v;
        return "\"" + t_v.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>RFC4180 최소 파서. 따옴표 안의 쉼표·줄바꿈을 지킨다 —
    /// cardExplain에 쉼표가 들어가는 순간 Split(',')짜리는 조용히 열을 밀어버린다.</summary>
    static List<List<string>> ParseCsv(string _text)
    {
        var t_rows  = new List<List<string>>();
        var t_row   = new List<string>();
        var t_field = new StringBuilder();
        bool t_quoted = false;

        for (int i = 0; i < _text.Length; i++)
        {
            char c = _text[i];

            if (t_quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < _text.Length && _text[i + 1] == '"') { t_field.Append('"'); i++; }
                    else t_quoted = false;
                }
                else t_field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    t_quoted = true;
                    break;
                case ',':
                    t_row.Add(t_field.ToString()); t_field.Clear();
                    break;
                case '\r':
                    break;   // \r\n은 \n에서 한 번만 끊는다
                case '\n':
                    t_row.Add(t_field.ToString()); t_field.Clear();
                    t_rows.Add(t_row); t_row = new List<string>();
                    break;
                default:
                    t_field.Append(c);
                    break;
            }
        }

        if (t_field.Length > 0 || t_row.Count > 0)
        {
            t_row.Add(t_field.ToString());
            t_rows.Add(t_row);
        }

        // 선두 BOM 제거 — 남기면 첫 헤더가 "﻿name"이 되어 name 열을 못 찾는다.
        if (t_rows.Count > 0 && t_rows[0].Count > 0)
            t_rows[0][0] = t_rows[0][0].TrimStart('﻿');

        return t_rows;
    }

    static string Cell(List<string> _row, Dictionary<string, int> _header, string _column)
    {
        if (!_header.TryGetValue(_column, out int t_i)) return "";
        return t_i < _row.Count ? _row[t_i] : "";
    }

    // ── 에셋 헬퍼 (CardAuthoringWindow와 같은 규약) ─────────────────────────

    static CardRegistry LoadRegistry() => AssetDatabase.LoadAssetAtPath<CardRegistry>(REGISTRY_PATH);

    /// <summary>CardRegistry 맨 뒤에 append. 이미 있으면 기존 ID 반환(중복 등록 방지).</summary>
    static int AppendToRegistry(CardData _card)
    {
        CardRegistry t_reg = LoadRegistry();
        if (t_reg == null || _card == null) return -1;

        var t_so  = new SerializedObject(t_reg);
        var t_arr = t_so.FindProperty("allCards");

        for (int i = 0; i < t_arr.arraySize; i++)
            if (t_arr.GetArrayElementAtIndex(i).objectReferenceValue == _card) return i;

        int t_id = t_arr.arraySize;
        t_arr.InsertArrayElementAtIndex(t_id);
        t_arr.GetArrayElementAtIndex(t_id).objectReferenceValue = _card;
        t_so.ApplyModifiedProperties();
        EditorUtility.SetDirty(t_reg);
        return t_id;
    }

    static bool EnsureFolder(string _path)
    {
        string t_path = _path.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(t_path)) return true;
        if (!t_path.StartsWith("Assets")) return false;

        string[] t_parts = t_path.Split('/');
        string   t_cur   = t_parts[0];
        for (int i = 1; i < t_parts.Length; i++)
        {
            string t_next = $"{t_cur}/{t_parts[i]}";
            if (!AssetDatabase.IsValidFolder(t_next)) AssetDatabase.CreateFolder(t_cur, t_parts[i]);
            t_cur = t_next;
        }
        return AssetDatabase.IsValidFolder(t_path);
    }

    /// <summary>표의 행 순서는 CardRegistry ID 순 — 표를 봤을 때 와이어 ID를 그대로 읽을 수 있게.
    /// 미등록 카드는 뒤에 이어 붙인다.</summary>
    static List<CardData> AllCards()
    {
        var t_list = new List<CardData>();
        var t_seen = new HashSet<CardData>();

        CardRegistry t_reg = LoadRegistry();
        if (t_reg != null)
        {
            foreach (CardData c in t_reg.All)
                if (c != null && t_seen.Add(c)) t_list.Add(c);
        }

        var t_rest = new List<CardData>();
        foreach (string g in AssetDatabase.FindAssets("t:CardData"))
        {
            var c = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(g));
            if (c != null && t_seen.Add(c)) t_rest.Add(c);
        }
        t_rest.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        t_list.AddRange(t_rest);

        return t_list;
    }

    static Dictionary<string, ScriptableObject> AllSynergies()
    {
        var t_map = new Dictionary<string, ScriptableObject>();
        foreach (string g in AssetDatabase.FindAssets("t:SynergyData"))
        {
            var s = AssetDatabase.LoadAssetAtPath<SynergyData>(AssetDatabase.GUIDToAssetPath(g));
            if (s != null && !t_map.ContainsKey(s.name)) t_map[s.name] = s;
        }
        return t_map;
    }
}
