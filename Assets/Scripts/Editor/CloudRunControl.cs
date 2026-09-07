using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

/// <summary>
/// gcloud CLI 로 Cloud Run 전투 재생 서비스를 직접 조작한다.
///
/// <para>클립보드 복사로 두면 실제 상태와 창이 갈린다 — 창은 "껐다"고 표시하는데 최소 인스턴스는
/// 1로 남아 유휴 과금이 계속되는 상황이 눈에 안 보인다. 그래서 조회·변경을 같은 자리에서 한다.</para>
///
/// <para>실행은 비동기다. <c>gcloud run services update</c> 는 30초를 넘기는 일이 흔해서
/// 동기로 돌리면 에디터가 그동안 통째로 멈춘다. <see cref="EditorApplication.update"/> 로 종료를
/// 폴링하고 결과만 콜백으로 돌려준다.</para>
/// </summary>
public static class CloudRunControl
{
    public const string SERVICE = "battle-replay";
    public const string REGION  = "asia-northeast3";

    /// <summary>변경 명령은 이 시간을 넘기면 죽인다. 조회는 훨씬 빨리 끝난다.</summary>
    const int TIMEOUT_SECONDS = 180;

    public sealed class Result
    {
        public bool   Ok;
        public int    ExitCode;
        public string StdOut;
        public string StdErr;
        /// <summary>사용자에게 그대로 보여줄 사유. 실패했을 때만 채운다.</summary>
        public string Error;
        /// <summary>`gcloud auth login` 이 필요한 상태인지. 사유 문구를 따로 띄우려고 나눈다.</summary>
        public bool   NeedsLogin;
    }

    // ── gcloud 찾기 ────────────────────────────────────────────────────────

    static string cachedGcloud;

    /// <summary>gcloud.cmd 경로. PATH 에 없으면 기본 설치 위치를 본다.</summary>
    public static bool TryResolve(out string _path, out string _error)
    {
        _error = null;
        if (!string.IsNullOrEmpty(CloudRunControl.cachedGcloud) && File.Exists(CloudRunControl.cachedGcloud))
        {
            _path = CloudRunControl.cachedGcloud;
            return true;
        }

        foreach (string t_candidate in Candidates())
        {
            if (string.IsNullOrEmpty(t_candidate) || !File.Exists(t_candidate)) continue;
            CloudRunControl.cachedGcloud = t_candidate;
            _path = t_candidate;
            return true;
        }

        _path = null;
        _error = "gcloud CLI 를 찾지 못했다. Google Cloud SDK 를 설치하고 Unity 를 다시 켤 것 " +
                 "(에디터는 실행 시점의 PATH 를 들고 있어서, 설치 후 재시작 없이는 안 보인다).";
        return false;
    }

