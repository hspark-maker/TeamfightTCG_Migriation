using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 릴리즈 관리 창. Tools > Card Battle > 릴리즈 관리.
///
/// 모드 · 카드 표 · 검증 · 콘텐츠 버전을 한 화면에 묶는다. 따로 놀면 조용히 어긋나기 때문이다:
/// 모드만 바꾸고 표를 안 실으면 수치가 틀리고, 표만 갈고 내보내면 런타임 프로필과 데이터가 갈린다.
///
/// <para>빌드는 여기서 하지 않는다. Unity 의 Build Profiles 로만 만든다 —
/// 이 창이 BuildPipeline.BuildPlayer 를 부르면 그 옵션이 Build Profiles 의 공유 설정
/// (Library/BuildProfiles/SharedProfile.asset 의 m_Development)까지 바꿔 놓아서,
/// 여기서 Test 빌드를 한 번 뽑으면 이후 Build Profiles 빌드가 전부 개발 빌드로 나갔다.</para>
///
/// <para>대신 빌드 전처리 <see cref="ContentProfileValidator"/> 가 어느 경로로 빌드하든
/// 같은 검증을 걸고, 그 빌드가 개발/릴리즈 중 무엇인지 콘솔에 남긴다.</para>
/// </summary>
public partial class ReleaseManagerWindow : EditorWindow
{
    const string PREF_TAB       = "Release.Tab";

    enum Tab
    {
        Release,
        Data,
    }

    Tab selectedTab;

    Vector2 scroll;
    string  lastReport;
    List<string> issues;
    List<string> validationWarnings;   // 빌드를 막지 않는다 — 밸런스 눈치용
    bool showColumnHelp;

    // 표 대조 결과 캐시. 매 프레임 CSV를 파싱하고 카드를 복제할 수는 없으므로 모드가 바뀌거나
    // 검증을 다시 돌릴 때만 계산한다. driftMode = 이 결과가 어느 모드 표를 본 것인지.
    List<string>    drift;
    string          driftError;
    EContentRunMode driftMode;
    bool            driftValid;

    [MenuItem("Tools/Card Battle/릴리즈 관리")]
    static void Open() => GetWindow<ReleaseManagerWindow>("릴리즈 관리").minSize = new Vector2(520, 560);

    void OnEnable()
    {
        this.selectedTab = (Tab)Mathf.Clamp(EditorPrefs.GetInt(PREF_TAB, 0), 0, 1);
        Revalidate();
        EnableDataTab();
        EnableVersionManagement();
    }

