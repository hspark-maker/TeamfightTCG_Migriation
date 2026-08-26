using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 카드 추가 도구. Tools > Card Battle > 카드 추가.
///
/// 카드 하나 늘리는 데 필요한 건 두 가지뿐이다:
///   1) CardData 에셋         — 없으면 아무 데도 안 나옴
///   2) CardRegistry.allCards — 카드 전체 목록의 단일 진실원. 빠지면 컬렉션에도 안 뜨고 멀티도 깨진다
/// (예전엔 DeckBuilderUI/MainMenuInitializer가 카드 목록 사본을 따로 들고 있어서 3~4군데를
///  갱신해야 했다. 지금은 둘 다 CardRegistry를 참조하므로 여기만 등록하면 끝.)
///
/// **와이어 ID는 CardData.id다** — 배열 순서가 아니다. 그래서 목록 재정렬·빈 칸 제거는 안전하고,
/// 이 도구는 새 카드에 남는 번호를 부여한 뒤 목록에 넣는다. 바꾸면 안 되는 건 부여된 번호 쪽이다.
/// </summary>
public class CardAuthoringWindow : EditorWindow
{
    const string DEFAULT_CARD_ROOT = "Assets/SO/Cards";
    const string PREF_CARD_ROOT    = "CardAuthoring.CardRoot";
    const string REGISTRY_PATH     = "Assets/SO/CardRegistry.asset";

    /// <summary>카드 에셋을 만들 폴더. 기본 Assets/SO/Cards, 창에서 바꾸면 EditorPrefs에 남는다.</summary>
    string cardRoot = DEFAULT_CARD_ROOT;

    // ── 새 카드 입력 ──
    string      newName = "";
    string      newDisplayName = "";
    int         newMaxHp = 5;
    int         newBonusHp;
    CardKeyword newKeywords = CardKeyword.None;
    readonly List<SynergyData> newSynergies = new List<SynergyData>();
    Sprite baseArt;

    Vector2 scroll;
    int     tab;

    [MenuItem("Tools/Card Battle/카드 추가")]
    static void Open() => GetWindow<CardAuthoringWindow>("카드 추가").minSize = new Vector2(420, 560);

