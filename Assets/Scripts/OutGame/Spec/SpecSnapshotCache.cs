using System;
using System.IO;
using UnityEngine;

public static class SpecSnapshotCache
{
    const int CacheSchemaVersion = 1;
    static readonly object SaveLock = new object();

    [Serializable]
    sealed class CacheEnvelope
    {
        public int schemaVersion;
        public string envId;
        public string fingerprint;
        public string payload;
    }

    public static bool TryLoad(string _envId, out string _payload, out string _fingerprint)
    {
        _payload = null;
        _fingerprint = null;
        try
        {
            string t_path = PathOf(_envId);
            if (TryReadEnvelope(t_path, _envId, out _payload, out _fingerprint)) return true;

            string t_directory = Path.GetDirectoryName(t_path);
            if (!Directory.Exists(t_directory)) return false;
            string[] t_backups = Directory.GetFiles(t_directory, Path.GetFileName(t_path) + ".*.bak");
            Array.Sort(t_backups, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            foreach (string t_backup in t_backups)
            {
                if (!TryReadEnvelope(t_backup, _envId, out _payload, out _fingerprint)) continue;
                try { File.Copy(t_backup, t_path, true); } catch { }
                return true;
            }
            return false;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[SpecCache] Cache read failed: {t_exception.GetBaseException().Message}");
            return false;
        }
    }

    static bool TryReadEnvelope(string _path, string _envId, out string _payload, out string _fingerprint)
    {
        _payload = null;
        _fingerprint = null;
        if (!File.Exists(_path)) return false;
        CacheEnvelope t_cache = JsonUtility.FromJson<CacheEnvelope>(File.ReadAllText(_path));
        if (t_cache == null || t_cache.schemaVersion != CacheSchemaVersion ||
            !string.Equals(t_cache.envId, _envId, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(t_cache.payload) || string.IsNullOrEmpty(t_cache.fingerprint))
            return false;
        var t_manager = new SpecDataManager();
        if (!t_manager.Load(t_cache.payload)) return false;
        _payload = t_cache.payload;
        _fingerprint = t_cache.fingerprint;
        return true;
    }

    public static bool TrySave(string _envId, string _payload, string _fingerprint, out string _error)
    {
        lock (SaveLock)
        {
            _error = null;
            string t_temp = null;
            string t_backup = null;
            try
            {
                string t_path = PathOf(_envId);
                Directory.CreateDirectory(Path.GetDirectoryName(t_path));
                string t_token = Guid.NewGuid().ToString("N");
                t_temp = t_path + "." + t_token + ".tmp";
                t_backup = t_path + "." + t_token + ".bak";
                var t_cache = new CacheEnvelope
                {
                    schemaVersion = CacheSchemaVersion, envId = _envId,
                    fingerprint = _fingerprint, payload = _payload,
                };
                using (var t_stream = new FileStream(t_temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var t_writer = new StreamWriter(t_stream))
                {
                    t_writer.Write(JsonUtility.ToJson(t_cache));
                    t_writer.Flush();
                    t_stream.Flush(true);
                }
                if (File.Exists(t_path))
                {
                    try { File.Replace(t_temp, t_path, t_backup); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Move(t_path, t_backup);
                        try { File.Move(t_temp, t_path); }
                        catch { File.Move(t_backup, t_path); throw; }
                    }
                }
                else File.Move(t_temp, t_path);
                if (File.Exists(t_backup)) File.Delete(t_backup);
                return true;
            }
            catch (Exception t_exception)
            {
                _error = t_exception.GetBaseException().Message;
                try { if (!string.IsNullOrEmpty(t_temp) && File.Exists(t_temp)) File.Delete(t_temp); } catch { }
                return false;
            }
        }
    }

    static string PathOf(string _envId)
    {
        foreach (char t_char in Path.GetInvalidFileNameChars()) _envId = _envId.Replace(t_char, '_');
        return Path.Combine(Application.persistentDataPath, "spec-cache", _envId + ".json");
    }
}