    void OnDisable()
    {
        EditorPrefs.SetInt(PREF_TAB, (int)this.selectedTab);
        DisableDataTab();
    }

    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 에셋·빌드를 다룰 수 없다. 플레이를 멈추고 다시 열 것.", MessageType.Warning);
            return;
        }

        this.selectedTab = (Tab)GUILayout.Toolbar(
            (int)this.selectedTab,
            new[] { "릴리즈", "데이터" },
            GUILayout.Height(26));

        if (this.selectedTab == Tab.Data)
        {
            DrawDataTab();
            return;
        }

        this.scroll = EditorGUILayout.BeginScrollView(this.scroll);
        DrawModeSection();
        DrawVersionManagementSection();
        DrawValidationSection();
        DrawReport();
        EditorGUILayout.EndScrollView();
    }

    // ── ① 실행 모드 ────────────────────────────────────────────────────────

    void DrawModeSection()
    {
        EContentRunMode t_mode = ContentRunModeEditor.Current;
        ContentProfileConfig t_profile = ContentRunModeEditor.ProfileOf(t_mode);

        Header("① 실행 모드");

        using (new EditorGUILayout.HorizontalScope())
        {
            for (int t_i = 0; t_i <= 1; t_i++)
            {
                var t_target = (EContentRunMode)t_i;
                bool t_on = t_target == t_mode;
                string t_text = ContentRunModeEditor.Label(t_target) + (t_on ? "  ●" : "");
                if (GUILayout.Toggle(t_on, t_text, EditorStyles.miniButton, GUILayout.Height(26)) && !t_on)
                    SwitchMode(t_target);
            }
        }

        EditorGUILayout.LabelField("세이브 폴더", t_profile != null ? t_profile.SaveFolder : "(프로필 에셋 없음)");
        EditorGUILayout.LabelField("테스트 카드", t_mode == EContentRunMode.Test ? "포함" : "제외");
        EditorGUILayout.LabelField("적용 대상 시트", ContentRunModeEditor.SheetNameOf(t_mode));
    }

    void SwitchMode(EContentRunMode _target)
    {
        string t_label = ContentRunModeEditor.Label(_target);

        // 시트가 소스라 미리 볼 파일이 없다 — 못 읽는 경우는 SwitchTo가 사유를 담아 돌려준다.
        if (!EditorUtility.DisplayDialog($"{t_label}로 전환",
                $"실행 모드를 {t_label}로 바꾸고 {ContentRunModeEditor.SheetNameOf(_target)} SpecData를 사용한다.\n\n" +
                "두 시트는 같은 카드 ID를 공유하므로 지금 에셋에 있는 수치는 사라진다.", "전환", "취소"))
            return;

        string t_report = ContentRunModeEditor.SwitchTo(_target, out string t_error);
        Finish(t_report, t_error);
    }

    // ── ② 카드 표 ──────────────────────────────────────────────────────────

    void DrawTableSection()
    {
        EContentRunMode t_mode = ContentRunModeEditor.Current;
        string t_label = ContentRunModeEditor.Label(t_mode);

        Header("② 카드 스펙시트");

        EditorGUILayout.LabelField("카드 값의 진실원", ContentRunModeEditor.SheetNameOf(t_mode) + " SpecData");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("런타임 표", ContentRunModeEditor.SheetNameOf(t_mode));

        // 값이 들어오는 문은 스펙시트 하나뿐이다. 값 변화의 기록은 카드 .asset 자체가 git에 추적되므로
        // 별도 CSV 스냅샷을 두지 않는다 — 두면 어느 쪽이 진짜인지가 다시 흐려진다.
        if (GUILayout.Button($"{ContentRunModeEditor.SheetNameOf(t_mode)} SpecData 사용 중", GUILayout.Height(26)))
        {
            Finish("카드 수치는 SO에 굽지 않습니다. SpecData 도구에서 표를 갱신하세요.", null);
        }

        EditorGUILayout.HelpBox(
            "카드 값의 진실원은 구글 스펙시트다. 시트를 고쳤으면 CookApps > SpecData 창에서 " +
            "'시트 적용 & CS 생성'을 먼저 돌린 뒤 위 버튼을 누른다.\n" +
            "라이브 = Card 시트 / 테스트 = Card_Test 시트. 같은 카드는 두 시트에서 **같은 id**여야 한다 " +
            "— 매칭 키가 id라 번호가 갈리면 새 에셋으로 복제된다.\n" +
            "시트에서 이름을 바꾸면 에셋 이름도 따라 바뀐다(참조는 guid라 유지된다).",
            MessageType.Info);

        this.showColumnHelp = EditorGUILayout.Foldout(this.showColumnHelp, "열 설명", true);
        if (this.showColumnHelp) EditorGUILayout.HelpBox("카드 값은 Card/Card_Test SpecData가 소유하며 런타임은 int ID로 참조합니다.", MessageType.None);
    }

    // ── ③ 검증 ─────────────────────────────────────────────────────────────

    void DrawValidationSection()
    {
        Header("③ 검증");

        if (GUILayout.Button("다시 검사", GUILayout.Height(22))) Revalidate();

        if (this.issues == null)
        {
            EditorGUILayout.HelpBox("아직 검사하지 않았다.", MessageType.Info);
            return;
        }
        if (this.issues.Count > 0)
            EditorGUILayout.HelpBox($"문제 {this.issues.Count}건 — 빌드가 막힌다.\n· " + string.Join("\n· ", this.issues),
                MessageType.Error);
        else
            EditorGUILayout.HelpBox("통과 — 프로필·레지스트리·라이브 소비 SO 모두 정상.", MessageType.Info);

        if (this.validationWarnings != null && this.validationWarnings.Count > 0)
            EditorGUILayout.HelpBox($"경고 {this.validationWarnings.Count}건 — 빌드는 막지 않는다.\n· "
                + string.Join("\n· ", this.validationWarnings), MessageType.Warning);
    }

    void Revalidate()
    {
        this.validationWarnings = new List<string>();
        this.issues     = ContentProfileValidator.Collect(this.validationWarnings, ContentRunModeEditor.Current);
        this.driftValid = false;   // 표를 갈았거나 에셋이 움직였다 — 대조 결과도 같이 낡는다
    }

    // ── 표 대조 ────────────────────────────────────────────────────────────

    /// <summary>빌드가 쓸 표와 카드 에셋을 값 단위로 견준다. <see cref="ContentRunModeEditor.Applied"/> 도장은
    /// "어느 표를 실었나"만 알 뿐, 적용 후 인스펙터에서 고친 값은 못 잡는다 — 그 구멍을 여기서 막는다.</summary>
    void EnsureDrift(EContentRunMode _mode)
    {
        if (this.driftValid && this.driftMode == _mode) return;

        this.drift      = new List<string>();
        this.driftError = null;
        this.driftMode  = _mode;
        this.driftValid = true;
    }

    void DrawDriftBox(EContentRunMode _mode)
    {
        string t_label = ContentRunModeEditor.Label(_mode);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("표 대조 (빌드 실행 모드 기준)", $"{t_label} 표 ↔ 카드 에셋");
            if (GUILayout.Button("다시 대조", GUILayout.Width(80))) this.driftValid = false;
        }

        if (this.drift == null)
        {
            EditorGUILayout.HelpBox($"대조 못 함 — {this.driftError}", MessageType.Warning);
            return;
        }
        if (this.drift.Count == 0)
        {
            EditorGUILayout.HelpBox($"{t_label} 표와 카드 에셋의 값이 모두 같다.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            $"이 빌드에 실리는 값은 표가 아니라 카드 에셋이다 — 둘이 다르다.\n" +
            string.Join("\n", this.drift) + "\n\n" +
            $"표가 맞다면 ②에서 '{t_label} 표 → 카드 적용', 에셋이 맞다면 '카드 → {t_label} 표 내보내기'.",
            MessageType.Warning);
    }

    // ── 공통 ───────────────────────────────────────────────────────────────

    void DrawReport()
    {
        if (string.IsNullOrEmpty(this.lastReport)) return;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(this.lastReport, GUILayout.MinHeight(90));
    }

    /// <summary>표 조작 결과를 보고하고 검증을 다시 돌린다 — 표를 갈면 검증 결과도 같이 낡기 때문.</summary>
    void Finish(string _report, string _error)
    {
        if (_error != null)
        {
            EditorUtility.DisplayDialog("실패", _error, "확인");
            return;
        }

        this.lastReport = _report;
        Revalidate();
    }

    static void Header(string _title)
    {
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField(_title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    }
}