    void OnEnable()  => this.cardRoot = EditorPrefs.GetString(PREF_CARD_ROOT, DEFAULT_CARD_ROOT);
    void OnDisable() => EditorPrefs.SetString(PREF_CARD_ROOT, this.cardRoot);

    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 에셋을 만들 수 없다. 플레이를 멈추고 다시 열 것.", MessageType.Warning);
            return;
        }

        this.tab = GUILayout.Toolbar(this.tab, new[] { "새 카드", "등록 상태 점검" });
        EditorGUILayout.Space(6);
        this.scroll = EditorGUILayout.BeginScrollView(this.scroll);
        if (this.tab == 0) DrawCreateTab();
        else               DrawAuditTab();
        EditorGUILayout.EndScrollView();
    }

    // ── 새 카드 ────────────────────────────────────────────────────────────

    void DrawCreateTab()
    {
        EditorGUILayout.LabelField("생성 위치", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        this.cardRoot = EditorGUILayout.TextField(this.cardRoot);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string t_abs = EditorUtility.OpenFolderPanel("카드 폴더 선택", this.cardRoot, "");
            if (!string.IsNullOrEmpty(t_abs))
            {
                string t_rel = ToProjectRelative(t_abs);
                if (t_rel != null) this.cardRoot = t_rel;
                else EditorUtility.DisplayDialog("경로 오류", "프로젝트의 Assets 폴더 안이어야 한다.", "확인");
            }
            GUIUtility.ExitGUI();
        }
        if (GUILayout.Button("기본", GUILayout.Width(46)))
        {
            this.cardRoot = DEFAULT_CARD_ROOT;
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField($"→ {this.cardRoot}/<이름>/<이름>.asset", EditorStyles.miniLabel);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("기본", EditorStyles.boldLabel);
        this.newName        = EditorGUILayout.TextField("에셋/폴더 이름", this.newName);
        this.newDisplayName = EditorGUILayout.TextField("표시 이름", this.newDisplayName);
        this.newMaxHp       = EditorGUILayout.IntField("최대 체력", this.newMaxHp);
        this.newBonusHp     = EditorGUILayout.IntField("추가 생명력", this.newBonusHp);
        this.newKeywords    = (CardKeyword)EditorGUILayout.EnumFlagsField("키워드", this.newKeywords);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("시너지", EditorStyles.boldLabel);
        for (int i = 0; i < this.newSynergies.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            this.newSynergies[i] = (SynergyData)EditorGUILayout.ObjectField(
                this.newSynergies[i], typeof(SynergyData), false);
            if (GUILayout.Button("−", GUILayout.Width(24))) { this.newSynergies.RemoveAt(i); i--; }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("시너지 추가", GUILayout.Width(120))) this.newSynergies.Add(null);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("이미지 (나중에 채워도 됨)", EditorStyles.boldLabel);
        this.baseArt = (Sprite)EditorGUILayout.ObjectField("arts[0] (미진화)", this.baseArt, typeof(Sprite), false);

        EditorGUILayout.Space(10);
        string t_err = ValidateNew();
        if (t_err != null)
        {
            EditorGUILayout.HelpBox(t_err, MessageType.Error);
            GUI.enabled = false;
        }
        if (GUILayout.Button("카드 만들기 + 등록", GUILayout.Height(32))) CreateCard();
        GUI.enabled = true;

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            "멀티 와이어 ID는 카드 번호(CardData.id)다. 이 도구가 남는 번호를 자동 부여한다.\n" +
            "목록 순서는 의미가 없다 — 부여된 번호를 바꾸면 양 클라 해석이 어긋나 매치가 깨진다.",
            MessageType.Info);
    }

    string ValidateNew()
    {
        if (string.IsNullOrWhiteSpace(this.cardRoot)) return "생성 위치를 입력할 것.";
        if (!this.cardRoot.Replace('\\', '/').StartsWith("Assets/")) return "생성 위치는 Assets/ 아래여야 한다.";
        if (string.IsNullOrWhiteSpace(this.newName)) return "에셋 이름을 입력할 것.";
        if (this.newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "파일명에 못 쓰는 문자가 있다.";
        if (AssetDatabase.IsValidFolder($"{this.cardRoot}/{this.newName}")) return $"'{this.newName}' 폴더가 이미 있다.";
        if (this.newMaxHp <= 0) return "최대 체력은 1 이상이어야 한다.";
        if (LoadRegistry() == null) return $"CardRegistry를 못 찾음: {REGISTRY_PATH}";
        return null;
    }

    void CreateCard()
    {
        // 지정한 폴더가 없으면 중간 단계까지 만들어준다(경로를 직접 타이핑한 경우).
        if (!EnsureFolder(this.cardRoot))
        {
            EditorUtility.DisplayDialog("실패", $"폴더를 만들 수 없다: {this.cardRoot}", "확인");
            return;
        }
        AssetDatabase.CreateFolder(this.cardRoot, this.newName);
        string t_dir  = $"{this.cardRoot}/{this.newName}";
        string t_path = $"{t_dir}/{this.newName}.asset";

        var t_card = CreateInstance<CardData>();
        t_card.displayName = string.IsNullOrWhiteSpace(this.newDisplayName) ? this.newName : this.newDisplayName;
        t_card.maxHp       = this.newMaxHp;
        t_card.bonusHp     = this.newBonusHp;
        t_card.keywords    = this.newKeywords;
        t_card.arts    = new CardArtSet[CardData.MaxEvolutionStage + 1];
        t_card.arts[0] = new CardArtSet { battleImage = this.baseArt };

        var t_syn = new List<SynergyData>();
        foreach (var s in this.newSynergies)
            if (s != null && !t_syn.Contains(s)) t_syn.Add(s);   // 중복 나열은 소비측에서 1회 취급 — 여기서 미리 정리
        t_card.synergies = t_syn.ToArray();

        AssetDatabase.CreateAsset(t_card, t_path);
        AssetDatabase.SaveAssets();

        int t_id = RegisterCard(t_card);

        AssetDatabase.Refresh();
        Selection.activeObject = t_card;
        EditorGUIUtility.PingObject(t_card);

        Debug.Log($"[카드 추가] {t_path}\n  카드 번호={t_id}");
        EditorUtility.DisplayDialog("카드 추가 완료",
            $"{this.newName}\n\n카드 번호(와이어 ID): {t_id}\n" +
            "덱 편성 컬렉션에도 자동으로 뜬다(CardRegistry 참조).\n\n" +
            "남은 것: 이미지 / passive / attackEffect / 보이스 — 인스펙터에서 채울 것.", "확인");

        this.newName = ""; this.newDisplayName = "";
        this.newSynergies.Clear();
        this.baseArt = null;
    }

    // ── 등록 상태 점검 ─────────────────────────────────────────────────────

    void DrawAuditTab()
    {
        CardRegistry t_reg = LoadRegistry();
        if (t_reg == null) { EditorGUILayout.HelpBox($"CardRegistry 없음: {REGISTRY_PATH}", MessageType.Error); return; }

        SerializedObject t_so = new SerializedObject(t_reg);
        SerializedProperty t_arr = t_so.FindProperty("allCards");

        var t_registered = new HashSet<CardData>();
        int t_nullSlots = 0;
        for (int i = 0; i < t_arr.arraySize; i++)
        {
            var c = t_arr.GetArrayElementAtIndex(i).objectReferenceValue as CardData;
            if (c == null) t_nullSlots++;
            else t_registered.Add(c);
        }

        List<CardData> t_all = AllCardAssets();
        EditorGUILayout.LabelField($"카드 에셋 {t_all.Count}개 / CardRegistry {t_arr.arraySize}칸", EditorStyles.boldLabel);

        if (t_nullSlots > 0)
            EditorGUILayout.HelpBox($"CardRegistry에 빈 칸 {t_nullSlots}개. 지워도 된다 — " +
                                    "와이어 ID는 카드 번호라 목록 순서에 매달리지 않는다.", MessageType.Info);

        var t_missing = new List<CardData>();
        foreach (var c in t_all) if (!t_registered.Contains(c)) t_missing.Add(c);

        EditorGUILayout.Space(6);
        if (t_missing.Count == 0)
        {
            EditorGUILayout.HelpBox("모든 카드가 CardRegistry에 등록돼 있다.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox($"CardRegistry 미등록 {t_missing.Count}개 — 멀티에서 이 카드가 나오면 깨진다.", MessageType.Error);
            foreach (var c in t_missing)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(c, typeof(CardData), false);
                if (GUILayout.Button("등록", GUILayout.Width(60))) { RegisterCard(c); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button($"미등록 {t_missing.Count}개 전부 등록"))
            {
                foreach (var c in t_missing) RegisterCard(c);
                GUIUtility.ExitGUI();
            }
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("현재 ID 배정", EditorStyles.boldLabel);
        for (int i = 0; i < t_arr.arraySize; i++)
        {
            var c = t_arr.GetArrayElementAtIndex(i).objectReferenceValue as CardData;
            EditorGUILayout.LabelField($"  {i,3} : {(c != null ? c.name : "<빈 칸>")}");
        }
    }

    // ── 등록 헬퍼 ──────────────────────────────────────────────────────────

    static CardRegistry LoadRegistry() => AssetDatabase.LoadAssetAtPath<CardRegistry>(REGISTRY_PATH);

    /// <summary>번호를 부여하고(없을 때만) 목록에 넣는다. 반환값은 카드 번호 = 와이어 ID.</summary>
    static int RegisterCard(CardData _card)
    {
        if (_card == null) return -1;

        EnsureId(_card);
        AppendToRegistry(_card);
        AssetDatabase.SaveAssets();
        return _card.id;
    }

    /// <summary>비어 있는 카드에만 남는 최소 번호를 부여한다. 이미 번호가 있으면 손대지 않는다 —
    /// 번호 변경은 세이브와 와이어를 동시에 깨는 유일한 조작이다.</summary>
    static void EnsureId(CardData _card)
    {
        if (_card.id > 0) return;

        var t_used = new HashSet<int>();
        foreach (string g in AssetDatabase.FindAssets("t:CardData"))
        {
            var c = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(g));
            if (c != null && c != _card && c.id > 0) t_used.Add(c.id);
        }

        int t_id = 1;
        while (t_used.Contains(t_id)) t_id++;

        _card.id = t_id;
        EditorUtility.SetDirty(_card);
    }

    /// <summary>목록에 없으면 뒤에 넣는다. 순서는 의미가 없으므로 위치는 아무래도 좋다.</summary>
    static void AppendToRegistry(CardData _card)
    {
        CardRegistry t_reg = LoadRegistry();
        if (t_reg == null || _card == null) return;

        var t_so  = new SerializedObject(t_reg);
        var t_arr = t_so.FindProperty("allCards");

        for (int i = 0; i < t_arr.arraySize; i++)
            if (t_arr.GetArrayElementAtIndex(i).objectReferenceValue == _card) return;   // 이미 등록됨

        int t_slot = t_arr.arraySize;
        t_arr.InsertArrayElementAtIndex(t_slot);
        t_arr.GetArrayElementAtIndex(t_slot).objectReferenceValue = _card;
        t_so.ApplyModifiedProperties();
        EditorUtility.SetDirty(t_reg);
    }

    /// <summary>절대 경로를 프로젝트 상대(Assets/...)로. 프로젝트 밖이면 null.</summary>
    static string ToProjectRelative(string _absolute)
    {
        string t_abs  = _absolute.Replace('\\', '/');
        string t_root = Application.dataPath.Replace('\\', '/');   // .../Assets
        if (t_abs == t_root) return "Assets";
        if (!t_abs.StartsWith(t_root + "/")) return null;
        return "Assets" + t_abs.Substring(t_root.Length);
    }

    /// <summary>Assets/ 아래 폴더를 없으면 단계별로 만든다.</summary>
    static bool EnsureFolder(string _path)
    {
        string t_path = _path.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(t_path)) return true;
        if (!t_path.StartsWith("Assets")) return false;

        string[] t_parts = t_path.Split('/');
        string   t_cur   = t_parts[0];                 // "Assets"
        for (int i = 1; i < t_parts.Length; i++)
        {
            string t_next = $"{t_cur}/{t_parts[i]}";
            if (!AssetDatabase.IsValidFolder(t_next))
                AssetDatabase.CreateFolder(t_cur, t_parts[i]);
            t_cur = t_next;
        }
        return AssetDatabase.IsValidFolder(t_path);
    }

    static List<CardData> AllCardAssets()
    {
        var t_list = new List<CardData>();
        foreach (string g in AssetDatabase.FindAssets("t:CardData"))
        {
            var c = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(g));
            if (c != null) t_list.Add(c);
        }
        t_list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return t_list;
    }
}
