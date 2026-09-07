using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class ReleaseManagerWindow
{
    EContentRunMode replayEnvMode;
    SpecFirestoreUploader.BattleReplayConfigState replayConfig;
    List<SpecFirestoreUploader.ReplayDailyState> replayDaily;
    string replayError;
    string replayReport;
    string replayNote;

    // google-services.json 파싱을 OnGUI 마다 돌리면 마우스가 움직일 때마다 다시 읽는다.
    // 창을 열 때 한 번만 구하고 그 결과를 들고 있는다.
    string replayProjectId;
    string replayProjectError;

    // Cloud Run 실측 상태. gcloud 로 읽기 전에는 알 수 없어서 "모름"과 "0" 을 구분해 둔다.
    string replayMinScale;
    string replayServiceUrl;
    string replayCloudError;
    bool   replayCloudNeedsLogin;
    string replayCloudBusy;

    void EnableBattleReplayManagement()
    {
        this.replayEnvMode = ContentRunModeEditor.Current;
        this.replayConfig = null;
        this.replayDaily = null;
        this.replayError = null;
        this.replayReport = null;
        this.replayNote = string.Empty;
        this.replayMinScale = null;
        this.replayServiceUrl = null;
        this.replayCloudError = null;
        this.replayCloudNeedsLogin = false;
        this.replayCloudBusy = null;
        if (!SpecFirestoreUploader.TryGetCloudProjectId(out this.replayProjectId, out this.replayProjectError))
            this.replayProjectId = null;
    }

    void DrawBattleReplayManagementSection()
    {
        Header("④ 서버 전투 검증");
        this.replayEnvMode = (EContentRunMode)EditorGUILayout.EnumPopup("대상 환경", this.replayEnvMode);
        bool t_hasEnv = TryGetDataEnvId(this.replayEnvMode, out string t_envId, out string t_envError);
        EditorGUILayout.LabelField("설정 경로",
            t_hasEnv ? $"{FirebaseRootPath.Environment(t_envId)}/config/battleReplay" : "(환경 프로필 없음)");
        if (!t_hasEnv) EditorGUILayout.HelpBox(t_envError, MessageType.Error);

        using (new EditorGUI.DisabledScope(!t_hasEnv || !AdminReady))
        {
            if (GUILayout.Button("설정 · 최근 7일 새로고침", GUILayout.Height(26)))
                RefreshBattleReplay(t_envId);
        }

        if (!string.IsNullOrEmpty(this.replayError))
            EditorGUILayout.HelpBox(this.replayError, MessageType.Error);

        if (this.replayConfig == null)
        {
            EditorGUILayout.HelpBox("서버 상태를 아직 읽지 않았습니다. 문서가 없으면 Functions는 꺼짐으로 처리합니다.", MessageType.Info);
        }
        else
        {
            bool t_enabled = this.replayConfig.Enabled;
            EditorGUILayout.HelpBox(
                t_enabled
                    ? "켜짐 — ruleset 2 이상은 C# 재생 결과로 정산하며 Cloud Run을 호출합니다."
                    : "꺼짐 — Cloud Run 호출을 생략하고 두 클라이언트 합의로 정산합니다. 치트 방어가 낮아진 상태입니다.",
                t_enabled ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.LabelField("문서", this.replayConfig.Exists ? "있음" : "없음 (기본값: 꺼짐)");
            EditorGUILayout.LabelField("마지막 변경", this.replayConfig.UpdatedAt ?? "-");
            EditorGUILayout.LabelField("변경자", this.replayConfig.UpdatedBy ?? "-");
            if (!string.IsNullOrEmpty(this.replayConfig.Note))
                EditorGUILayout.LabelField("기존 메모", this.replayConfig.Note);

            this.replayNote = EditorGUILayout.TextField("변경 메모", this.replayNote ?? string.Empty);
            using (new EditorGUI.DisabledScope(!t_hasEnv || !AdminReady))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(t_enabled))
                    if (GUILayout.Button("검증 켜기", GUILayout.Height(28))) SetBattleReplay(t_envId, true);
                using (new EditorGUI.DisabledScope(!t_enabled))
                    if (GUILayout.Button("검증 끄기", GUILayout.Height(28))) SetBattleReplay(t_envId, false);
            }
        }

        DrawReplayDaily();
        DrawCloudRunCommands(t_hasEnv);

        if (!string.IsNullOrEmpty(this.replayReport))
            EditorGUILayout.HelpBox(this.replayReport, MessageType.Info);
    }

    void RefreshBattleReplay(string _envId)
    {
        try
        {
            EditorUtility.DisplayProgressBar("서버 전투 검증", "설정과 최근 집계를 읽는 중...", 0.5f);
            bool t_configOk = SpecFirestoreUploader.TryGetBattleReplayConfig(
                _envId, out SpecFirestoreUploader.BattleReplayConfigState t_config, out string t_configError);
            bool t_dailyOk = SpecFirestoreUploader.TryGetReplayDaily(
                _envId, 7, out List<SpecFirestoreUploader.ReplayDailyState> t_daily, out string t_dailyError);
            this.replayConfig = t_configOk ? t_config : null;
            this.replayDaily = t_dailyOk ? t_daily : null;
            this.replayError = !t_configOk ? t_configError : !t_dailyOk ? t_dailyError : null;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    void SetBattleReplay(string _envId, bool _enabled)
    {
        string t_action = _enabled ? "켜기" : "끄기";
        string t_warning = _enabled
            ? "Cloud Run min instance를 먼저 1로 올렸는지 확인하십시오. 설정 전파에는 Functions 인스턴스별 최대 60초가 걸립니다."
            : "최대 60초 뒤 Cloud Run 호출이 멈추고 클라이언트 합의 정산으로 후퇴합니다. 호출이 멎은 뒤 min instance를 0으로 내리십시오.";
        if (!EditorUtility.DisplayDialog($"서버 전투 검증 {t_action}",
                $"{ContentRunModeEditor.Label(this.replayEnvMode)} 환경의 검증을 {t_action}합니다.\n\n{t_warning}", t_action, "취소"))
            return;

        try
        {
            EditorUtility.DisplayProgressBar("서버 전투 검증", $"설정을 {t_action}는 중...", 0.6f);
            bool t_ok = SpecFirestoreUploader.SetBattleReplayEnabled(
                _envId, _enabled, this.replayNote, out string t_error);
            this.replayError = t_ok ? null : t_error;
            this.replayReport = t_ok ? $"서버 전투 검증을 {t_action}로 저장했습니다. Functions 캐시 반영은 최대 60초입니다." : null;
            if (t_ok) RefreshBattleReplay(_envId);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    void DrawReplayDaily()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("최근 7일 (UTC)", EditorStyles.boldLabel);
        if (this.replayDaily == null)
        {
            EditorGUILayout.LabelField("새로고침하면 집계를 표시합니다.");
            return;
        }

        long t_ok = 0;
        long t_divergent = 0;
        long t_unavailable = 0;
        foreach (SpecFirestoreUploader.ReplayDailyState t_day in this.replayDaily)
        {
            t_ok += t_day.ReplayOk;
            t_divergent += t_day.Divergent;
            t_unavailable += t_day.Unavailable;
            EditorGUILayout.LabelField(t_day.Day,
                $"정산 {t_day.Settled}  재생 {t_day.ReplayOk}  실패 {t_day.ReplayFailed}  불가(시도) {t_day.Unavailable}  발산 {t_day.Divergent} (결과 {t_day.OutcomeMismatch} / 해시 {t_day.HashMismatch})");
        }
        string t_rate = t_ok > 0 ? (100.0 * t_divergent / t_ok).ToString("0.###") + "%" : "-";
        EditorGUILayout.LabelField("7일 발산율", $"{t_divergent} / {t_ok} = {t_rate}", EditorStyles.boldLabel);
        if (t_divergent > 0)
            EditorGUILayout.HelpBox("서버 재생 발산이 발견됐습니다. battle_replay_divergence 로그와 매치 문서를 확인하십시오.", MessageType.Error);
        else if (t_unavailable > 0)
            EditorGUILayout.HelpBox("재생 불가 시도가 있습니다. replayUnavailable 매치와 Cloud Run 상태를 확인하십시오.", MessageType.Warning);
    }

    void DrawCloudRunCommands(bool _hasEnv)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Cloud Run 비용 제어", EditorStyles.boldLabel);
        if (!_hasEnv)
        {
            EditorGUILayout.HelpBox("환경 프로필이 없습니다.", MessageType.Warning);
            return;
        }
        if (string.IsNullOrEmpty(this.replayProjectId))
        {
            EditorGUILayout.HelpBox(this.replayProjectError ?? "Firebase 프로젝트를 확인할 수 없습니다.", MessageType.Warning);
            return;
        }

        // live·test 가 같은 Cloud Run 서비스를 공유한다. 여기서 내리면 두 환경이 같이 내려간다.
        EditorGUILayout.LabelField("서비스",
            $"{CloudRunControl.SERVICE} ({CloudRunControl.REGION} / {this.replayProjectId}) — live·test 공용");
        EditorGUILayout.LabelField("최소 인스턴스",
            this.replayMinScale == null ? "(조회 안 함)" :
            this.replayMinScale.Length == 0 ? "0 (미설정 = 유휴 과금 없음)" : this.replayMinScale);
        if (!string.IsNullOrEmpty(this.replayServiceUrl))
            EditorGUILayout.LabelField("서비스 URL", this.replayServiceUrl);

        if (!string.IsNullOrEmpty(this.replayCloudBusy))
            EditorGUILayout.HelpBox(this.replayCloudBusy, MessageType.Info);
        else if (!string.IsNullOrEmpty(this.replayCloudError))
            EditorGUILayout.HelpBox(this.replayCloudError, this.replayCloudNeedsLogin ? MessageType.Warning : MessageType.Error);

        EditorGUILayout.HelpBox(
            "켜기: min 1 적용 후 검증 켜기. 끄기: 검증 끄기 → 60초 대기 후 min 0 적용.", MessageType.None);

        using (new EditorGUI.DisabledScope(CloudRunControl.IsBusy))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("상태 조회", GUILayout.Height(24))) RunCloudRunDescribe();
            if (GUILayout.Button("min 1 적용", GUILayout.Height(24))) RunCloudRunSetMin(1);
            if (GUILayout.Button("min 0 적용", GUILayout.Height(24))) RunCloudRunSetMin(0);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (this.replayCloudNeedsLogin && GUILayout.Button("gcloud 로그인"))
            {
                if (!CloudRunControl.TryOpenLogin(out string t_loginError))
                    this.replayCloudError = t_loginError;
                else
                    this.replayReport = "새 콘솔 창에서 gcloud auth login 을 마친 뒤 '상태 조회' 를 다시 누르십시오.";
            }
            if (GUILayout.Button("min 1 명령 복사"))
                CopyCommand(MinInstancesCommand(1));
            if (GUILayout.Button("min 0 명령 복사"))
                CopyCommand(MinInstancesCommand(0));
        }
    }

    string MinInstancesCommand(int _min) =>
        $"gcloud run services update {CloudRunControl.SERVICE} --project {this.replayProjectId} " +
        $"--region {CloudRunControl.REGION} --min {_min}";

    void RunCloudRunDescribe()
    {
        this.replayCloudBusy = "Cloud Run 상태를 읽는 중...";
        this.replayCloudError = null;
        CloudRunControl.Describe(this.replayProjectId, ApplyDescribe);
        Repaint();
    }

    void ApplyDescribe(CloudRunControl.Result _result)
    {
        // gcloud 가 도는 동안 창을 닫으면 콜백이 파괴된 EditorWindow 에 닿는다.
        if (this == null) return;
        this.replayCloudBusy = null;
        this.replayCloudError = _result.Error;
        this.replayCloudNeedsLogin = _result.NeedsLogin;
        if (_result.Ok)
        {
            // describe 는 "minScale<TAB>url" 한 줄을 준다. minScale 이 미설정이면 앞칸이 빈 문자열이다.
            string[] t_parts = (_result.StdOut ?? string.Empty).Trim().Split('\t');
            this.replayMinScale = t_parts.Length > 0 ? t_parts[0].Trim() : string.Empty;
            this.replayServiceUrl = t_parts.Length > 1 ? t_parts[1].Trim() : null;
        }
        Repaint();
    }

    void RunCloudRunSetMin(int _min)
    {
        string t_warning = _min == 0
            ? "유휴 과금이 멈추는 대신 다음 요청이 콜드 스타트를 겪습니다. 검증 토글을 먼저 끄고 60초를 기다렸는지 확인하십시오."
            : "인스턴스가 상시 대기하며 유휴 과금이 발생합니다.";
        if (!EditorUtility.DisplayDialog($"Cloud Run 최소 인스턴스 {_min}",
                $"{CloudRunControl.SERVICE} ({CloudRunControl.REGION}) 의 최소 인스턴스를 {_min} 로 바꿉니다.\n" +
                "live·test 가 같은 서비스를 공유하므로 두 환경에 함께 적용됩니다.\n\n" + t_warning,
                "적용", "취소"))
            return;

        this.replayCloudBusy = $"min {_min} 적용 중... (수십 초 걸립니다)";
        this.replayCloudError = null;
        CloudRunControl.SetMinInstances(this.replayProjectId, _min, _result =>
        {
            if (this == null) return;
            this.replayCloudBusy = null;
            this.replayCloudError = _result.Error;
            this.replayCloudNeedsLogin = _result.NeedsLogin;
            if (_result.Ok)
            {
                this.replayReport = $"Cloud Run 최소 인스턴스를 {_min} 로 바꿨습니다.";
                RunCloudRunDescribe();
            }
            Repaint();
        });
        Repaint();
    }

    void CopyCommand(string _command)
    {
        EditorGUIUtility.systemCopyBuffer = _command;
        this.replayReport = "클립보드에 복사했습니다:\n" + _command;
    }
}
