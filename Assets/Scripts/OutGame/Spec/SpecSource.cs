using UnityEngine;

// 스펙시트(SpecData.bytes) 파싱 결과 한 벌. 시트를 읽는 축이 여럿이라 복호화·파싱을 여기서 1회만 한다.
// 못 읽으면 Manager가 null로 남고, 각 조회 창구가 SO 인스펙터 값으로 폴백한다.
public static class SpecSource
{
    static bool s_loaded;
    static SpecDataManager s_manager;
    static string s_fingerprint;
    static string s_battleFingerprint;
    static string s_origin;

    /// <summary>이번 세션이 물고 있는 스펙 원본 — "서버캐시" 또는 "내장본". 대조 로그의 기준점이다.</summary>
    public static string Origin
    {
        get { EnsureLoaded(); return s_origin ?? "없음"; }
    }

    public static string Fingerprint
    {
        get { EnsureLoaded(); return s_fingerprint ?? "nospec"; }
    }

    public static string BattleFingerprint
    {
        get { EnsureLoaded(); return s_battleFingerprint ?? "nospec"; }
    }

    /// <summary>시트를 못 읽었으면 null — 호출부는 폴백으로 떨어져야 한다.</summary>
    public static SpecDataManager Manager
    {
        get
        {
            EnsureLoaded();
            return s_manager;
        }
    }

    // 부트에서 1회. 지연 로드도 되지만 첫 조회 프레임에 복호화·파싱이 걸리지 않게 미리 당긴다.
    public static void Init() => EnsureLoaded();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_loaded = false;
        s_manager = null;
        s_fingerprint = null;
        s_battleFingerprint = null;
        s_origin = null;
    }

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(폴백으로 계속 돈다).

        string t_envId = null;
        try { t_envId = ContentProfileConfig.Active.CloudEnvId; }
        catch (System.Exception) { }

        SpecDataManager t_manager = null;

        // 봉투에 적힌 지문을 그대로 믿으면 payload와 지문이 어긋난 캐시(부분 기록·손상·payload만 고친 파일)가
        // 그대로 통과해, **내가 들고 있는 데이터와 내가 주장하는 지문이 다른 상태**로 대전에 들어간다.
        // 그래서 payload에서 지문을 다시 계산해 봉투 값과 대조하고, 어긋나면 캐시를 버린다.
        //
        // 이건 자기정합성 검사지 인증이 아니다 — 지문까지 같이 고쳐 쓰면 여기는 통과한다.
        // 조작 스펙을 실제로 막는 것은 서버 대조(BattleContentSync)와 상대와의 지문 대조(InitialDeck)다.
        if (!string.IsNullOrEmpty(t_envId) &&
            SpecSnapshotCache.TryLoad(t_envId, out string t_cachedJson, out string t_cachedFingerprint))
        {
            string t_recomputed = null;
            string t_cacheError = "캐시 파싱 실패";
            if (TryLoadManager(t_cachedJson, out SpecDataManager t_cachedManager)
                && !TryCombinedFingerprint(t_cachedManager, t_envId, out t_recomputed, out t_cacheError))
                t_recomputed = null;

            if (t_recomputed != null && string.Equals(t_recomputed, t_cachedFingerprint, System.StringComparison.Ordinal))
            {
                t_manager = t_cachedManager;
                s_fingerprint = t_recomputed;
                s_origin = "서버캐시";
            }
            else
            {
                Debug.LogError("[SpecSource] 서버캐시를 신뢰할 수 없다 — 버리고 내장본으로 돈다. " +
                               $"봉투={t_cachedFingerprint} 재계산={t_recomputed ?? "(실패: " + t_cacheError + ")"}");
            }
        }

        if (t_manager == null)
        {
            string t_json = SpecDataResourceLoader.LoadSpecData();
            if (string.IsNullOrEmpty(t_json))
            {
                Debug.LogWarning("[SpecSource] SpecData 리소스를 못 읽었다. 시트를 쓰는 축은 전부 SO 값으로 돈다.");
                return;
            }
            if (!TryLoadManager(t_json, out t_manager))
            {
                Debug.LogWarning("[SpecSource] SpecData 파싱 실패. 시트를 쓰는 축은 전부 SO 값으로 돈다.");
                return;
            }
            s_fingerprint = null;   // 캐시 경로에서 채웠더라도 원본이 바뀌었으니 다시 계산한다
            s_origin = "내장본";
        }

        s_manager = t_manager;
        string t_error = null;
        if (string.IsNullOrEmpty(s_fingerprint) && !string.IsNullOrEmpty(t_envId)
            && !TryCombinedFingerprint(t_manager, t_envId, out s_fingerprint, out t_error))
        {
            s_fingerprint = "nospec";
            // 지문이 nospec이면 멀티 InitialDeck 송신이 차단된다 — 조용히 넘기면 원인을 못 찾는다.
            Debug.LogError($"[SpecSource] 지문 계산 실패: {t_error} — 멀티플레이가 차단된다.");
            return;
        }
        if (!string.IsNullOrEmpty(t_envId))
        {
            string t_battleTable = ContentProfileConfig.Active.RunMode == EContentRunMode.Test ? "Card_Test" : "Card";
            if (SpecPayloadCodec.TryBuildLocalTable(t_manager, t_battleTable, out SpecTablePayload t_battlePayload, out string t_battleError))
                s_battleFingerprint = SpecPayloadCodec.CombinedHash(t_envId, new[] { t_battlePayload });
            else
                Debug.LogError($"[SpecSource] 전투 지문 계산 실패 table={t_battleTable}: {t_battleError} — 멀티플레이가 차단된다.");

            Debug.Log($"[SpecSource] 스펙 로드 완료 원본={s_origin} env={t_envId} 전투표={t_battleTable} " +
                      $"지문={s_fingerprint ?? "(없음)"} 전투지문={s_battleFingerprint ?? "(없음)"}");
        }
    }

    static bool TryLoadManager(string _json, out SpecDataManager _manager)
    {
        _manager = null;
        if (string.IsNullOrEmpty(_json)) return false;
        var t_manager = new SpecDataManager();
        if (!t_manager.Load(_json)) return false;
        _manager = t_manager;
        return true;
    }

    /// <summary>6표 전체를 접은 콘텐츠 지문. 로비 게이트(BattleContentSync)가 서버와 대조하는 값과
    /// **같은 함수**로 만든다 — 여기서 따로 접으면 캐시 검증과 서버 대조가 서로 다른 값을 보게 된다.</summary>
    static bool TryCombinedFingerprint(SpecDataManager _manager, string _envId, out string _fingerprint, out string _error)
    {
        _fingerprint = null;
        _error = null;
        var t_tables = new System.Collections.Generic.List<SpecTablePayload>();
        foreach (string t_tableName in SpecPayloadCodec.TableNames)
        {
            if (!SpecPayloadCodec.TryBuildLocalTable(_manager, t_tableName, out SpecTablePayload t_table, out string t_tableError))
            {
                _error = $"table={t_tableName}: {t_tableError}";
                return false;
            }
            t_tables.Add(t_table);
        }
        _fingerprint = SpecPayloadCodec.CombinedHash(_envId, t_tables);
        return true;
    }
}
