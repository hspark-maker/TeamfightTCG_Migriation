using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>스펙 업로더용 관리자 로그인. Firestore 운영 규칙이 스펙 쓰기를
/// <c>admin</c> 커스텀 클레임에만 허용하므로, 업로더는 API key만으로는 더 이상 쓸 수 없다.
///
/// <para>Identity Toolkit REST로 이메일·비밀번호 로그인해 ID 토큰을 받고,
/// <see cref="SpecFirestoreUploader"/>가 모든 Firestore 요청에 Bearer로 싣는다.</para>
///
/// <para><b>토큰 보관은 <see cref="SessionState"/>다.</b> 도메인 리로드(스크립트 컴파일)는 넘기고
/// 유니티를 닫으면 사라진다. EditorPrefs(레지스트리)에 두지 않는 이유는 refresh 토큰이
/// 스펙 쓰기 권한을 가진 장기 자격증명이기 때문이다 — 디스크에 남기지 않는다.
/// 비밀번호는 어디에도 저장하지 않는다.</para></summary>
public static class SpecAdminAuth
{
    const string EMAIL_PREF_KEY    = "SpecFirestore.AdminEmail";
    const string ID_TOKEN_KEY      = "SpecFirestore.IdToken";
    const string REFRESH_TOKEN_KEY = "SpecFirestore.RefreshToken";
    const string EXPIRES_AT_KEY    = "SpecFirestore.ExpiresAtUtcTicks";
    const string SIGNED_EMAIL_KEY  = "SpecFirestore.SignedInEmail";

    // 만료 직전에 굴린다. 업로드 하나가 수십 초 걸릴 수 있어 여유를 크게 잡는다.
    const int REFRESH_MARGIN_SECONDS = 300;

    const string SIGN_IN_URL     = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=";
    const string SIGN_IN_IDP_URL = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=";
    const string REFRESH_URL     = "https://securetoken.googleapis.com/v1/token?key=";

    /// <summary>마지막으로 입력한 이메일. 편의용이라 이것만 EditorPrefs에 남긴다.</summary>
    public static string LastEmail
    {
        get => EditorPrefs.GetString(EMAIL_PREF_KEY, string.Empty);
        set => EditorPrefs.SetString(EMAIL_PREF_KEY, value ?? string.Empty);
    }

    public static bool IsSignedIn => !string.IsNullOrEmpty(SessionState.GetString(REFRESH_TOKEN_KEY, string.Empty));

    public static string SignedInEmail => SessionState.GetString(SIGNED_EMAIL_KEY, string.Empty);

    /// <summary>로그인한 계정이 admin 클레임을 실제로 갖고 있는가. ID 토큰 payload를 그대로 읽는다.
    /// false면 업로드는 규칙에서 거부되므로 미리 알려준다.
    /// 캐시하지 않고 매번 토큰에서 읽는 이유는 도메인 리로드로 static이 날아가도
    /// SessionState의 토큰은 남기 때문이다 — 캐시하면 컴파일 후 false로 오인한다.</summary>
    public static bool HasAdminClaim => ReadAdminClaim(SessionState.GetString(ID_TOKEN_KEY, string.Empty));

    public static void SignOut()
    {
        SessionState.EraseString(ID_TOKEN_KEY);
        SessionState.EraseString(REFRESH_TOKEN_KEY);
        SessionState.EraseString(EXPIRES_AT_KEY);
        SessionState.EraseString(SIGNED_EMAIL_KEY);
    }

    public static bool TrySignIn(string _email, string _password, out string _error)
    {
        _error = null;

        if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrEmpty(_password))
        {
            _error = "이메일과 비밀번호를 입력해야 한다.";
            return false;
        }

        if (!SpecFirestoreUploader.TryReadFirebaseConfig(out _, out string t_apiKey, out _error)) return false;

        string t_body = JsonUtility.ToJson(new SignInRequest
        {
            email = _email.Trim(),
            password = _password,
            returnSecureToken = true,
        });

        if (!TryPostJson(SIGN_IN_URL + Uri.EscapeDataString(t_apiKey), t_body, out string t_response, out _error))
            return false;

        SignInResponse t_parsed;
        try { t_parsed = JsonUtility.FromJson<SignInResponse>(t_response); }
        catch (Exception t_exception)
        {
            _error = $"로그인 응답 파싱 실패: {t_exception.Message}";
            return false;
        }

        if (t_parsed == null || string.IsNullOrEmpty(t_parsed.idToken))
        {
            _error = "로그인 응답에 idToken이 없다.";
            return false;
        }

