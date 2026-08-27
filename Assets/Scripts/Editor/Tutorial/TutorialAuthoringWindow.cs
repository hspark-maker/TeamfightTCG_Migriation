using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 저작 도구. Tools > Card Battle > 튜토리얼 저작 도구.
///
/// <b>왼쪽은 목록, 오른쪽은 고른 것 하나.</b> 스텝 하나가 알아야 할 것은 넷인데(값 · 그 시점의 해금 상태 ·
/// 저작 오류 · 되감기), 그것을 목록에 다 펴면 33스텝이 곧 160줄짜리 벽이 된다. 그래서 목록은 한 줄로 줄이고
/// 나머지는 전부 오른쪽으로 보냈다.
///
/// <b>"고른 것 하나" = 스텝이 아니라 비트다</b>(<see cref="TutorialBeatGrouping"/>). 저작 행의 3분의 1은
/// 유저에게 아무것도 그리지 않는 잡일(지급 · 오버레이 닫기 · 연출 종료 대기)인데, 그것들이 사건과 같은 무게로
/// 목록에 서면 목록이 "유저가 겪는 순서"가 아니라 "실행기가 처리하는 순서"로 읽힌다. 그래서 잡일 행은
/// 자기가 딸린 사건 줄로 접어 넣고, 오른쪽에서 그 사건과 함께 편집한다.
///
/// 접기는 <b>파생일 뿐이다</b> — 저작 SO의 행도, 세이브가 붙잡는 stepId도, 실행 순서도 접기 전과 같다.
/// 되감기와 구조 편집이 여전히 행 단위로 남아 있는 이유다.
///
/// 상태와 검증을 한 창에 둔 이유: 저작 실수는 런타임이 대부분 조용히 삼키고(기본 onFailure=Skip), 진행이 막히면
/// fail-open이 전 기능을 열어 증상까지 위장한다. 그래서 "돌려보고 안다"가 성립하지 않는다 — 플레이 전에 보여야 한다.
///
/// 값 편집은 언제나 열려 있다(Undo가 받는다). <b>구조 편집</b>만 토글로 가린다 —
/// 그쪽이 stepId 계약과 되감기 예약을 건드리는 쪽이다(<see cref="TutorialSequenceEditOps"/>).
/// </summary>
public class TutorialAuthoringWindow : EditorWindow
{
    enum EMode { Onboarding, Triggered }

    const float MinListWidth   = 200f;
    const float MinDetailWidth = 280f;
    const float SplitterWidth  = 5f;
    const float DetailMaxWidth = 560f;

    // 도메인 리로드를 건너뛰고 살아남아야 한다 — 되살아날 때마다 FindAsset이 첫 GUID로 되돌리면
    // 같은 타입 SO가 둘 이상일 때 초기화가 쓰는 것과 다른 에셋을 검증하게 된다.
    [SerializeField] OutgameTutorialData   data;
    [SerializeField] TriggeredTutorialData triggeredData;

    [SerializeField] EMode mode;
    [SerializeField] bool  structureEdit;
    [SerializeField] bool  issuesOnly;
    [SerializeField] bool  showSettings;
    [SerializeField] float listWidth = 300f;

    int dataAssetCount;
    int triggeredAssetCount;

    int selectedOuter = -1;
    int selectedStep  = -1;

    Vector2 listScroll;
    Vector2 detailScroll;

    bool draggingSplitter;

    // 레이아웃 도중에 리스트를 바꾸면 그 패스가 무너진다(GUI 그룹이 짝을 잃는다).
    // 버튼은 할 일만 적어 두고, 한 패스가 끝난 뒤에 실행한다.
    System.Action pendingEdit;

    SerializedObject serialized;
    SerializedObject triggeredSerialized;

    // 캐시 — 매 OnGUI마다 33스텝을 다시 훑을 이유가 없다. 무효화는 아래 Invalidate 계열이 맡는다.
    TutorialSequenceState state;

    // 편마다 접힌 비트 목록(파생 — 저작 데이터는 평평한 그대로다)
    Dictionary<int, List<TutorialBeat>> beats;

    List<TutorialIssue> issues;
    List<TutorialIssue> triggeredIssues;

    Dictionary<long, List<TutorialIssue>> issueByStep;
    Dictionary<long, List<TutorialIssue>> triggeredIssueByStep;

    int errorCount;
    int warningCount;
    int infoCount;

    [MenuItem("Tools/Card Battle/튜토리얼 저작 도구")]
    static void Open() => GetWindow<TutorialAuthoringWindow>("튜토리얼 저작").minSize = new Vector2(680, 480);

    // ── 수명주기 ────────────────────────────────────────────────────────────

    void OnEnable()
    {
        if (this.data == null)          this.data          = FindAsset<OutgameTutorialData>();
        if (this.triggeredData == null) this.triggeredData = FindAsset<TriggeredTutorialData>();

        CountAssets();
        Invalidate();

        EditorApplication.projectChanged += Invalidate;
        Undo.undoRedoPerformed           += Invalidate;

        // 인스펙터에서 값을 고치는 것도 흔한 동선이다. 그 편집은 projectChanged를 태우지 않으므로
        // 이 훅이 없으면 창을 도킹해 둔 채로 옛 판정을 보게 된다.
        Undo.postprocessModifications += OnModified;
    }

    void OnDisable()
    {
        EditorApplication.projectChanged -= Invalidate;
        Undo.undoRedoPerformed           -= Invalidate;
        Undo.postprocessModifications    -= OnModified;
    }

    void OnFocus() => Invalidate();

    // 플레이 중 현재 좌표가 움직이는 것을 따라 그린다(OnGUI만으로는 창이 멈춰 보인다).
    void OnInspectorUpdate()
    {
        if (Application.isPlaying) Repaint();
    }

    UndoPropertyModification[] OnModified(UndoPropertyModification[] _modifications)
    {
        Invalidate();
        return _modifications;
    }

    void CountAssets()
    {
        this.dataAssetCount      = AssetDatabase.FindAssets("t:" + nameof(OutgameTutorialData)).Length;
        this.triggeredAssetCount = AssetDatabase.FindAssets("t:" + nameof(TriggeredTutorialData)).Length;
    }

