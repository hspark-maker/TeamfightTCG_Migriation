using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 아웃게임 첫시작 튜토리얼 스텝 되감기 도구. Tools > Card Battle > 튜토리얼 스텝 되감기.
///
/// 저작된 시퀀스를 편·스텝째로 펼쳐 놓고 원하는 칸을 눌러 그 스텝부터 다시 검증한다.
/// 되감기는 플레이 중에만 가능하다 — 진행도가 세이브(런타임 로드)에 있고, 되감은 좌표는
/// 브리지가 <see cref="OutgameTutorialRunner.OnRewound"/>로 받아 그 씬에서 곧바로 다시 세운다.
///
/// 목록의 출처가 모드마다 다르다: 플레이 중에는 <b>실제 주입된</b> 시퀀스(러너)를 읽고,
/// 정지 상태에서는 창이 잡은 SO 에셋을 읽는다. 씬 브리지에 다른 에셋이 배선돼 있어도 플레이 중 표시는 어긋나지 않는다.
/// </summary>
public class OutgameTutorialStepWindow : EditorWindow
{
    const string PREF_PREPARE = "OutgameTutorialStep.Prepare";

    OutgameTutorialData data;

    // 되감을 때 앞선 스텝이 지급했어야 할 덱·카드를 같이 채울지.
    bool prepare = true;

    readonly HashSet<int> openChapters = new HashSet<int>();

    Vector2 scroll;

    [MenuItem("Tools/Card Battle/튜토리얼 스텝 되감기")]
    static void Open() => GetWindow<OutgameTutorialStepWindow>("튜토리얼 스텝").minSize = new Vector2(420, 480);

    void OnEnable()
    {
        this.prepare = EditorPrefs.GetBool(PREF_PREPARE, true);
        if (this.data == null) this.data = FindSequenceAsset();
    }

    void OnDisable() => EditorPrefs.SetBool(PREF_PREPARE, this.prepare);

    // 플레이 중 현재 좌표가 움직이는 것을 따라 그린다(OnGUI만으로는 창이 멈춰 보인다).
    void OnInspectorUpdate()
    {
        if (Application.isPlaying) Repaint();
    }

    void OnGUI()
    {
        DrawHeader();

        int t_chapters = ChapterCount();
        if (t_chapters == 0)
        {
            EditorGUILayout.HelpBox(Application.isPlaying
                ? "러너에 시퀀스가 주입되지 않았다 — 브리지가 있는 씬(로비)에서 플레이할 것."
                : "저작된 챕터가 없다 — 위에 OutgameTutorialData 에셋을 지정할 것.", MessageType.Info);
            return;
        }

        this.scroll = EditorGUILayout.BeginScrollView(this.scroll);
        for (int t_c = 0; t_c < t_chapters; t_c++) DrawChapter(t_c);
        EditorGUILayout.EndScrollView();
    }

    void DrawHeader()
    {
        using (new EditorGUI.DisabledScope(Application.isPlaying))
            this.data = (OutgameTutorialData)EditorGUILayout.ObjectField("시퀀스 SO", this.data, typeof(OutgameTutorialData), false);

        EditorGUILayout.LabelField("현재 좌표", CurrentCoordLabel(), EditorStyles.boldLabel);

        this.prepare = EditorGUILayout.ToggleLeft(
            new GUIContent("되감을 때 앞선 지급도 채우기",
                           "목표 칸 직전까지의 덱 지급(DeckGrant)을 재생하고, 팩을 쓰는 스텝의 카드 풀을 소유로 준다.\n"
                         + "끄면 좌표만 움직인다 — 덱이 없는 세이브로 전투 스텝에 서면 그 자리에서 막힌다."),
            this.prepare);

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("되감기는 플레이 중에만 가능하다(진행도가 런타임 세이브에 있다). 지금은 저작 내용만 훑는다.", MessageType.Info);

        EditorGUILayout.Space(4);
    }

    void DrawChapter(int _chapter)
    {
        int t_steps = StepCountOf(_chapter);

        string t_label = LabelOf(_chapter);
        string t_title = string.IsNullOrEmpty(t_label)
            ? $"{_chapter + 1}편  ({t_steps}스텝)"
            : $"{_chapter + 1}편 — {t_label}  ({t_steps}스텝)";

        bool t_open = this.openChapters.Contains(_chapter);
        if (EditorGUILayout.Foldout(t_open, t_title, true) != t_open)
        {
            if (t_open) this.openChapters.Remove(_chapter);
            else        this.openChapters.Add(_chapter);
        }

        if (!this.openChapters.Contains(_chapter)) return;

        if (t_steps == 0)
        {
            EditorGUILayout.HelpBox("이 편에 저작된 스텝이 없다 — 진행이 여기서 멈춘다.", MessageType.Warning);
            return;
        }

        using (new EditorGUI.IndentLevelScope())
            for (int t_s = 0; t_s < t_steps; t_s++) DrawStepRow(_chapter, t_s);

        EditorGUILayout.Space(4);
    }

