using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

public partial class ReleaseManagerWindow
{
    // 창을 닫았다 다시 열어도 실행 중인 배포와 결과를 이어받는다.
    static bool resourceCommandBusy;
    static string resourceCommandReport;
    static MessageType resourceReportType;

    void DrawResourcesTab()
    {
        this.scroll = EditorGUILayout.BeginScrollView(this.scroll);
        Header("Firebase 리소스 배포");
        EditorGUILayout.LabelField("프로젝트 / 사이트", "bm-cardbattle / bm-cardbattle-assets");
        EditorGUILayout.LabelField("앱 버전 / 플랫폼", $"{PlayerSettings.bundleVersion} / {EditorUserBuildSettings.activeBuildTarget}");
        var t_settings = AddressableAssetSettingsDefaultObject.Settings;
        string t_url = t_settings != null ? t_settings.RemoteCatalogLoadPath.GetValue(t_settings) : "(Addressables 설정 없음)";
        EditorGUILayout.LabelField("다운로드 주소");
        EditorGUILayout.SelectableLabel(t_url, EditorStyles.textField, GUILayout.Height(20));
        EditorGUILayout.HelpBox(
            "카드 아트를 빌드한 뒤 검사 → 배포 순서로 진행합니다. 앱 버전·플랫폼은 Player Settings / Build Profiles에서 선택합니다.\n" +
            "배포는 Firebase CLI 로그인(firebase login)을 사용하며, 이 창의 관리자 로그인과 별개입니다.", MessageType.Info);

        using (new EditorGUI.DisabledScope(resourceCommandBusy || EditorApplication.isCompiling))
        {
            if (GUILayout.Button("① 리소스 전체 빌드", GUILayout.Height(28))) BuildResources();
            using (new EditorGUI.DisabledScope(!Directory.Exists(Path.Combine(RepoRoot, "ServerData"))))
            {
                if (GUILayout.Button("② 배포 전 검사", GUILayout.Height(26)))
                    RunResourceCommand("node scripts/validate-resource-hosting.cjs", "배포 전 검사");
                if (GUILayout.Button("③ Firebase Hosting 배포", GUILayout.Height(28)))
                    RunResourceCommand("firebase deploy --only hosting --project bm-cardbattle --non-interactive", "Firebase Hosting 배포");
            }
        }

        EditorGUILayout.HelpBox(
            "배포 대상은 ServerData 전체입니다. 이전 앱 버전·플랫폼 파일도 유지하세요. 배포 버튼은 기존 검사를 다시 실행합니다.\n" +
            "출시된 앱의 리소스 패치는 Addressables Groups의 Update a Previous Build에서 출시 당시 상태 파일을 선택한 뒤 배포합니다.",
            MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("배포 폴더 열기"))
                EditorUtility.RevealInFinder(Directory.Exists(Path.Combine(RepoRoot, "ServerData"))
                    ? Path.Combine(RepoRoot, "ServerData") : RepoRoot);
            if (GUILayout.Button("배포 안내 열기"))
                EditorUtility.OpenWithDefaultApp(Path.Combine(RepoRoot, "docs/FirebaseResourceHosting.md"));
        }
        if (!string.IsNullOrEmpty(resourceCommandReport))
            EditorGUILayout.HelpBox(resourceCommandReport, resourceReportType);
        EditorGUILayout.EndScrollView();
        if (resourceCommandBusy) Repaint();
    }

    void BuildResources()
    {
        try
        {
            FirebaseResourceBuild.Build();
            resourceCommandReport = "리소스 빌드 완료. 배포 전 검사 후 Firebase Hosting에 배포하세요.";
            resourceReportType = MessageType.Info;
        }
        catch (Exception t_exception)
        {
            resourceCommandReport = "리소스 빌드 실패: " + t_exception.Message;
            resourceReportType = MessageType.Error;
            UnityEngine.Debug.LogException(t_exception);
        }
    }

    // 명령은 위 버튼의 고정 문자열만 받는다. 경로는 shell 명령에 붙이지 않고 WorkingDirectory로 넘긴다.
    async void RunResourceCommand(string _command, string _label)
    {
        if (resourceCommandBusy) return;
        resourceCommandBusy = true;
        resourceCommandReport = _label + " 실행 중…";
        resourceReportType = MessageType.Info;
        string t_root = RepoRoot;
        EditorApplication.LockReloadAssemblies();
        try
        {
            bool t_windows = Application.platform == RuntimePlatform.WindowsEditor;
            using var t_process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = t_windows ? "cmd.exe" : "/bin/sh",
                    Arguments = t_windows ? "/d /s /c \"" + _command + "\"" : "-c \"" + _command + "\"",
                    WorkingDirectory = t_root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                }
            };
            t_process.Start();
            Task<string> t_stdout = t_process.StandardOutput.ReadToEndAsync();
            Task<string> t_stderr = t_process.StandardError.ReadToEndAsync();
            await Task.Run(() => t_process.WaitForExit());
            string t_output = (await t_stdout + "\n" + await t_stderr).Trim();
            bool t_ok = t_process.ExitCode == 0;
            resourceCommandReport = $"{_label} {(t_ok ? "완료" : "실패")} (exit {t_process.ExitCode})\n{t_output}";
            resourceReportType = t_ok ? MessageType.Info : MessageType.Error;
            if (t_ok) UnityEngine.Debug.Log(resourceCommandReport);
            else UnityEngine.Debug.LogError(resourceCommandReport);
        }
        catch (Exception t_exception)
        {
            resourceCommandReport = _label + " 실행 실패: " + t_exception.Message +
                "\nNode.js와 Firebase CLI 설치·로그인 상태를 확인하세요.";
            resourceReportType = MessageType.Error;
        }
        finally
        {
            resourceCommandBusy = false;
            EditorApplication.UnlockReloadAssemblies();
            if (this != null) Repaint();
        }
    }
}
