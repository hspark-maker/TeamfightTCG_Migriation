using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 카드 표 ↔ CardData SO 엔진. **UI는 없다** — 창은 ReleaseManagerWindow 하나뿐이다.
///
/// **값이 들어오는 문은 구글 스펙시트 하나다**(<see cref="CardSpecImporter"/> → <see cref="ImportRows"/>).
/// CSV는 반대 방향(내보내기)과 대조 전용으로 남았다 — 들어오는 문이 둘이면 어느 쪽으로 마지막에
/// 덮었는지에 따라 에셋이 달라진다. 내보낸 CSV에 **UTF-8 BOM**을 붙이는 이유는 Excel이
/// BOM 없는 파일의 한글을 깨서 열기 때문이다.
///
/// **이 도구가 소유하는 열만 덮어쓴다.** 아트·패시브·보이스·attackEffect는 표에 없고 건드리지도 않는다 —
/// 표를 다시 밀어넣어도 인스펙터에서 채운 배선이 날아가지 않는다.
///
/// **행을 지워도 카드 에셋과 등록은 남는다.** 표는 값을 밀어 넣는 창구일 뿐 삭제 창구가 아니다.
/// 멀티 와이어 ID는 id 열의 카드 번호이므로 행 순서·목록 순서는 의미가 없다 — 바꾸면 안 되는 건 번호 자체다.
/// </summary>
public static partial class CardTableTool
{
    // Asset filenames were standardized in English. Keep accepting the legacy
    // Korean sheet keys during the external Google Sheet transition window.
    static readonly Dictionary<string, string> LegacyCardAssetNames = new Dictionary<string, string>
    {
        { "모닥콩", "Campbean" }, { "포슬램", "Poslamb" }, { "찌릿핀", "Sparkfin" },
        { "물방울룽", "WaterdropLong" }, { "바위콩", "Rockbean" }, { "깜밤이", "Nightchestnut" },
        { "솜구름몽", "Cloudmong" }, { "얼음꼬미", "Icekomi" }, { "꿀꿀비", "Honeybee" },
        { "톱니두더", "Gearmole" }, { "화르룩스", "Flarelux" }, { "철갑몽치", "IronMongchi" },
        { "풍선펭", "BalloonPeng" }, { "버섯냥", "MushroomCat" }, { "별토리", "Startori" },
        { "늪꾸리", "Swampfrog" }, { "번개뿔", "Thunderhorn" }, { "단풍꼬리", "Mapletail" },
        { "눈덩곰", "SnowballBear" }, { "와글도도", "Waggledodo" }, { "자석게", "MagnetCrab" },
        { "해롱문어", "DizzyOctopus" }, { "폭탄밤", "Bombbat" }, { "우드혼", "Woodhorn" },
        { "파도리", "Waveri" }, { "모래몽", "Sandmong" }, { "수정뿔루", "Crystalhorn" },
        { "대장부리", "CaptainBeak" }, { "꿈먹이", "Dreameater" },
        { "왕밤도치", "KingChestnutHedgehog" }
    };

    static readonly Dictionary<string, string> LegacySynergyAssetNames = new Dictionary<string, string>
    {
        { "덩치", "Bulk" }, { "돌보미", "Caretaker" }, { "낙인", "Brand" },
        { "비늘", "Scale" }, { "수호자", "Guardian" }, { "언데드", "Undead" },
        { "유산", "Legacy" }, { "포식자", "Predator" }, { "흐름", "Flow" }
    };

    static string NormalizeCardAssetName(string _name)
        => LegacyCardAssetNames.TryGetValue(_name, out string t_name) ? "Data_Card_" + t_name : _name;

    static string NormalizeSynergyAssetName(string _name)
        => LegacySynergyAssetNames.TryGetValue(_name, out string t_name) ? "Data_Synergy_" + t_name : _name;

    const int HP_CURVE_MIN_LEVEL = CardData.MinHpCurveLevel;
    const int HP_CURVE_MAX_LEVEL = CardData.MaxHpCurveLevel;
    const string REGISTRY_PATH   = "Assets/SO/CardRegistry.asset";

