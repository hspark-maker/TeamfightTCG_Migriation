using System;
using System.IO;
using CookApps.Tech;
using UnityEngine;

/// <summary>원격 스펙 스냅샷의 로컬 보관소. 저장·불러오기는 <see cref="LocalData"/>(com.cookapps.localdata.v2)가 맡는다
/// — 임시 파일 쓰기 후 교체, 경로 기반 키 암호화, CRC 무결성이 패키지 안에 있어 여기서 다시 만들지 않는다.
///
/// <para>이건 캐시다. 읽기에 실패하면 <c>false</c>만 돌려주고 원격에서 다시 받는다 —
/// 예전의 .bak 회수 사슬은 패키지의 원자적 교체로 대체됐다.</para></summary>
public static class SpecSnapshotCache
{
    const int CacheFormatVersion = 3;

    // Preserve: 직렬화가 Newtonsoft 리플렉션이라 관리 코드 스트리핑이 필드를 걷어내면 캐시가 조용히 빈다.
    [Serializable]
    [UnityEngine.Scripting.Preserve]
    sealed class CacheEnvelope
    {
        public int schemaVersion;
        public string envId;
        public int contentMajor;
        public long contentMinor;
        public string fingerprint;
        public string payload;
    }

    public static bool TryLoad(string _envId, out string _payload, out string _fingerprint)
        => TryLoad(_envId, out _payload, out _fingerprint, out _, out _);

    public static bool TryLoad(
        string _envId, out string _payload, out string _fingerprint,
        out int _contentMajor, out long _contentMinor)
    {
        _payload = null;
        _fingerprint = null;
        _contentMajor = 0;
        _contentMinor = -1;

        CacheEnvelope t_cache = LocalData.Default.Load<CacheEnvelope>(PathOf(_envId));
        if (t_cache == null || t_cache.schemaVersion != CacheFormatVersion ||
            !string.Equals(t_cache.envId, _envId, StringComparison.Ordinal) ||
            !ContentVersion.IsSupportedMajor(t_cache.contentMajor) ||
            string.IsNullOrEmpty(t_cache.payload) || string.IsNullOrEmpty(t_cache.fingerprint))
            return false;

        // 표를 실제로 세울 수 있는 스냅샷만 채택한다 — 형식만 맞고 내용이 깨진 캐시를 여기서 거른다.
        var t_manager = new SpecDataManager();
        if (!t_manager.Load(t_cache.payload)) return false;

        _payload = t_cache.payload;
        _fingerprint = t_cache.fingerprint;
        _contentMajor = t_cache.contentMajor;
        _contentMinor = t_cache.contentMinor;
        return true;
    }

    public static bool TrySave(string _envId, string _payload, string _fingerprint, out string _error)
        => TrySave(_envId, _payload, _fingerprint, ContentVersion.Major, -1, out _error);

    public static bool TrySave(
        string _envId, string _payload, string _fingerprint,
        int _contentMajor, long _contentMinor, out string _error)
    {
        _error = null;
        try
        {
            string t_path = PathOf(_envId);
            Directory.CreateDirectory(Path.GetDirectoryName(t_path));

            var t_cache = new CacheEnvelope
            {
                schemaVersion = CacheFormatVersion, envId = _envId,
                contentMajor = _contentMajor, contentMinor = _contentMinor,
                fingerprint = _fingerprint, payload = _payload,
            };

            if (!LocalData.Default.Save(t_cache, t_path))
            {
                _error = "LocalData 저장이 실패했다(자세한 사유는 앞선 로그).";
                return false;
            }

            DeleteLegacy(_envId);
            return true;
        }
        catch (Exception t_exception)
        {
            _error = t_exception.GetBaseException().Message;
            return false;
        }
    }

    static string PathOf(string _envId) => Path.Combine(CacheDirectory, SafeName(_envId) + ".dat");

    /// <summary>패키지 도입 전의 평문 JSON 캐시와 그 백업 사본. 새 캐시를 세운 뒤 한 번 걷어낸다.</summary>
    static void DeleteLegacy(string _envId)
    {
        try
        {
            string t_stem = SafeName(_envId) + ".json";
            string t_legacy = Path.Combine(CacheDirectory, t_stem);
            if (File.Exists(t_legacy)) File.Delete(t_legacy);
            foreach (string t_backup in Directory.GetFiles(CacheDirectory, t_stem + ".*.bak")) File.Delete(t_backup);
        }
        catch { }
    }

    static string CacheDirectory => Path.Combine(Application.persistentDataPath, "spec-cache");

    static string SafeName(string _envId)
    {
        foreach (char t_char in Path.GetInvalidFileNameChars()) _envId = _envId.Replace(t_char, '_');
        return _envId;
    }
}
