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
    UpdateRequired,
    Blocked,
}

public sealed class ContentUpdateRequiredException : Exception
{
    public ContentUpdateRequiredException(string _message) : base(_message) { }
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

    /// <summary>직전에 캐시로 갈아끼운 스냅샷 지문. 초기화 경로가 SpecSource가 실제로 그것을 물었는지 확인하는 데 쓴다.</summary>
    static string s_adoptedFingerprint;

    sealed class RemoteSpecVector
    {
        public int Major;
        public long Minor;
        public bool FromIndex;
        public Dictionary<string, string> Hashes;
        public Dictionary<string, string> BlobPaths;

        public string VersionText => ContentVersion.Format(Major, Minor);
    }

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
        s_adoptedFingerprint = null;
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
            Debug.Log($"[BattleContent] 게이트 시작 env={t_envId} 모드={t_mode} content-major={ContentVersion.Major}");

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

            Task<RemoteSpecVector> t_metaTask = FetchRemoteVectorAsync(t_envId);
            if (await Task.WhenAny(t_metaTask, Task.Delay(FirebaseTimeouts.TransactionMilliseconds, _ct)) != t_metaTask)
            {
                TrackLate(t_metaTask);
                return Fallback($"서버 메타 조회 {FirebaseTimeouts.TransactionMilliseconds}ms 초과");
            }
            RemoteSpecVector t_remote = await t_metaTask;
            Dictionary<string, string> t_remoteHashes = t_remote.Hashes;
            Debug.Log($"[BattleContent] 서버 콘텐츠={t_remote.VersionText} source={(t_remote.FromIndex ? "index" : "legacy-meta")}");

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
            Task<string> t_downloadTask = DownloadSnapshotAsync(t_envId, t_remote, t_localTables);
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
            if (!SpecSnapshotCache.TrySave(
                    t_envId, t_payload, t_fingerprint, t_remote.Major, t_remote.Minor, out string t_cacheError))
                throw new IOException("Spec cache write failed: " + t_cacheError);

