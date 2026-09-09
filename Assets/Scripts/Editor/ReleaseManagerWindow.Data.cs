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
    const string DATA_PUBLISH_PREF_KEY = "SpecFirestore.PublishIndex";
    const int CONTENT_NOTICE_BODY_MAX_LENGTH = 200;

    EContentRunMode dataUploadMode;
    List<string> dataTables;
    HashSet<string> dataSelected = new();
    string dataLoadError;
    string dataReport;
    // 업로드가 끝나면 새 콘텐츠 버전을 공개할지. 끄면 표 문서만 올라가고 _index 포인터는 그대로다.
    bool dataPublishIndex = true;
    string dataPublishNoticeTitle = string.Empty;
    string dataPublishNoticeBody = string.Empty;
    Vector2 dataScroll;
    bool dataRulesOpen;
    bool dataRulesKnown;
    bool specCsvOpen;

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

        DrawSpecCsvSection();

        Header("Firestore 데이터 관리");
        DrawRulesState();
        DrawAdminAuthStatus();

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

        EditorGUILayout.Space(6);
        bool t_publish = EditorGUILayout.ToggleLeft(
            "업로드 후 테이블 버전을 올린다 (_index 공개)", this.dataPublishIndex);
        if (t_publish != this.dataPublishIndex)
        {
            this.dataPublishIndex = t_publish;
            EditorPrefs.SetBool(DATA_PUBLISH_PREF_KEY, t_publish);
        }
        if (!this.dataPublishIndex)
            EditorGUILayout.HelpBox(
                "버전을 올리지 않으면 표 문서(blob/current · rows/ · 메타)만 최신이 되고 _index는 옛 버전을 가리킨 채 남는다." +
                "\n그 사이 클라이언트는 옛 콘텐츠를 보지만 서버의 rows 폴백 경로는 새 데이터를 볼 수 있어 판정이 갈릴 수 있다." +
                "\n확인이 끝나면 이 체크를 켜고 다시 실행해 공개하라 — 해시가 같은 표는 건너뛴다.",
                MessageType.Warning);
        else
            EditorGUILayout.HelpBox(
                "세대 체크: 새 카드 ID · 새 키워드 · 새 시너지 · 새 랭크 등급을 추가했다면 " +
                "ContentVersion.MinAppMajor를 올리고 그 세대를 지원하는 앱 빌드를 먼저 배포해야 한다. " +
                "누락하면 구 앱이 새 행을 해석하지 못해 초기화에 실패할 수 있다.",
                MessageType.Info);

        if (this.dataPublishIndex)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("이번 공개 공지 (선택)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "본문이 있을 때만 업데이트 뒤 1회 표시됩니다. 공지 ID는 새 콘텐츠 버전으로 자동 생성됩니다.",
                MessageType.None);
            this.dataPublishNoticeTitle = EditorGUILayout.TextField(
                "공지 제목", this.dataPublishNoticeTitle ?? string.Empty);
            EditorGUILayout.LabelField($"공지 본문 ({(this.dataPublishNoticeBody ?? string.Empty).Length}/{CONTENT_NOTICE_BODY_MAX_LENGTH})");
            this.dataPublishNoticeBody = EditorGUILayout.TextArea(
                this.dataPublishNoticeBody ?? string.Empty, GUILayout.MinHeight(54));
        }

        string t_blocker = DataUploadBlocker(t_hasEnv, t_envError);
        if (!string.IsNullOrEmpty(t_blocker))
            EditorGUILayout.HelpBox(t_blocker, MessageType.Error);

        using (new EditorGUI.DisabledScope(t_blocker != null))
        {
            string t_verb = this.dataPublishIndex ? "업로드 + 공개" : "업로드만";
            if (GUILayout.Button($"{t_envId} 환경으로 {t_verb} ({this.dataSelected.Count}개)", GUILayout.Height(32)))
                RunDataUpload(t_envId, t_modeMismatch, this.dataPublishIndex);
        }

        EditorGUILayout.Space(10);

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

    // ── SpecData ↔ docs CSV ────────────────────────────────────────────────
    // 예전에는 CookApps 메뉴에 항목 4개로 흩어져 있었다. 이름만 봐서는 무엇이 무엇을 덮는지
    // (bytes → CSV 인지 CSV → bytes 인지) 알 수 없어 아무도 손대지 못했다.
    // 여기 모아 방향과 위험을 글로 적는다 — 되돌리기 어려운 쪽(CSV → bytes)은 아래에 따로 뺐다.

    void DrawSpecCsvSection()
    {
        Header("SpecData ↔ docs CSV");

        EditorGUILayout.HelpBox(
            "저장소의 docs/SpecData/{표}_sheet.csv 는 '지금 앱에 실린 SpecData'를 사람이 읽을 수 있게 떠 둔 사본이다.\n" +
            "값의 진실원은 구글 스펙시트 → SpecData.bytes 순서이고, 이 CSV 는 그 결과를 따라 적는 문서다.",
            MessageType.Info);

        this.specCsvOpen = EditorGUILayout.Foldout(this.specCsvOpen, "내보내기 · 되돌려 넣기", true);
        if (!this.specCsvOpen) return;

        EditorGUI.indentLevel++;

        // ① 정방향: bytes → CSV. 문서를 최신으로 맞추는 쪽이라 위험이 낮다.
        EditorGUILayout.LabelField("SpecData → docs CSV (문서 갱신)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "지금 앱에 실린 SpecData 값으로 docs CSV 를 덮어쓴다. 시트를 받은 지 오래됐으면 문서가 과거로 되돌아간다.",
            EditorStyles.wordWrappedMiniLabel);

        bool t_auto = SpecDocsCsvExporter.AutoExport;
        bool t_next = EditorGUILayout.ToggleLeft(
            "시트 적용 직후 자동으로 내보내기", t_auto);
        if (t_next != t_auto) SpecDocsCsvExporter.AutoExport = t_next;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("지금 내보내기", GUILayout.Height(24)))
                SpecDocsCsvExporter.RunExportInteractive(false);

            // CSV 에서 행이 사라지는 건 표가 줄었다는 뜻이라 기본은 막는다. 시트에서 실제로 지운 경우에만 쓴다.
            if (GUILayout.Button("내보내기 (행 삭제 허용)", GUILayout.Height(24)))
                SpecDocsCsvExporter.RunExportInteractive(true);
        }

        EditorGUILayout.Space(6);

        // ② 역방향: CSV → bytes. 진실원을 우회하므로 경고를 먼저 세운다.
        EditorGUILayout.LabelField("docs CSV → SpecData (로컬 실험본)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "시트를 거치지 않고 CSV 를 고쳐 바로 돌려 보고 싶을 때만 쓴다.\n" +
            "여기서 만든 SpecData.bytes 는 로컬 실험본이라, 시트에 반영하지 않으면 다음 '시트 적용 & CS 생성'에서 사라진다.",
            MessageType.Warning);

        if (GUILayout.Button("docs CSV 로 SpecData 덮어쓰기", GUILayout.Height(24))
            && EditorUtility.DisplayDialog(
                "로컬 실험본 만들기",
                "docs/SpecData CSV 내용으로 Assets/Resources/SpecData.bytes 를 다시 쓴다.\n" +
                "진실원(스펙시트)을 우회하는 임시 경로다. 계속할까?",
                "덮어쓰기", "취소"))
        {
            SpecLocalCsvImporter.RunImportInteractive();
        }

        EditorGUI.indentLevel--;
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

        // 런타임은 블롭만 읽는다 — rows/ 미러는 콘솔에서 표를 눈으로 볼 때만 쓴다.
        // 끄면 업로드 쓰기가 표당 2건(메타·블롭)으로 떨어지고, 대신 rows/ 는 그 시점에 멈춘다.
        bool t_mirror = EditorGUILayout.ToggleLeft(
            "rows/ 미러도 함께 쓰기 (끄면 업로드 비용 급감 · 콘솔 열람용 사본은 낡는다)",
            SpecFirestoreUploader.MirrorRows);
        if (t_mirror != SpecFirestoreUploader.MirrorRows) SpecFirestoreUploader.MirrorRows = t_mirror;
        if (!t_mirror)
            EditorGUILayout.HelpBox(
                "rows/ 미러가 꺼져 있다. 게임은 블롭만 보므로 영향이 없지만, Firestore 콘솔의 rows/ 는 " +
                "마지막으로 미러한 revision에 멈춘다. 서버 폴백도 낡은 미러는 읽지 않고 실패한다.",
                MessageType.Info);

        EditorGUILayout.Space(4f);

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

        this.dataPublishIndex = EditorPrefs.GetBool(DATA_PUBLISH_PREF_KEY, true);
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
        if (!ContentVersionConsistency.TryValidate(out string t_versionError)) return t_versionError;
        if (!SpecAdminAuth.IsSignedIn) return "관리자 로그인이 필요하다.";
        if (!SpecAdminAuth.HasAdminClaim) return "로그인한 계정에 admin 클레임이 없어 스펙을 쓸 수 없다.";
        if (!string.IsNullOrEmpty(this.dataLoadError)) return "표 목록을 먼저 정상적으로 읽어야 한다.";
        if (this.dataSelected.Count == 0) return "업로드할 표를 하나 이상 선택해야 한다.";
        if (this.issues == null) return "콘텐츠 검증을 먼저 실행해야 한다.";
        if (this.issues.Count > 0) return $"콘텐츠 검증 문제 {this.issues.Count}건을 먼저 해결해야 한다.";
        if (this.dataPublishIndex)
        {
            string t_title = (this.dataPublishNoticeTitle ?? string.Empty).Trim();
            string t_body = (this.dataPublishNoticeBody ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(t_title) && string.IsNullOrEmpty(t_body))
                return "콘텐츠 공지 제목을 입력했다면 본문도 입력해야 한다.";
            if (t_body.Length > CONTENT_NOTICE_BODY_MAX_LENGTH)
                return $"콘텐츠 공지 본문은 {CONTENT_NOTICE_BODY_MAX_LENGTH}자 이하여야 한다.";
        }
        return null;
    }

    static void AppendUploadResult(
        StringBuilder _report, string _table, string _line, string _error, ref int _done, ref int _failed)
    {
        if (string.IsNullOrEmpty(_error))
        {
            _report.AppendLine($"OK   {_line}");
            _done++;
            return;
        }

        _report.AppendLine($"FAIL {_table}: {_error}");
        Debug.LogError($"[SpecFirestore] {_table} upload failed: {_error}");
        _failed++;
    }

    void RunDataUpload(string _envId, bool _modeMismatch, bool _publish)
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
                "표별 메타·행 갱신·사라진 행 삭제가 각각 하나의 원자 커밋으로 반영된다." +
                (_publish
                    ? "\n\n표가 모두 성공하면 새 테이블 버전을 공개한다(_index 갱신)." +
                      "\n\n세대 확인: 새 카드 ID · 키워드 · 시너지 · 랭크 등급을 추가했다면 " +
                      "ContentVersion.MinAppMajor를 올리고 새 앱을 먼저 배포했는지 확인할 것." +
                      (string.IsNullOrWhiteSpace(this.dataPublishNoticeBody)
                          ? "\n\n이번 공개에는 사용자 공지가 없다."
                          : $"\n\n사용자 공지:\n{this.dataPublishNoticeTitle}\n{this.dataPublishNoticeBody}")
                    : "\n\n버전은 올리지 않는다 — _index는 현재 버전을 계속 가리키고 표 문서만 최신이 된다."),
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
                        Debug.LogError($"[SpecFirestore] {t_table} upload failed: {t_error}");
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

        // 선택한 표 중 하나라도 실패하거나 취소됐으면 새 콘텐츠 minor를 공개하지 않는다.
        // 표 문서는 먼저 올라가도 구클라이언트의 레거시 경로만 볼 수 있고, 신클라이언트는 기존 _index를 유지한다.
        if (!_publish && !t_cancelled && t_failed == 0)
            t_report.AppendLine("SKIP publish: 테이블 버전을 올리지 않는 업로드다 — _index는 그대로다.");
        if (_publish && !t_cancelled && t_failed == 0)
        {
            string t_publishLine = SpecFirestoreUploader.PublishIndex(
                _envId, this.dataPublishNoticeTitle, this.dataPublishNoticeBody, out string t_publishError);
            if (string.IsNullOrEmpty(t_publishError))
            {
                t_report.AppendLine($"PUBLISH {t_publishLine}");
                this.dataPublishNoticeTitle = string.Empty;
                this.dataPublishNoticeBody = string.Empty;
            }
            else
            {
                t_report.AppendLine($"FAIL publish: {t_publishError}");
                Debug.LogError($"[SpecFirestore] Content index publish failed: {t_publishError}");
                t_failed++;
            }
        }

        string t_cancelNote = t_cancelled ? " / 사용자 취소" : string.Empty;
        this.dataReport = $"성공 {t_done} / 실패 {t_failed}{t_cancelNote}\n\n{t_report}";
        Debug.Log($"[SpecFirestore] env={_envId}, succeeded={t_done}, failed={t_failed}, cancelled={t_cancelled}");
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
