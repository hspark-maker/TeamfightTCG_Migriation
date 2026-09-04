using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;

internal sealed class BattleSpecRepository
{
    // 재생이 실제로 읽는 표. 파싱 캐시 키가 이 4표의 payloadHash 조합이라 어느 하나가 재발행되면
    // 캐시가 정확히 무효화된다.
    static readonly string[] ReplayTables =
    {
        "Card", "SynergyDef", "SynergyTierDef", "SynergyEffectDef",
    };

    // ⚠ 클라가 보내는 contentFingerprint 와 대조하는 범위. **여기에 표를 추가하지 마라** —
    // 배포된 클라의 BattleFingerprint 가 Card 만으로 지문을 계산하므로, 늘리는 순간 모든 요청이
    // content_fingerprint_mismatch 로 거절된다.
    //
    // 이 지문은 "클라가 어느 Card 표 세대를 보고 있었나"만 답한다. 시너지 3표는 여기 없다 —
    // **규칙 세대 고정은 지문이 아니라 매치 문서의 specPins 가 한다.** 매치 생성 시 4표의
    // payloadHash·blobPath 를 박아두고 재생은 그 고정본만 읽으므로(GetAsync 는 pin 없으면 실패),
    // 전투 도중 표가 재발행돼도 그 판의 규칙은 바뀌지 않는다. 두 장치의 역할을 섞지 말 것.
    static readonly string[] BattleFingerprintTables = { "Card" };

    readonly FirestoreDb db;
    readonly ConcurrentDictionary<string, Lazy<Task<BattleRuleSet>>> parsed = new();

    public BattleSpecRepository(FirestoreDb _db) => db = _db;

    public async Task<BattleRuleSet> GetAsync(
        string _env,
        string _requestedFingerprint,
        IReadOnlyDictionary<string, ReplaySpecPinDto> _pins,
        CancellationToken _cancellationToken)
    {
        var t_pinnedTables = new Dictionary<string, PublishedTable>(StringComparer.Ordinal);
        foreach (string t_name in ReplayTables)
        {
            if (!_pins.TryGetValue(t_name, out ReplaySpecPinDto? t_pin))
                throw new SpecLoadException("spec_pin_missing:" + t_name);
            t_pinnedTables[t_name] = new PublishedTable(t_pin.BlobPath, t_pin.PayloadHash.ToLowerInvariant());
        }
        var t_index = new PublishedIndex(_env, t_pinnedTables);
        string t_actualFingerprint = CombinedFingerprint(_env, t_index.Tables, BattleFingerprintTables);
        if (!string.Equals(t_actualFingerprint, _requestedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ContentFingerprintException("content_fingerprint_mismatch");

        string t_key = _env + "/" + string.Join("/", ReplayTables.Select(
            _name => _name + "=" + t_index.Tables[_name].PayloadHash));
        Lazy<Task<BattleRuleSet>> t_lazy = parsed.GetOrAdd(t_key, _ => new Lazy<Task<BattleRuleSet>>(
            () => LoadRulesAsync(t_index, CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await t_lazy.Value.WaitAsync(_cancellationToken);
        }
        catch
        {
            parsed.TryRemove(new KeyValuePair<string, Lazy<Task<BattleRuleSet>>>(t_key, t_lazy));
            throw;
        }
    }

    async Task<BattleRuleSet> LoadRulesAsync(PublishedIndex _index, CancellationToken _cancellationToken)
    {
        var t_payloads = new Dictionary<string, SpecTable>(StringComparer.Ordinal);
        foreach (string t_name in ReplayTables)
        {
            PublishedTable t_published = _index.Tables[t_name];
            DocumentSnapshot t_snapshot = await db.Document(t_published.BlobPath)
                .GetSnapshotAsync(_cancellationToken);
            if (!t_snapshot.Exists) throw new SpecLoadException("spec_blob_missing:" + t_name);
            if (!t_snapshot.TryGetValue("payload", out string? t_payload) || string.IsNullOrEmpty(t_payload)
                || !t_snapshot.TryGetValue("payloadHash", out string? t_hashText) || string.IsNullOrEmpty(t_hashText)
                || !t_snapshot.TryGetValue("rowCount", out long t_rowCount))
                throw new SpecLoadException("spec_blob_field_missing:" + t_name);
            string t_documentHash = t_hashText.ToLowerInvariant();
            string t_actualHash = PayloadHash(t_payload);
            if (t_documentHash != t_actualHash || t_published.PayloadHash != t_actualHash)
                throw new SpecLoadException("spec_blob_hash_mismatch:" + t_name);

            SpecTable t_table = SpecTable.Parse(t_name, t_payload);
            if (t_rowCount != t_table.Rows.Count)
                throw new SpecLoadException("spec_blob_row_count_mismatch:" + t_name);
            t_payloads[t_name] = t_table;
        }
        return BattleRuleSet.Create(t_payloads);
    }

    static string CombinedFingerprint(
        string _env,
        IReadOnlyDictionary<string, PublishedTable> _tables,
        IReadOnlyList<string> _tableNames)
    {
        var t_text = new StringBuilder(_env);
        foreach (string t_name in _tableNames)
            t_text.Append('|').Append(t_name).Append('=').Append(_tables[t_name].PayloadHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(t_text.ToString()))).ToLowerInvariant();
    }

    static string PayloadHash(string _payload)
    {
        byte[] t_hash = MD5.HashData(Encoding.UTF8.GetBytes(_payload));
        return Convert.ToHexString(t_hash, 0, 8).ToLowerInvariant();
    }

    sealed record PublishedIndex(string Env, IReadOnlyDictionary<string, PublishedTable> Tables);
    sealed record PublishedTable(string BlobPath, string PayloadHash);
}

internal sealed class SpecTable
{
    public string Name { get; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }

    SpecTable(string _name, IReadOnlyList<IReadOnlyDictionary<string, string>> _rows)
    {
        Name = _name;
        Rows = _rows;
    }

    public static SpecTable Parse(string _name, string _payload)
    {
        List<List<string>>? t_matrix;
        try { t_matrix = JsonSerializer.Deserialize<List<List<string>>>(_payload); }
        catch (JsonException t_exception) { throw new SpecLoadException("spec_payload_invalid:" + _name, t_exception); }
        if (t_matrix == null || t_matrix.Count < 2 || t_matrix[0].Count == 0)
            throw new SpecLoadException("spec_payload_empty:" + _name);

        List<string> t_columns = t_matrix[0];
        var t_rows = new List<IReadOnlyDictionary<string, string>>(t_matrix.Count - 1);
        for (int i = 1; i < t_matrix.Count; i++)
        {
            if (t_matrix[i].Count != t_columns.Count)
                throw new SpecLoadException("spec_payload_shape:" + _name);
            var t_row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int j = 0; j < t_columns.Count; j++)
                if (!t_row.TryAdd(t_columns[j], t_matrix[i][j]))
                    throw new SpecLoadException("spec_payload_duplicate_column:" + _name);
            t_rows.Add(t_row);
        }
        return new SpecTable(_name, t_rows);
    }
}

internal sealed class SpecLoadException : Exception
{
    public SpecLoadException(string _message) : base(_message) { }
    public SpecLoadException(string _message, Exception _inner) : base(_message, _inner) { }
}

internal sealed class ContentFingerprintException : Exception
{
    public ContentFingerprintException(string _message) : base(_message) { }
}
