using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BattleGoldenReplayHarness
{
    const string ToggleMenu = "Tools/Card Battle/Golden/Enable Capture";
    const string ReplayMenu = "Tools/Card Battle/Golden/Replay Corpus";
    const string EditorPrefKey = "battle.golden.capture.enabled";

    [MenuItem(ToggleMenu)]
    static void ToggleCapture()
    {
        bool t_enabled = !EditorPrefs.GetBool(EditorPrefKey, false);
        EditorPrefs.SetBool(EditorPrefKey, t_enabled);
        Menu.SetChecked(ToggleMenu, t_enabled);
        UnityEngine.Debug.Log($"[BattleGolden] capture {(t_enabled ? "enabled" : "disabled")}");
    }

    [MenuItem(ToggleMenu, true)]
    static bool ValidateToggleCapture()
    {
        Menu.SetChecked(ToggleMenu, EditorPrefs.GetBool(EditorPrefKey, false));
        return true;
    }

    [MenuItem(ReplayMenu)]
    public static void Run()
    {
        string t_root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string t_functions = Path.Combine(t_root, "functions");
        if (!Directory.Exists(t_functions)) throw new DirectoryNotFoundException(t_functions);

        bool t_windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        var t_start = new ProcessStartInfo
        {
            FileName = t_windows ? "cmd.exe" : "/bin/bash",
            Arguments = t_windows
                ? "/d /s /c \"npm run test:battle-golden\""
                : "-lc \"npm run test:battle-golden\"",
            WorkingDirectory = t_functions,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        t_start.EnvironmentVariables["REQUIRE_BATTLE_GOLDENS"] = "1";

        using Process t_process = Process.Start(t_start);
        if (t_process == null) throw new InvalidOperationException("golden replay process failed to start");

        // 두 파이프를 순차 ReadToEnd 하면 데드락이다 — 자식이 stderr 버퍼(~4KB)를 채우면 stderr 쓰기에서
        // 멈추고 부모는 stdout 읽기에서 멈춰 서로 기다린다. WaitForExit 의 타임아웃까지 못 가서
        // 에디터가 무기한 정지한다. 테스트가 assert 로 실패할 때(스택이 stderr 로 나감)가 정확히 그 조건이다.
        var t_stdoutBuffer = new StringBuilder();
        var t_stderrBuffer = new StringBuilder();
        t_process.OutputDataReceived += (_, t_args) => { if (t_args.Data != null) t_stdoutBuffer.AppendLine(t_args.Data); };
        t_process.ErrorDataReceived  += (_, t_args) => { if (t_args.Data != null) t_stderrBuffer.AppendLine(t_args.Data); };
        t_process.BeginOutputReadLine();
        t_process.BeginErrorReadLine();
        if (!t_process.WaitForExit(10 * 60 * 1000))
        {
            try { t_process.Kill(); } catch (Exception) { }
            throw new TimeoutException("golden replay exceeded 10 minutes");
        }
        // 인자 없는 WaitForExit 가 비동기 수집 완료까지 마저 기다린다.
        t_process.WaitForExit();
        string t_stdout = t_stdoutBuffer.ToString();
        string t_stderr = t_stderrBuffer.ToString();
        if (!string.IsNullOrWhiteSpace(t_stdout)) UnityEngine.Debug.Log(t_stdout.Trim());
        if (t_process.ExitCode != 0)
            throw new InvalidOperationException($"golden replay failed ({t_process.ExitCode})\n{t_stderr}");
        UnityEngine.Debug.Log("[BattleGolden] corpus replay passed");
    }
}
