using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

public enum EBattleContentGateResult
{
    Current,
    OfflineAllowed,
    UpdatedRestartRequired,
    Blocked,
}

public static class BattleContentSync
{
    static readonly TimeSpan CheckTtl = TimeSpan.FromSeconds(60);
    static FirebaseContext s_context;
    static bool s_initialized;
    static DateTime s_lastCheckUtc;
    static string s_lastLocalFingerprint;
    static Task s_lateTask;

    public static void Initialize(in FirebaseContext _context)
    {
        s_context = _context;
        s_initialized = _context.IsValid;
    }

    public static void Shutdown()
    {
        s_context = default;
        s_initialized = false;
        s_lastCheckUtc = default;
        s_lastLocalFingerprint = null;
        s_lateTask = null;
    }

    public static async UniTask<EBattleContentGateResult> CheckBeforeBattleAsync(bool _multiplayer, CancellationToken _ct)
    {
        // 대조 결과를 콘솔에서 그대로 읽을 수 있어야 한다 — 모든 출구가 Verdict를 거쳐 한 줄씩 남긴다.
        var t_watch = System.Diagnostics.Stopwatch.StartNew();
        string t_mode = _multiplayer ? "멀티" : "싱글";

        EBattleContentGateResult Verdict(EBattleContentGateResult _result, string _reason)
        {
            string t_line = $"[BattleContent] 판정={_result} ({t_mode}) — {_reason} [{t_watch.ElapsedMilliseconds}ms]";
            if (_result == EBattleContentGateResult.Blocked) Debug.LogWarning(t_line);
            else Debug.Log(t_line);
            return _result;
        }

        EBattleContentGateResult Fallback(string _reason)
            => Verdict(_multiplayer ? EBattleContentGateResult.Blocked : EBattleContentGateResult.OfflineAllowed, _reason);

        if (!s_initialized) return Fallback("Firebase 모듈 미초기화");
        if (s_lateTask != null && !s_lateTask.IsCompleted) return Fallback("직전 조회가 아직 진행 중");
        try
        {
            string t_envId = s_context.EnvId;
            Debug.Log($"[BattleContent] 게이트 시작 env={t_envId} 모드={t_mode} schema=v{SpecPayloadCodec.SchemaVersion}");

            var t_localTables = new List<SpecTablePayload>();
            foreach (string t_tableName in SpecPayloadCodec.TableNames)
            {
                if (!SpecPayloadCodec.TryBuildLocalTable(SpecSource.Manager, t_tableName, out SpecTablePayload t_table, out string t_error))
                {
                    Debug.LogError($"[BattleContent] 로컬 스냅샷 생성 실패 table={t_tableName}: {t_error}");
                    return Verdict(EBattleContentGateResult.Blocked, $"로컬 표 '{t_tableName}' 생성 실패");
                }
                t_localTables.Add(t_table);
            }
            string t_localFingerprint = SpecPayloadCodec.CombinedHash(t_envId, t_localTables);

            var t_localLog = new StringBuilder();
            foreach (SpecTablePayload t_table in t_localTables)
                t_localLog.Append($"\n  {t_table.Table,-16} rows={t_table.Rows.Count,-5} hash={t_table.PayloadHash}");
            Debug.Log($"[BattleContent] 로컬 스냅샷 지문={t_localFingerprint} 전투지문={SpecSource.BattleFingerprint}{t_localLog}");

            if (string.Equals(t_localFingerprint, s_lastLocalFingerprint, StringComparison.Ordinal) &&
                DateTime.UtcNow - s_lastCheckUtc < CheckTtl)
                return Verdict(EBattleContentGateResult.Current,
                               $"TTL 유효({(int)(DateTime.UtcNow - s_lastCheckUtc).TotalSeconds}초 전 대조) — 서버 조회 생략");

            Task t_auth = FirebaseAuthService.Instance.InitializeAsync().AsTask();
            if (await Task.WhenAny(t_auth, Task.Delay(FirebaseTimeouts.AuthAndReadMilliseconds, _ct)) != t_auth)
            {
                TrackLate(t_auth);
                return Fallback($"인증 대기 {FirebaseTimeouts.AuthAndReadMilliseconds}ms 초과");
            }
            await t_auth;
            if (!FirebaseAuthService.Instance.IsCurrentUserActive)
                return Fallback("인증 사용자 비활성");

            Task<Dictionary<string, string>> t_metaTask = FetchMetaVectorAsync(t_envId);
            if (await Task.WhenAny(t_metaTask, Task.Delay(FirebaseTimeouts.TransactionMilliseconds, _ct)) != t_metaTask)
            {
                TrackLate(t_metaTask);
                return Fallback($"서버 메타 조회 {FirebaseTimeouts.TransactionMilliseconds}ms 초과");
            }
            Dictionary<string, string> t_remoteHashes = await t_metaTask;

            int t_mismatch = 0;
            var t_compare = new StringBuilder();
            foreach (SpecTablePayload t_table in t_localTables)
            {
                bool t_found = t_remoteHashes.TryGetValue(t_table.Table, out string t_remoteHash);
                bool t_match = t_found && string.Equals(t_table.PayloadHash, t_remoteHash, StringComparison.Ordinal);
                if (!t_match) t_mismatch++;
                string t_remoteText = t_found ? t_remoteHash : "(없음)";
                t_compare.Append($"\n  {t_table.Table,-16} 로컬={t_table.PayloadHash} 서버={t_remoteText,-16} {(t_match ? "일치" : "불일치")}");
            }
            Debug.Log($"[BattleContent] 스냅샷 대조 env={t_envId} 불일치 {t_mismatch}/{t_localTables.Count}{t_compare}");

            if (t_mismatch == 0)
            {
                s_lastLocalFingerprint = t_localFingerprint;
                s_lastCheckUtc = DateTime.UtcNow;
                return Verdict(EBattleContentGateResult.Current, "서버와 동일 — 그대로 전투 진입");
            }

            Debug.Log($"[BattleContent] 불일치 {t_mismatch}건 — 서버 스냅샷 전체 내려받기 시작");
            Task<string> t_downloadTask = DownloadFullSnapshotAsync(t_envId, t_remoteHashes);
            if (await Task.WhenAny(t_downloadTask, Task.Delay(FirebaseTimeouts.TransactionMilliseconds, _ct)) != t_downloadTask)
            {
                TrackLate(t_downloadTask);
                return Verdict(EBattleContentGateResult.Blocked,
                               $"스냅샷 다운로드 {FirebaseTimeouts.TransactionMilliseconds}ms 초과");
            }
            string t_payload = await t_downloadTask;
            var t_manager = new SpecDataManager();
            if (!t_manager.Load(t_payload)) throw new InvalidOperationException("Downloaded SpecData manager validation failed.");

            var t_tables = new List<SpecTablePayload>();
            foreach (string t_tableName in SpecPayloadCodec.TableNames)
            {
                if (!SpecPayloadCodec.TryBuildLocalTable(t_manager, t_tableName, out SpecTablePayload t_table, out string t_error))
                    throw new InvalidOperationException(t_error);
                t_tables.Add(t_table);
            }
            string t_fingerprint = SpecPayloadCodec.CombinedHash(t_envId, t_tables);
            if (!SpecSnapshotCache.TrySave(t_envId, t_payload, t_fingerprint, out string t_cacheError))
                throw new IOException("Spec cache write failed: " + t_cacheError);

            Debug.Log($"[BattleContent] 캐시 교체 완료 지문 {t_localFingerprint} -> {t_fingerprint} ({t_payload.Length:N0}자)");
            return Verdict(EBattleContentGateResult.UpdatedRestartRequired, "새 스냅샷 캐시 완료 — 재시작 후 적용");
        }
        catch (OperationCanceledException) { return Verdict(EBattleContentGateResult.Blocked, "취소됨"); }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[BattleContent] Server comparison failed: {t_exception.GetBaseException().Message}");
            return Fallback($"서버 대조 실패: {t_exception.GetBaseException().Message}");
        }
    }

    static async Task<string> DownloadFullSnapshotAsync(string _envId, Dictionary<string, string> _beforeHashes)
    {
        Task<SpecTablePayload>[] t_tasks = SpecPayloadCodec.TableNames.Select(t => FetchTableAsync(_envId, t)).ToArray();
        SpecTablePayload[] t_tables = await Task.WhenAll(t_tasks);
        Dictionary<string, string> t_afterHashes = await FetchMetaVectorAsync(_envId);
        if (_beforeHashes.Count != t_afterHashes.Count ||
            _beforeHashes.Any(t => !t_afterHashes.TryGetValue(t.Key, out string t_hash) ||
                                   !string.Equals(t.Value, t_hash, StringComparison.Ordinal)))
            throw new InvalidOperationException("Remote spec changed during download. Retry the battle entry.");
        Array.Sort(t_tables, (a, b) => Array.IndexOf(SpecPayloadCodec.TableNames, a.Table).CompareTo(Array.IndexOf(SpecPayloadCodec.TableNames, b.Table)));

        var t_log = new StringBuilder();
        foreach (SpecTablePayload t_table in t_tables)
            t_log.Append($"\n  {t_table.Table,-16} rows={t_table.Rows.Count,-5} hash={t_table.PayloadHash}");
        Debug.Log($"[BattleContent] 서버 스냅샷 수신 env={_envId}{t_log}");

        return SpecPayloadCodec.BuildManagerJson(t_tables);
    }

    static async Task<Dictionary<string, string>> FetchMetaVectorAsync(string _envId)
    {
        // async 람다는 반환형이 UniTask로 추론돼 Task.WhenAll에 넘길 수 없다 — 명시 메서드로 뺀다.
        Task<KeyValuePair<string, string>>[] t_tasks =
            SpecPayloadCodec.TableNames.Select(t_table => FetchMetaHashAsync(_envId, t_table)).ToArray();
        KeyValuePair<string, string>[] t_results = await Task.WhenAll(t_tasks);
        return t_results.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
    }

    static async Task<KeyValuePair<string, string>> FetchMetaHashAsync(string _envId, string _table)
    {
        FirebaseFirestore t_store = s_context.GetFirestore();
        string t_path = FirebaseRootPath.Environment(_envId) + "/specs/" + _table;
        DocumentSnapshot t_meta = await t_store.Document(t_path).GetSnapshotAsync(Source.Server);
        if (!t_meta.Exists) throw new InvalidOperationException($"Remote spec '{_table}' is missing.");
        IDictionary<string, object> t_fields = t_meta.ToDictionary();
        if (Convert.ToInt64(t_fields["schemaVersion"]) != SpecPayloadCodec.SchemaVersion)
            throw new InvalidOperationException($"Remote spec '{_table}' metadata is incompatible.");
        string t_hash = t_fields["payloadHash"] as string;
        if (string.IsNullOrEmpty(t_hash)) throw new InvalidOperationException($"Remote spec '{_table}' hash is missing.");
        return new KeyValuePair<string, string>(_table, t_hash);
    }

    static void TrackLate(Task _task)
    {
        s_lateTask = _task;
        _task.ContinueWith(t =>
        {
            _ = t.Exception;
            if (ReferenceEquals(s_lateTask, t)) s_lateTask = null;
        });
    }

    static async Task<SpecTablePayload> FetchTableAsync(string _envId, string _table)
    {
        FirebaseFirestore t_store = s_context.GetFirestore();
        string t_path = FirebaseRootPath.Environment(_envId) + "/specs/" + _table;
        for (int t_attempt = 0; t_attempt < 2; t_attempt++)
        {
            DocumentSnapshot t_meta = await t_store.Document(t_path).GetSnapshotAsync(Source.Server);
            if (!t_meta.Exists) throw new InvalidOperationException($"Remote spec '{_table}' is missing.");
            IDictionary<string, object> t_fields = t_meta.ToDictionary();
            long t_schema = Convert.ToInt64(t_fields["schemaVersion"]);
            long t_rowCount = Convert.ToInt64(t_fields["rowCount"]);
            string t_hash = t_fields["payloadHash"] as string;
            var t_columns = ((IEnumerable<object>)t_fields["columns"]).Select(v => v as string).ToList();
            if (t_schema != SpecPayloadCodec.SchemaVersion || string.IsNullOrEmpty(t_hash))
                throw new InvalidOperationException($"Remote spec '{_table}' metadata is incompatible.");

            QuerySnapshot t_rows = await t_store.Collection(t_path + "/rows").GetSnapshotAsync(Source.Server);
            var t_documents = t_rows.Documents.Select(d => (IDictionary<string, object>)d.ToDictionary()).ToList();
            string t_error = t_documents.Count == t_rowCount ? null : $"row count {t_documents.Count}/{t_rowCount}";
            if (t_documents.Count == t_rowCount &&
                SpecPayloadCodec.TryBuildRemoteTable(_table, t_columns, t_documents, out SpecTablePayload t_payload, out t_error) &&
                string.Equals(t_payload.PayloadHash, t_hash, StringComparison.Ordinal))
                return t_payload;
            if (t_attempt == 1) throw new InvalidOperationException($"Remote spec '{_table}' validation failed: {t_error}");
        }
        throw new InvalidOperationException($"Remote spec '{_table}' validation failed.");
    }
}

sealed class BattleContentFirebaseModule : IFirebaseModule
{
    public void Initialize(in FirebaseContext _context) => BattleContentSync.Initialize(in _context);
    public void RetryPending() { }
    public void FlushPending() { }
    public void Shutdown() => BattleContentSync.Shutdown();
}