    void Invalidate()
    {
        this.state                = null;
        this.beats                = null;
        this.issues               = null;
        this.triggeredIssues      = null;
        this.issueByStep          = null;
        this.triggeredIssueByStep = null;

        // 구조가 바뀌면 잡고 있던 SerializedProperty가 옛 배열을 가리킨다 — 통째로 다시 뜬다.
        this.serialized          = null;
        this.triggeredSerialized = null;

        ClampSelection();
        Repaint();
    }

    // 편집으로 좌표가 줄어들면 선택이 허공을 가리킨다.
    void ClampSelection()
    {
        if (this.selectedOuter >= OuterCount())                     { this.selectedOuter = -1; this.selectedStep = -1; }
        else if (this.selectedOuter >= 0
              && this.selectedStep >= StepCountOf(this.selectedOuter)) this.selectedStep = -1;

        if (this.selectedOuter < 0) SelectFirstStep();
    }

    // 아무것도 안 고른 채로 열리면 오른쪽이 빈 화면이다 — 첫 스텝을 미리 세워 둔다.
    void SelectFirstStep()
    {
        int t_outers = OuterCount();

        for (int t_o = 0; t_o < t_outers; t_o++)
        {
            if (StepCountOf(t_o) == 0) continue;

            this.selectedOuter = t_o;
            this.selectedStep  = 0;
            return;
        }
    }

    void EnsureSerialized()
    {
        if (this.serialized == null && this.data != null)                   this.serialized          = new SerializedObject(this.data);
        if (this.triggeredSerialized == null && this.triggeredData != null) this.triggeredSerialized = new SerializedObject(this.triggeredData);
    }

    void EnsureAnalysis()
    {
        if (this.issues != null) return;

        this.state           = TutorialSequenceState.Build(this.data);
        this.issues          = TutorialValidator.Validate(this.data);
        this.triggeredIssues = TutorialValidator.ValidateTriggered(this.triggeredData);

        this.issueByStep          = GroupByStep(this.issues);
        this.triggeredIssueByStep = GroupByStep(this.triggeredIssues);

        this.errorCount   = 0;
        this.warningCount = 0;
        this.infoCount    = 0;

        CountLevels(this.issues);
        CountLevels(this.triggeredIssues);
    }

    void CountLevels(List<TutorialIssue> _list)
    {
        for (int t_i = 0; t_i < _list.Count; t_i++)
        {
            switch (_list[t_i].Level)
            {
                case ETutorialIssueLevel.Error:   this.errorCount++;   break;
                case ETutorialIssueLevel.Warning: this.warningCount++; break;
                default:                          this.infoCount++;    break;
            }
        }
    }

    static Dictionary<long, List<TutorialIssue>> GroupByStep(List<TutorialIssue> _list)
    {
        var t_map = new Dictionary<long, List<TutorialIssue>>();

        for (int t_i = 0; t_i < _list.Count; t_i++)
        {
            long t_key = StepKey(_list[t_i].Chapter, _list[t_i].Step);

            if (!t_map.TryGetValue(t_key, out var t_bucket))
            {
                t_bucket     = new List<TutorialIssue>();
                t_map[t_key] = t_bucket;
            }

            t_bucket.Add(_list[t_i]);
        }

        return t_map;
    }

    static long StepKey(int _outer, int _step) => ((long)_outer << 32) | (uint)_step;

    // ── 그리기 ──────────────────────────────────────────────────────────────

    void OnGUI()
    {
        EnsureAnalysis();
        EnsureSerialized();

        this.serialized?.Update();
        this.triggeredSerialized?.Update();

        DrawToolbar();
        DrawSettings();

        EditorGUILayout.BeginHorizontal();
        DrawListPane();
        DrawSplitter();
        DrawDetailPane();
        EditorGUILayout.EndHorizontal();

        // 값 편집(드로어가 그린 필드)을 먼저 굳힌 뒤에 구조 편집을 돌린다 — 순서가 뒤집히면 방금 친 값이 날아간다.
        if (this.serialized != null && this.serialized.ApplyModifiedProperties())                   Invalidate();
        if (this.triggeredSerialized != null && this.triggeredSerialized.ApplyModifiedProperties()) Invalidate();

        if (this.pendingEdit == null) return;

        var t_edit = this.pendingEdit;
        this.pendingEdit = null;

        t_edit();
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        var t_mode = (EMode)GUILayout.Toolbar((int)this.mode, s_modeLabels, EditorStyles.toolbarButton, GUILayout.Width(120));
        if (t_mode != this.mode)
        {
            this.mode = t_mode;
            this.selectedOuter = -1;
            this.selectedStep  = -1;

            // 비트 캐시는 편 번호로만 키가 잡혀 있다 — 버리지 않으면 트리거 묶음에 온보딩 접기가 씌워진다.
            Invalidate();
        }

        GUILayout.Space(8);

        this.structureEdit = GUILayout.Toggle(this.structureEdit, "구조 편집", EditorStyles.toolbarButton, GUILayout.Width(66));
        this.issuesOnly    = GUILayout.Toggle(this.issuesOnly,    "문제만",   EditorStyles.toolbarButton, GUILayout.Width(52));

        using (new EditorGUI.DisabledScope(this.data == null))
            if (GUILayout.Button("ID 부여", EditorStyles.toolbarButton, GUILayout.Width(58)))
            {
                this.data.AssignMissingStepIds();
                Invalidate();
            }

        GUILayout.FlexibleSpace();

        GUILayout.Label(VerdictLabel(), this.errorCount > 0 ? ErrorStyle : EditorStyles.miniLabel);

        this.showSettings = GUILayout.Toggle(this.showSettings, "설정", EditorStyles.toolbarButton, GUILayout.Width(42));

        EditorGUILayout.EndHorizontal();
    }

    static readonly string[] s_modeLabels = { "온보딩", "트리거" };

    string VerdictLabel()
    {
        if (this.data == null) return "온보딩 SO 미지정";

        if (this.errorCount == 0 && this.warningCount == 0)
            return this.infoCount == 0 ? "이상 없음" : $"안내 {this.infoCount}";

        return $"오류 {this.errorCount} · 경고 {this.warningCount} · 안내 {this.infoCount}";
    }

