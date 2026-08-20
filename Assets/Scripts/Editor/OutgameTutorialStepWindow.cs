using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 아웃게임 첫시작 튜토리얼 스텝 되감기 도구. Tools > Card Battle > 튜토리얼 스텝 되감기.
///
/// 저작된 시퀀스를 편·스텝째로 펼쳐 놓고 칸 하나를 고르면 <b>다음 플레이</b>에 그 스텝부터 시작한다.
/// 이 창은 예약만 남긴다(<see cref="OutgameTutorialRewind"/>) — 세이브를 미는 일은 매니저들이 슬롯을
/// 캐싱하기 전에 일어나야 해서 부트에서만 안전하다. 그래서 플레이를 켜지 않고 쓰는 도구다.
/// </summary>
public class OutgameTutorialStepWindow : EditorWindow
{
    OutgameTutorialData data;

    readonly HashSet<int> openChapters = new HashSet<int>();

    Vector2 scroll;

    [MenuItem("Tools/Card Battle/튜토리얼 스텝 되감기")]
    static void Open() => GetWindow<OutgameTutorialStepWindow>("튜토리얼 스텝").minSize = new Vector2(420, 480);

    void OnEnable()
    {
        if (this.data == null) this.data = FindSequenceAsset();
    }

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
            EditorGUILayout.HelpBox("저작된 챕터가 없다 — 위에 OutgameTutorialData 에셋을 지정할 것.", MessageType.Info);
            return;
        }

        this.scroll = EditorGUILayout.BeginScrollView(this.scroll);
        for (int t_c = 0; t_c < t_chapters; t_c++) DrawChapter(t_c);
        EditorGUILayout.EndScrollView();
    }

    void DrawHeader()
    {
        this.data = (OutgameTutorialData)EditorGUILayout.ObjectField("시퀀스 SO", this.data, typeof(OutgameTutorialData), false);

        EditorGUILayout.LabelField("현재 좌표", CurrentCoordLabel(), EditorStyles.boldLabel);

        DrawScheduleBanner();

        EditorGUILayout.HelpBox(
            "칸을 누르면 다음 플레이에 적용된다: 아웃게임 세이브를 첫실행으로 밀고(소유·강화/진화·재화·덱·랭크·도감보상·트리거 낙인)"
          + " 그 칸 직전까지의 지급만 재생한다(덱 지급 + 팩 풀 전량 소유). 되돌릴 수 없다 — 진행 중인 세이브는 사라진다.",
            MessageType.Warning);

        EditorGUILayout.Space(4);
    }

    void DrawScheduleBanner()
    {
        if (!OutgameTutorialRewind.TryGetScheduled(out int t_chapter, out int t_step, out bool t_wipePending))
        {
            EditorGUILayout.LabelField("예약", "없음", EditorStyles.miniLabel);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("예약", $"{t_chapter}-{t_step}  {ActionNameAt(t_chapter, t_step)}", EditorStyles.boldLabel);
        if (GUILayout.Button("예약 취소", GUILayout.Width(72))) OutgameTutorialRewind.Cancel();
        EditorGUILayout.EndHorizontal();

        // 밀기만 돌고 지급 재생 전에 부트가 끊긴 상태(BootInstaller 없는 씬에서 Play 등).
        // 이 자리에 드러내지 않으면 취소할 방법이 없어, 한참 진행한 세이브 위에 다음 부트가 지급을 덧씌운다.
        if (!t_wipePending)
            EditorGUILayout.HelpBox("세이브 밀기는 이미 끝났고 지급 재생만 남았다 — 다음 부트가 이 좌표까지의 지급을 얹는다. "
                                  + "그 사이에 진행했다면 [예약 취소]로 걷어라.", MessageType.Warning);

        // 예약은 부트에서만 소비된다 — 지금 도는 플레이는 그대로다.
        if (Application.isPlaying)
            EditorGUILayout.HelpBox("이미 플레이 중이다 — 이 예약은 지금 세션이 아니라 다음 플레이에 적용된다.", MessageType.Info);
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
        bool t_isHere      = IsCurrent(_chapter, _step);
        bool t_isScheduled = IsScheduled(_chapter, _step);

        using (new EditorGUILayout.VerticalScope(t_isHere || t_isScheduled ? EditorStyles.helpBox : GUIStyle.none))
        {
            bool t_found  = TryGetStep(_chapter, _step, out var t_def);
            string t_name = t_found ? t_def.Action.ToString() : "(빈 칸)";

            // ▶ = 지금 서 있는 칸(플레이 중), ◆ = 다음 플레이에 시작할 칸
            string t_mark = t_isScheduled ? "◆" : t_isHere ? "▶" : "  ";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{t_mark} {_chapter}-{_step}  {t_name}",
                                       t_isHere || t_isScheduled ? EditorStyles.boldLabel : EditorStyles.label);

            if (GUILayout.Button(t_isScheduled ? "예약됨" : "여기부터", GUILayout.Width(64)))
                OutgameTutorialRewind.Schedule(_chapter, _step);

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
        if (_def.OnFailure == EOutgameTutorialFailure.Halt) Append(t_sb, "실패 시 정지");

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

    static bool IsScheduled(int _chapter, int _step)
        => OutgameTutorialRewind.TryGetScheduled(out int t_c, out int t_s, out _) && t_c == _chapter && t_s == _step;

    string CurrentCoordLabel()
    {
        if (!Application.isPlaying)              return "— (플레이 중 아님)";
        if (OutgameTutorialProgress.IsCompleted) return "졸업 완료 (되감으면 낙인도 풀린다)";

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_step    = OutgameTutorialProgress.StepIndex;

        return $"{t_chapter}-{t_step}  {ActionNameAt(t_chapter, t_step)}";
    }

    string ActionNameAt(int _chapter, int _step)
        => TryGetStep(_chapter, _step, out var t_def) ? t_def.Action.ToString() : "(빈 칸)";

    // ── 목록 출처는 언제나 창이 잡은 SO 하나다(플레이 여부와 무관 — 예약은 부트가 이 시퀀스로 재생한다) ──

    int ChapterCount() => this.data != null && this.data.chapters != null ? this.data.chapters.Count : 0;

    int StepCountOf(int _chapter) => TryGetChapter(_chapter, out var t_chapter) ? t_chapter.StepCount : 0;

    string LabelOf(int _chapter)
        => TryGetChapter(_chapter, out var t_chapter) && !string.IsNullOrEmpty(t_chapter.Label) ? t_chapter.Label : string.Empty;

    bool TryGetStep(int _chapter, int _step, out TutorialStepDef _def)
    {
        _def = null;

        return TryGetChapter(_chapter, out var t_chapter) && t_chapter.TryGetStep(_step, out _def);
    }

    bool TryGetChapter(int _chapter, out OutgameTutorialChapter _result)
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