            s_adoptedFingerprint = t_fingerprint;
            Debug.Log($"[BattleContent] 캐시 교체 완료 지문 {t_localFingerprint} -> {t_fingerprint} ({t_payload.Length:N0}자)");
            return Verdict(EBattleContentGateResult.UpdatedRestartRequired, "새 스냅샷 캐시 완료 — 재시작 후 적용");
        }
        catch (ContentUpdateRequiredException t_exception)
        {
            return Verdict(EBattleContentGateResult.UpdateRequired, t_exception.Message);
        }
        catch (OperationCanceledException) { return Verdict(EBattleContentGateResult.Blocked, "취소됨"); }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[BattleContent] Server comparison failed: {t_exception.GetBaseException().Message}");
            return Fallback($"서버 대조 실패: {t_exception.GetBaseException().Message}");
        }
    }

    /// <summary>초기화 단계에서 최신 콘텐츠를 채택한다. 전투 게이트의 검증·다운로드 경로를 그대로 재사용한다.</summary>
    public static async UniTask SyncForInitializationAsync(CancellationToken _ct)
    {
        var t_wait = System.Diagnostics.Stopwatch.StartNew();
        while (!s_initialized)
        {
            _ct.ThrowIfCancellationRequested();
            if (t_wait.ElapsedMilliseconds >= FirebaseTimeouts.AuthAndReadMilliseconds)
                throw new TimeoutException("Firebase 콘텐츠 모듈 초기화 대기 시간이 초과됐다.");
            await Task.Delay(50, _ct);
        }

        EBattleContentGateResult t_result = await CheckBeforeBattleAsync(false, _ct);
        if (t_result == EBattleContentGateResult.UpdatedRestartRequired)
        {
            // Reload가 캐시를 못 믿고 내장본으로 떨어져도 예외는 나지 않는다 —
            // 확인하지 않으면 서버와 다른 데이터로 초기화가 성공 처리된다.
            string t_expected = s_adoptedFingerprint;
            SpecSource.Reload();
            if (!string.Equals(SpecSource.Fingerprint, t_expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"내려받은 스냅샷을 채택하지 못했다: 기대={t_expected} 실제={SpecSource.Fingerprint} 원본={SpecSource.Origin}");
            Debug.Log($"[BattleContent] 초기화 중 새 콘텐츠 스냅샷 채택 완료 지문={t_expected}");
            return;
        }

        if (t_result != EBattleContentGateResult.Current)
        {
            if (t_result == EBattleContentGateResult.UpdateRequired)
                throw new ContentUpdateRequiredException("이 앱이 지원하지 않는 콘텐츠 major가 배포됐다.");
            throw new InvalidOperationException($"콘텐츠 초기화에 실패했다: {t_result}");
        }
    }

    /// <summary>불일치 표만 서버에서 받고, 해시가 이미 같은 표는 로컬 것을 그대로 쓴다.
    /// 표 하나 바뀌었다고 여섯 표의 행 문서를 전부 다시 읽으면 read가 표 수만큼 곱해진다.</summary>
    static async Task<string> DownloadSnapshotAsync(
        string _envId, RemoteSpecVector _before, List<SpecTablePayload> _localTables)
    {
        Dictionary<string, string> _beforeHashes = _before.Hashes;
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
            .Select(t => FetchTableAsync(
                _envId, t,
                _beforeHashes.TryGetValue(t, out string t_hash) ? t_hash : null,
                _before.BlobPaths.TryGetValue(t, out string t_path) ? t_path : null))
            .ToArray();
        SpecTablePayload[] t_fetched = await Task.WhenAll(t_tasks);
        foreach (SpecTablePayload t_table in t_fetched) t_byTable[t_table.Table] = t_table;

        RemoteSpecVector t_after = await FetchRemoteVectorAsync(_envId);
        Dictionary<string, string> t_afterHashes = t_after.Hashes;
        if (_before.Major != t_after.Major || _before.Minor != t_after.Minor ||
            _before.FromIndex != t_after.FromIndex || _beforeHashes.Count != t_afterHashes.Count ||
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

    static async Task<RemoteSpecVector> FetchRemoteVectorAsync(string _envId)
    {
        FirebaseFirestore t_store = s_context.GetFirestore();
        string t_path = FirebaseRootPath.Environment(_envId) + "/specs/_index";
        DocumentSnapshot t_index = await t_store.Document(t_path).GetSnapshotAsync(Source.Server);
        if (!t_index.Exists)
        {
            return new RemoteSpecVector
            {
                Major = ContentVersion.Major,
                Minor = -1,
                FromIndex = false,
                Hashes = await FetchLegacyMetaVectorAsync(_envId),
                BlobPaths = new Dictionary<string, string>(StringComparer.Ordinal),
            };
        }

        IDictionary<string, object> t_fields = t_index.ToDictionary();
        if (!TryInteger(t_fields, "major", out long t_majorValue) ||
            t_majorValue < int.MinValue || t_majorValue > int.MaxValue)
            throw new InvalidOperationException("Remote spec index major is missing or invalid.");
        int t_major = (int)t_majorValue;
        if (!ContentVersion.IsSupportedMajor(t_major))
            throw new ContentUpdateRequiredException($"Remote content major {t_major} is not supported by this app.");
        if (t_fields.ContainsKey("minAppMajor"))
        {
            if (!TryInteger(t_fields, "minAppMajor", out long t_minAppMajor) ||
                t_minAppMajor < 0 || t_minAppMajor > int.MaxValue)
                throw new InvalidOperationException("Remote spec index minAppMajor is invalid.");
            if (t_minAppMajor > ContentVersion.Major)
                throw new ContentUpdateRequiredException(
                    $"Remote content requires app major {t_minAppMajor} or newer (current {ContentVersion.Major}).");
        }
        if (!TryInteger(t_fields, "minor", out long t_minor) || t_minor < 0)
            throw new InvalidOperationException("Remote spec index minor is missing or invalid.");
        if (!t_fields.TryGetValue("tables", out object t_tablesValue) ||
            !(t_tablesValue is IDictionary<string, object> t_tables))
            throw new InvalidOperationException("Remote spec index tables map is missing.");

        var t_hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var t_blobPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string t_table in SpecPayloadCodec.TableNames)
        {
            if (!t_tables.TryGetValue(t_table, out object t_entryValue) ||
                !(t_entryValue is IDictionary<string, object> t_entry) ||
                !t_entry.TryGetValue("payloadHash", out object t_hashValue) ||
                !(t_hashValue is string t_hash) || string.IsNullOrEmpty(t_hash))
                throw new InvalidOperationException($"Remote spec index entry '{t_table}' is missing or invalid.");
            if (!t_entry.TryGetValue("blobPath", out object t_pathValue) ||
                !(t_pathValue is string t_blobPath) || string.IsNullOrEmpty(t_blobPath))
                throw new InvalidOperationException($"Remote spec index blob path '{t_table}' is missing or invalid.");
            t_hashes.Add(t_table, t_hash);
            t_blobPaths.Add(t_table, t_blobPath);
        }

        return new RemoteSpecVector
        {
            Major = t_major,
            Minor = t_minor,
            FromIndex = true,
            Hashes = t_hashes,
            BlobPaths = t_blobPaths,
        };
    }

    static bool TryInteger(IDictionary<string, object> _fields, string _name, out long _value)
    {
        _value = 0;
        return _fields.TryGetValue(_name, out object t_value) &&
               t_value != null &&
               long.TryParse(Convert.ToString(t_value, System.Globalization.CultureInfo.InvariantCulture), out _value);
    }

    static async Task<Dictionary<string, string>> FetchLegacyMetaVectorAsync(string _envId)
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
        int t_major = Convert.ToInt32(t_fields.TryGetValue("major", out object t_majorValue)
            ? t_majorValue : t_fields["schemaVersion"]);
        if (!ContentVersion.IsSupportedMajor(t_major))
            throw new ContentUpdateRequiredException($"Remote spec '{_table}' major {t_major} is not supported.");
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
    static async Task<SpecTablePayload> FetchTableAsync(
        string _envId, string _table, string _expectedHash, string _publishedPath)
    {
        FirebaseFirestore t_store = s_context.GetFirestore();
        string t_path = string.IsNullOrEmpty(_publishedPath)
            ? FirebaseRootPath.Environment(_envId) + "/specs/" + _table + "/blob/current"
            : _publishedPath;
        DocumentSnapshot t_blob = await t_store.Document(t_path).GetSnapshotAsync(Source.Server);
        if (!t_blob.Exists) throw new InvalidOperationException($"Remote spec blob '{_table}' is missing.");

        IDictionary<string, object> t_fields = t_blob.ToDictionary();
        int t_major = Convert.ToInt32(t_fields.TryGetValue("major", out object t_majorValue)
            ? t_majorValue : t_fields["schemaVersion"]);
        if (!ContentVersion.IsSupportedMajor(t_major))
            throw new ContentUpdateRequiredException($"Remote spec blob '{_table}' major {t_major} is not supported.");
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
