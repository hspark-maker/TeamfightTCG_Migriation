using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// SpecData 표를 Firestore의 메타 문서와 행 문서로 업로드한다.
/// 표 하나의 메타 갱신, 행 upsert, 사라진 행 삭제는 documents:commit 한 번으로 원자 반영한다.
/// </summary>
public static class SpecFirestoreUploader
{
    const string GOOGLE_SERVICES_PATH = "Assets/google-services.json";
    const string SPEC_COLLECTION = "specs";
    const string ROW_COLLECTION = "rows";
    const int SCHEMA_VERSION = SpecPayloadCodec.SchemaVersion;
    const int LIST_PAGE_SIZE = 300;
    const int MAX_COMMIT_WRITES = 500;
    const int MAX_COMMIT_BYTES = 10 * 1024 * 1024;
    const int MAX_ROW_DOCUMENT_WARN_BYTES = 900 * 1024;

    sealed class TableRow
    {
        public string Id;
        public object[] Values;
    }

    sealed class TableSnapshot
    {
        public FieldInfo[] Fields;
        public List<string> Columns;
        public List<TableRow> Rows;
        public string PayloadHash;
        public int PayloadBytes;
    }

    [Serializable] sealed class FirestoreIntegerValue { public string integerValue; }
    [Serializable] sealed class FirestoreStringValue { public string stringValue; }
    [Serializable] sealed class MetaFields
    {
        public FirestoreIntegerValue revision;
        public FirestoreStringValue payloadHash;
    }
    [Serializable] sealed class MetaDocument
    {
        public MetaFields fields;
        public string updateTime;
    }
    [Serializable] sealed class ListedDocument { public string name; }
    [Serializable] sealed class ListDocumentsResponse
    {
        public ListedDocument[] documents;
        public string nextPageToken;
    }

    [Serializable] sealed class GsProjectInfo { public string project_id; }
    [Serializable] sealed class GsApiKey { public string current_key; }
    [Serializable] sealed class GsClient { public GsApiKey[] api_key; }
    [Serializable] sealed class GsRoot { public GsProjectInfo project_info; public GsClient[] client; }

    public static List<string> ListTables(out string _error)
    {
        var t_names = new List<string>();
        if (!TryLoadManager(out object t_manager, out _error)) return t_names;

        foreach (KeyValuePair<string, IEnumerable> t_pair in EnumerateTables(t_manager))
            t_names.Add(t_pair.Key);

        t_names.Sort(StringComparer.Ordinal);
        return t_names;
    }

    /// <summary>
    /// 표 하나를 원자적으로 교체한다. 모든 작성자가 메타 문서를 함께 갱신한다는 전제 아래
    /// updateTime precondition이 동시 업로드를 차단한다. 실패한 commit body는 자동 재전송하지 않는다.
    /// </summary>
    public static string Upload(string _envId, string _table, out string _error)
    {
        _error = null;

        // 표를 스냅샷으로 뜨는 비용을 치르기 전에 자격부터 본다. 여기서 막지 않으면
        // 수십 초 걸리는 준비를 다 하고 첫 요청에서 403으로 죽는다.
        if (!SpecAdminAuth.IsSignedIn)
        {
            _error = "관리자 로그인이 필요하다. 데이터 탭에서 로그인한 뒤 다시 시도할 것.";
            return null;
        }

        if (!SpecAdminAuth.HasAdminClaim)
        {
            _error = $"'{SpecAdminAuth.SignedInEmail}' 계정에 admin 클레임이 없다. " +
                     "스펙 쓰기는 규칙에서 거부된다 — functions/scripts/grant-admin.js 로 클레임을 부여할 것.";
            return null;
        }

        if (!TryReadFirebaseConfig(out string t_projectId, out string t_apiKey, out _error)) return null;
        if (!TryLoadManager(out object t_manager, out _error)) return null;
        if (!TryBuildSnapshot(t_manager, _table, out TableSnapshot t_snapshot, out _error)) return null;

        using var t_client = new HttpClient { Timeout = TimeSpan.FromSeconds(FirebaseTimeouts.RestRequestSeconds) };

        if (!TryReadMeta(t_client, t_projectId, t_apiKey, _envId, _table,
                         out long t_currentRevision, out string t_updateTime, out bool t_metaExists, out _error))
            return null;

        if (t_currentRevision == long.MaxValue)
        {
            _error = $"{_table} revision이 최댓값이라 증가시킬 수 없다.";
            return null;
        }

        if (!TryListRemoteRowIds(t_client, t_projectId, t_apiKey, _envId, _table,
                                 out HashSet<string> t_remoteIds, out _error))
            return null;

        var t_localIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TableRow t_row in t_snapshot.Rows) t_localIds.Add(t_row.Id);
        t_remoteIds.ExceptWith(t_localIds);

