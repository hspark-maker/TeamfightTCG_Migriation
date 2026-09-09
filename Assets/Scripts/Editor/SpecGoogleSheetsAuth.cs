using System;
using System.Globalization;
using UnityEditor;

/// <summary>Google Sheets 업로드 인증. Firebase 관리자 세션과 분리하며 에디터 종료 때 토큰을 버린다.</summary>
public static class SpecGoogleSheetsAuth
{
    const string SCOPE = "https://www.googleapis.com/auth/spreadsheets";
    const string ACCESS_TOKEN_KEY = "SpecGoogleSheets.AccessToken";
    const string EXPIRES_AT_KEY = "SpecGoogleSheets.ExpiresAtUtcTicks";
    const int EXPIRY_MARGIN_SECONDS = 60;

    public static bool IsSignedIn => TryGetAccessToken(out _, out _);

    public static bool TrySignIn(out string _error)
    {
        if (!GoogleOAuthSignIn.TryAcquireGoogleAccessToken(
                SCOPE, out string t_token, out int t_expiresIn, out _error)) return false;

        SessionState.SetString(ACCESS_TOKEN_KEY, t_token);
        SessionState.SetString(EXPIRES_AT_KEY,
            DateTime.UtcNow.AddSeconds(t_expiresIn).Ticks.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    public static bool TryGetAccessToken(out string _token, out string _error)
    {
        _token = null;
        _error = null;
        string t_token = SessionState.GetString(ACCESS_TOKEN_KEY, string.Empty);
        if (string.IsNullOrEmpty(t_token))
        {
            _error = "Google Sheets에 연결해야 한다. 시트 업로드 탭에서 연결할 것.";
            return false;
        }
        if (!long.TryParse(SessionState.GetString(EXPIRES_AT_KEY, string.Empty),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out long t_expiresAt) ||
            t_expiresAt <= DateTime.UtcNow.AddSeconds(EXPIRY_MARGIN_SECONDS).Ticks)
        {
            _error = "Google Sheets 연결이 만료됐다. 시트 업로드 탭에서 다시 연결할 것.";
            return false;
        }
        _token = t_token;
        return true;
    }

    public static void SignOut()
    {
        SessionState.EraseString(ACCESS_TOKEN_KEY);
        SessionState.EraseString(EXPIRES_AT_KEY);
    }
}
