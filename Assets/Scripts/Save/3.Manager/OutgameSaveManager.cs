using System;
using UnityEngine;

/// <summary>
/// 아웃게임 세이브 매니저 — 전투 밖 영속 데이터(재화·소유·도감 진행도)를 보관하고
/// 저장 매체(ISaveStorage)에 읽고 쓴다. 저장할 값이 늘면 Data에 필드를 추가하면 된다. 
/// </summary>
public static class OutgameSaveManager
{
    // 세이브 스키마 버전. 필드는 추가만 하고(하위호환), 구조를 바꿔야 할 때 올린다.
    public const int VERSION = 1;

    // 저장 매체에서 세이브를 식별하는 키.
    const string SAVE_KEY    = "outgame_save";
    const string CORRUPT_KEY = "outgame_save_corrupt";

    // 저장 매체. 기본은 PlayerPrefs, SetStorage로 교체 가능(repository 교체 지점).
    static ISaveStorage s_storage = new PlayerPrefsSaveStorage();

    // 저장되는 값. 도메인이 늘면 여기에 필드를 추가한다.
    // (JsonUtility는 없는 필드를 기본값으로 읽으므로 구버전 세이브도 그대로 로드된다.)
    [Serializable]
    class Data
    {
        public int version = VERSION;
        // A-3 재화:  public long gold;
        // B-5 소유:  public string[] ownedCards;
        // C   도감:  public RowProgress[] rows;  등
    }

    static Data s_data = new Data();

    /// <summary>저장 매체를 교체한다(부트에서 선택적으로 주입). null이면 기본(PlayerPrefs) 유지.</summary>
    public static void SetStorage(ISaveStorage _storage)
    {
        if (_storage != null) s_storage = _storage;
    }

    /// <summary>부트 시 1회 호출한다(소비자보다 먼저). 세이브가 없으면 기본값으로 시작.</summary>
    public static void Load()
    {
        var t_json = s_storage.Load(SAVE_KEY);
        if (string.IsNullOrEmpty(t_json))
        {
            s_data = new Data();
            return;
        }

        try
        {
            s_data = JsonUtility.FromJson<Data>(t_json) ?? new Data();
        }
        catch (Exception t_e)
        {
            // 손상된 세이브: 원본 문자열을 corrupt 키에 보존한 뒤 기본값으로 시작한다.
            // (진행도를 0으로 덮어쓰지 않는다 — 원본은 백업 키에 남는다.)
            Debug.LogError($"[OutgameSaveManager] 로드 실패, 원본을 백업하고 기본값으로 시작합니다: {t_e}");
            s_storage.Save(CORRUPT_KEY, t_json);
            s_data = new Data();
        }
    }

    /// <summary>현재 값을 저장 매체에 기록한다. 값을 바꾼 곳(재화·소유 등)에서 호출한다.</summary>
    public static void Save()
    {
        s_data.version = VERSION;
        s_storage.Save(SAVE_KEY, JsonUtility.ToJson(s_data));
    }
}
