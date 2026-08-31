using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;

/// <summary>에디터에서 구글 계정으로 로그인해 구글 ID 토큰을 받아온다.
/// RFC 8252(네이티브 앱 OAuth) 그대로다 — 브라우저를 띄우고 루프백으로 code를 받아 PKCE로 교환한다.
///
/// <para>여기서 받는 건 <b>구글</b> ID 토큰이고, Firestore 규칙이 보는 <b>Firebase</b> ID 토큰이 아니다.
/// 교환은 <see cref="SpecAdminAuth.TrySignInWithGoogle"/>가 accounts:signInWithIdp로 한다.</para>
///
/// <para><b>HttpListener 대신 TcpListener를 쓴다.</b> 윈도우에서 HttpListener는 URL ACL이 없으면
/// 접근 거부로 죽는 경우가 있어, 루프백 소켓을 직접 열고 HTTP 응답 한 장을 손으로 써서 보낸다.</para>
///
/// <para>데스크톱 OAuth 클라이언트의 client secret은 RFC 8252상 기밀이 아니다(배포 바이너리에 들어가는 값).
/// 그래도 저장소에는 넣지 않고 EditorPrefs에만 둔다.</para></summary>
public static class GoogleOAuthSignIn
{
    const string AUTH_ENDPOINT  = "https://accounts.google.com/o/oauth2/v2/auth";
    const string TOKEN_ENDPOINT = "https://oauth2.googleapis.com/token";
    const string SCOPE          = "openid email profile";

    const int    LISTEN_TIMEOUT_SECONDS = 180;
    const int    POLL_INTERVAL_MS       = 100;

    const string CLIENT_ID_PREF     = "SpecFirestore.GoogleClientId";
    const string CLIENT_SECRET_PREF = "SpecFirestore.GoogleClientSecret";

    public static string ClientId
    {
        get => EditorPrefs.GetString(CLIENT_ID_PREF, string.Empty);
        set => EditorPrefs.SetString(CLIENT_ID_PREF, value ?? string.Empty);
    }

