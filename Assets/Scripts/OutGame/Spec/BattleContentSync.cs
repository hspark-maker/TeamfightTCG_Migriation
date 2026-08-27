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

    /// <summary>시간 초과로 놓아준 조회를 "아직 도는 중"으로 인정하는 상한. 이걸 넘기면 버려진 것으로 본다.</summary>
    static readonly TimeSpan LateTaskGrace = TimeSpan.FromSeconds(30);

    static FirebaseContext s_context;
    static bool s_initialized;
    static DateTime s_lastCheckUtc;
    static string s_lastLocalFingerprint;
    static Task s_lateTask;
    static DateTime s_lateTaskStartedUtc;

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
        s_lateTaskStartedUtc = default;
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
        if (IsLateTaskBlocking(out string t_lateReason)) return Fallback(t_lateReason);
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

            Debug.Log($"[BattleContent] 불일치 {t_mismatch}건 — 불일치 표만 내려받기 시작");
            Task<string> t_downloadTask = DownloadSnapshotAsync(t_envId, t_remoteHashes, t_localTables);
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

    /// <summary>불일치 표만 서버에서 받고, 해시가 이미 같은 표는 로컬 것을 그대로 쓴다.
    /// 표 하나 바뀌었다고 여섯 표의 행 문서를 전부 다시 읽으면 read가 표 수만큼 곱해진다.</summary>
    static async Task<string> DownloadSnapshotAsync(
        string _envId, Dictionary<string, string> _beforeHashes, List<SpecTablePayload> _localTables)
    {
        var t_byTable = new Dictionary<string, SpecTablePayload>(StringComparer.Ordinal);
        var t_stale = new List<string>();
        foreach (SpecTablePayload t_local in _localTables)
        {
            if (_beforeHashes.TryGetValue(t_local.Table, out string t_remoteHash) &&
                string.Equals(t_local.PayloadHash, t_remoteHash, StringComparison.Ordinal))
                t_byTable[t_local.Table] = t_local;
            else
                t_stale.Add(t_local.Table);
        }

        Task<SpecTablePayload>[] t_tasks = t_stale
            .Select(t => FetchTableAsync(_envId, t, _beforeHashes.TryGetValue(t, out string t_hash) ? t_hash : null))
            .ToArray();
        SpecTablePayload[] t_fetched = await Task.WhenAll(t_tasks);
        foreach (SpecTablePayload t_table in t_fetched) t_byTable[t_table.Table] = t_table;

        Dictionary<string, string> t_afterHashes = await FetchMetaVectorAsync(_envId);
        if (_beforeHashes.Count != t_afterHashes.Count ||
            _beforeHashes.Any(t => !t_afterHashes.TryGetValue(t.Key, out string t_hash) ||
                                   !string.Equals(t.Value, t_hash, StringComparison.Ordinal)))
            throw new InvalidOperationException("Remote spec changed during download. Retry the battle entry.");

        var t_tables = new SpecTablePayload[SpecPayloadCodec.TableNames.Length];
        for (int i = 0; i < SpecPayloadCodec.TableNames.Length; i++)
        {
            string t_name = SpecPayloadCodec.TableNames[i];
            if (!t_byTable.TryGetValue(t_name, out t_tables[i]))
                throw new InvalidOperationException($"Spec table '{t_name}' missing after download.");
        }

        var t_log = new StringBuilder();
        foreach (SpecTablePayload t_table in t_tables)
            t_log.Append($"\n  {t_table.Table,-16} rows={t_table.Rows.Count,-5} hash={t_table.PayloadHash} " +
                         $"{(t_stale.Contains(t_table.Table) ? "수신" : "로컬재사용")}");
        Debug.Log($"[BattleContent] 스냅샷 구성 env={_envId} 수신 {t_stale.Count}/{t_tables.Length}표{t_log}");

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
        s_lateTaskStartedUtc = DateTime.UtcNow;
        _task.ContinueWith(t =>
        {
            _ = t.Exception;
            if (ReferenceEquals(s_lateTask, t)) s_lateTask = null;
        });
    }

    /// <summary>직전에 시간 초과로 놓아준 조회가 아직 도는 중인가. 중복 조회를 막는 장치지만
    /// <b>영원히 막으면 안 된다</b> — Firestore 호출이 응답 없이 매달리면 s_lateTask가 끝나지 않아
    /// 멀티 진입이 그 세션 내내 Blocked로 고정된다. 유예를 넘긴 작업은 버려진 것으로 보고 새로 시도한다.</summary>
    static bool IsLateTaskBlocking(out string _reason)
    {
        _reason = null;
        if (s_lateTask == null || s_lateTask.IsCompleted) return false;

        TimeSpan t_elapsed = DateTime.UtcNow - s_lateTaskStartedUtc;
        if (t_elapsed >= LateTaskGrace)
        {
            Debug.LogWarning($"[BattleContent] 직전 조회가 {(int)t_elapsed.TotalSeconds}초째 응답 없음 — " +
                             "버려진 것으로 보고 새로 조회한다.");
            s_lateTask = null;
            return false;
        }
        _reason = $"직전 조회가 아직 진행 중({(int)t_elapsed.TotalSeconds}초)";
        return true;
    }

    /// <summary>표 하나를 블롭 문서 한 번으로 받는다. <c>rows/</c> 서브컬렉션은 콘솔 열람용 미러라 런타임은 읽지 않는다
    /// — 읽으면 read가 행 수에 비례한다. 블롭은 메타와 같은 commit에 실리므로 행 개수 경합 재시도가 필요 없다.</summary>
    static async Task<SpecTablePayload> FetchTableAsync(string _envId, string _table, string _expectedHash)
    {
        FirebaseFirestore t_store = s_context.GetFirestore();
        string t_path = FirebaseRootPath.Environment(_envId) + "/specs/" + _table + "/blob/current";
        DocumentSnapshot t_blob = await t_store.Document(t_path).GetSnapshotAsync(Source.Server);
        if (!t_blob.Exists) throw new InvalidOperationException($"Remote spec blob '{_table}' is missing.");

        IDictionary<string, object> t_fields = t_blob.ToDictionary();
        if (Convert.ToInt64(t_fields["schemaVersion"]) != SpecPayloadCodec.SchemaVersion)
            throw new InvalidOperationException($"Remote spec blob '{_table}' is incompatible.");
        string t_payloadText = t_fields["payload"] as string;
        if (string.IsNullOrEmpty(t_payloadText))
            throw new InvalidOperationException($"Remote spec blob '{_table}' payload is empty.");

        if (!SpecPayloadCodec.TryBuildFromPayloadText(_table, t_payloadText, out SpecTablePayload t_payload, out string t_error))
            throw new InvalidOperationException($"Remote spec '{_table}' parse failed: {t_error}");

        // 블롭이 메타보다 뒤처졌거나 내용이 손상됐으면 여기서 걸린다 — 해시는 파싱 결과로 다시 계산한 값이다.
        if (!string.Equals(t_payload.PayloadHash, _expectedHash, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Remote spec '{_table}' hash mismatch: blob {t_payload.PayloadHash} vs meta {_expectedHash}.");
        return t_payload;
    }
}

sealed class BattleContentFirebaseModule : IFirebaseModule
{
    public void Initialize(in FirebaseContext _context) => BattleContentSync.Initialize(in _context);
    public void RetryPending() { }
    public UniTask FlushPendingAsync() => UniTask.CompletedTask;
    public void Shutdown() => BattleContentSync.Shutdown();
}
