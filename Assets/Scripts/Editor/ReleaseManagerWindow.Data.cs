using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class ReleaseManagerWindow
{
    const string DATA_SELECTION_PREF_KEY = "SpecFirestore.Selected";
    const string DATA_SELECTION_INITIALIZED_PREF_KEY = "SpecFirestore.Selected.Initialized";

    EContentRunMode dataUploadMode;
    List<string> dataTables;
    HashSet<string> dataSelected = new();
    string dataLoadError;
    string dataReport;
    Vector2 dataScroll;
    bool dataRulesOpen;
    bool dataRulesKnown;

    void EnableDataTab()
    {
        this.dataUploadMode = ContentRunModeEditor.Current;
        ReloadDataTables();
        RefreshRulesState();
    }

    void DisableDataTab()
    {
        EditorPrefs.SetString(DATA_SELECTION_PREF_KEY, string.Join("|", this.dataSelected));
        EditorPrefs.SetBool(DATA_SELECTION_INITIALIZED_PREF_KEY, true);
    }

    void DrawDataTab()
    {
        this.dataScroll = EditorGUILayout.BeginScrollView(this.dataScroll);

        Header("Firestore 데이터 관리");
        DrawRulesState();

        EditorGUILayout.HelpBox(
            "SpecData 표를 Firestore의 환경별 문서로 업로드한다. 이 탭은 앞으로 데이터 배포·검수 기능의 단일 진입점으로 사용한다.",
            MessageType.Info);

        this.dataUploadMode = (EContentRunMode)EditorGUILayout.EnumPopup("업로드 대상 환경", this.dataUploadMode);
        EContentRunMode t_currentMode = ContentRunModeEditor.Current;
        EditorGUILayout.LabelField("빌드 실행 모드", ContentRunModeEditor.Label(t_currentMode));

        bool t_hasEnv = TryGetDataEnvId(this.dataUploadMode, out string t_envId, out string t_envError);
        EditorGUILayout.LabelField("문서 경로", t_hasEnv ? $"{FirebaseRootPath.Environment(t_envId)}/specs/<표 이름>" : "(환경 프로필 없음)");

        bool t_modeMismatch = this.dataUploadMode != t_currentMode;
        if (t_modeMismatch)
        {
            EditorGUILayout.HelpBox(
                $"빌드 실행 모드는 {ContentRunModeEditor.Label(t_currentMode)}이지만 업로드 대상은 " +
                $"{ContentRunModeEditor.Label(this.dataUploadMode)}다. 업로드 전에 한 번 더 확인한다.",
                MessageType.Warning);
        }

        if (!t_hasEnv)
            EditorGUILayout.HelpBox(t_envError, MessageType.Error);

        EditorGUILayout.Space();
        DrawDataTableSelection();

        string t_blocker = DataUploadBlocker(t_hasEnv, t_envError);
        if (!string.IsNullOrEmpty(t_blocker))
            EditorGUILayout.HelpBox(t_blocker, MessageType.Error);

        using (new EditorGUI.DisabledScope(t_blocker != null))
        {
            if (GUILayout.Button($"{t_envId} 환경으로 업로드 ({this.dataSelected.Count}개)", GUILayout.Height(32)))
                RunDataUpload(t_envId, t_modeMismatch);
        }

        if (!string.IsNullOrEmpty(this.dataReport))
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("데이터 작업 결과", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(this.dataReport, GUILayout.MinHeight(90));
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawRulesState()
    {
        if (!this.dataRulesKnown)
        {
            EditorGUILayout.HelpBox("firestore.rules 상태를 확인하지 못했다.", MessageType.Warning);
            return;
        }

        if (this.dataRulesOpen)
        {
            EditorGUILayout.HelpBox(
                "로컬 firestore.rules 문자열 검사에서 전체 읽기·쓰기 허용을 감지했다. 임시 개발 규칙이며 배포 전 운영 규칙으로 복구해야 한다.",
                MessageType.Error);
        }
        else
        {
            EditorGUILayout.HelpBox("로컬 firestore.rules에서 전면 개방 규칙을 찾지 못했다.", MessageType.Info);
        }
    }

    void DrawDataTableSelection()
    {
        if (!string.IsNullOrEmpty(this.dataLoadError))
        {
            EditorGUILayout.HelpBox(this.dataLoadError, MessageType.Error);
            if (GUILayout.Button("다시 읽기")) ReloadDataTables();
            return;
        }

        EditorGUILayout.LabelField($"업로드 표 {this.dataTables.Count}개", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("전체 선택"))
                foreach (string t_table in this.dataTables) this.dataSelected.Add(t_table);
            if (GUILayout.Button("전체 해제")) this.dataSelected.Clear();
            if (GUILayout.Button("다시 읽기"))
            {
                ReloadDataTables();
                RefreshRulesState();
                Revalidate();
            }
        }

        foreach (string t_table in this.dataTables)
        {
            bool t_was = this.dataSelected.Contains(t_table);
            bool t_now = EditorGUILayout.ToggleLeft(t_table, t_was);
            if (t_now == t_was) continue;

            if (t_now) this.dataSelected.Add(t_table);
            else       this.dataSelected.Remove(t_table);
        }
    }

    void ReloadDataTables()
    {
        this.dataTables = SpecFirestoreUploader.ListTables(out this.dataLoadError);
        this.dataReport = null;

        bool t_initialized = EditorPrefs.GetBool(DATA_SELECTION_INITIALIZED_PREF_KEY, false);
        string t_saved = EditorPrefs.GetString(DATA_SELECTION_PREF_KEY, string.Empty);
        this.dataSelected = new HashSet<string>(t_saved.Split('|'));
        this.dataSelected.Remove(string.Empty);
        this.dataSelected.IntersectWith(this.dataTables);

        if (!t_initialized)
            foreach (string t_table in this.dataTables) this.dataSelected.Add(t_table);
    }

    void RefreshRulesState()
    {
        const string t_rulesPath = "firestore.rules";
        this.dataRulesKnown = File.Exists(t_rulesPath);
        this.dataRulesOpen = false;
        if (!this.dataRulesKnown) return;

        string t_rules = File.ReadAllText(t_rulesPath);
        this.dataRulesOpen = t_rules.IndexOf("allow read, write: if true", StringComparison.Ordinal) >= 0;
    }

    string DataUploadBlocker(bool _hasEnv, string _envError)
    {
        if (!_hasEnv) return _envError;
        if (!string.IsNullOrEmpty(this.dataLoadError)) return "표 목록을 먼저 정상적으로 읽어야 한다.";
        if (this.dataSelected.Count == 0) return "업로드할 표를 하나 이상 선택해야 한다.";
        if (this.issues == null) return "콘텐츠 검증을 먼저 실행해야 한다.";
        if (this.issues.Count > 0) return $"콘텐츠 검증 문제 {this.issues.Count}건을 먼저 해결해야 한다.";
        return null;
    }

    void RunDataUpload(string _envId, bool _modeMismatch)
    {
        Revalidate();
        string t_blocker = DataUploadBlocker(true, null);
        if (t_blocker != null)
        {
            EditorUtility.DisplayDialog("업로드 차단", t_blocker, "확인");
            return;
        }

        if (_modeMismatch && !EditorUtility.DisplayDialog(
                "실행 모드와 다른 환경",
                $"현재 빌드 실행 모드와 다른 '{_envId}' 환경에 업로드한다. 계속할까?",
                "계속", "취소"))
            return;

        if (!EditorUtility.DisplayDialog(
                "스펙시트 업로드",
                $"{FirebaseRootPath.Environment(_envId)}/specs/ 아래 표 {this.dataSelected.Count}개를 배포한다.\n" +
                "표별 메타·행 갱신·사라진 행 삭제가 각각 하나의 원자 커밋으로 반영된다.",
                "업로드", "취소"))
            return;

        EditorPrefs.SetString(DATA_SELECTION_PREF_KEY, string.Join("|", this.dataSelected));
        EditorPrefs.SetBool(DATA_SELECTION_INITIALIZED_PREF_KEY, true);

        var t_report = new StringBuilder();
        var t_ordered = new List<string>(this.dataSelected);
        t_ordered.Sort(StringComparer.Ordinal);
        int t_done = 0;
        int t_failed = 0;
        bool t_cancelled = false;

        try
        {
            for (int i = 0; i < t_ordered.Count; i++)
            {
                string t_table = t_ordered[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "스펙시트 업로드", $"{t_table} …", (float)i / t_ordered.Count))
                {
                    t_cancelled = true;
                    break;
                }

                try
                {
                    string t_line = SpecFirestoreUploader.Upload(_envId, t_table, out string t_error);
                    if (string.IsNullOrEmpty(t_error))
                    {
                        t_report.AppendLine($"OK   {t_line}");
                        t_done++;
                    }
                    else
                    {
                        t_report.AppendLine($"FAIL {t_table}: {t_error}");
                        Debug.LogError($"[SpecFirestore] {t_table} 업로드 실패: {t_error}");
                        t_failed++;
                    }
                }
                catch (Exception t_exception)
                {
                    t_report.AppendLine($"FAIL {t_table}: {t_exception.Message}");
                    Debug.LogException(t_exception);
                    t_failed++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string t_cancelNote = t_cancelled ? " / 사용자 취소" : string.Empty;
        this.dataReport = $"성공 {t_done} / 실패 {t_failed}{t_cancelNote}\n\n{t_report}";
        Debug.Log($"[SpecFirestore] env={_envId}, 성공={t_done}, 실패={t_failed}, 취소={t_cancelled}");
    }

    static bool TryGetDataEnvId(EContentRunMode _mode, out string _envId, out string _error)
    {
        ContentProfileConfig t_profile = ContentRunModeEditor.ProfileOf(_mode);
        _envId = t_profile != null ? t_profile.CloudEnvId : null;
        _error = null;

        if (!string.IsNullOrWhiteSpace(_envId)) return true;

        _error = $"{ContentRunModeEditor.Label(_mode)} ContentProfileConfig 또는 CloudEnvId가 없다.";
        return false;
    }
}
