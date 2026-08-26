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

        string t_json = null;
        if (!string.IsNullOrEmpty(t_envId) &&
            SpecSnapshotCache.TryLoad(t_envId, out string t_cachedJson, out string t_cachedFingerprint))
        {
            t_json = t_cachedJson;
            s_fingerprint = t_cachedFingerprint;
            s_origin = "서버캐시";
        }
        if (string.IsNullOrEmpty(t_json))
        {
            t_json = SpecDataResourceLoader.LoadSpecData();
            s_origin = "내장본";
        }
        if (string.IsNullOrEmpty(t_json))
        {
            Debug.LogWarning("[SpecSource] SpecData 리소스를 못 읽었다. 시트를 쓰는 축은 전부 SO 값으로 돈다.");
            return;
        }

        var t_manager = new SpecDataManager();
        if (!t_manager.Load(t_json))
        {
            Debug.LogWarning("[SpecSource] SpecData 파싱 실패. 시트를 쓰는 축은 전부 SO 값으로 돈다.");
            return;
        }

        s_manager = t_manager;
        if (string.IsNullOrEmpty(s_fingerprint) && !string.IsNullOrEmpty(t_envId))
        {
            var t_tables = new System.Collections.Generic.List<SpecTablePayload>();
            foreach (string t_tableName in SpecPayloadCodec.TableNames)
            {
                if (!SpecPayloadCodec.TryBuildLocalTable(t_manager, t_tableName, out SpecTablePayload t_table, out string t_error))
                {
                    s_fingerprint = "nospec";
                    // 지문이 nospec이면 멀티 InitialDeck 송신이 차단된다 — 조용히 넘기면 원인을 못 찾는다.
                    Debug.LogError($"[SpecSource] 지문 계산 실패 table={t_tableName}: {t_error} — 멀티플레이가 차단된다.");
                    return;
                }
                t_tables.Add(t_table);
            }
            s_fingerprint = SpecPayloadCodec.CombinedHash(t_envId, t_tables);
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
}
