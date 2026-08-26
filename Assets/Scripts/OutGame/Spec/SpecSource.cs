using UnityEngine;

// 스펙시트(SpecData.bytes) 파싱 결과 한 벌. 시트를 읽는 축이 여럿이라 복호화·파싱을 여기서 1회만 한다.
// 못 읽으면 Manager가 null로 남고, 각 조회 창구가 SO 인스펙터 값으로 폴백한다.
public static class SpecSource
{
    static bool s_loaded;
    static SpecDataManager s_manager;

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
    }

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(폴백으로 계속 돈다).

        string t_json = SpecDataResourceLoader.LoadSpecData();
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
    }
}
