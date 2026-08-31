using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>기기 로컬 설정 저장소. PlayerPrefs를 대체한다.
///
/// PlayerPrefs는 회사·제품 이름 하나로 묶인 기기 전역 저장소라(Windows는 레지스트리)
/// 같은 PC에서 클라이언트를 둘 띄우면 서로의 값을 덮는다. 여기서는 세이브와 같은 규칙으로
/// <see cref="DevAccountScope"/> 폴더 아래 문서 하나에 담아, 인스턴스별로 갈릴 수 있게 한다.
///
/// 값은 문자열로만 보관한다 — 타입은 읽는 쪽이 안다(숫자 서식은 InvariantCulture 고정).</summary>
public static class LocalPrefs
{
    const string FILE_NAME = "prefs.json";
    const string FOLDER = "Prefs";

    // PlayerPrefs에 남아 있던 값을 한 번만 옮겨 온다(구버전에서 올라온 기기).
    static readonly string[] LEGACY_KEYS =
    {
        "settings.frameRate",
        "settings.screenShake",
        "BGMVolume",
        "SFXVolume",
        "firebase.playerSave.ownerUid",
        "firebase.playerSave.deviceId",
        "firebase.matchResult.pending.v2",
        "outgame.tutorial.rewind",
        "outgame.tutorial.rewind.replay",
    };

    static readonly Dictionary<string, string> s_values = new Dictionary<string, string>(StringComparer.Ordinal);
    static bool s_loaded;
    static bool s_dirty;

    public static bool HasKey(string _key)
    {
        EnsureLoaded();
        return s_values.ContainsKey(_key);
    }

    public static string GetString(string _key, string _default = "")
    {
        EnsureLoaded();
        return s_values.TryGetValue(_key, out string t_value) ? t_value : _default;
    }

    public static int GetInt(string _key, int _default = 0)
        => int.TryParse(GetString(_key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t_value)
            ? t_value
            : _default;

    public static float GetFloat(string _key, float _default = 0f)
        => float.TryParse(GetString(_key, null), NumberStyles.Float, CultureInfo.InvariantCulture, out float t_value)
            ? t_value
            : _default;

    public static void SetString(string _key, string _value)
    {
        EnsureLoaded();
        if (s_values.TryGetValue(_key, out string t_current) && string.Equals(t_current, _value, StringComparison.Ordinal))
            return;

        s_values[_key] = _value ?? string.Empty;
        s_dirty = true;
    }

    public static void SetInt(string _key, int _value)
        => SetString(_key, _value.ToString(CultureInfo.InvariantCulture));

    public static void SetFloat(string _key, float _value)
        => SetString(_key, _value.ToString("R", CultureInfo.InvariantCulture));

    public static void DeleteKey(string _key)
    {
        EnsureLoaded();
        if (!s_values.Remove(_key)) return;

        s_dirty = true;
    }

    /// <summary>디스크에 굳힌다. 값이 안 바뀌었으면 아무 일도 하지 않는다.</summary>
    public static void Save()
    {
        EnsureLoaded();
        if (!s_dirty) return;

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonUtility.ToJson(Store.From(s_values)));
            s_dirty = false;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[LocalPrefs] 저장 실패: {t_exception.Message}");
        }
    }

    static string DirectoryPath => Path.Combine(Application.persistentDataPath, DevAccountScope.Folder(FOLDER));
    static string FilePath => Path.Combine(DirectoryPath, FILE_NAME);

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;

        try
        {
            if (File.Exists(FilePath))
            {
                Store t_store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                t_store?.Into(s_values);
                return;
            }
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[LocalPrefs] 읽기 실패(기본값으로 시작): {t_exception.Message}");
            s_values.Clear();
        }

        MigrateFromPlayerPrefs();
    }

    // 파일이 없는 첫 실행에서만 돈다. 옮긴 뒤 PlayerPrefs 쪽은 지우지 않는다 — 구버전으로 되돌아갈 여지를 남긴다.
    static void MigrateFromPlayerPrefs()
    {
        for (int i = 0; i < LEGACY_KEYS.Length; i++)
        {
            string t_key = LEGACY_KEYS[i];
            if (!PlayerPrefs.HasKey(t_key)) continue;

            s_values[t_key] = PlayerPrefs.GetString(t_key, string.Empty);
            s_dirty = true;
        }

        if (s_dirty) Save();
    }

    [Serializable]
    class Store
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();

        public static Store From(Dictionary<string, string> _values)
        {
            var t_store = new Store();
            foreach (var t_pair in _values)
            {
                t_store.keys.Add(t_pair.Key);
                t_store.values.Add(t_pair.Value);
            }
            return t_store;
        }

        public void Into(Dictionary<string, string> _values)
        {
            _values.Clear();
            int t_count = Math.Min(this.keys.Count, this.values.Count);
            for (int i = 0; i < t_count; i++) _values[this.keys[i]] = this.values[i];
        }
    }
}
