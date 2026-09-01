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
    string adminEmail;
    string adminPassword = string.Empty;
    string adminAuthError;
    bool adminOAuthOpen;
    bool adminPasswordOpen;
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
        DrawAdminAuth();

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

        EditorGUILayout.Space(10);
        DrawCompositionUpload(t_hasEnv, t_envId, t_envError, t_modeMismatch);

        if (!string.IsNullOrEmpty(this.dataReport))
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("데이터 작업 결과", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(this.dataReport, GUILayout.MinHeight(90));
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>SO 저작에서 만드는 구성 표 업로드 칸. 스펙시트 표가 아니라 위 목록에는 뜨지 않는다.</summary>
    void DrawCompositionUpload(bool _hasEnv, string _envId, string _envError, bool _modeMismatch)
    {
        EditorGUILayout.LabelField("구성 표 (SO 저작 → 스펙)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"{SpecFirestoreUploader.ALBUM_ENTRY_TABLE}(도감 테마·페이지·칸)와 " +
            $"{SpecFirestoreUploader.TOURNAMENT_CHAPTER_TABLE}(챕터·정점)를 CardAlbumConfig·TournamentConfig 저작에서 만들어 올린다. " +
            "서버가 페이지 완성·챕터 완주를 판정할 근거 표다. " +
            "위 검증 게이트는 콘텐츠 프로필·카드 표만 보므로, 도감·토너먼트 저작 결함은 업로드 시점에 따로 검사해 중단하거나 경고한다.",
            MessageType.Info);

        string t_blocker = CompositionUploadBlocker(_hasEnv, _envError);
        if (!string.IsNullOrEmpty(t_blocker))
            EditorGUILayout.HelpBox(t_blocker, MessageType.Error);

        using (new EditorGUI.DisabledScope(t_blocker != null))
        {
            if (GUILayout.Button($"{_envId} 환경으로 구성 표 업로드 (2개)", GUILayout.Height(28)))
                RunCompositionUpload(_envId, _modeMismatch);
        }
    }

    /// <summary>관리자 로그인 칸. 운영 규칙이 스펙 쓰기를 admin 클레임에만 허용하므로
    /// 업로드 전에 여기서 로그인해야 한다. 비밀번호는 저장하지 않고, 토큰은 유니티 세션 동안만 산다.</summary>
    void DrawAdminAuth()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("관리자 로그인", EditorStyles.boldLabel);

        if (SpecAdminAuth.IsSignedIn)
        {
            if (SpecAdminAuth.HasAdminClaim)
            {
                EditorGUILayout.HelpBox($"{SpecAdminAuth.SignedInEmail} (admin) 로 로그인됨.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"{SpecAdminAuth.SignedInEmail} 로 로그인했지만 admin 클레임이 없다. " +
                    "이 계정으로는 스펙을 쓸 수 없다 — functions/scripts/grant-admin.js 로 클레임을 부여한 뒤 " +
                    "다시 로그인할 것(토큰에 클레임이 박히므로 재로그인이 필요하다).",
                    MessageType.Error);
            }

            if (GUILayout.Button("로그아웃"))
            {
                SpecAdminAuth.SignOut();
                this.adminPassword = string.Empty;
                this.adminAuthError = null;
            }
            return;
        }

        using (new EditorGUI.DisabledScope(!GoogleOAuthSignIn.IsConfigured))
        {
            if (GUILayout.Button("구글 계정으로 로그인", GUILayout.Height(28)))
            {
                bool t_ok = SpecAdminAuth.TrySignInWithGoogle(out string t_error);
                this.adminAuthError = t_ok ? null : t_error;
                if (t_ok && !SpecAdminAuth.HasAdminClaim)
                    this.adminAuthError = "로그인은 됐지만 admin 클레임이 없다.";
            }
        }

        this.adminOAuthOpen = EditorGUILayout.Foldout(this.adminOAuthOpen, "구글 OAuth 클라이언트 설정", true);
        if (this.adminOAuthOpen)
        {
            EditorGUILayout.HelpBox(
                "Google Cloud 콘솔 > API 및 서비스 > 사용자 인증 정보에서 '데스크톱 앱' 유형 OAuth 클라이언트를 " +
                "만들고 그 값을 넣는다. 리다이렉트는 루프백을 자동으로 쓰므로 따로 등록할 필요가 없다. " +
                "이 값은 EditorPrefs에만 저장되고 저장소에는 들어가지 않는다.",
                MessageType.Info);

            string t_clientId = EditorGUILayout.TextField("클라이언트 ID", GoogleOAuthSignIn.ClientId);
            if (t_clientId != GoogleOAuthSignIn.ClientId) GoogleOAuthSignIn.ClientId = t_clientId;

            string t_clientSecret = EditorGUILayout.PasswordField("클라이언트 보안 비밀", GoogleOAuthSignIn.ClientSecret);
            if (t_clientSecret != GoogleOAuthSignIn.ClientSecret) GoogleOAuthSignIn.ClientSecret = t_clientSecret;
        }

        EditorGUILayout.Space();
        this.adminPasswordOpen = EditorGUILayout.Foldout(this.adminPasswordOpen, "이메일·비밀번호로 로그인", true);
        if (this.adminPasswordOpen)
        {
            this.adminEmail ??= SpecAdminAuth.LastEmail;
            this.adminEmail = EditorGUILayout.TextField("이메일", this.adminEmail);
            this.adminPassword = EditorGUILayout.PasswordField("비밀번호", this.adminPassword);

            if (GUILayout.Button("로그인"))
            {
                bool t_ok = SpecAdminAuth.TrySignIn(this.adminEmail, this.adminPassword, out string t_error);
                this.adminAuthError = t_ok ? null : t_error;
                // 성공하든 실패하든 비밀번호는 메모리에 남기지 않는다.
                this.adminPassword = string.Empty;
                if (t_ok && !SpecAdminAuth.HasAdminClaim)
                    this.adminAuthError = "로그인은 됐지만 admin 클레임이 없다.";
            }
        }

        if (!string.IsNullOrEmpty(this.adminAuthError))
            EditorGUILayout.HelpBox(this.adminAuthError, MessageType.Error);
        else if (!GoogleOAuthSignIn.IsConfigured)
            EditorGUILayout.HelpBox("구글 OAuth 클라이언트 ID를 넣으면 구글 로그인을 쓸 수 있다.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("스펙 업로드에는 admin 클레임을 가진 계정 로그인이 필요하다.", MessageType.Warning);
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
        if (!SpecAdminAuth.IsSignedIn) return "관리자 로그인이 필요하다.";
        if (!SpecAdminAuth.HasAdminClaim) return "로그인한 계정에 admin 클레임이 없어 스펙을 쓸 수 없다.";
        if (!string.IsNullOrEmpty(this.dataLoadError)) return "표 목록을 먼저 정상적으로 읽어야 한다.";
        if (this.dataSelected.Count == 0) return "업로드할 표를 하나 이상 선택해야 한다.";
        if (this.issues == null) return "콘텐츠 검증을 먼저 실행해야 한다.";
        if (this.issues.Count > 0) return $"콘텐츠 검증 문제 {this.issues.Count}건을 먼저 해결해야 한다.";
        return null;
    }

    // 스펙시트 선택·적재와 무관한 경로라 표 선택과 SpecData 적재 실패는 여기서 보지 않는다
    string CompositionUploadBlocker(bool _hasEnv, string _envError)
    {
        if (!_hasEnv) return _envError;
        if (!SpecAdminAuth.IsSignedIn) return "관리자 로그인이 필요하다.";
        if (!SpecAdminAuth.HasAdminClaim) return "로그인한 계정에 admin 클레임이 없어 스펙을 쓸 수 없다.";
        if (this.issues == null) return "콘텐츠 프로필·카드 표 검증을 먼저 실행해야 한다.";
        if (this.issues.Count > 0) return $"콘텐츠 프로필·카드 표 검증 문제 {this.issues.Count}건을 먼저 해결해야 한다.";
        return null;
    }

    void RunCompositionUpload(string _envId, bool _modeMismatch)
    {
        Revalidate();
        string t_blocker = CompositionUploadBlocker(true, null);
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
                "구성 표 업로드",
                $"{FirebaseRootPath.Environment(_envId)}/specs/ 아래 " +
                $"{SpecFirestoreUploader.ALBUM_ENTRY_TABLE}·{SpecFirestoreUploader.TOURNAMENT_CHAPTER_TABLE} 표를 배포한다.\n" +
                "SO 저작을 그대로 옮기며, 표별 메타·행 갱신·사라진 행 삭제가 각각 하나의 원자 커밋으로 반영된다.",
                "업로드", "취소"))
            return;

        var t_report = new StringBuilder();
        int t_done = 0;
        int t_failed = 0;

        try
        {
            EditorUtility.DisplayProgressBar("구성 표 업로드", $"{SpecFirestoreUploader.ALBUM_ENTRY_TABLE} …", 0f);
            string t_albumLine = SpecFirestoreUploader.UploadAlbumEntries(_envId, out string t_albumError);
            AppendUploadResult(t_report, SpecFirestoreUploader.ALBUM_ENTRY_TABLE, t_albumLine, t_albumError,
                               ref t_done, ref t_failed);

            EditorUtility.DisplayProgressBar("구성 표 업로드", $"{SpecFirestoreUploader.TOURNAMENT_CHAPTER_TABLE} …", 0.5f);
            string t_chapterLine = SpecFirestoreUploader.UploadTournamentChapters(_envId, out string t_chapterError);
            AppendUploadResult(t_report, SpecFirestoreUploader.TOURNAMENT_CHAPTER_TABLE, t_chapterLine, t_chapterError,
                               ref t_done, ref t_failed);
        }
        catch (Exception t_exception)
        {
            t_report.AppendLine($"FAIL {t_exception.Message}");
            Debug.LogException(t_exception);
            t_failed++;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        this.dataReport = $"성공 {t_done} / 실패 {t_failed}\n\n{t_report}";
        Debug.Log($"[SpecFirestore] 구성 표 env={_envId}, 성공={t_done}, 실패={t_failed}");
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
        Debug.LogError($"[SpecFirestore] {_table} 업로드 실패: {_error}");
        _failed++;
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