    static IEnumerable<string> Candidates()
    {
        string t_pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string t_dir in t_pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(t_dir)) continue;
            string t_joined;
            // PATH 에 따옴표나 잘못된 문자가 섞이면 Path.Combine 이 던진다 — 한 항목 때문에 탐색을 접지 않는다.
            try { t_joined = Path.Combine(t_dir.Trim(), "gcloud.cmd"); }
            catch (ArgumentException) { continue; }
            yield return t_joined;
        }

        yield return Environment.GetEnvironmentVariable("LOCALAPPDATA") +
                     @"\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd";
        yield return Environment.GetEnvironmentVariable("ProgramFiles(x86)") +
                     @"\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd";
        yield return Environment.GetEnvironmentVariable("ProgramFiles") +
                     @"\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd";
    }

    // ── 명령 ───────────────────────────────────────────────────────────────

    /// <summary>최소 인스턴스와 서비스 URL 을 읽는다. 읽기 전용이라 확인 절차가 필요 없다.</summary>
    public static void Describe(string _projectId, Action<Result> _onDone)
    {
        Run($"run services describe {SERVICE} --project {_projectId} --region {REGION} " +
            "--format \"value(spec.template.metadata.annotations['autoscaling.knative.dev/minScale'],status.url)\"",
            _onDone);
    }

    /// <summary>최소 인스턴스를 바꾼다. 0 이면 유휴 과금이 멈추고 대신 콜드 스타트를 감수한다.</summary>
    public static void SetMinInstances(string _projectId, int _min, Action<Result> _onDone)
    {
        Run($"run services update {SERVICE} --project {_projectId} --region {REGION} --min {_min}", _onDone);
    }

    /// <summary>
    /// 재인증. 브라우저를 띄워야 해서 출력을 가로채지 않고 콘솔 창을 그대로 보여 준다 —
    /// 리다이렉트하면 gcloud 가 프롬프트를 못 띄우고 그대로 멎는다.
    /// </summary>
    public static bool TryOpenLogin(out string _error)
    {
        if (!TryResolve(out string t_gcloud, out _error)) return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"\"{t_gcloud}\" auth login\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception t_exception)
        {
            _error = "gcloud auth login 실행 실패: " + t_exception.Message;
            return false;
        }
    }

    // ── 실행기 ─────────────────────────────────────────────────────────────

    public static bool IsBusy { get; private set; }

    static void Run(string _arguments, Action<Result> _onDone)
    {
        if (IsBusy)
        {
            _onDone(new Result { Ok = false, Error = "gcloud 명령이 이미 실행 중이다." });
            return;
        }
        if (!TryResolve(out string t_gcloud, out string t_resolveError))
        {
            _onDone(new Result { Ok = false, Error = t_resolveError });
            return;
        }

        var t_info = new ProcessStartInfo
        {
            // .cmd 는 CreateProcess 로 직접 못 띄운다. 출력을 가로채려면 UseShellExecute 를 꺼야 하므로
            // cmd.exe 를 한 겹 두른다.
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{t_gcloud}\" {_arguments}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process t_process;
        var t_out = new StringBuilder();
        var t_err = new StringBuilder();
        try
        {
            t_process = new Process { StartInfo = t_info };
            // 출력 스트림을 다 읽기 전에 WaitForExit 하면 파이프가 차서 서로 막힌다 — 이벤트로 흘려 받는다.
            t_process.OutputDataReceived += (_, _event) =>
            { if (_event.Data != null) lock (t_out) t_out.AppendLine(_event.Data); };
            t_process.ErrorDataReceived += (_, _event) =>
            { if (_event.Data != null) lock (t_err) t_err.AppendLine(_event.Data); };
            t_process.Start();
            t_process.BeginOutputReadLine();
            t_process.BeginErrorReadLine();
        }
        catch (Exception t_exception)
        {
            _onDone(new Result { Ok = false, Error = "gcloud 실행 실패: " + t_exception.Message });
            return;
        }

        IsBusy = true;
        double t_deadline = EditorApplication.timeSinceStartup + TIMEOUT_SECONDS;

        void Poll()
        {
            if (!t_process.HasExited)
            {
                if (EditorApplication.timeSinceStartup < t_deadline) return;
                try { t_process.Kill(); } catch (Exception) { /* 이미 끝난 경우 */ }
                Finish(new Result
                {
                    Ok = false,
                    Error = $"gcloud 명령이 {TIMEOUT_SECONDS}초를 넘겨 중단했다. Cloud Run 콘솔에서 실제 반영 여부를 확인할 것.",
                });
                return;
            }

            // 이벤트로 받는 나머지 줄이 아직 큐에 남아 있을 수 있다. 인자 없는 WaitForExit 가 그걸 비운다.
            t_process.WaitForExit();
            string t_stdout;
            string t_stderr;
            lock (t_out) t_stdout = t_out.ToString().Trim();
            lock (t_err) t_stderr = t_err.ToString().Trim();
            int t_code = t_process.ExitCode;
            bool t_needsLogin = t_stderr.IndexOf("auth login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t_stderr.IndexOf("Reauthentication failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t_stderr.IndexOf("do not have permission", StringComparison.OrdinalIgnoreCase) >= 0;
            Finish(new Result
            {
                Ok = t_code == 0,
                ExitCode = t_code,
                StdOut = t_stdout,
                StdErr = t_stderr,
                NeedsLogin = t_code != 0 && t_needsLogin,
                Error = t_code == 0 ? null :
                    t_needsLogin
                        ? "gcloud 인증이 만료됐다. 아래 'gcloud 로그인' 을 눌러 다시 로그인한 뒤 재시도할 것."
                        : $"gcloud 실패 (exit {t_code}):\n{Short(t_stderr.Length > 0 ? t_stderr : t_stdout)}",
            });
        }

        void Finish(Result _result)
        {
            EditorApplication.update -= Poll;
            IsBusy = false;
            try { t_process.Dispose(); } catch (Exception) { /* 무시 */ }
            if (!_result.Ok && !string.IsNullOrEmpty(_result.Error)) Debug.LogError("[CloudRun] " + _result.Error);
            _onDone(_result);
        }

        EditorApplication.update += Poll;
    }

    static string Short(string _text)
    {
        if (string.IsNullOrEmpty(_text)) return "(출력 없음)";
        return _text.Length <= 600 ? _text : _text.Substring(0, 600) + " …";
    }
}
