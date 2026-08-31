#if UNITY_EDITOR_WIN
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

// 온라인 전용 초기화 검증용 메뉴. 판정·규칙 조작은 tools/OfflineBootTest.ps1 이 단독으로 갖는다 —
// 여기서 방화벽을 직접 만지면 배치 파일과 진실원이 갈린다.
public static class OfflineBootTestMenu
{
    const string ROOT = "Tools/Card Battle/오프라인 초기화 검증/";
    const string BLOCK_MENU = ROOT + "차단 켜기 (관리자)";
    const string UNBLOCK_MENU = ROOT + "차단 해제 (관리자)";
    const string STATUS_MENU = ROOT + "상태 확인";

    [MenuItem(BLOCK_MENU, false, 0)]
    static void Block() => RunElevated("on");

    [MenuItem(UNBLOCK_MENU, false, 1)]
    static void Unblock() => RunElevated("off");

    [MenuItem(STATUS_MENU, false, 20)]
    static void Status()
    {
        if (!TryGetScript(out string t_script)) return;

        string t_output = Capture(t_script, "status");
        if (t_output != null) Debug.Log($"[오프라인 초기화 검증]\n{t_output.TrimEnd()}");
    }

    // UAC 창을 띄워야 하므로 출력을 되받지 못한다 — -NoExit 로 창을 남겨 확인 항목을 그 자리에서 읽게 한다.
    static void RunElevated(string _action)
    {
        if (!TryGetScript(out string t_script)) return;

        var t_info = new ProcessStartInfo("powershell.exe")
        {
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -NoExit -File \"{t_script}\" -Action {_action}",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(t_script),
        };

        try
        {
            Process.Start(t_info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Debug.LogWarning("[오프라인 초기화 검증] 관리자 권한 요청이 취소됐습니다.");
        }
    }

    static string Capture(string _script, string _action)
    {
        // 콘솔 코드페이지를 UTF-8로 고정하지 않으면 되받은 한글이 깨진다.
        string t_command =
            $"[Console]::OutputEncoding=[Text.Encoding]::UTF8; & '{_script}' -Action {_action}";

        var t_info = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{t_command}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using (Process t_process = Process.Start(t_info))
        {
            string t_stdout = t_process.StandardOutput.ReadToEnd();
            string t_stderr = t_process.StandardError.ReadToEnd();
            t_process.WaitForExit();

            if (!string.IsNullOrEmpty(t_stderr))
            {
                Debug.LogError($"[오프라인 초기화 검증] {t_stderr.TrimEnd()}");
                return null;
            }

            return t_stdout;
        }
    }

    static bool TryGetScript(out string _script)
    {
        _script = ToolPath("OfflineBootTest.ps1");
        if (File.Exists(_script)) return true;

        Debug.LogError($"[오프라인 초기화 검증] 스크립트가 없습니다: {_script}");
        return false;
    }

    static string ToolPath(string _fileName)
    {
        string t_projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        return Path.Combine(t_projectRoot, "tools", _fileName);
    }
}
#endif