    // 열 순서 = 내보내기 순서. 읽을 때는 헤더 이름으로 찾으므로 Excel에서 열을 옮겨도 된다.
    // 표에 없는 축은 인스펙터 전용이다: bonusHp(시너지 덩치 채널), explainKeywords(설명 전용 표시),
    // 강화 키워드(기본 키워드가 강화되는 개념이라 별도 열이 없다).
    static readonly string[] Columns =
    {
        "id",
        "name", "displayName", "channel", "maxHp",
        "keywords", "keywordUnlockLevel",
        "synergies", "defaultEvolutionStage",
        "hp2", "hp3", "hp4", "hp5", "hp6", "hp7", "hp8", "hp9", "hp10",
        "cardExplain", "grade",
    };

    /// <summary>창이 띄우는 열 설명. 규칙 문구의 진실원을 표 도구 쪽에 둔다(UI가 규칙을 다시 적지 않게).</summary>
    public static string ColumnHelp =>
        "열: " + string.Join(", ", Columns) + "\n\n" +
        "· id : 카드 고유 번호. 한 번 부여하면 바꾸지 않는다. 빈칸이면 남은 번호를 자동 부여한다.\n" +
        "· keywords : 키워드 이름을 | 로 나열 (예: Ranged|Peerless). 해금 전에는 없는 것으로 친다.\n" +
        "· keywordUnlockLevel : keywords가 열리는 강화 레벨. 0/빈칸 = 처음부터 열림.\n" +
        "· synergies : SynergyData 에셋 이름을 | 로 나열\n" +
        "· grade : 카드 희소 등급 이름(Silver/Gold/Prism). 빈칸 = Unknown(미배정).\n" +
        "  숫자나 모르는 이름은 값을 바꾸지 않고 경고만 남긴다.\n" +
        "· hp2~hp10 : 그 레벨 진입 시 증가 HP. 강화는 Lv2부터라 그 아래 열은 없다. 9칸 전부 비면\n" +
        "  CardGrowthConfig 전역식, 하나라도 채우면 나머지 빈칸은 0으로 저장된다.\n" +
        "· 진화 레벨과 비용/성공률은 CardGrowthConfig 소유.\n" +
        "· 표에 없는 열(아트·패시브·보이스)은 건드리지 않는다.\n" +
        "· 행을 지워도 카드는 지워지지 않는다(에셋·등록 보존).\n" +
        "· 라이브(Card)/테스트(Card_Test) 시트는 같은 CardData를 공유한다 — 모드 전환은 곧 덮어쓰기다.\n" +
        "· 두 시트의 같은 카드는 반드시 같은 id여야 한다(매칭 키가 id).";

    // ── 가져오기 ───────────────────────────────────────────────────────────

