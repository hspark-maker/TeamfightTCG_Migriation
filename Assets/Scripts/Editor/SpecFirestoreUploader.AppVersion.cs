using System;
using System.Net;
using System.Net.Http;
using System.Text;

public static partial class SpecFirestoreUploader
{
    [Serializable] sealed class AppVersionPolicyFields
    {
        public FirestoreStringValue minSupported;
        public FirestoreStringValue latest;
        public FirestoreStringValue storeUrlAndroid;
        public FirestoreStringValue storeUrlIOS;
        public FirestoreStringValue storeUrl;
        public FirestoreStringValue noticeId;
        public FirestoreStringValue noticeTitle;
        public FirestoreStringValue noticeBody;
    }

    [Serializable] sealed class AppVersionPolicyDocument
    {
        public AppVersionPolicyFields fields;
        public string updateTime;
    }

    public sealed class AppVersionPolicyState
    {
        public bool Exists;
        public string UpdateTime;
        public string MinSupported = string.Empty;
        public string Latest = string.Empty;
        public string StoreUrlAndroid = string.Empty;
        public string StoreUrlIOS = string.Empty;
        public string StoreUrl = string.Empty;
        public string NoticeId = string.Empty;
        public string NoticeTitle = string.Empty;
        public string NoticeBody = string.Empty;
    }

    public static bool TryGetAppVersionPolicy(string _envId, out AppVersionPolicyState _state, out string _error)
    {
        _state = null;
        _error = null;
        if (!SpecAdminAuth.IsSignedIn)
        {
            _error = "앱 버전 정책 조회에는 로그인이 필요하다.";
            return false;
        }
        if (!TryReadFirebaseConfig(out string t_projectId, out string t_apiKey, out _error)) return false;

        using var t_client = new HttpClient { Timeout = TimeSpan.FromSeconds(FirebaseTimeouts.RestRequestSeconds) };
        string t_url = AppVersionPolicyUrl(t_projectId, _envId) + "?key=" + Uri.EscapeDataString(t_apiKey);
        if (!TrySend(t_client, HttpMethod.Get, t_url, null,
                     out HttpStatusCode t_status, out string t_text, out _error)) return false;

        if (t_status == HttpStatusCode.NotFound)
        {
            _state = new AppVersionPolicyState();
            return true;
        }
        if ((int)t_status < 200 || (int)t_status >= 300)
        {
            _error = $"앱 버전 정책 조회 실패 {(int)t_status}: {Shorten(t_text)}";
            return false;
        }

        AppVersionPolicyDocument t_document;
        try { t_document = UnityEngine.JsonUtility.FromJson<AppVersionPolicyDocument>(t_text); }
        catch (Exception t_exception)
        {
            _error = $"앱 버전 정책 응답 파싱 실패: {t_exception.Message}";
            return false;
        }

        AppVersionPolicyFields t_fields = t_document?.fields;
        _state = new AppVersionPolicyState
        {
            Exists = true,
            UpdateTime = t_document?.updateTime,
            MinSupported = TextOf(t_fields?.minSupported),
            Latest = TextOf(t_fields?.latest),
            StoreUrlAndroid = TextOf(t_fields?.storeUrlAndroid),
            StoreUrlIOS = TextOf(t_fields?.storeUrlIOS),
            StoreUrl = TextOf(t_fields?.storeUrl),
            NoticeId = TextOf(t_fields?.noticeId),
            NoticeTitle = TextOf(t_fields?.noticeTitle),
            NoticeBody = TextOf(t_fields?.noticeBody),
        };
        return true;
    }

    public static string UploadAppVersionPolicy(string _envId, AppVersionPolicyState _state, out string _error)
    {
        _error = null;
        if (!SpecAdminAuth.IsSignedIn || !SpecAdminAuth.HasAdminClaim)
        {
            _error = "관리자 권한이 있어야 앱 버전 정책을 올릴 수 있다.";
            return null;
        }
        if (_state == null)
        {
            _error = "올릴 앱 버전 정책이 없다.";
            return null;
        }
        if (!TryReadFirebaseConfig(out string t_projectId, out string t_apiKey, out _error)) return null;

        var t_builder = new StringBuilder(2048);
        t_builder.Append("{\"writes\":[{\"update\":{\"name\":");
        AppendJsonString(t_builder, AppVersionPolicyResourceName(t_projectId, _envId));
        t_builder.Append(",\"fields\":{");
        AppendAppVersionPolicyFields(t_builder, _state);
        t_builder.Append("}},\"currentDocument\":{");
        if (_state.Exists)
        {
            t_builder.Append("\"updateTime\":");
            AppendJsonString(t_builder, _state.UpdateTime);
        }
        else t_builder.Append("\"exists\":false");
        t_builder.Append("}}]}");

        using var t_client = new HttpClient { Timeout = TimeSpan.FromSeconds(FirebaseTimeouts.RestRequestSeconds) };
        if (!TryCommit(t_client, t_projectId, t_apiKey, t_builder.ToString(), out _error)) return null;
        return $"app policy 공개 (env={_envId}, min={_state.MinSupported}, latest={_state.Latest})";
    }

    static void AppendAppVersionPolicyFields(StringBuilder _builder, AppVersionPolicyState _state)
    {
        AppendStringField(_builder, "minSupported", Clean(_state.MinSupported));
        _builder.Append(',');
        AppendStringField(_builder, "latest", Clean(_state.Latest));
        _builder.Append(',');
        AppendStringField(_builder, "storeUrlAndroid", Clean(_state.StoreUrlAndroid));
        _builder.Append(',');
        AppendStringField(_builder, "storeUrlIOS", Clean(_state.StoreUrlIOS));
        _builder.Append(',');
        AppendStringField(_builder, "storeUrl", Clean(_state.StoreUrl));
        _builder.Append(',');
        AppendStringField(_builder, "noticeId", Clean(_state.NoticeId));
        _builder.Append(',');
        AppendStringField(_builder, "noticeTitle", Clean(_state.NoticeTitle));
        _builder.Append(',');
        AppendStringField(_builder, "noticeBody", Clean(_state.NoticeBody));
    }

    static string AppVersionPolicyUrl(string _projectId, string _envId) =>
        ApiRoot(_projectId) + "/documents/" + EscapedEnvironmentPath(_envId) + "/config/app";

    static string AppVersionPolicyResourceName(string _projectId, string _envId) =>
        "projects/" + _projectId + "/databases/" + FirebaseRootPath.DatabaseId + "/documents/" +
        FirebaseRootPath.Environment(_envId) + "/config/app";

    static string TextOf(FirestoreStringValue _value) => _value?.stringValue ?? string.Empty;
    static string Clean(string _value) => (_value ?? string.Empty).Trim();
}