        int t_writeCount = 1 + t_snapshot.Rows.Count + t_remoteIds.Count;
        if (t_writeCount > MAX_COMMIT_WRITES)
        {
            _error = $"{_table} 원자 커밋이 {t_writeCount} writes다. Firestore 한도 {MAX_COMMIT_WRITES}을 넘으므로 " +
                     "분할하지 않고 중단한다(행 갱신 + 삭제 + 메타 포함).";
            return null;
        }

        long t_revision = t_currentRevision + 1;
        string t_body = BuildCommitJson(
            t_projectId, _envId, _table, t_snapshot, t_remoteIds, t_revision, t_updateTime, t_metaExists);
        int t_commitBytes = Encoding.UTF8.GetByteCount(t_body);
        if (t_commitBytes > MAX_COMMIT_BYTES)
        {
            _error = $"{_table} commit 요청이 {t_commitBytes:N0}B로 Firestore 10MiB 한도를 넘는다.";
            return null;
        }

        if (!TryCommit(t_client, t_projectId, t_apiKey, t_body, out _error)) return null;

        return $"{_table}: rev {t_revision}, {t_snapshot.Rows.Count}행, 삭제 {t_remoteIds.Count}, " +
               $"{t_snapshot.PayloadBytes:N0}B → {FirebaseRootPath.Environment(_envId)}/{SPEC_COLLECTION}/{_table}";
    }

    static bool TryLoadManager(out object _manager, out string _error)
    {
        _manager = null;
        _error = null;

        string t_json = SpecDataResourceLoader.LoadSpecData();
        if (string.IsNullOrEmpty(t_json))
        {
            _error = "SpecData 리소스를 못 읽었다. CookApps > SpecData 창에서 '시트 적용 & CS 생성'을 먼저 실행할 것.";
            return false;
        }

        var t_manager = new SpecDataManager();
        if (!t_manager.Load(t_json))
        {
            _error = "SpecData 파싱 실패. 생성된 리소스가 손상됐을 수 있다(재생성 필요).";
            return false;
        }

        _manager = t_manager;
        return true;
    }

    static IEnumerable<KeyValuePair<string, IEnumerable>> EnumerateTables(object _manager)
    {
        foreach (PropertyInfo t_property in _manager.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (t_property.GetIndexParameters().Length > 0) continue;

            object t_container;
            try { t_container = t_property.GetValue(_manager); }
            catch (Exception) { continue; }
            if (t_container == null) continue;

            PropertyInfo t_all = t_container.GetType().GetProperty("All", BindingFlags.Public | BindingFlags.Instance);
            if (t_all?.GetValue(t_container) is IEnumerable t_rows)
                yield return new KeyValuePair<string, IEnumerable>(t_property.Name, t_rows);
        }
    }

    static bool TryBuildSnapshot(object _manager, string _table, out TableSnapshot _snapshot, out string _error)
    {
        _snapshot = null;
        _error = null;
        IEnumerable t_source = null;

        foreach (KeyValuePair<string, IEnumerable> t_pair in EnumerateTables(_manager))
        {
            if (!string.Equals(t_pair.Key, _table, StringComparison.Ordinal)) continue;
            t_source = t_pair.Value;
            break;
        }

        if (t_source == null)
        {
            _error = $"스펙시트에 '{_table}' 표가 없다.";
            return false;
        }

        FieldInfo[] t_fields = null;
        FieldInfo t_idField = null;
        var t_columns = new List<string>();
        var t_rows = new List<TableRow>();
        var t_ids = new HashSet<string>(StringComparer.Ordinal);
        var t_payload = new StringBuilder(4096);

        foreach (object t_sourceRow in t_source)
        {
            if (t_sourceRow == null)
            {
                _error = $"'{_table}' 표에 null 행이 있다.";
                return false;
            }

            if (t_fields == null)
            {
                t_fields = t_sourceRow.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo t_field in t_fields)
                {
                    if (!IsSupportedFieldType(t_field.FieldType))
                    {
                        _error = $"'{_table}.{t_field.Name}' 타입 {t_field.FieldType.Name}은 Firestore 행 변환을 지원하지 않는다.";
                        return false;
                    }
                    t_columns.Add(t_field.Name);
                    if (t_field.Name == "id") t_idField = t_field;
                }

                if (t_idField == null || t_idField.FieldType != typeof(int))
                {
                    _error = $"'{_table}' 표에는 int id 필드가 필요하다.";
                    return false;
                }

                t_payload.Append('[');
                AppendJsonStringArray(t_payload, t_columns);
            }

            var t_values = new object[t_fields.Length];
            var t_textValues = new List<string>(t_fields.Length);
            for (int i = 0; i < t_fields.Length; i++)
            {
                t_values[i] = t_fields[i].GetValue(t_sourceRow);
                t_textValues.Add(Text(t_values[i]));
            }

            var t_rowFields = new StringBuilder();
            AppendRowFields(t_rowFields, t_fields, t_values);
            int t_rowBytes = Encoding.UTF8.GetByteCount(t_rowFields.ToString());
            if (t_rowBytes > MAX_ROW_DOCUMENT_WARN_BYTES)
            {
                _error = $"'{_table}' id {t_idField.GetValue(t_sourceRow)} 행이 {t_rowBytes:N0}B다. " +
                         "Firestore 문서 1MiB 한도에 근접해 업로드하지 않는다.";
                return false;
            }

            string t_id = ((int)t_idField.GetValue(t_sourceRow)).ToString(CultureInfo.InvariantCulture);
            if (!t_ids.Add(t_id))
            {
                _error = $"'{_table}' 표에 중복 id {t_id}가 있다.";
                return false;
            }

            t_rows.Add(new TableRow { Id = t_id, Values = t_values });
            t_payload.Append(',');
            AppendJsonStringArray(t_payload, t_textValues);
        }

        if (t_fields == null)
        {
            _error = $"'{_table}' 표에 행이 하나도 없다.";
            return false;
        }

        t_rows.Sort((a, b) => int.Parse(a.Id, CultureInfo.InvariantCulture).CompareTo(int.Parse(b.Id, CultureInfo.InvariantCulture)));
        t_payload.Clear().Append('[');
        AppendJsonStringArray(t_payload, t_columns);
        foreach (TableRow t_row in t_rows)
        {
            var t_textValues = new List<string>(t_row.Values.Length);
            foreach (object t_value in t_row.Values) t_textValues.Add(Text(t_value));
            t_payload.Append(',');
            AppendJsonStringArray(t_payload, t_textValues);
        }
        t_payload.Append(']');
        string t_payloadText = t_payload.ToString();
        _snapshot = new TableSnapshot
        {
            Fields = t_fields,
            Columns = t_columns,
            Rows = t_rows,
            PayloadHash = HashOf(t_payloadText),
            PayloadBytes = Encoding.UTF8.GetByteCount(t_payloadText),
        };
        return true;
    }

    static bool IsSupportedFieldType(Type _type) =>
        _type == typeof(int) || _type == typeof(long) || _type == typeof(string);

    static string Text(object _value) => _value switch
    {
        null => "",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => _value.ToString(),
    };

    static bool TryReadMeta(
        HttpClient _client, string _projectId, string _apiKey, string _envId, string _table,
        out long _revision, out string _updateTime, out bool _exists, out string _error)
    {
        _revision = 0;
        _updateTime = null;
        _exists = false;
        _error = null;

        string t_url = DocumentUrl(_projectId, _envId, _table) + "?key=" + Uri.EscapeDataString(_apiKey);
        if (!TrySend(_client, HttpMethod.Get, t_url, null, out HttpStatusCode t_status, out string t_text, out _error))
            return false;

        if (t_status == HttpStatusCode.NotFound) return true;
        if ((int)t_status < 200 || (int)t_status >= 300)
        {
            _error = $"메타 조회 실패 {(int)t_status}: {Shorten(t_text)}";
            return false;
        }

        MetaDocument t_document;
        try { t_document = JsonUtility.FromJson<MetaDocument>(t_text); }
        catch (Exception t_exception)
        {
            _error = $"메타 응답 파싱 실패: {t_exception.Message}";
            return false;
        }

        _exists = true;
        _updateTime = t_document?.updateTime;
        string t_revision = t_document?.fields?.revision?.integerValue;
        if (!string.IsNullOrEmpty(t_revision) &&
            !long.TryParse(t_revision, NumberStyles.Integer, CultureInfo.InvariantCulture, out _revision))
        {
            _error = $"기존 메타 revision '{t_revision}'을 읽을 수 없다.";
            return false;
        }

        if (string.IsNullOrEmpty(_updateTime))
        {
            _error = "기존 메타 문서에 updateTime이 없다.";
            return false;
        }
        return true;
    }

    static bool TryListRemoteRowIds(
        HttpClient _client, string _projectId, string _apiKey, string _envId, string _table,
        out HashSet<string> _ids, out string _error)
    {
        _ids = new HashSet<string>(StringComparer.Ordinal);
        _error = null;
        string t_pageToken = null;
        var t_seenTokens = new HashSet<string>(StringComparer.Ordinal);

        do
        {
            var t_url = new StringBuilder(DocumentUrl(_projectId, _envId, _table));
            t_url.Append('/').Append(ROW_COLLECTION)
                 .Append("?pageSize=").Append(LIST_PAGE_SIZE)
                 .Append("&key=").Append(Uri.EscapeDataString(_apiKey));
            if (!string.IsNullOrEmpty(t_pageToken))
                t_url.Append("&pageToken=").Append(Uri.EscapeDataString(t_pageToken));

            if (!TrySend(_client, HttpMethod.Get, t_url.ToString(), null,
                         out HttpStatusCode t_status, out string t_text, out _error))
                return false;

            if ((int)t_status < 200 || (int)t_status >= 300)
            {
                _error = $"기존 행 목록 조회 실패 {(int)t_status}: {Shorten(t_text)}";
                return false;
            }

            ListDocumentsResponse t_response;
            try { t_response = JsonUtility.FromJson<ListDocumentsResponse>(t_text); }
            catch (Exception t_exception)
            {
                _error = $"행 목록 응답 파싱 실패: {t_exception.Message}";
                return false;
            }

            if (t_response?.documents != null)
            {
                foreach (ListedDocument t_document in t_response.documents)
                {
                    if (string.IsNullOrEmpty(t_document?.name)) continue;
                    int t_slash = t_document.name.LastIndexOf('/');
                    string t_id = t_slash >= 0 ? t_document.name.Substring(t_slash + 1) : t_document.name;
                    _ids.Add(Uri.UnescapeDataString(t_id));
                }
            }

            t_pageToken = t_response?.nextPageToken;
            if (!string.IsNullOrEmpty(t_pageToken) && !t_seenTokens.Add(t_pageToken))
            {
                _error = "Firestore 행 목록이 같은 pageToken을 반복했다.";
                return false;
            }
        }
        while (!string.IsNullOrEmpty(t_pageToken));

        return true;
    }

    static string BuildCommitJson(
        string _projectId, string _envId, string _table, TableSnapshot _snapshot,
        HashSet<string> _staleIds, long _revision, string _updateTime, bool _metaExists)
    {
        string t_metaName = ResourceName(_projectId, _envId, _table);
        var t_builder = new StringBuilder(Math.Max(4096, _snapshot.PayloadBytes * 2));
        t_builder.Append("{\"writes\":[");

        t_builder.Append("{\"update\":{\"name\":");
        AppendJsonString(t_builder, t_metaName);
        t_builder.Append(",\"fields\":");
        AppendMetaFields(t_builder, _table, _snapshot, _revision);
        t_builder.Append("},\"currentDocument\":{");
        if (_metaExists)
        {
            t_builder.Append("\"updateTime\":");
            AppendJsonString(t_builder, _updateTime);
        }
        else
        {
            t_builder.Append("\"exists\":false");
        }
        t_builder.Append("}}");

        foreach (TableRow t_row in _snapshot.Rows)
        {
            t_builder.Append(",{");
            t_builder.Append("\"update\":{\"name\":");
            AppendJsonString(t_builder, t_metaName + "/" + ROW_COLLECTION + "/" + t_row.Id);
            t_builder.Append(",\"fields\":");
            AppendRowFields(t_builder, _snapshot.Fields, t_row.Values);
            t_builder.Append("}}");
        }

        var t_orderedStale = new List<string>(_staleIds);
        t_orderedStale.Sort(StringComparer.Ordinal);
        foreach (string t_id in t_orderedStale)
        {
            t_builder.Append(",{\"delete\":");
            AppendJsonString(t_builder, t_metaName + "/" + ROW_COLLECTION + "/" + t_id);
            t_builder.Append('}');
        }

        t_builder.Append("]}");
        return t_builder.ToString();
    }

    static void AppendMetaFields(
        StringBuilder _builder, string _table, TableSnapshot _snapshot, long _revision)
    {
        _builder.Append('{');
        AppendStringField(_builder, "table", _table);
        _builder.Append(",\"schemaVersion\":{\"integerValue\":\"").Append(SCHEMA_VERSION).Append("\"}");
        _builder.Append(",\"revision\":{\"integerValue\":\"").Append(_revision).Append("\"}");
        _builder.Append(",\"rowCount\":{\"integerValue\":\"").Append(_snapshot.Rows.Count).Append("\"}");
        _builder.Append(",\"columns\":{\"arrayValue\":{\"values\":[");
        for (int i = 0; i < _snapshot.Columns.Count; i++)
        {
            if (i > 0) _builder.Append(',');
            _builder.Append("{\"stringValue\":");
            AppendJsonString(_builder, _snapshot.Columns[i]);
            _builder.Append('}');
        }
        _builder.Append("]}}");
        _builder.Append(',');
        AppendStringField(_builder, "idColumn", "id");
        _builder.Append(',');
        AppendStringField(_builder, "payloadHash", _snapshot.PayloadHash);
        _builder.Append(',');
        AppendStringField(_builder, "uploadedBy", Environment.UserName ?? "unknown");
        _builder.Append(',');
        AppendStringField(_builder, "appVersion", Application.version);
        _builder.Append(",\"updatedAt\":{\"timestampValue\":\"")
                .Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
                .Append("\"}");
        _builder.Append('}');
    }

    static void AppendRowFields(StringBuilder _builder, FieldInfo[] _fields, object[] _values)
    {
        _builder.Append('{');
        for (int i = 0; i < _fields.Length; i++)
        {
            if (i > 0) _builder.Append(',');
            _builder.Append('"').Append(_fields[i].Name).Append("\":");
            Type t_type = _fields[i].FieldType;
            if (t_type == typeof(int) || t_type == typeof(long))
            {
                _builder.Append("{\"integerValue\":");
                AppendJsonString(_builder, Text(_values[i]));
                _builder.Append('}');
            }
            else
            {
                _builder.Append("{\"stringValue\":");
                AppendJsonString(_builder, _values[i] as string ?? string.Empty);
                _builder.Append('}');
            }
        }
        _builder.Append('}');
    }

    static bool TryCommit(
        HttpClient _client, string _projectId, string _apiKey, string _body, out string _error)
    {
        string t_url = ApiRoot(_projectId) + "/documents:commit?key=" + Uri.EscapeDataString(_apiKey);
        if (!TrySend(_client, HttpMethod.Post, t_url, _body,
                     out HttpStatusCode t_status, out string t_text, out _error))
        {
            if (_error != null) _error += " 성공 여부가 불명확하므로 메타 revision을 확인한 뒤 다시 시도할 것.";
            return false;
        }

        if ((int)t_status >= 200 && (int)t_status < 300) return true;
        _error = $"Firestore commit 실패 {(int)t_status}: {Shorten(t_text)}";
        return false;
    }

    static bool TrySend(
        HttpClient _client, HttpMethod _method, string _url, string _body,
        out HttpStatusCode _status, out string _response, out string _error)
    {
        _status = 0;
        _response = null;
        _error = null;
        try
        {
            // 운영 규칙에서 스펙 문서는 admin 클레임을 가진 계정만 쓸 수 있고 읽기도 로그인이 필요하다.
            // API key는 프로젝트를 가리킬 뿐 신원이 아니라, 모든 요청에 ID 토큰을 함께 싣는다.
            if (!SpecAdminAuth.TryGetIdToken(out string t_idToken, out _error)) return false;

            using var t_request = new HttpRequestMessage(_method, _url);
            t_request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t_idToken);
            if (_body != null) t_request.Content = new StringContent(_body, Encoding.UTF8, "application/json");
            using HttpResponseMessage t_response = _client.SendAsync(t_request).GetAwaiter().GetResult();
            _status = t_response.StatusCode;
            _response = t_response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception t_exception)
        {
            _error = $"Firestore 요청 실패: {t_exception.Message}";
            return false;
        }
    }

    static string ApiRoot(string _projectId) =>
        "https://firestore.googleapis.com/v1/projects/" + Uri.EscapeDataString(_projectId) +
        "/databases/" + Uri.EscapeDataString(FirebaseRootPath.DatabaseId);

    static string DocumentUrl(string _projectId, string _envId, string _table) =>
        ApiRoot(_projectId) + "/documents/" + EscapedEnvironmentPath(_envId) +
        "/" + SPEC_COLLECTION + "/" + Uri.EscapeDataString(_table);

    static string EscapedEnvironmentPath(string _envId)
    {
        FirebaseRootPath.Environment(_envId);
        return FirebaseRootPath.EnvironmentCollection + "/" + Uri.EscapeDataString(_envId);
    }

    static string ResourceName(string _projectId, string _envId, string _table) =>
        "projects/" + _projectId + "/databases/" + FirebaseRootPath.DatabaseId + "/documents/" + FirebaseRootPath.Environment(_envId) +
        "/" + SPEC_COLLECTION + "/" + _table;

    /// <summary>google-services.json에서 프로젝트·API key를 읽는다.
    /// <see cref="SpecAdminAuth"/>가 로그인에 같은 API key를 써야 해서 internal이다.</summary>
    internal static bool TryReadFirebaseConfig(out string _projectId, out string _apiKey, out string _error)
    {
        _projectId = null;
        _apiKey = null;
        _error = null;

        var t_asset = AssetDatabase.LoadAssetAtPath<TextAsset>(GOOGLE_SERVICES_PATH);
        if (t_asset == null)
        {
            _error = $"{GOOGLE_SERVICES_PATH} 을 못 찾았다.";
            return false;
        }

        GsRoot t_root;
        try { t_root = JsonUtility.FromJson<GsRoot>(t_asset.text); }
        catch (Exception t_exception)
        {
            _error = $"{GOOGLE_SERVICES_PATH} 파싱 실패: {t_exception.Message}";
            return false;
        }

        _projectId = t_root?.project_info?.project_id;
        if (t_root?.client != null && t_root.client.Length > 0 &&
            t_root.client[0].api_key != null && t_root.client[0].api_key.Length > 0)
            _apiKey = t_root.client[0].api_key[0].current_key;

        if (string.IsNullOrEmpty(_projectId) || string.IsNullOrEmpty(_apiKey))
        {
            _error = $"{GOOGLE_SERVICES_PATH} 에서 project_id 또는 api_key 를 못 읽었다.";
            return false;
        }
        return true;
    }

    static void AppendStringField(StringBuilder _builder, string _name, string _value)
    {
        _builder.Append('"').Append(_name).Append("\":{\"stringValue\":");
        AppendJsonString(_builder, _value);
        _builder.Append('}');
    }

    static void AppendJsonStringArray(StringBuilder _builder, List<string> _values)
    {
        _builder.Append('[');
        for (int i = 0; i < _values.Count; i++)
        {
            if (i > 0) _builder.Append(',');
            AppendJsonString(_builder, _values[i]);
        }
        _builder.Append(']');
    }

    static void AppendJsonString(StringBuilder _builder, string _value)
    {
        _builder.Append('"');
        foreach (char t_char in _value ?? string.Empty)
        {
            switch (t_char)
            {
                case '"': _builder.Append("\\\""); break;
                case '\\': _builder.Append("\\\\"); break;
                case '\n': _builder.Append("\\n"); break;
                case '\r': _builder.Append("\\r"); break;
                case '\t': _builder.Append("\\t"); break;
                default:
                    if (t_char < 0x20) _builder.Append("\\u").Append(((int)t_char).ToString("x4"));
                    else _builder.Append(t_char);
                    break;
            }
        }
        _builder.Append('"');
    }

    static string HashOf(string _payload)
    {
        using var t_md5 = MD5.Create();
        byte[] t_hash = t_md5.ComputeHash(Encoding.UTF8.GetBytes(_payload));
        var t_builder = new StringBuilder(16);
        for (int i = 0; i < 8; i++) t_builder.Append(t_hash[i].ToString("x2"));
        return t_builder.ToString();
    }

    static string Shorten(string _text)
    {
        if (string.IsNullOrEmpty(_text)) return "(본문 없음)";
        return _text.Length <= 400 ? _text : _text.Substring(0, 400) + "…";
    }
}