    /// <summary>표 **내용**에서 시작하는 가져오기. 0행 = 헤더(열 이름), 1행부터 데이터.
    ///
    /// 파일 경로가 아니라 행 목록을 받는 이유: 표의 출처가 CSV 하나가 아니게 됐다(스펙시트 → <see cref="CardSpecImporter"/>).
    /// 소스마다 파싱·검증을 복제하면 "CSV로 넣을 때와 시트로 넣을 때가 다른" 상태가 반드시 생긴다 —
    /// id 예약·키워드/시너지 해석·hp곡선·경고·레지스트리 등록은 전부 여기 한 갈래로만 흐른다.</summary>
    public static string ImportRows(List<List<string>> _rows, string _cardRoot, out string _error)
    {
        _error = null;

        if (LoadRegistry() == null)
        {
            _error = $"CardRegistry를 못 찾음: {REGISTRY_PATH}";
            return "";
        }

        List<List<string>> t_rows = _rows;
        if (t_rows == null || t_rows.Count < 2)
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

        // 번호 → 카드. **매칭의 1순위는 이름이 아니라 번호다** — 번호가 카드를 가리키는 안정 키이고
        // (CardData.id 주석), 이름은 기획이 표에서 바꿀 수 있는 표시값이다. 이름으로만 찾으면
        // 표에서 이름 한 글자만 고쳐도 같은 카드가 새 에셋으로 복제된다(guid가 갈려 배선이 통째로 끊긴다).
        var t_byId = new Dictionary<int, CardData>();
        foreach (CardData c in t_existing.Values)
            if (c != null && c.id > 0 && !t_byId.ContainsKey(c.id)) t_byId[c.id] = c;

        Dictionary<string, ScriptableObject> t_synergies = AllSynergies();

        int t_created = 0, t_updated = 0;
        var t_warnings = new List<string>();

        // 번호 예약대장(id → 소유 카드 이름). 표에 적힌 번호가 먼저 자리를 잡아야
        // 빈 칸 자동 부여가 그 번호를 가로채지 않는다 — 그래서 행 처리 전에 한 번 훑는다.
        Dictionary<int, string> t_idOwner = ClaimIds(t_rows, t_header, t_existing, t_warnings);

        for (int r = 1; r < t_rows.Count; r++)
        {
            List<string> t_row = t_rows[r];
            string t_name = NormalizeCardAssetName(Cell(t_row, t_header, "name").Trim());
            if (string.IsNullOrEmpty(t_name)) continue;   // 빈 행(Excel이 흔히 남긴다)

            // 번호 → 이름 순으로 찾는다. 둘 다 없을 때만 새 카드다.
            int t_rowId = t_header.ContainsKey("id") ? ParseInt(Cell(t_row, t_header, "id"), 0) : 0;
            CardData t_card = null;
            if (t_rowId > 0) t_byId.TryGetValue(t_rowId, out t_card);
            if (t_card == null) t_existing.TryGetValue(t_name, out t_card);

            if (t_card == null)
            {
                t_card = CreateCardAsset(_cardRoot, t_name);
                if (t_card == null) { t_warnings.Add($"{t_name}: 에셋 생성 실패"); continue; }
                t_existing[t_name] = t_card;
                t_created++;
            }
            else
            {
                t_updated++;
                // 번호로 찾았는데 이름이 다르면 표가 이긴다 — 에셋 파일을 따라 바꾼다.
                // 리네임은 guid를 보존하므로 이 카드를 참조하는 시나리오·카드팩·AI덱·도감 배선은 그대로 산다.
                if (t_card.name != t_name) RenameCardAsset(t_card, t_name, t_existing, t_warnings);
            }

            if (t_card.id > 0) t_byId[t_card.id] = t_card;

            ApplyId(t_card, t_row, t_header, t_name, t_idOwner, t_warnings);
            if (t_card.id > 0) t_byId[t_card.id] = t_card;
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

    /// <summary>이미 쓰이는 번호를 모아 예약대장을 만든다. 에셋에 박힌 번호가 먼저 들어가고,
    /// 표가 다른 번호를 적었으면 표가 이긴다(기획이 표에서 번호를 옮길 수 있어야 하므로).
    /// 두 카드가 같은 번호를 노리면 먼저 잡은 쪽이 유지되고 뒤쪽은 경고 후 기존 번호를 지킨다.</summary>
    static Dictionary<int, string> ClaimIds(List<List<string>> _rows, Dictionary<string, int> _header,
                                            Dictionary<string, CardData> _existing, List<string> _warnings)
    {
        var t_owner = new Dictionary<int, string>();

        foreach (var t_pair in _existing)
        {
            CardData t_card = t_pair.Value;
            if (t_card == null || t_card.id <= 0) continue;

            if (t_owner.TryGetValue(t_card.id, out string t_prev) && t_prev != t_pair.Key)
            {
                _warnings.Add($"{t_pair.Key}.id: 번호 {t_card.id}가 '{t_prev}'와 중복 — 표에서 한쪽을 고칠 것");
                continue;
            }
            t_owner[t_card.id] = t_pair.Key;
        }

        if (!_header.ContainsKey("id")) return t_owner;

        // 표가 잡은 번호. 매칭이 번호 우선이므로 **번호를 적은 행은 언제나 그 번호의 주인**이다
        // (이름이 달라졌어도 같은 카드다 — 리네임은 표가 이긴다). 진짜 충돌은 두 행이 같은 번호를 적은 경우뿐.
        var t_claimedByRow = new Dictionary<int, string>();

        for (int r = 1; r < _rows.Count; r++)
        {
            string t_name = NormalizeCardAssetName(Cell(_rows[r], _header, "name").Trim());
            if (string.IsNullOrEmpty(t_name)) continue;

            int t_id = ParseInt(Cell(_rows[r], _header, "id"), 0);
            if (t_id <= 0) continue;

            if (t_claimedByRow.TryGetValue(t_id, out string t_first))
            {
                _warnings.Add($"{t_name}.id: 번호 {t_id}를 '{t_first}' 행이 이미 썼다 — 표에서 한쪽을 고칠 것");
                continue;
            }

            t_claimedByRow[t_id] = t_name;
            // 이 카드가 다른 번호를 갖고 있었다면 옛 번호는 대장에 남겨 둔다. 번호는 재사용하지 않는다.
            t_owner[t_id] = t_name;
        }
        return t_owner;
    }

    /// <summary>번호로 찾은 카드의 에셋 이름을 표에 맞춘다. **리네임은 guid를 보존**하므로
    /// 이 카드를 참조하는 에셋(시나리오·카드팩·AI덱·도감·튜토리얼)의 배선은 끊기지 않는다.
    /// 같은 이름이 이미 있으면 건드리지 않는다 — 두 에셋이 한 이름을 갖게 두는 것보다 표를 고치는 게 맞다.</summary>
    static void RenameCardAsset(CardData _card, string _newName, Dictionary<string, CardData> _existing,
                                List<string> _warnings)
    {
        if (_existing.TryGetValue(_newName, out CardData t_taken) && t_taken != _card)
        {
            _warnings.Add($"{_card.name}: 이름을 '{_newName}'으로 바꾸려 했지만 이미 그 이름의 카드가 있다 — 이름 유지");
            return;
        }

        string t_old  = _card.name;
        string t_path = AssetDatabase.GetAssetPath(_card);
        string t_fail = AssetDatabase.RenameAsset(t_path, _newName);
        if (!string.IsNullOrEmpty(t_fail))
        {
            _warnings.Add($"{t_old}: 이름 변경 실패({_newName}) — {t_fail}");
            return;
        }

        _existing.Remove(t_old);
        _existing[_newName] = _card;
        _warnings.Add($"{t_old} → {_newName}: 번호 {_card.id}로 찾아 이름을 표에 맞췄다(참조 유지)");
    }

    /// <summary>표의 번호를 카드에 반영한다. 빈 칸이면 기존 번호를 지키고, 그마저 없으면 남은 번호를 새로 부여한다.
    /// 번호는 세이브·표 참조가 매달리는 안정 키라 자동 부여는 "빈 카드 채우기"에만 쓴다.</summary>
    static void ApplyId(CardData _card, List<string> _row, Dictionary<string, int> _header,
                        string _name, Dictionary<int, string> _idOwner, List<string> _warnings)
    {
        int t_id = _header.ContainsKey("id") ? ParseInt(Cell(_row, _header, "id"), 0) : 0;

        if (t_id > 0)
        {
            // 예약대장이 이 이름에 내줬을 때만 반영한다(충돌은 ClaimIds가 이미 경고했다).
            if (_idOwner.TryGetValue(t_id, out string t_holder) && t_holder == _name)
            {
                _card.id = t_id;
                return;
            }
        }
        else if (t_id < 0)
        {
            _warnings.Add($"{_name}.id: 음수 번호는 무시한다");
        }

        if (_card.id > 0) return;

        int t_next = NextFreeId(_idOwner);
        _card.id          = t_next;
        _idOwner[t_next]  = _name;
    }

    // 대장에 없는 가장 작은 양수. 번호를 촘촘하게 유지해 표에서 눈으로 훑기 쉽게 한다.
    static int NextFreeId(Dictionary<int, string> _idOwner)
    {
        int t_id = 1;
        while (_idOwner.ContainsKey(t_id)) t_id++;
        return t_id;
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
        if (_header.ContainsKey("grade"))
        {
            string t_grade = Cell(_row, _header, "grade").Trim();
            // Enum.TryParse는 "3"이나 "99" 같은 숫자 문자열도 통과시킨다 — 정의에 없는 값까지 만들어 낸다.
            // 등급은 사람이 적는 저작 값이라 **이름으로만** 받고, 숫자·오타는 덮어쓰지 않고 경고로 남긴다
            // (조용히 기본 등급으로 떨어지면 표와 에셋이 갈린 걸 아무도 못 본다).
            if (t_grade.Length == 0)
                _card.grade = ECardGrade.Unknown;
            else if (!char.IsDigit(t_grade[0])
                     && Enum.TryParse(t_grade, true, out ECardGrade t_parsedGrade)
                     && Enum.IsDefined(typeof(ECardGrade), t_parsedGrade))
                _card.grade = t_parsedGrade;
            else
                _warnings.Add($"{_name}.grade: 알 수 없는 등급 '{t_grade}' — 기존 값 유지(등급 이름으로 적을 것)");
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

        ApplyHpCurve(_card, _row, _header, _name, _warnings);

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

        var t_card = ScriptableObject.CreateInstance<CardData>();
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
            string t_token = NormalizeSynergyAssetName(t_raw.Trim());
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

    static void ApplyHpCurve(CardData _card, List<string> _row, Dictionary<string, int> _header,
                             string _name, List<string> _warnings)
    {
        int t_curveColumnCount = 0;
        bool t_hasValue = false;
        var t_values = new int[HP_CURVE_MAX_LEVEL + 1];

        for (int t_level = HP_CURVE_MIN_LEVEL; t_level <= HP_CURVE_MAX_LEVEL; t_level++)
        {
            string t_column = "hp" + t_level;
            if (!_header.ContainsKey(t_column)) continue;

            t_curveColumnCount++;
            string t_text = Cell(_row, _header, t_column).Trim();
            if (t_text.Length == 0) continue;

            t_hasValue = true;
            if (!int.TryParse(t_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t_hp))
            {
                _warnings.Add($"{_name}.{t_column}: 정수가 아닌 값 '{t_text}' — 0으로 처리");
                continue;
            }

            if (t_hp < 0)
            {
                _warnings.Add($"{_name}.{t_column}: 음수 '{t_hp}' — 0으로 처리");
                t_hp = 0;
            }
            t_values[t_level] = t_hp;
        }

        if (t_curveColumnCount == 0) return;
        if (t_curveColumnCount != HP_CURVE_MAX_LEVEL - HP_CURVE_MIN_LEVEL + 1)
        {
            _warnings.Add($"{_name}: hp2~hp10 열이 일부만 존재 — 기존 성장 곡선 유지");
            return;
        }
        _card.hpGainByLevel = t_hasValue ? t_values : Array.Empty<int>();
    }

    static bool TryParseHpColumn(string _column, out int _level)
    {
        _level = -1;
        if (string.IsNullOrEmpty(_column) || !_column.StartsWith("hp", StringComparison.Ordinal)) return false;
        if (!int.TryParse(_column.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out _level)) return false;
        return _level >= HP_CURVE_MIN_LEVEL && _level <= HP_CURVE_MAX_LEVEL;
    }

    // ── CSV ────────────────────────────────────────────────────────────────

    static string Cell(List<string> _row, Dictionary<string, int> _header, string _column)
    {
        if (!_header.TryGetValue(_column, out int t_i)) return "";
        return t_i < _row.Count ? _row[t_i] : "";
    }

    // ── 에셋 헬퍼 (CardAuthoringWindow와 같은 규약) ─────────────────────────

    static CardRegistry LoadRegistry() => AssetDatabase.LoadAssetAtPath<CardRegistry>(REGISTRY_PATH);

    /// <summary>목록에 없으면 뒤에 넣는다(중복 등록 방지). 순서는 의미가 없다 — 와이어 ID는 카드 번호다.</summary>
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

    /// <summary>표의 행 순서는 카드 번호 순 — 표를 봤을 때 와이어 ID를 그대로 읽을 수 있게.
    /// 번호가 없는 카드(신규)는 이름 순으로 뒤에 붙어 눈에 띈다.</summary>
    static List<CardData> AllCards()
    {
        var t_numbered = new List<CardData>();
        var t_unnumbered = new List<CardData>();
        var t_seen = new HashSet<CardData>();

        CardRegistry t_reg = LoadRegistry();
        if (t_reg != null)
        {
            foreach (CardData c in t_reg.All)
                if (c != null && t_seen.Add(c)) (c.id > 0 ? t_numbered : t_unnumbered).Add(c);
        }

        foreach (string g in AssetDatabase.FindAssets("t:CardData"))
        {
            var c = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(g));
            if (c != null && t_seen.Add(c)) (c.id > 0 ? t_numbered : t_unnumbered).Add(c);
        }

        t_numbered.Sort((a, b) => a.id.CompareTo(b.id));
        t_unnumbered.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        t_numbered.AddRange(t_unnumbered);

        return t_numbered;
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