    void DrawStepRow(int _chapter, int _step)
    {
        bool t_isHere = IsCurrent(_chapter, _step);

        using (new EditorGUILayout.VerticalScope(t_isHere ? EditorStyles.helpBox : GUIStyle.none))
        {
            bool t_found  = TryGetStep(_chapter, _step, out var t_def);
            string t_name = t_found ? t_def.Action.ToString() : "(빈 칸)";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{(t_isHere ? "▶" : " ")} {_chapter}-{_step}  {t_name}",
                                       t_isHere ? EditorStyles.boldLabel : EditorStyles.label);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button("여기부터", GUILayout.Width(64)))
                    OutgameDebugActions.RestartTutorialFromStep(_chapter, _step, this.prepare);

            EditorGUILayout.EndHorizontal();

            if (!t_found) return;

            if (!string.IsNullOrEmpty(t_def.GuideMessage))
                EditorGUILayout.LabelField(OneLine(t_def.GuideMessage), EditorStyles.miniLabel);

            string t_detail = DetailOf(t_def);
            if (!string.IsNullOrEmpty(t_detail)) EditorGUILayout.LabelField(t_detail, EditorStyles.miniLabel);
        }
    }

    // 이 스텝이 무엇에 걸리는가 — 저작 실수(앵커 없는 클릭 대기 등)를 목록에서 바로 보라고 한 줄로 편다.
    static string DetailOf(TutorialStepDef _def)
    {
        var t_sb = new StringBuilder();

        if (_def.Anchor != EOutgameTutorialAnchor.None) Append(t_sb, $"앵커 {_def.Anchor}");
        if (_def.Pack != null)                          Append(t_sb, $"팩 {_def.Pack.PackId}");
        if (_def.Scenario != null)                      Append(t_sb, $"시나리오 {_def.Scenario.name}");

        if (_def.UnlocksAll)                       Append(t_sb, "해금 전체");
        else if (_def.Unlocks != null && _def.Unlocks.Count > 0) Append(t_sb, $"해금 {string.Join(",", _def.Unlocks)}");

        if (_def.Locks != null && _def.Locks.Count > 0) Append(t_sb, $"잠금 {string.Join(",", _def.Locks)}");
        if (!_def.UseDim)                               Append(t_sb, "딤 없음");
        if (_def.LeavesScene)                           Append(t_sb, "씬 이탈");

        return t_sb.ToString();
    }

    static void Append(StringBuilder _sb, string _text)
    {
        if (_sb.Length > 0) _sb.Append("  ·  ");
        _sb.Append(_text);
    }

    static string OneLine(string _text) => _text.Replace("\r", " ").Replace("\n", " ");

    static bool IsCurrent(int _chapter, int _step)
        => Application.isPlaying
        && !OutgameTutorialProgress.IsCompleted
        && OutgameTutorialProgress.ChapterIndex == _chapter
        && OutgameTutorialProgress.StepIndex == _step;

    static string CurrentCoordLabel()
    {
        if (!Application.isPlaying)             return "— (플레이 중 아님)";
        if (OutgameTutorialProgress.IsCompleted) return "졸업 완료 (되감으면 낙인도 풀린다)";

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_step    = OutgameTutorialProgress.StepIndex;
        string t_name = OutgameTutorialRunner.TryGetStepAt(t_chapter, t_step, out var t_def) ? t_def.Action.ToString() : "(빈 칸)";

        return $"{t_chapter}-{t_step}  {t_name}";
    }

    // ── 목록 출처: 플레이 중이면 실제 주입된 러너, 아니면 창이 잡은 에셋 ──

    int ChapterCount()
    {
        if (Application.isPlaying) return OutgameTutorialRunner.ChapterCount;

        return this.data != null && this.data.chapters != null ? this.data.chapters.Count : 0;
    }

    int StepCountOf(int _chapter)
    {
        if (Application.isPlaying) return OutgameTutorialRunner.StepCountOf(_chapter);

        return TryGetAssetChapter(_chapter, out var t_chapter) ? t_chapter.StepCount : 0;
    }

    string LabelOf(int _chapter)
    {
        if (Application.isPlaying) return OutgameTutorialRunner.ChapterLabelOf(_chapter);

        return TryGetAssetChapter(_chapter, out var t_chapter) && !string.IsNullOrEmpty(t_chapter.Label)
            ? t_chapter.Label
            : string.Empty;
    }

    bool TryGetStep(int _chapter, int _step, out TutorialStepDef _def)
    {
        if (Application.isPlaying) return OutgameTutorialRunner.TryGetStepAt(_chapter, _step, out _def);

        _def = null;
        return TryGetAssetChapter(_chapter, out var t_chapter) && t_chapter.TryGetStep(_step, out _def);
    }

    bool TryGetAssetChapter(int _chapter, out OutgameTutorialChapter _result)
    {
        _result = null;
        if (this.data == null || this.data.chapters == null) return false;
        if (_chapter < 0 || _chapter >= this.data.chapters.Count) return false;

        _result = this.data.chapters[_chapter];
        return _result != null;
    }

    static OutgameTutorialData FindSequenceAsset()
    {
        string[] t_guids = AssetDatabase.FindAssets("t:OutgameTutorialData");
        if (t_guids.Length == 0) return null;

        return AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(AssetDatabase.GUIDToAssetPath(t_guids[0]));
    }
}
