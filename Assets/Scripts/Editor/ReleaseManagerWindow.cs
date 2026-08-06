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
/// **빌드의 실행 모드는 에디터 모드와 무관하다.** <see cref="ContentProfileConfig"/>는 빌드에서
/// EditorPrefs를 읽지 않고 `DEVELOPMENT_BUILD ? Test : Live`로 결정한다. 그래서 이 창은
/// "개발 빌드 = 테스트 프로필 / 릴리즈 빌드 = 라이브 프로필"을 명시하고, 에셋에 실린 표가
/// 그 프로필과 다르면 빌드를 막는다.
/// </summary>
public class ReleaseManagerWindow : EditorWindow
{
    const string PREF_BUILD_DIR = "Release.BuildDir";
    const string PREF_DEV_BUILD = "Release.DevelopmentBuild";

    string buildDir;
    bool   developmentBuild;

    Vector2 scroll;
    string  lastReport;
    List<string> issues;
    bool showColumnHelp;

    [MenuItem("Tools/Card Battle/릴리즈 관리")]
    static void Open() => GetWindow<ReleaseManagerWindow>("릴리즈 관리").minSize = new Vector2(520, 560);

    void OnEnable()
    {
        this.buildDir         = EditorPrefs.GetString(PREF_BUILD_DIR, "Builds");
        this.developmentBuild = EditorPrefs.GetBool(PREF_DEV_BUILD, true);
        Revalidate();
    }

    void OnDisable()
    {
        EditorPrefs.SetString(PREF_BUILD_DIR, this.buildDir);
        EditorPrefs.SetBool(PREF_DEV_BUILD, this.developmentBuild);
    }

    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 에셋·빌드를 다룰 수 없다. 플레이를 멈추고 다시 열 것.", MessageType.Warning);
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
        EditorGUILayout.LabelField("적용 대상 표", ContentRunModeEditor.ActiveTablePath);
    }

    void SwitchMode(EContentRunMode _target)
    {
        string t_label = ContentRunModeEditor.Label(_target);

        if (!File.Exists(ContentRunModeEditor.TablePathOf(_target)))
        {
            EditorUtility.DisplayDialog("전환 불가",
                $"{t_label} 표 파일이 없다:\n{ContentRunModeEditor.TablePathOf(_target)}\n\n" +
                "②에서 그 경로로 먼저 내보내거나 경로를 고칠 것.", "확인");
            return;
        }

        if (!EditorUtility.DisplayDialog($"{t_label}로 전환",
                $"실행 모드를 {t_label}로 바꾸고 {t_label} 표를 카드 에셋에 덮어쓴다.\n\n" +
                "두 표는 같은 CardData를 공유하므로 지금 에셋에 있는 수치는 사라진다.\n" +
                "보존하려면 취소하고 ②에서 현재 모드 표로 먼저 내보낼 것.", "전환", "취소"))
            return;

        string t_report = ContentRunModeEditor.SwitchTo(_target, out string t_error);
        Finish(t_report, t_error);
    }

    // ── ② 카드 표 ──────────────────────────────────────────────────────────

    void DrawTableSection()
    {
        EContentRunMode t_mode = ContentRunModeEditor.Current;
        string t_label = ContentRunModeEditor.Label(t_mode);

        Header("② 카드 표");

        ContentRunModeEditor.LiveTablePath = PathField("라이브 표", ContentRunModeEditor.LiveTablePath);
        ContentRunModeEditor.TestTablePath = PathField("테스트 표", ContentRunModeEditor.TestTablePath);
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

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button($"카드 → {t_label} 표 내보내기", GUILayout.Height(26)))
                Finish(ContentRunModeEditor.ExportTable(t_mode), null);

            using (new EditorGUI.DisabledScope(!File.Exists(ContentRunModeEditor.ActiveTablePath)))
            {
                if (GUILayout.Button($"{t_label} 표 → 카드 적용", GUILayout.Height(26)))
                {
                    string t_report = ContentRunModeEditor.ApplyTable(t_mode, out string t_error);
                    Finish(t_report, t_error);
                }
            }
        }

        this.showColumnHelp = EditorGUILayout.Foldout(this.showColumnHelp, "열 설명", true);
        if (this.showColumnHelp) EditorGUILayout.HelpBox(CardTableTool.ColumnHelp, MessageType.None);
    }

    static string PathField(string _label, string _value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            string t_next = EditorGUILayout.TextField(_label, _value);
            if (GUILayout.Button("…", GUILayout.Width(26)))
            {
                string t_picked = EditorUtility.OpenFilePanel(_label, "Assets/SO", "csv");
                if (!string.IsNullOrEmpty(t_picked)) t_next = ToProjectRelative(t_picked) ?? t_picked;
            }
            return t_next;
        }
    }

    static string ToProjectRelative(string _absolute)
    {
        string t_abs  = _absolute.Replace('\\', '/');
        string t_root = Application.dataPath.Replace('\\', '/');
        return t_abs.StartsWith(t_root + "/") ? "Assets" + t_abs.Substring(t_root.Length) : null;
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
        if (this.issues.Count == 0)
        {
            EditorGUILayout.HelpBox("통과 — 프로필·레지스트리·라이브 소비 SO 모두 정상.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox($"문제 {this.issues.Count}건 — 빌드가 막힌다.\n· " + string.Join("\n· ", this.issues),
            MessageType.Error);
    }

    void Revalidate() => this.issues = ContentProfileValidator.Collect();

    // ── ④ 빌드 ─────────────────────────────────────────────────────────────

    void DrawBuildSection()
    {
        Header("④ 빌드");

        BuildTarget t_target = EditorUserBuildSettings.activeBuildTarget;
        EditorGUILayout.LabelField("타겟 플랫폼", $"{t_target}  (바꾸려면 File > Build Settings)");

        this.developmentBuild = EditorGUILayout.Toggle("개발 빌드", this.developmentBuild);
        this.buildDir = EditorGUILayout.TextField("출력 폴더", this.buildDir);

        EContentRunMode t_runtimeMode = this.developmentBuild ? EContentRunMode.Test : EContentRunMode.Live;
        EditorGUILayout.LabelField("빌드 실행 모드", $"{ContentRunModeEditor.Label(t_runtimeMode)} " +
            $"({(this.developmentBuild ? "DEVELOPMENT_BUILD" : "릴리즈")} 기준 — 에디터 모드와 무관)");

        int t_scenes = EnabledScenes().Length;
        EditorGUILayout.LabelField("포함 씬", $"{t_scenes}개 (Build Settings 기준)");

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
        string t_path = Path.Combine(this.buildDir, OutputName(_target));

        if (!EditorUtility.DisplayDialog("빌드",
                $"{ContentRunModeEditor.Label(_runtimeMode)} 빌드를 만든다.\n\n" +
                $"플랫폼: {_target}\n출력: {t_path}\n\n시간이 걸린다.", "빌드", "취소"))
            return;

        Directory.CreateDirectory(this.buildDir);

        var t_options = new BuildPlayerOptions
        {
            scenes           = EnabledScenes(),
            locationPathName = t_path,
            target           = _target,
            options          = this.developmentBuild ? BuildOptions.Development : BuildOptions.None,
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

    static string OutputName(BuildTarget _target)
    {
        string t_product = string.IsNullOrEmpty(PlayerSettings.productName) ? "Game" : PlayerSettings.productName;
        switch (_target)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64: return $"{t_product}/{t_product}.exe";
            case BuildTarget.Android:             return $"{t_product}.apk";
            case BuildTarget.StandaloneOSX:       return $"{t_product}.app";
            default:                              return t_product;
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
