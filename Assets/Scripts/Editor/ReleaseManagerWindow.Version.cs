using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class ReleaseManagerWindow
{
    EContentRunMode versionEnvMode;
    SpecFirestoreUploader.ContentIndexState versionIndex;
    string versionError;
    string versionReport;
    int versionSelection;
    bool versionManualEntry;
    string versionManualVersion;
    bool versionAllowUnsupportedMajor;

    void EnableVersionManagement()
    {
        this.versionEnvMode = ContentRunModeEditor.Current;
        this.versionIndex = null;
        this.versionError = null;
        this.versionReport = null;
        this.versionSelection = 0;
        this.versionManualEntry = false;
        this.versionManualVersion = string.Empty;
        this.versionAllowUnsupportedMajor = false;
    }

    void DrawVersionManagementSection()
    {
        Header("콘텐츠 버전 관리");
        // 두 축은 서로 묶여 있지 않다. 앱 버전은 스토어 표기, 테이블 세대는 데이터 호환 계약이다.
        EditorGUILayout.LabelField("앱 빌드 버전", PlayerSettings.bundleVersion);
        EditorGUILayout.LabelField("테이블 버전 규칙", $"{ContentVersion.Major}.<자동 시리얼>");
        EditorGUILayout.LabelField("새 테이블 최소 세대", ContentVersion.MinAppMajor.ToString());
        EditorGUILayout.LabelField("지원 테이블 세대", SupportedMajorsText());

        if (!ContentVersionConsistency.TryValidate(out string t_consistencyError))
            EditorGUILayout.HelpBox(t_consistencyError, MessageType.Warning);

        this.versionEnvMode = (EContentRunMode)EditorGUILayout.EnumPopup("대상 환경", this.versionEnvMode);
        bool t_hasEnv = TryGetDataEnvId(this.versionEnvMode, out string t_envId, out string t_envError);
        EditorGUILayout.LabelField("인덱스 경로",
            t_hasEnv ? $"{FirebaseRootPath.Environment(t_envId)}/specs/_index" : "(환경 프로필 없음)");
        if (!t_hasEnv) EditorGUILayout.HelpBox(t_envError, MessageType.Error);

        DrawAdminAuth();

        using (new EditorGUI.DisabledScope(!t_hasEnv || !SpecAdminAuth.IsSignedIn))
        {
            if (GUILayout.Button("서버 버전 새로고침", GUILayout.Height(26)))
                RefreshVersionIndex(t_envId);
        }

        if (!string.IsNullOrEmpty(this.versionError))
            EditorGUILayout.HelpBox(this.versionError, MessageType.Error);
        if (this.versionIndex == null)
        {
            EditorGUILayout.HelpBox(
                "서버 인덱스를 아직 읽지 않았거나 인덱스가 없습니다. 기존 공개분에는 릴리스 인덱스 스냅샷이 없어 자동 롤백할 수 없습니다.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("현재 테이블 버전", this.versionIndex.ContentVersion);
        EditorGUILayout.LabelField("테이블 세대 / 시리얼", $"{this.versionIndex.Major} / {this.versionIndex.Minor}");
        EditorGUILayout.LabelField("다음 테이블 시리얼", this.versionIndex.NextMinor.ToString());
        EditorGUILayout.LabelField("최소 세대", this.versionIndex.MinAppMajor.ToString());
        EditorGUILayout.LabelField("갱신 시각", this.versionIndex.UpdateTime ?? "-");
        if (this.versionIndex.MinAppMajor > ContentVersion.Major)
            EditorGUILayout.HelpBox("현재 테이블은 이 앱이 지원하는 것보다 높은 테이블 세대를 요구합니다.", MessageType.Warning);

        List<string> t_history = this.versionIndex.History;
        bool t_hasHistory = t_history != null && t_history.Count > 0;
        if (!t_hasHistory)
            EditorGUILayout.HelpBox(
                "롤백 이력이 없습니다. 이 기능 도입 후 공개되는 버전부터 목록에 쌓입니다. " +
                "그 전 버전도 릴리스 스냅샷이 있으면 아래 직접 입력으로 되돌릴 수 있습니다. " +
                "Firestore 콘솔에서 수동 복원할 때는 nextMinor를 절대 되돌리지 마십시오.",
                MessageType.Warning);

        // 이력은 오래된 것부터 쌓이는데 되돌릴 일이 가장 많은 건 직전 버전이다 — 최신을 위로 올리고 기본 선택으로 둔다.
        this.versionManualEntry = EditorGUILayout.ToggleLeft(
            "버전 직접 입력 (이력 목록 밖의 스냅샷)", this.versionManualEntry);
        string t_target;
        if (this.versionManualEntry || !t_hasHistory)
        {
            this.versionManualVersion = EditorGUILayout.TextField("롤백 대상", this.versionManualVersion);
            t_target = (this.versionManualVersion ?? string.Empty).Trim();
            EditorGUILayout.HelpBox(
                "형식은 세대.시리얼입니다. 이력 목록 밖이어도 릴리스 인덱스 스냅샷이 남아 있으면 되돌릴 수 있습니다.",
                MessageType.None);
        }
        else
        {
            string[] t_versions = new string[t_history.Count];
            for (int i = 0; i < t_versions.Length; i++) t_versions[i] = t_history[t_history.Count - 1 - i];
            this.versionSelection = Mathf.Clamp(this.versionSelection, 0, t_versions.Length - 1);
            this.versionSelection = EditorGUILayout.Popup("롤백 대상", this.versionSelection, t_versions);
            t_target = t_versions[this.versionSelection];
        }

        // 미지원 major로 되돌리면 클라만 막히는 게 아니다 — functions의 SUPPORTED_CONTENT_MAJORS 검사가
        // throw해서 스펙을 읽는 callable이 전부 죽는다. 그래서 경고가 아니라 차단이고, 해제는 명시적으로 받는다.
        bool t_unsupportedMajor = TryMajor(t_target, out int t_targetMajor) &&
                                  !ContentVersion.IsSupportedMajor(t_targetMajor);
        if (t_unsupportedMajor)
        {
            EditorGUILayout.HelpBox(
                $"테이블 세대 {t_targetMajor}는 이 저장소가 지원하는 목록에 없습니다. 이대로 되돌리면 클라이언트가 " +
                "UpdateRequired가 되고, 배포된 functions의 SUPPORTED_CONTENT_MAJORS에도 없으면 스펙을 읽는 " +
                "callable이 전부 실패합니다. 그 세대를 지원하는 functions를 먼저 배포한 뒤에만 해제하십시오.",
                MessageType.Error);
            this.versionAllowUnsupportedMajor = EditorGUILayout.ToggleLeft(
                "미지원 테이블 세대 롤백을 허용한다 (functions 배포 확인함)", this.versionAllowUnsupportedMajor);
        }
        else this.versionAllowUnsupportedMajor = false;

        string t_blocker = VersionRollbackBlocker(t_hasEnv, t_envError, t_target, t_unsupportedMajor);
        if (!string.IsNullOrEmpty(t_blocker)) EditorGUILayout.HelpBox(t_blocker, MessageType.Error);
        using (new EditorGUI.DisabledScope(t_blocker != null))
        {
            if (GUILayout.Button($"{t_target}으로 롤백", GUILayout.Height(30)))
                RunVersionRollback(t_envId, t_target);
        }

        if (!string.IsNullOrEmpty(this.versionReport))
            EditorGUILayout.HelpBox(this.versionReport, MessageType.Info);
    }

    void RefreshVersionIndex(string _envId)
    {
        try
        {
            EditorUtility.DisplayProgressBar("콘텐츠 버전", "서버 인덱스를 읽는 중...", 0.5f);
            bool t_ok = SpecFirestoreUploader.TryGetPublishedIndex(
                _envId, out SpecFirestoreUploader.ContentIndexState t_state, out string t_error);
            this.versionIndex = t_ok ? t_state : null;
            this.versionError = t_ok ? null : t_error;
            this.versionSelection = 0;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    string VersionRollbackBlocker(bool _hasEnv, string _envError, string _target, bool _unsupportedMajor)
    {
        if (!_hasEnv) return _envError;
        if (!SpecAdminAuth.IsSignedIn) return "관리자 로그인이 필요합니다.";
        if (!SpecAdminAuth.HasAdminClaim) return "로그인한 계정에 admin 클레임이 없습니다.";
        if (this.versionIndex == null) return "서버 인덱스를 먼저 새로고침해야 합니다.";
        if (!IsVersionFormat(_target)) return "롤백 대상 버전을 세대.시리얼 형식으로 입력해야 합니다.";
        if (string.Equals(this.versionIndex.ContentVersion, _target)) return "현재 버전과 같은 버전입니다.";
        // 버전 대조(ContentVersionConsistency)는 일부러 넣지 않는다 — 사고 대응 때 롤백이 막히면 안 된다.
        if (_unsupportedMajor && !this.versionAllowUnsupportedMajor)
            return "미지원 테이블 세대로의 롤백은 위 체크박스로 명시적으로 허용해야 합니다.";
        return null;
    }

    static bool IsVersionFormat(string _version)
    {
        if (string.IsNullOrEmpty(_version)) return false;
        int t_dot = _version.IndexOf('.');
        if (t_dot <= 0 || t_dot == _version.Length - 1) return false;
        return int.TryParse(_version.Substring(0, t_dot), out int t_major) && t_major >= 0 &&
               long.TryParse(_version.Substring(t_dot + 1), out long t_minor) && t_minor >= 0;
    }

    void RunVersionRollback(string _envId, string _target)
    {
        const string t_warning =
            "_index를 선택한 릴리스 스냅샷으로 되돌립니다. nextMinor와 history는 현재 값을 보존합니다.\n\n" +
            "주의: blob/current, rows/{id}, 표 메타는 최신 상태로 남습니다. _index를 쓰지 않는 레거시 경로와 " +
            "서버 rows 폴백은 최신 콘텐츠를 볼 수 있어 클라이언트와 서버 판정이 달라질 수 있습니다.";
        if (!EditorUtility.DisplayDialog("콘텐츠 롤백 확인", $"{_target}으로 롤백하시겠습니까?\n\n{t_warning}", "롤백", "취소"))
            return;
        if (!EditorUtility.DisplayDialog("콘텐츠 롤백 최종 확인", "이 작업은 전체 사용자에게 즉시 영향을 줍니다. 계속하시겠습니까?", "실행", "취소"))
            return;

        try
        {
            EditorUtility.DisplayProgressBar("콘텐츠 버전", $"{_target} 스냅샷을 복원하는 중...", 0.65f);
            string t_report = SpecFirestoreUploader.RollbackIndex(_envId, _target, out string t_error);
            this.versionReport = t_error == null ? t_report : null;
            this.versionError = t_error;
            if (t_error == null) RefreshVersionIndex(_envId);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    static string SupportedMajorsText()
    {
        var t_values = new string[ContentVersion.SupportedMajorCount];
        for (int i = 0; i < t_values.Length; i++) t_values[i] = ContentVersion.SupportedMajorAt(i).ToString();
        return string.Join(", ", t_values);
    }

    static bool TryMajor(string _version, out int _major)
    {
        _major = 0;
        if (string.IsNullOrEmpty(_version)) return false;
        int t_dot = _version.IndexOf('.');
        return t_dot > 0 && int.TryParse(_version.Substring(0, t_dot), out _major);
    }
}
