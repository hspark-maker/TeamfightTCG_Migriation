using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 릴리즈 관리 창. Tools > Card Battle > 릴리즈 관리.
///
/// 모드 · 카드 표 · 검증 · 빌드를 한 화면에 묶는다. 셋이 따로 놀면 조용히 어긋나기 때문이다:
/// 모드만 바꾸고 표를 안 실으면 수치가 틀리고, 표만 갈고 빌드하면 런타임 프로필과 데이터가 갈린다.
///
/// 현재 에디터 프로필이 곧 빌드 실행 모드다. 테스트 프로필은 개발 빌드로, 라이브 프로필은
/// 릴리즈 빌드로 만들며, 에셋에 실린 표가 그 프로필과 다르면 빌드를 막는다.
/// </summary>
public partial class ReleaseManagerWindow : EditorWindow
{
    const string PREF_BUILD_DIR = "Release.BuildDir";
    const string PREF_TAB       = "Release.Tab";

    enum Tab
    {
        Release,
        Data,
    }

    string buildDir;
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
        this.buildDir = EditorPrefs.GetString(PREF_BUILD_DIR, "Builds");
        this.selectedTab = (Tab)Mathf.Clamp(EditorPrefs.GetInt(PREF_TAB, 0), 0, 1);
        Revalidate();
        EnableDataTab();
    }

    void OnDisable()
    {
        EditorPrefs.SetString(PREF_BUILD_DIR, this.buildDir);
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
            new[] { "릴리즈", "데이터 · Firestore" },
            GUILayout.Height(26));

        if (this.selectedTab == Tab.Data)
        {
            DrawDataTab();
            return;
        }

        this.scroll = EditorGUILayout.BeginScrollView(this.scroll);
        DrawModeSection();
        DrawTableSection();
        DrawValidationSection();
        DrawBuildSection();
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
        EditorGUILayout.LabelField("적용 대상 시트", CardSpecImporter.SheetNameOf(t_mode));
    }

    void SwitchMode(EContentRunMode _target)
    {
        string t_label = ContentRunModeEditor.Label(_target);

        // 시트가 소스라 미리 볼 파일이 없다 — 못 읽는 경우는 SwitchTo가 사유를 담아 돌려준다.
        if (!EditorUtility.DisplayDialog($"{t_label}로 전환",
                $"실행 모드를 {t_label}로 바꾸고 {CardSpecImporter.SheetNameOf(_target)} 시트를 카드 에셋에 덮어쓴다.\n\n" +
                "두 시트는 같은 CardData를 공유하므로 지금 에셋에 있는 수치는 사라진다.", "전환", "취소"))
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

        ContentRunModeEditor.CardRoot = EditorGUILayout.TextField("카드 에셋 위치", ContentRunModeEditor.CardRoot);

        EditorGUILayout.Space(4);
        if (ContentRunModeEditor.IsDesynced)
        {
            EditorGUILayout.HelpBox(
                $"에셋에 실린 표는 '{ContentRunModeEditor.Label(ContentRunModeEditor.Applied)}'인데 실행 모드는 " +
                $"'{t_label}'다 — 수치가 어긋나 있다.", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.LabelField("에셋에 실린 표", ContentRunModeEditor.Label(ContentRunModeEditor.Applied));
        }

        // 값이 들어오는 문은 스펙시트 하나뿐이다. 값 변화의 기록은 카드 .asset 자체가 git에 추적되므로
        // 별도 CSV 스냅샷을 두지 않는다 — 두면 어느 쪽이 진짜인지가 다시 흐려진다.
        if (GUILayout.Button($"스펙시트({CardSpecImporter.SheetNameOf(t_mode)}) → 카드 적용", GUILayout.Height(26)))
        {
            string t_report = CardSpecImporter.ImportToAssets(t_mode, out string t_error);
            Finish(t_report, t_error);
        }

        EditorGUILayout.HelpBox(
            "카드 값의 진실원은 구글 스펙시트다. 시트를 고쳤으면 CookApps > SpecData 창에서 " +
            "'시트 적용 & CS 생성'을 먼저 돌린 뒤 위 버튼을 누른다.\n" +
            "라이브 = Card 시트 / 테스트 = Card_Test 시트. 같은 카드는 두 시트에서 **같은 id**여야 한다 " +
            "— 매칭 키가 id라 번호가 갈리면 새 에셋으로 복제된다.\n" +
            "시트에서 이름을 바꾸면 에셋 이름도 따라 바뀐다(참조는 guid라 유지된다).",
            MessageType.Info);

        this.showColumnHelp = EditorGUILayout.Foldout(this.showColumnHelp, "열 설명", true);
        if (this.showColumnHelp) EditorGUILayout.HelpBox(CardTableTool.ColumnHelp, MessageType.None);
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

        this.drift      = ContentRunModeEditor.DiffTable(_mode, out this.driftError);
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
            CardTableTool.DriftSummary(this.drift) + "\n\n" +
            $"표가 맞다면 ②에서 '{t_label} 표 → 카드 적용', 에셋이 맞다면 '카드 → {t_label} 표 내보내기'.",
            MessageType.Warning);
    }

    // ── ④ 빌드 ─────────────────────────────────────────────────────────────

    void DrawBuildSection()
    {
        Header("④ 빌드");

        BuildTarget t_target = EditorUserBuildSettings.activeBuildTarget;
        EditorGUILayout.LabelField("타겟 플랫폼", $"{t_target}  (바꾸려면 File > Build Settings)");

        this.buildDir = EditorGUILayout.TextField("출력 폴더", this.buildDir);

        EContentRunMode t_runtimeMode = ContentRunModeEditor.Current;
        EditorGUILayout.LabelField("빌드 실행 모드", ContentRunModeEditor.Label(t_runtimeMode));

        int t_scenes = EnabledScenes().Length;
        EditorGUILayout.LabelField("포함 씬", $"{t_scenes}개 (Build Settings 기준)");

        EditorGUILayout.Space(4);
        EnsureDrift(t_runtimeMode);
        DrawDriftBox(t_runtimeMode);
        EditorGUILayout.Space(4);

        string t_block = BuildBlocker(t_runtimeMode, t_scenes);
        if (t_block != null) EditorGUILayout.HelpBox(t_block, MessageType.Error);

        using (new EditorGUI.DisabledScope(t_block != null))
        {
            if (GUILayout.Button($"{ContentRunModeEditor.Label(t_runtimeMode)} 빌드 실행", GUILayout.Height(34)))
                RunBuild(t_target, t_runtimeMode);
        }
    }

    /// <summary>빌드를 막을 사유(없으면 null). 데이터가 런타임 프로필과 어긋난 채 빌드가 나가는 것을 막는 게 목적이다.</summary>
    string BuildBlocker(EContentRunMode _runtimeMode, int _sceneCount)
    {
        if (_sceneCount == 0) return "Build Settings에 활성 씬이 없다.";
        if (string.IsNullOrWhiteSpace(this.buildDir)) return "출력 폴더를 입력할 것.";
        if (this.issues == null) return "검증을 먼저 돌릴 것(③ 다시 검사).";
        if (this.issues.Count > 0) return $"검증 문제 {this.issues.Count}건을 먼저 해결할 것.";

        if (ContentRunModeEditor.Applied != _runtimeMode)
            return $"이 빌드는 {ContentRunModeEditor.Label(_runtimeMode)} 프로필로 돌지만 " +
                   $"카드 에셋에는 {ContentRunModeEditor.Label(ContentRunModeEditor.Applied)} 표가 실려 있다.\n" +
                   $"①에서 {ContentRunModeEditor.Label(_runtimeMode)}로 전환하거나 ②에서 그 표를 적용할 것.";

        return null;
    }

    void RunBuild(BuildTarget _target, EContentRunMode _runtimeMode)
    {
        string t_path = Path.Combine(this.buildDir, OutputName(_target, _runtimeMode));

        // 대조 경고는 빌드를 막지 않는다(의도적으로 에셋만 손댄 상태로 뽑는 일이 있다) —
        // 대신 되돌리기 어려운 시점에 한 번 더 눈에 띄게 한다.
        string t_driftNote = this.drift != null && this.drift.Count > 0
            ? $"\n\n⚠ 표와 다른 값 {this.drift.Count}건 — 표가 아니라 에셋 값으로 나간다."
            : "";

        if (!EditorUtility.DisplayDialog("빌드",
                $"{ContentRunModeEditor.Label(_runtimeMode)} 빌드를 만든다.\n\n" +
                $"플랫폼: {_target}\n출력: {t_path}{t_driftNote}\n\n시간이 걸린다.", "빌드", "취소"))
            return;

        Directory.CreateDirectory(this.buildDir);

        var t_options = new BuildPlayerOptions
        {
            scenes           = EnabledScenes(),
            locationPathName = t_path,
            target           = _target,
            options          = _runtimeMode == EContentRunMode.Test ? BuildOptions.Development : BuildOptions.None,
        };

        BuildReport t_report = BuildPipeline.BuildPlayer(t_options);
        BuildSummary t_summary = t_report.summary;

        string t_text = $"[빌드 {t_summary.result}] {ContentRunModeEditor.Label(_runtimeMode)}\n" +
                        $"{t_summary.outputPath}\n" +
                        $"{t_summary.totalSize / (1024 * 1024)}MB / {t_summary.totalTime}\n" +
                        $"에러 {t_summary.totalErrors} / 경고 {t_summary.totalWarnings}";

        this.lastReport = t_text;
        if (t_summary.result == BuildResult.Succeeded) Debug.Log(t_text);
        else                                           Debug.LogError(t_text);

        Revalidate();
    }

    static string[] EnabledScenes()
    {
        var t_list = new List<string>();
        foreach (EditorBuildSettingsScene t_scene in EditorBuildSettings.scenes)
            if (t_scene.enabled) t_list.Add(t_scene.path);
        return t_list.ToArray();
    }

    static string OutputName(BuildTarget _target, EContentRunMode _runtimeMode)
    {
        string t_product = string.IsNullOrEmpty(PlayerSettings.productName) ? "Game" : PlayerSettings.productName;
        string t_suffix = _runtimeMode == EContentRunMode.Test ? "_test" : "_live";
        string t_name = t_product + t_suffix;
        switch (_target)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64: return $"{t_name}/{t_name}.exe";
            case BuildTarget.Android:             return $"{t_name}.apk";
            case BuildTarget.StandaloneOSX:       return $"{t_name}.app";
            default:                              return t_name;
        }
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