    // SO 지정은 한 번 하면 끝이라 늘 자리를 내줄 이유가 없다 — 접어 둔다.
    void DrawSettings()
    {
        if (!this.showSettings) return;

        EditorGUI.BeginChangeCheck();

        this.data = (OutgameTutorialData)EditorGUILayout.ObjectField(
            "온보딩 SO", this.data, typeof(OutgameTutorialData), false);

        this.triggeredData = (TriggeredTutorialData)EditorGUILayout.ObjectField(
            "트리거 SO", this.triggeredData, typeof(TriggeredTutorialData), false);

        if (EditorGUI.EndChangeCheck()) Invalidate();

        // 초기화는 자기가 찾은 한 벌을 재생한다 — 여러 벌이면 여기서 검증한 것과 다를 수 있다.
        if (this.dataAssetCount > 1 || this.triggeredAssetCount > 1)
            EditorGUILayout.HelpBox($"같은 타입의 SO가 여러 벌이다(온보딩 {this.dataAssetCount} · 트리거 {this.triggeredAssetCount}) — "
                                  + "초기화가 실제로 재생하는 에셋을 직접 지정할 것.", MessageType.Warning);

        EditorGUILayout.Space(2);
    }

    // ── 왼쪽: 목록 ──────────────────────────────────────────────────────────