        Store(t_parsed.idToken, t_parsed.refreshToken, t_parsed.expiresIn);
        SessionState.SetString(SIGNED_EMAIL_KEY, _email.Trim());
        LastEmail = _email.Trim();
        return true;
    }

    /// <summary>구글 계정으로 로그인한다. 브라우저 OAuth로 구글 ID 토큰을 받아
    /// accounts:signInWithIdp 로 Firebase ID 토큰과 바꾼다. 이후 취급은 비밀번호 로그인과 완전히 같다 —
    /// admin 클레임도, 토큰 갱신도 같은 경로를 탄다.</summary>
    public static bool TrySignInWithGoogle(out string _error)
    {
        _error = null;

        if (!GoogleOAuthSignIn.TryAcquireGoogleIdToken(out string t_googleIdToken, out _error)) return false;
        if (!SpecFirestoreUploader.TryReadFirebaseConfig(out _, out string t_apiKey, out _error)) return false;

        string t_body = JsonUtility.ToJson(new IdpRequest
        {
            // postBody는 폼 인코딩 문자열을 JSON 안에 넣는 Identity Toolkit 특유의 형식이다.
            postBody = "id_token=" + t_googleIdToken + "&providerId=google.com",
            // 루프백 포트가 매번 바뀌므로 고정값을 쓴다. 구글 로그인 검증에는 쓰이지 않는다.
            requestUri = "http://localhost",
            returnSecureToken = true,
        });

        if (!TryPostJson(SIGN_IN_IDP_URL + Uri.EscapeDataString(t_apiKey), t_body,
                         out string t_response, out _error))
            return false;

        IdpResponse t_parsed;
        try { t_parsed = JsonUtility.FromJson<IdpResponse>(t_response); }
        catch (Exception t_exception)
        {
            _error = $"구글 로그인 응답 파싱 실패: {t_exception.Message}";
            return false;
        }

        if (t_parsed == null || string.IsNullOrEmpty(t_parsed.idToken))
        {
            _error = "구글 로그인 응답에 idToken이 없다.";
            return false;
        }

        Store(t_parsed.idToken, t_parsed.refreshToken, t_parsed.expiresIn);
        SessionState.SetString(SIGNED_EMAIL_KEY, t_parsed.email ?? "(구글 계정)");
        return true;
    }

    /// <summary>유효한 ID 토큰을 준다. 만료가 가까우면 refresh 토큰으로 먼저 굴린다.</summary>
    public static bool TryGetIdToken(out string _idToken, out string _error)
    {
        _idToken = null;
        _error = null;

        string t_refresh = SessionState.GetString(REFRESH_TOKEN_KEY, string.Empty);
        if (string.IsNullOrEmpty(t_refresh))
        {
            _error = "관리자 로그인이 필요하다. 데이터 탭에서 로그인할 것.";
            return false;
        }

        long t_ticks = 0L;
        long.TryParse(SessionState.GetString(EXPIRES_AT_KEY, "0"), out t_ticks);
        var t_expiresAt = new DateTime(t_ticks, DateTimeKind.Utc);
        if (DateTime.UtcNow.AddSeconds(REFRESH_MARGIN_SECONDS) < t_expiresAt)
        {
            _idToken = SessionState.GetString(ID_TOKEN_KEY, string.Empty);
            if (!string.IsNullOrEmpty(_idToken)) return true;
        }

        if (!SpecFirestoreUploader.TryReadFirebaseConfig(out _, out string t_apiKey, out _error)) return false;

        string t_body = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(t_refresh);
        if (!TryPostForm(REFRESH_URL + Uri.EscapeDataString(t_apiKey), t_body, out string t_response, out _error))
        {
            _error = $"토큰 갱신 실패({_error}). 다시 로그인할 것.";
            SignOut();
            return false;
        }

        RefreshResponse t_parsed;
        try { t_parsed = JsonUtility.FromJson<RefreshResponse>(t_response); }
        catch (Exception t_exception)
        {
            _error = $"갱신 응답 파싱 실패: {t_exception.Message}";
            return false;
        }

        if (t_parsed == null || string.IsNullOrEmpty(t_parsed.id_token))
        {
            _error = "갱신 응답에 id_token이 없다. 다시 로그인할 것.";
            SignOut();
            return false;
        }

        Store(t_parsed.id_token, t_parsed.refresh_token, t_parsed.expires_in);
        _idToken = t_parsed.id_token;
        return true;
    }

    static void Store(string _idToken, string _refreshToken, string _expiresInSeconds)
    {
        SessionState.SetString(ID_TOKEN_KEY, _idToken);
        if (!string.IsNullOrEmpty(_refreshToken)) SessionState.SetString(REFRESH_TOKEN_KEY, _refreshToken);

        int t_seconds = 3600;
        if (!string.IsNullOrEmpty(_expiresInSeconds) && int.TryParse(_expiresInSeconds, out int t_parsed))
            t_seconds = t_parsed;
        SessionState.SetString(EXPIRES_AT_KEY, DateTime.UtcNow.AddSeconds(t_seconds).Ticks.ToString());
    }

    /// <summary>JWT payload(가운데 조각)를 base64url 디코드해 admin 클레임만 확인한다.
    /// 서명 검증은 하지 않는다 — 토큰을 신뢰할지는 서버가 정하고, 여기서는 "내가 올릴 수 있는가"만 미리 본다.</summary>
    static bool ReadAdminClaim(string _idToken)
    {
        try
        {
            string[] t_parts = _idToken.Split('.');
            if (t_parts.Length < 2) return false;

            string t_payload = t_parts[1].Replace('-', '+').Replace('_', '/');
            switch (t_payload.Length % 4)
            {
                case 2: t_payload += "=="; break;
                case 3: t_payload += "=";  break;
            }

            string t_json = Encoding.UTF8.GetString(Convert.FromBase64String(t_payload));
            AdminClaim t_claim = JsonUtility.FromJson<AdminClaim>(t_json);
            return t_claim != null && t_claim.admin;
        }
        catch
        {
            return false;
        }
    }

    static bool TryPostJson(string _url, string _body, out string _response, out string _error)
        => TrySend(_url, new StringContent(_body, Encoding.UTF8, "application/json"), out _response, out _error);

    static bool TryPostForm(string _url, string _body, out string _response, out string _error)
        => TrySend(_url, new StringContent(_body, Encoding.UTF8, "application/x-www-form-urlencoded"),
                   out _response, out _error);

    static bool TrySend(string _url, HttpContent _content, out string _response, out string _error)
    {
        _response = null;
        _error = null;
        try
        {
            using var t_client = new HttpClient { Timeout = TimeSpan.FromSeconds(FirebaseTimeouts.RestRequestSeconds) };
            using HttpResponseMessage t_result = t_client.PostAsync(_url, _content).GetAwaiter().GetResult();
            string t_text = t_result.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (t_result.IsSuccessStatusCode)
            {
                _response = t_text;
                return true;
            }

            _error = $"{(int)t_result.StatusCode}: {Describe(t_text)}";
            return false;
        }
        catch (Exception t_exception)
        {
            _error = t_exception.Message;
            return false;
        }
    }

    /// <summary>Identity Toolkit 오류 코드를 사람 말로 바꾼다. 못 알아보면 원문 앞부분을 그대로 보여준다.</summary>
    static string Describe(string _responseText)
    {
        if (string.IsNullOrEmpty(_responseText)) return "(응답 없음)";

        foreach (KeyValuePair<string, string> t_known in s_knownErrors)
            if (_responseText.IndexOf(t_known.Key, StringComparison.Ordinal) >= 0)
                return t_known.Value;

        return _responseText.Length <= 200 ? _responseText : _responseText.Substring(0, 200) + "…";
    }

    static readonly Dictionary<string, string> s_knownErrors = new Dictionary<string, string>
    {
        { "EMAIL_NOT_FOUND",              "등록되지 않은 이메일이다." },
        { "INVALID_PASSWORD",             "비밀번호가 틀렸다." },
        { "INVALID_LOGIN_CREDENTIALS",    "이메일 또는 비밀번호가 틀렸다." },
        { "USER_DISABLED",                "비활성화된 계정이다." },
        { "TOO_MANY_ATTEMPTS_TRY_LATER",  "시도가 너무 잦다. 잠시 후 다시 할 것." },
        { "OPERATION_NOT_ALLOWED",        "콘솔에서 이메일·비밀번호 로그인이 꺼져 있다." },
        { "TOKEN_EXPIRED",                "토큰이 만료됐다. 다시 로그인할 것." },
        { "USER_NOT_FOUND",               "계정을 찾을 수 없다. 다시 로그인할 것." },
        { "INVALID_IDP_RESPONSE",         "구글 토큰을 Firebase가 거부했다. OAuth 클라이언트가 같은 프로젝트 것인지 확인할 것." },
        { "OPERATION_NOT_ALLOWED : GOOGLE", "콘솔에서 구글 로그인 제공업체가 꺼져 있다." },
        { "FEDERATED_USER_ID_ALREADY_LINKED", "이미 다른 계정에 연결된 구글 ID다." },
    };

    [Serializable] sealed class IdpRequest      { public string postBody; public string requestUri; public bool returnSecureToken; }
    [Serializable] sealed class IdpResponse     { public string idToken; public string refreshToken; public string expiresIn; public string email; }
    [Serializable] sealed class SignInRequest   { public string email; public string password; public bool returnSecureToken; }
    [Serializable] sealed class SignInResponse  { public string idToken; public string refreshToken; public string expiresIn; public string localId; }
    [Serializable] sealed class RefreshResponse { public string id_token; public string refresh_token; public string expires_in; }
    [Serializable] sealed class AdminClaim      { public bool admin; }
}