    public static string ClientSecret
    {
        get => EditorPrefs.GetString(CLIENT_SECRET_PREF, string.Empty);
        set => EditorPrefs.SetString(CLIENT_SECRET_PREF, value ?? string.Empty);
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>브라우저 로그인을 끝까지 진행해 구글 ID 토큰을 돌려준다.
    /// 브라우저 왕복을 기다리는 동안 에디터는 멈춘다 — 취소 가능한 진행바로 빠져나갈 수 있다.</summary>
    public static bool TryAcquireGoogleIdToken(out string _googleIdToken, out string _error)
    {
        _googleIdToken = null;
        _error = null;

        if (!IsConfigured)
        {
            _error = "구글 OAuth 클라이언트 ID가 비어 있다. 데이터 탭에서 입력할 것.";
            return false;
        }

        string t_verifier  = CreateCodeVerifier();
        string t_challenge = CreateCodeChallenge(t_verifier);
        string t_state     = CreateCodeVerifier();

        TcpListener t_listener;
        int t_port;
        try
        {
            t_listener = new TcpListener(IPAddress.Loopback, 0);
            t_listener.Start();
            t_port = ((IPEndPoint)t_listener.LocalEndpoint).Port;
        }
        catch (Exception t_exception)
        {
            _error = $"루프백 포트를 열지 못했다: {t_exception.Message}";
            return false;
        }

        string t_redirectUri = $"http://127.0.0.1:{t_port}";

        try
        {
            OpenBrowser(BuildAuthUrl(t_redirectUri, t_challenge, t_state));

            if (!TryWaitForCode(t_listener, t_state, out string t_code, out _error)) return false;
            if (!TryExchangeCode(t_code, t_verifier, t_redirectUri, out _googleIdToken, out _error)) return false;
            return true;
        }
        finally
        {
            try { t_listener.Stop(); } catch { /* 이미 닫혔으면 무시 */ }
            EditorUtility.ClearProgressBar();
        }
    }

    static string BuildAuthUrl(string _redirectUri, string _challenge, string _state)
    {
        var t_url = new StringBuilder(AUTH_ENDPOINT);
        t_url.Append("?client_id=").Append(Uri.EscapeDataString(ClientId))
             .Append("&redirect_uri=").Append(Uri.EscapeDataString(_redirectUri))
             .Append("&response_type=code")
             .Append("&scope=").Append(Uri.EscapeDataString(SCOPE))
             .Append("&code_challenge=").Append(_challenge)
             .Append("&code_challenge_method=S256")
             .Append("&state=").Append(_state)
             // 계정을 매번 고르게 한다 — 여러 구글 계정을 쓰는 개발기에서 엉뚱한 계정으로 붙는 사고를 막는다.
             .Append("&prompt=select_account");
        return t_url.ToString();
    }

    static void OpenBrowser(string _url)
    {
        try { Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true }); }
        catch { EditorUtility.OpenWithDefaultApp(_url); }
    }

    /// <summary>루프백 소켓에서 리다이렉트 요청 한 건을 받아 code를 꺼낸다.</summary>
    static bool TryWaitForCode(TcpListener _listener, string _expectedState, out string _code, out string _error)
    {
        _code = null;
        _error = null;

        DateTime t_deadline = DateTime.UtcNow.AddSeconds(LISTEN_TIMEOUT_SECONDS);

        while (!_listener.Pending())
        {
            if (DateTime.UtcNow > t_deadline)
            {
                _error = $"{LISTEN_TIMEOUT_SECONDS}초 안에 브라우저 응답이 오지 않았다.";
                return false;
            }

            if (EditorUtility.DisplayCancelableProgressBar(
                    "구글 로그인", "브라우저에서 로그인을 마치면 자동으로 이어진다.", 0.5f))
            {
                _error = "사용자가 취소했다.";
                return false;
            }

            Thread.Sleep(POLL_INTERVAL_MS);
        }

        using TcpClient t_client = _listener.AcceptTcpClient();
        using NetworkStream t_stream = t_client.GetStream();

        string t_requestLine = ReadRequestLine(t_stream);
        // "GET /?code=...&state=... HTTP/1.1"
        string[] t_parts = t_requestLine.Split(' ');
        string t_target = t_parts.Length >= 2 ? t_parts[1] : string.Empty;

        Dictionary<string, string> t_query = ParseQuery(t_target);
        t_query.TryGetValue("error", out string t_oauthError);
        t_query.TryGetValue("code", out string t_code);
        t_query.TryGetValue("state", out string t_state);

        bool t_ok = string.IsNullOrEmpty(t_oauthError) && !string.IsNullOrEmpty(t_code) && t_state == _expectedState;
        WriteResponse(t_stream, t_ok);

        if (!string.IsNullOrEmpty(t_oauthError))
        {
            _error = $"구글이 로그인을 거부했다: {t_oauthError}";
            return false;
        }

        if (string.IsNullOrEmpty(t_code))
        {
            _error = "리다이렉트에 code가 없다.";
            return false;
        }

        // state 불일치 = 내가 시작하지 않은 콜백이다. CSRF 방어라 반드시 버린다.
        if (t_state != _expectedState)
        {
            _error = "state가 일치하지 않는다. 로그인을 다시 시도할 것.";
            return false;
        }

        _code = t_code;
        return true;
    }

    static string ReadRequestLine(NetworkStream _stream)
    {
        var t_builder = new StringBuilder(512);
        int t_byte;
        // 첫 줄만 필요하다. 헤더 본문은 읽지 않고 버린다.
        while ((t_byte = _stream.ReadByte()) >= 0)
        {
            if (t_byte == '\r') continue;
            if (t_byte == '\n') break;
            t_builder.Append((char)t_byte);
            if (t_builder.Length > 8192) break;
        }
        return t_builder.ToString();
    }

    static void WriteResponse(NetworkStream _stream, bool _ok)
    {
        string t_message = _ok
            ? "<h2>로그인 완료</h2><p>이 창을 닫고 Unity로 돌아가세요.</p>"
            : "<h2>로그인 실패</h2><p>Unity 콘솔의 오류를 확인하세요.</p>";

        string t_body = "<!doctype html><html><head><meta charset=\"utf-8\"></head>" +
                        "<body style=\"font-family:sans-serif;text-align:center;padding-top:60px\">" +
                        t_message + "</body></html>";

        byte[] t_bodyBytes = Encoding.UTF8.GetBytes(t_body);
        string t_header = "HTTP/1.1 200 OK\r\n" +
                          "Content-Type: text/html; charset=utf-8\r\n" +
                          $"Content-Length: {t_bodyBytes.Length}\r\n" +
                          "Connection: close\r\n\r\n";

        byte[] t_headerBytes = Encoding.ASCII.GetBytes(t_header);
        _stream.Write(t_headerBytes, 0, t_headerBytes.Length);
        _stream.Write(t_bodyBytes, 0, t_bodyBytes.Length);
        _stream.Flush();
    }

    static Dictionary<string, string> ParseQuery(string _target)
    {
        var t_result = new Dictionary<string, string>(StringComparer.Ordinal);
        int t_mark = _target.IndexOf('?');
        if (t_mark < 0) return t_result;

        foreach (string t_pair in _target.Substring(t_mark + 1).Split('&'))
        {
            if (t_pair.Length == 0) continue;
            int t_equals = t_pair.IndexOf('=');
            if (t_equals < 0) continue;
            t_result[Uri.UnescapeDataString(t_pair.Substring(0, t_equals))] =
                Uri.UnescapeDataString(t_pair.Substring(t_equals + 1));
        }
        return t_result;
    }

    static bool TryExchangeCode(
        string _code, string _verifier, string _redirectUri, out string _googleIdToken, out string _error)
    {
        _googleIdToken = null;
        _error = null;

        var t_form = new StringBuilder();
        t_form.Append("code=").Append(Uri.EscapeDataString(_code))
              .Append("&client_id=").Append(Uri.EscapeDataString(ClientId))
              .Append("&redirect_uri=").Append(Uri.EscapeDataString(_redirectUri))
              .Append("&grant_type=authorization_code")
              .Append("&code_verifier=").Append(Uri.EscapeDataString(_verifier));

        // 데스크톱 클라이언트는 secret을 요구한다. 비워둔 구성(순수 PKCE)도 있어 있을 때만 붙인다.
        if (!string.IsNullOrWhiteSpace(ClientSecret))
            t_form.Append("&client_secret=").Append(Uri.EscapeDataString(ClientSecret));

        try
        {
            using var t_client = new HttpClient { Timeout = TimeSpan.FromSeconds(FirebaseTimeouts.RestRequestSeconds) };
            using var t_content = new StringContent(
                t_form.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
            using HttpResponseMessage t_response = t_client.PostAsync(TOKEN_ENDPOINT, t_content)
                                                           .GetAwaiter().GetResult();
            string t_text = t_response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!t_response.IsSuccessStatusCode)
            {
                _error = $"토큰 교환 실패 {(int)t_response.StatusCode}: {Shorten(t_text)}";
                return false;
            }

            var t_parsed = UnityEngine.JsonUtility.FromJson<TokenResponse>(t_text);
            if (t_parsed == null || string.IsNullOrEmpty(t_parsed.id_token))
            {
                _error = "토큰 응답에 id_token이 없다. OAuth 클라이언트 범위에 openid가 있는지 확인할 것.";
                return false;
            }

            _googleIdToken = t_parsed.id_token;
            return true;
        }
        catch (Exception t_exception)
        {
            _error = $"토큰 교환 요청 실패: {t_exception.Message}";
            return false;
        }
    }

    static string CreateCodeVerifier()
    {
        var t_bytes = new byte[32];
        using var t_random = RandomNumberGenerator.Create();
        t_random.GetBytes(t_bytes);
        return Base64Url(t_bytes);
    }

    static string CreateCodeChallenge(string _verifier)
    {
        using var t_sha = SHA256.Create();
        return Base64Url(t_sha.ComputeHash(Encoding.ASCII.GetBytes(_verifier)));
    }

    static string Base64Url(byte[] _bytes)
        => Convert.ToBase64String(_bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string Shorten(string _text)
    {
        if (string.IsNullOrEmpty(_text)) return "(응답 없음)";
        return _text.Length <= 300 ? _text : _text.Substring(0, 300) + "…";
    }

    [Serializable] sealed class TokenResponse { public string id_token; public string access_token; public string refresh_token; }
}