    void DrawListPane()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(this.listWidth));

        int t_outers = OuterCount();
        if (t_outers == 0)
        {
            EditorGUILayout.LabelField(this.mode == EMode.Onboarding ? "저작된 편이 없다" : "저작된 묶음이 없다",
                                       EditorStyles.miniLabel);
        }

        this.listScroll = EditorGUILayout.BeginScrollView(this.listScroll, GUILayout.Width(this.listWidth));

        for (int t_o = 0; t_o < t_outers; t_o++) DrawOuterGroup(t_o);

        if (this.structureEdit && this.mode == EMode.Onboarding && GUILayout.Button("+ 편 추가", EditorStyles.miniButton))
            Defer(() => TutorialSequenceEditOps.AddChapter(this.data, t_outers));

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawOuterGroup(int _outer)
    {
        int t_steps = StepCountOf(_outer);
        OuterBadge(_outer, out var t_worst, out int t_badgeCount);

        EditorGUILayout.Space(6);

        // 편 사이를 선 하나로 가른다 — 굵기만으로는 33줄 안에서 헤더가 묻힌다.
        var t_line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(t_line, SplitterColor);

        EditorGUILayout.BeginHorizontal();

        string t_head = OuterHeadLabel(_outer, t_steps);
        if (t_badgeCount > 0) t_head += $"  {MarkOf(t_worst)}{t_badgeCount}";

        GUILayout.Label(t_head, t_badgeCount == 0                            ? GroupStyle
                              : t_worst == ETutorialIssueLevel.Error         ? GroupErrorStyle
                              :                                                GroupWarningStyle);

        // 편 헤더를 눌러 그 편 자체(스텝 아닌 것)를 고른다 — 빈 편의 이슈와 편 조작이 오른쪽에 뜬다.
        if (ClickedLastRect()) Select(_outer, -1);

        EditorGUILayout.EndHorizontal();

        var t_beats = BeatsOf(_outer);
        for (int t_b = 0; t_b < t_beats.Count; t_b++) DrawBeatRow(_outer, t_beats[t_b]);

        if (this.structureEdit && GUILayout.Button(t_steps == 0 ? "  + 첫 스텝" : "  + 스텝", EditorStyles.miniButton))
        {
            int t_at = t_steps;
            Defer(() => AddStepAt(_outer, t_at));
        }
    }

    // 목록의 한 줄 = 비트 하나. 무대 준비·뒤처리 행은 줄로 서지 않고 대표 줄 옆에 개수로만 남는다 —
    // 그래야 목록이 "실행기가 처리하는 순서"가 아니라 "유저가 겪는 순서"로 읽힌다.
    void DrawBeatRow(int _outer, TutorialBeat _beat)
    {
        var t_issues = BeatIssues(_outer, _beat);
        if (this.issuesOnly && t_issues == null) return;

        int  t_primary  = _beat.PrimaryStep;
        bool t_found    = TryGetStepAt(_outer, t_primary, out var t_def);
        bool t_selected = this.selectedOuter == _outer && _beat.Contains(this.selectedStep);

        BeatMarks(_outer, _beat, out bool t_isHere, out bool t_isScheduled);

        // ▶ = 지금 서 있는 칸(플레이 중), ◆ = 다음 플레이에 시작할 칸
        string t_mark = t_isScheduled ? "◆" : t_isHere ? "▶" : " ";
        string t_id   = this.mode == EMode.Onboarding ? (t_found && t_def.StepId > 0 ? $"#{t_def.StepId}" : "#-") : string.Empty;
        string t_name = t_found ? t_def.Action.ToString() : "(빈 칸)";

        var t_worst = WorstLevel(t_issues);
        string t_badge = t_worst == ETutorialIssueLevel.Error ? " ✖" : t_worst == ETutorialIssueLevel.Warning ? " ▲" : string.Empty;

        var t_style = t_selected ? RowSelectedStyle
                    : t_worst == ETutorialIssueLevel.Error   ? RowErrorStyle
                    : t_worst == ETutorialIssueLevel.Warning ? RowWarningStyle
                    : t_isHere || t_isScheduled              ? RowMarkedStyle
                    :                                          RowStyle;

        GUILayout.Label($"{t_mark} {_outer}-{t_primary} {t_id} {t_name}{ChipsOf(_beat)}{t_badge}", t_style);

        if (ClickedLastRect()) Select(_outer, t_primary);
    }

    // 대표 줄 옆에 접혀 들어간 행의 개수. 사건 없이 잡일만 모인 무리는 첫 행이 대표로 서므로 그 한 줄은 뺀다.
    static string ChipsOf(TutorialBeat _beat)
    {
        int t_pre  = _beat.PreCount;
        int t_post = _beat.PostCount;

        if (_beat.BeatStep < 0)
        {
            if (t_pre > 0)       t_pre--;
            else if (t_post > 0) t_post--;
        }

        string t_text = string.Empty;

        if (t_pre  > 0) t_text += $"  ·무대 {t_pre}";
        if (t_post > 0) t_text += $"  ·정리 {t_post}";

        return t_text;
    }

    // 좌표 표시(▶·◆)는 비트 안 어느 행에 서 있어도 그 비트에 뜬다 — 접혀 들어간 행에 서면 표시가 사라지면 안 된다.
    void BeatMarks(int _outer, TutorialBeat _beat, out bool _isHere, out bool _isScheduled)
    {
        _isHere      = false;
        _isScheduled = false;

        if (this.mode != EMode.Onboarding) return;

        for (int t_s = _beat.First; t_s <= _beat.Last; t_s++)
        {
            _isHere      |= IsCurrent(_outer, t_s);
            _isScheduled |= IsScheduled(_outer, t_s);
        }
    }

    // 비트가 품은 행들의 이슈를 합친다(한 행짜리면 그 행의 목록을 그대로 쓴다 — 대부분이 이쪽이다).
    List<TutorialIssue> BeatIssues(int _outer, TutorialBeat _beat)
    {
        if (_beat.Count == 1) return IssuesAt(_outer, _beat.First);

        List<TutorialIssue> t_merged = null;

        for (int t_s = _beat.First; t_s <= _beat.Last; t_s++)
        {
            var t_bucket = IssuesAt(_outer, t_s);
            if (t_bucket == null) continue;

            (t_merged ??= new List<TutorialIssue>()).AddRange(t_bucket);
        }

        return t_merged;
    }

    List<TutorialBeat> BeatsOf(int _outer)
    {
        this.beats ??= new Dictionary<int, List<TutorialBeat>>();

        if (this.beats.TryGetValue(_outer, out var t_cached)) return t_cached;

        t_cached = TutorialBeatGrouping.Build(StepCountOf(_outer),
                                              _step => TryGetStepAt(_outer, _step, out var t_def) ? t_def : null);

        this.beats[_outer] = t_cached;

        return t_cached;
    }

    void Select(int _outer, int _step)
    {
        this.selectedOuter = _outer;
        this.selectedStep  = _step;

        this.detailScroll = Vector2.zero;
        GUI.FocusControl(null);
    }

    // ── 가운데: 폭 손잡이 ───────────────────────────────────────────────────

    void DrawSplitter()
    {
        var t_rect = GUILayoutUtility.GetRect(SplitterWidth, SplitterWidth, GUILayout.Width(SplitterWidth), GUILayout.ExpandHeight(true));

        EditorGUI.DrawRect(new Rect(t_rect.x + 2f, t_rect.y, 1f, t_rect.height), SplitterColor);
        EditorGUIUtility.AddCursorRect(t_rect, MouseCursor.ResizeHorizontal);

        var t_event = Event.current;

        if (t_event.type == EventType.MouseDown && t_rect.Contains(t_event.mousePosition)) this.draggingSplitter = true;
        if (t_event.type == EventType.MouseUp)                                             this.draggingSplitter = false;

        if (!this.draggingSplitter || t_event.type != EventType.MouseDrag) return;

        this.listWidth = Mathf.Clamp(this.listWidth + t_event.delta.x, MinListWidth, position.width - MinDetailWidth);
        Repaint();
    }

    // ── 오른쪽: 고른 것 하나 ────────────────────────────────────────────────

    void DrawDetailPane()
    {
        EditorGUILayout.BeginVertical();
        this.detailScroll = EditorGUILayout.BeginScrollView(this.detailScroll);

        // 상세는 넓은 창에서도 읽는 줄 길이를 넘기지 않는다 — 라벨과 값이 화면 양끝으로 갈라지면 눈이 왕복한다.
        EditorGUILayout.BeginVertical(GUILayout.MaxWidth(DetailMaxWidth));

        float t_labelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 130f;

        if (this.selectedOuter < 0)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("왼쪽에서 비트를 고르면 여기에 값 · 상태 · 문제가 함께 뜬다.", WrapStyle);
        }
        else if (this.selectedStep < 0) DrawOuterDetail(this.selectedOuter);
        else                            DrawBeatDetail(this.selectedOuter, this.selectedStep);

        EditorGUIUtility.labelWidth = t_labelWidth;

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawOuterDetail(int _outer)
    {
        EditorGUILayout.LabelField(OuterHeadLabel(_outer, StepCountOf(_outer)), EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        if (this.mode == EMode.Onboarding) DrawChapterFields(_outer);
        else                               DrawEntryFields(_outer);

        DrawIssueCards(IssuesAt(_outer, 0));
    }

    void DrawChapterFields(int _chapter)
    {
        using (new EditorGUI.DisabledScope(!this.structureEdit))
        {
            string t_label  = LabelOf(_chapter);
            string t_edited = EditorGUILayout.DelayedTextField("편 이름", t_label);
            if (t_edited != t_label) Defer(() => TutorialSequenceEditOps.SetChapterLabel(this.data, _chapter, t_edited));
        }

        if (!this.structureEdit) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(_chapter == 0))
            if (GUILayout.Button("▲ 앞으로", EditorStyles.miniButtonLeft))
                Defer(() => TutorialSequenceEditOps.MoveChapter(this.data, _chapter, -1));

        using (new EditorGUI.DisabledScope(_chapter >= OuterCount() - 1))
            if (GUILayout.Button("▼ 뒤로", EditorStyles.miniButtonMid))
                Defer(() => TutorialSequenceEditOps.MoveChapter(this.data, _chapter, +1));

        if (GUILayout.Button("아래에 편 추가", EditorStyles.miniButtonMid))
            Defer(() => TutorialSequenceEditOps.AddChapter(this.data, _chapter + 1));

        if (GUILayout.Button("편 삭제", EditorStyles.miniButtonRight))
            Defer(() => TutorialSequenceEditOps.DeleteChapter(this.data, _chapter));

        EditorGUILayout.EndHorizontal();
    }

    // 묶음의 이름과 발화 키. 키는 완주 낙인의 식별자이기도 해서 바꾸면 이미 찍힌 낙인과 갈린다.
    void DrawEntryFields(int _entry)
    {
        if (!TryGetEntry(_entry, out var t_entry)) return;

        using (new EditorGUI.DisabledScope(!this.structureEdit))
        {
            string t_label  = t_entry.Label ?? string.Empty;
            string t_edited = EditorGUILayout.DelayedTextField("묶음 이름", t_label);
            if (t_edited != t_label) Defer(() => TutorialSequenceEditOps.SetTriggeredLabel(this.triggeredData, _entry, t_edited));

            var t_trigger = (EOutgameTutorialTrigger)EditorGUILayout.EnumPopup("발화 키", t_entry.Trigger);
            if (t_trigger != t_entry.Trigger)
            {
                Defer(() => TutorialSequenceEditOps.SetTriggeredKey(this.triggeredData, _entry, t_trigger));
                Debug.LogWarning("[튜토리얼 저작] 발화 키는 완주 낙인의 식별자다 — 바꾸면 이미 완주한 계정이 이 묶음을 다시 본다.");
            }
        }
    }

    /// <summary>고른 것 하나 = 비트 하나. 사건 행만이 아니라 그 사건에 매달린 무대 준비·뒤처리까지
    /// 한 화면에서 편집한다 — 팩 하나를 팔려고 목록을 세 번 오가던 동선이 여기서 끝난다.</summary>
    void DrawBeatDetail(int _outer, int _step)
    {
        var t_beats = BeatsOf(_outer);
        int t_index = TutorialBeatGrouping.IndexOf(t_beats, _step);

        // 접기가 못 닿는 좌표(편집 직후의 한 프레임 등)는 한 행짜리 비트로 본다 — 종전 화면 그대로다.
        var t_beat = t_index >= 0 ? t_beats[t_index] : new TutorialBeat(_step, _step, _step, 0, 0);

        int  t_primary = t_beat.PrimaryStep;
        bool t_found   = TryGetStepAt(_outer, t_primary, out var t_def);

        DrawBeatHeader(_outer, t_beat, t_found, t_def);

        // 접혀 들어간 행의 문제도 여기서 다 보여야 한다 — 목록에 줄이 없으니 드러날 다른 자리가 없다.
        for (int t_s = t_beat.First; t_s <= t_beat.Last; t_s++)
            DrawIssueCards(IssuesAt(_outer, t_s), t_beat.Count > 1 ? $"{_outer}-{t_s} · " : string.Empty);

        if (t_found) DrawStateBox(_outer, t_primary, t_def);

        // 한 행짜리 비트는 접을 것이 없다 — 섹션 머리를 세우지 않고 종전대로 그린다.
        if (t_beat.Count == 1)
        {
            if (this.structureEdit)
            {
                EditorGUILayout.Space(4);
                DrawStepTools(_outer, t_beat.First);
            }

            EditorGUILayout.Space(6);
            DrawStepValue(_outer, t_beat.First);
            return;
        }

        int t_preEnd   = t_beat.First + t_beat.PreCount  - 1;
        int t_postFrom = t_beat.Last  - t_beat.PostCount + 1;

        DrawBeatSection("무대 · 사건이 서기 전에 갖추는 것", _outer, t_beat.First, t_preEnd);

        if (t_beat.BeatStep >= 0) DrawBeatSection("사건 · 유저가 겪는 것", _outer, t_beat.BeatStep, t_beat.BeatStep);

        DrawBeatSection("뒤처리 · 사건이 남긴 것을 치운다", _outer, t_postFrom, t_beat.Last);
    }

    void DrawBeatSection(string _title, int _outer, int _from, int _to)
    {
        if (_from > _to) return;

        EditorGUILayout.Space(8);

        var t_line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(t_line, SplitterColor);

        EditorGUILayout.LabelField(_title, EditorStyles.miniBoldLabel);

        for (int t_s = _from; t_s <= _to; t_s++) DrawMemberStep(_outer, t_s);
    }

    // 비트 안의 한 행. 되감기와 구조 편집은 여전히 행 단위다 — stepId도 되감기 좌표도 행을 가리키기 때문이다.
    void DrawMemberStep(int _outer, int _step)
    {
        bool t_found = TryGetStepAt(_outer, _step, out var t_def);

        EditorGUILayout.BeginHorizontal();

        string t_id = this.mode == EMode.Onboarding && t_found && t_def.StepId > 0 ? $"  #{t_def.StepId}" : string.Empty;
        EditorGUILayout.LabelField($"{_outer}-{_step}{t_id}   {(t_found ? t_def.Action.ToString() : "(빈 칸)")}",
                                   EditorStyles.miniBoldLabel);

        if (this.mode == EMode.Onboarding)
        {
            bool t_scheduled = IsScheduled(_outer, _step);

            if (GUILayout.Button(t_scheduled ? "예약됨" : "여기부터", EditorStyles.miniButton, GUILayout.Width(56))
             && ConfirmRewind(_outer, _step))
                OutgameTutorialRewind.Schedule(_outer, _step);
        }

        EditorGUILayout.EndHorizontal();

        if (this.structureEdit) DrawStepTools(_outer, _step);

        DrawStepValue(_outer, _step);
    }

    void DrawStepValue(int _outer, int _step)
    {
        if (!TryGetStepAt(_outer, _step, out _))
        {
            EditorGUILayout.LabelField("이 칸은 비어 있다 — 행을 지우거나 액션을 저작해야 한다.", WrapStyle);
            return;
        }

        // 값 편집은 드로어에 맡긴다 — 어떤 액션이 어떤 필드를 쓰는지는 TutorialActionMeta가 이미 답한다.
        // (드로어가 자기 요약 줄을 머리에 그리므로 여기에 "값" 라벨을 따로 세우지 않는다.)
        var t_property = StepProperty(_outer, _step);
        if (t_property == null) return;

        t_property.isExpanded = true;
        EditorGUILayout.PropertyField(t_property, GUIContent.none, true);
    }

    // 되감기 대상은 비트의 첫 행이다 — "이 비트부터 다시"가 곧 그 사건의 무대 준비부터 다시라는 뜻이다.
    // 사건 행만 콕 집어 되감으려면 아래 섹션의 행별 [여기부터]를 쓴다.
    void DrawBeatHeader(int _outer, TutorialBeat _beat, bool _found, TutorialStepDef _def)
    {
        EditorGUILayout.BeginHorizontal();

        string t_coord = _beat.Count == 1 ? $"{_outer}-{_beat.First}" : $"{_outer}-{_beat.First}~{_beat.Last}";
        string t_id    = this.mode == EMode.Onboarding && _found && _def.StepId > 0 ? $"  #{_def.StepId}" : string.Empty;

        EditorGUILayout.LabelField($"{t_coord}{t_id}   {(_found ? _def.Action.ToString() : "(빈 칸)")}", EditorStyles.boldLabel);

        if (this.mode == EMode.Onboarding)
        {
            bool t_scheduled = IsScheduled(_outer, _beat.First);

            if (GUILayout.Button(t_scheduled ? "예약됨" : "여기부터", GUILayout.Width(64)) && ConfirmRewind(_outer, _beat.First))
                OutgameTutorialRewind.Schedule(_outer, _beat.First);
        }

        EditorGUILayout.EndHorizontal();

        if (this.mode == EMode.Onboarding) DrawScheduleLine();
    }

    // 되감기는 진행 중인 세이브를 지운다 — 목록에 상시 경고를 세우는 대신 누르는 순간에 묻는다.
    static bool ConfirmRewind(int _outer, int _step)
        => EditorUtility.DisplayDialog(
            "되감기 예약",
            $"다음 플레이를 {_outer}-{_step} 스텝부터 시작합니다.\n\n"
          + "아웃게임 세이브를 첫실행으로 밀고(소유·강화/진화·재화·덱·랭크·도감보상·트리거 낙인) "
          + "그 칸 직전까지의 지급만 재생합니다.\n\n진행 중인 세이브는 사라지며 되돌릴 수 없습니다.",
            "예약", "취소");

    void DrawScheduleLine()
    {
        if (!OutgameTutorialRewind.TryGetScheduled(out int t_chapter, out int t_step, out bool t_wipePending)) return;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"예약 {t_chapter}-{t_step}  {ActionNameAt(t_chapter, t_step)}", EditorStyles.miniLabel);
        if (GUILayout.Button("취소", EditorStyles.miniButton, GUILayout.Width(44))) OutgameTutorialRewind.Cancel();
        EditorGUILayout.EndHorizontal();

        // 밀기만 돌고 지급 재생 전에 초기화가 끊긴 상태(부트 초기화(InitializationRunner) 없는 씬에서 Play 등).
        // 이 자리에 드러내지 않으면 취소할 방법이 없어, 한참 진행한 세이브 위에 다음 초기화가 지급을 덧씌운다.
        if (!t_wipePending)
            EditorGUILayout.HelpBox("세이브 밀기는 끝났고 지급 재생만 남았다 — 그 사이에 진행했다면 [취소]로 걷어라.", MessageType.Warning);
    }

    // 이 스텝에 서 있을 때 게임의 문이 어디까지 열려 있는가 — 저작만 봐서는 알 수 없는 유일한 값이다.
    void DrawStateBox(int _outer, int _step, TutorialStepDef _def)
    {
        if (this.mode != EMode.Onboarding) return;
        if (this.state == null || !this.state.TryGet(_outer, _step, out var t_state)) return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("상태 · " + t_state.Summary, StateStyle);

            if (!TutorialStepDef.UsesAnchor(_def.Action) || _def.Anchor == EOutgameTutorialAnchor.None) return;

            var t_meta = TutorialAnchorMeta.Of(_def.Anchor);

            var t_sb = new StringBuilder("앵커 · ").Append(_def.Anchor).Append("  ·  ").Append(t_meta.Screen);

            if (t_meta.Gate != EOutgameFeature.None)
                t_sb.Append("  ·  잠금키 ").Append(t_meta.Gate).Append(t_state.IsUnlocked(t_meta.Gate) ? " 열림" : " 닫힘");

            EditorGUILayout.LabelField(t_sb.ToString(), StateStyle);

            if (!t_meta.IsRegistered) return;

            EditorGUILayout.LabelField("등록 · " + t_meta.Source, EditorStyles.miniLabel);
        }
    }

    void DrawStepTools(int _outer, int _step)
    {
        int t_steps = StepCountOf(_outer);

        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(_step == 0))
            if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(26)))
                Defer(() => MoveStepBy(_outer, _step, -1));

        using (new EditorGUI.DisabledScope(_step >= t_steps - 1))
            if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(26)))
                Defer(() => MoveStepBy(_outer, _step, +1));

        if (GUILayout.Button("복제", EditorStyles.miniButtonMid))     Defer(() => DuplicateStepAt(_outer, _step));
        if (GUILayout.Button("아래 추가", EditorStyles.miniButtonMid)) Defer(() => AddStepAt(_outer, _step + 1));
        if (GUILayout.Button("삭제", EditorStyles.miniButtonRight))    Defer(() => DeleteStepAt(_outer, _step));

        if (this.mode == EMode.Onboarding)
        {
            GUILayout.Space(8);
            if (GUILayout.Button("편 이동 ▾", EditorStyles.miniButton, GUILayout.Width(70))) ShowChapterMoveMenu(_outer, _step);
        }

        EditorGUILayout.EndHorizontal();
    }

    // stepId가 그대로 따라가므로 편을 옮겨도 그 스텝에 서 있던 세이브는 밀리지 않는다.
    void ShowChapterMoveMenu(int _chapter, int _step)
    {
        var t_menu  = new GenericMenu();
        int t_count = OuterCount();

        for (int t_c = 0; t_c < t_count; t_c++)
        {
            int    t_target = t_c;
            string t_label  = LabelOf(t_c);
            var    t_text   = new GUIContent(string.IsNullOrEmpty(t_label) ? $"{t_c + 1}편" : $"{t_c + 1}편 — {t_label}");

            if (t_c == _chapter) t_menu.AddDisabledItem(t_text);
            else                 t_menu.AddItem(t_text, false,
                () => Defer(() => TutorialSequenceEditOps.MoveStepToChapter(this.data, _chapter, _step, t_target)));
        }

        t_menu.ShowAsContext();
    }

    void DrawIssueCards(List<TutorialIssue> _issues, string _prefix = "")
    {
        if (_issues == null) return;

        EditorGUILayout.Space(4);

        for (int t_i = 0; t_i < _issues.Count; t_i++)
        {
            var t_issue = _issues[t_i];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{MarkOf(t_issue.Level)} {_prefix}{t_issue.Rule}", StyleOf(t_issue.Level));
                EditorGUILayout.LabelField(t_issue.Message, WrapStyle);

                if (!string.IsNullOrEmpty(t_issue.Fix)) EditorGUILayout.LabelField("고치는 법 · " + t_issue.Fix, FixStyle);
            }
        }
    }

    // ── 편집 배선 (모드에 따라 온보딩·트리거 연산을 가른다) ──────────────────

    bool AddStepAt(int _outer, int _index)
        => this.mode == EMode.Onboarding
            ? TutorialSequenceEditOps.AddStep(this.data, _outer, _index)
            : TutorialSequenceEditOps.AddTriggeredStep(this.triggeredData, _outer, _index);

    bool DuplicateStepAt(int _outer, int _step)
        => this.mode == EMode.Onboarding
            ? TutorialSequenceEditOps.DuplicateStep(this.data, _outer, _step)
            : TutorialSequenceEditOps.DuplicateTriggeredStep(this.triggeredData, _outer, _step);

    bool DeleteStepAt(int _outer, int _step)
        => this.mode == EMode.Onboarding
            ? TutorialSequenceEditOps.DeleteStep(this.data, _outer, _step)
            : TutorialSequenceEditOps.DeleteTriggeredStep(this.triggeredData, _outer, _step);

    bool MoveStepBy(int _outer, int _step, int _delta)
        => this.mode == EMode.Onboarding
            ? TutorialSequenceEditOps.MoveStep(this.data, _outer, _step, _delta)
            : TutorialSequenceEditOps.MoveTriggeredStep(this.triggeredData, _outer, _step, _delta);

    // 편집이 실제로 일어났을 때만 캐시를 버린다(삭제 대화상자를 취소하면 아무것도 안 바뀐다).
    // Repaint는 여기서 반드시 걸어야 한다 — GenericMenu 콜백은 GUI 패스 밖에서 도는 탓에
    // 다시 그리라고 말해 주지 않으면 예약된 편집이 그대로 잠들어 있다가 다음 편집에 덮인다.
    void Defer(System.Func<bool> _edit)
    {
        this.pendingEdit = () =>
        {
            if (_edit()) Invalidate();
        };

        Repaint();
    }

    // ── 모드에 무관한 조회 (왼쪽 목록이 온보딩·트리거를 한 코드로 그리는 근거) ──

    int OuterCount()
    {
        if (this.mode == EMode.Onboarding) return this.data != null && this.data.chapters != null ? this.data.chapters.Count : 0;

        return this.triggeredData != null && this.triggeredData.entries != null ? this.triggeredData.entries.Count : 0;
    }

    int StepCountOf(int _outer)
    {
        if (this.mode == EMode.Onboarding) return TryGetChapter(_outer, out var t_chapter) ? t_chapter.StepCount : 0;

        return TryGetEntry(_outer, out var t_entry) ? t_entry.StepCount : 0;
    }

    string OuterHeadLabel(int _outer, int _steps)
    {
        if (this.mode == EMode.Onboarding)
        {
            string t_label = LabelOf(_outer);
            return string.IsNullOrEmpty(t_label) ? $"{_outer + 1}편  ({_steps})" : $"{_outer + 1}편 — {t_label}  ({_steps})";
        }

        if (!TryGetEntry(_outer, out var t_entry)) return $"{_outer}  ({_steps})";

        return string.IsNullOrEmpty(t_entry.Label) ? $"{t_entry.Trigger}  ({_steps})" : $"{t_entry.Trigger} — {t_entry.Label}  ({_steps})";
    }

    bool TryGetStepAt(int _outer, int _step, out TutorialStepDef _def)
    {
        _def = null;

        if (this.mode == EMode.Onboarding) return TryGetChapter(_outer, out var t_chapter) && t_chapter.TryGetStep(_step, out _def);

        return TryGetEntry(_outer, out var t_entry) && t_entry.TryGetStep(_step, out _def);
    }

    List<TutorialIssue> IssuesAt(int _outer, int _step)
    {
        var t_map = this.mode == EMode.Onboarding ? this.issueByStep : this.triggeredIssueByStep;

        return t_map != null && t_map.TryGetValue(StepKey(_outer, _step), out var t_bucket) ? t_bucket : null;
    }

    // 편 헤더의 배지. 스텝 줄이 없는 이슈(빈 편·빈 묶음)는 여기서만 드러날 수 있어 경고도 함께 센다.
    void OuterBadge(int _outer, out ETutorialIssueLevel _worst, out int _count)
    {
        _worst = ETutorialIssueLevel.Info;
        _count = 0;

        var t_list = this.mode == EMode.Onboarding ? this.issues : this.triggeredIssues;
        if (t_list == null) return;

        for (int t_i = 0; t_i < t_list.Count; t_i++)
        {
            if (t_list[t_i].Chapter != _outer)                     continue;
            if (t_list[t_i].Level == ETutorialIssueLevel.Info)     continue;

            if (t_list[t_i].Level > _worst) { _worst = t_list[t_i].Level; _count = 0; }
            if (t_list[t_i].Level == _worst) _count++;
        }
    }

    SerializedProperty StepProperty(int _outer, int _step)
    {
        var t_object = this.mode == EMode.Onboarding ? this.serialized : this.triggeredSerialized;
        if (t_object == null) return null;

        var t_outerList = t_object.FindProperty(this.mode == EMode.Onboarding ? "chapters" : "entries");
        if (t_outerList == null || _outer < 0 || _outer >= t_outerList.arraySize) return null;

        var t_steps = t_outerList.GetArrayElementAtIndex(_outer).FindPropertyRelative("stepDefs");
        if (t_steps == null || _step < 0 || _step >= t_steps.arraySize) return null;

        return t_steps.GetArrayElementAtIndex(_step);
    }

    string LabelOf(int _chapter)
        => TryGetChapter(_chapter, out var t_chapter) && !string.IsNullOrEmpty(t_chapter.Label) ? t_chapter.Label : string.Empty;

    bool TryGetChapter(int _chapter, out OutgameTutorialChapter _result)
    {
        _result = null;
        if (this.data == null || this.data.chapters == null)      return false;
        if (_chapter < 0 || _chapter >= this.data.chapters.Count) return false;

        _result = this.data.chapters[_chapter];
        return _result != null;
    }

    bool TryGetEntry(int _entry, out TriggeredTutorialEntry _result)
    {
        _result = null;
        if (this.triggeredData == null || this.triggeredData.entries == null) return false;
        if (_entry < 0 || _entry >= this.triggeredData.entries.Count)         return false;

        _result = this.triggeredData.entries[_entry];
        return _result != null;
    }

    static T FindAsset<T>() where T : ScriptableObject
    {
        string[] t_guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        if (t_guids.Length == 0) return null;

        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(t_guids[0]));
    }

    // ── 좌표 ────────────────────────────────────────────────────────────────

    static bool IsCurrent(int _chapter, int _step)
        => Application.isPlaying
        && !OutgameTutorialProgress.IsCompleted
        && OutgameTutorialProgress.ChapterIndex == _chapter
        && OutgameTutorialProgress.StepIndex == _step;

    static bool IsScheduled(int _chapter, int _step)
        => OutgameTutorialRewind.TryGetScheduled(out int t_c, out int t_s, out _) && t_c == _chapter && t_s == _step;

    string ActionNameAt(int _chapter, int _step)
        => TryGetChapter(_chapter, out var t_chapter) && t_chapter.TryGetStep(_step, out var t_def) ? t_def.Action.ToString() : "(빈 칸)";

    // ── 잡동사니 ────────────────────────────────────────────────────────────

    static ETutorialIssueLevel WorstLevel(List<TutorialIssue> _issues)
    {
        var t_worst = ETutorialIssueLevel.Info;
        if (_issues == null) return t_worst;

        for (int t_i = 0; t_i < _issues.Count; t_i++)
            if (_issues[t_i].Level > t_worst) t_worst = _issues[t_i].Level;

        return t_worst;
    }

    static string MarkOf(ETutorialIssueLevel _level)
        => _level == ETutorialIssueLevel.Error ? "✖" : _level == ETutorialIssueLevel.Warning ? "▲" : "·";

    // 목록 줄은 라벨로 그리고 클릭만 가로챈다 — 버튼으로 그리면 행마다 테두리가 생겨 목록이 읽히지 않는다.
    static bool ClickedLastRect()
    {
        var t_event = Event.current;
        if (t_event.type != EventType.MouseDown || t_event.button != 0) return false;
        if (!GUILayoutUtility.GetLastRect().Contains(t_event.mousePosition)) return false;

        t_event.Use();
        return true;
    }

    // ── 스타일 (EditorStyles는 static 초기화 시점에 아직 없다 — 첫 OnGUI에서 만든다) ──

    static GUIStyle s_rowStyle;
    static GUIStyle s_rowSelectedStyle;
    static GUIStyle s_rowErrorStyle;
    static GUIStyle s_rowWarningStyle;
    static GUIStyle s_rowMarkedStyle;
    static GUIStyle s_groupStyle;
    static GUIStyle s_groupErrorStyle;
    static GUIStyle s_errorStyle;
    static GUIStyle s_warningStyle;
    static GUIStyle s_stateStyle;
    static GUIStyle s_fixStyle;
    static GUIStyle s_wrapStyle;

    static readonly Color ErrorColor    = new Color(0.90f, 0.38f, 0.33f);
    static readonly Color WarningColor  = new Color(0.92f, 0.74f, 0.28f);
    static readonly Color StateColor    = new Color(0.45f, 0.70f, 0.88f);
    static readonly Color SplitterColor = new Color(0f, 0f, 0f, 0.35f);

    static GUIStyle Row(Color _color, bool _bold = false)
        => new GUIStyle(EditorStyles.label)
        {
            padding   = new RectOffset(16, 4, 1, 1),   // 편 헤더보다 들여써 목록에 위계를 준다
            margin    = new RectOffset(0, 0, 0, 0),
            fontSize  = 11,
            fontStyle = _bold ? FontStyle.Bold : FontStyle.Normal,
            normal    = { textColor = _color },
        };

    static GUIStyle RowStyle         => s_rowStyle         ??= Row(EditorStyles.label.normal.textColor);
    static GUIStyle RowErrorStyle    => s_rowErrorStyle    ??= Row(ErrorColor);
    static GUIStyle RowWarningStyle  => s_rowWarningStyle  ??= Row(WarningColor);
    static GUIStyle RowMarkedStyle   => s_rowMarkedStyle   ??= Row(EditorStyles.label.normal.textColor, true);

    static GUIStyle RowSelectedStyle =>
        s_rowSelectedStyle ??= new GUIStyle(Row(Color.white, true)) { normal = { background = SelectionTexture, textColor = Color.white } };

    static GUIStyle GroupStyle =>
        s_groupStyle ??= new GUIStyle(EditorStyles.boldLabel) { padding = new RectOffset(2, 2, 2, 2), fontSize = 11 };

    static GUIStyle GroupErrorStyle =>
        s_groupErrorStyle ??= new GUIStyle(GroupStyle) { normal = { textColor = ErrorColor } };

    static GUIStyle s_groupWarningStyle;

    static GUIStyle GroupWarningStyle =>
        s_groupWarningStyle ??= new GUIStyle(GroupStyle) { normal = { textColor = WarningColor } };

    static GUIStyle ErrorStyle =>
        s_errorStyle ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ErrorColor } };

    static GUIStyle WarningStyle =>
        s_warningStyle ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = WarningColor } };

    static GUIStyle StateStyle =>
        s_stateStyle ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = StateColor } };

    static GUIStyle FixStyle =>
        s_fixStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic, wordWrap = true };

    static GUIStyle WrapStyle =>
        s_wrapStyle ??= new GUIStyle(EditorStyles.label) { wordWrap = true };

    static GUIStyle StyleOf(ETutorialIssueLevel _level)
        => _level == ETutorialIssueLevel.Error   ? ErrorStyle
         : _level == ETutorialIssueLevel.Warning ? WarningStyle
         :                                         EditorStyles.miniLabel;

    static Texture2D s_selectionTexture;

    // 선택 줄의 밑판. EditorStyles에는 목록 행용 하이라이트가 없어 1픽셀짜리를 직접 만든다.
    static Texture2D SelectionTexture
    {
        get
        {
            if (s_selectionTexture != null) return s_selectionTexture;

            s_selectionTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            s_selectionTexture.SetPixel(0, 0, new Color(0.24f, 0.38f, 0.58f));
            s_selectionTexture.Apply();

            return s_selectionTexture;
        }
    }
}
