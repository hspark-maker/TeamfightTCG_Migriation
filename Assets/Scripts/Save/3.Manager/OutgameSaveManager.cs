using System;
using UnityEngine;

/// <summary>
/// 아웃게임 세이브 매니저 — 저장소(IRepository)를 통해 세이브 값 객체(UserSaveData)를
/// 로드/저장한다. 소비자(재화·소유 등)는 Data로 현재 값에 접근하고, 바꾼 뒤 Save()를 부른다.
/// 저장 매체는 기본 PlayerPrefs이며, SetRepository로 교체할 수 있다(파일·클라우드 등).
/// </summary>
public static class OutgameSaveManager
{
    // 저장소에서 세이브를 식별하는 키.
    const string SAVE_KEY    = "outgame_save";
    const string CORRUPT_KEY = "outgame_save_corrupt";

    // 저장 매체. 기본은 PlayerPrefs, SetRepository로 교체 가능(repository 교체 지점).
    static IRepository s_repository = new PlayerPrefsRepository();

    /// <summary>현재 세이브 값 객체. 소비자는 이걸 읽고, 값을 바꾼 뒤 Save()를 호출한다.</summary>
    public static UserSaveData Data { get; private set; } = new UserSaveData();
    
    /// <summary>부트 시 1회 호출한다(소비자보다 먼저). 세이브가 없으면 기본값으로 시작.</summary>
    public static void Load()
    {
        var t_json = s_repository.Load(SAVE_KEY);
        if (string.IsNullOrEmpty(t_json))
        {
            Data = new UserSaveData();
            return;
        }

        try
        {
            Data = JsonUtility.FromJson<UserSaveData>(t_json) ?? new UserSaveData();
        }
        catch (Exception t_e)
        {
            // 손상된 세이브: 원본 문자열을 corrupt 키에 보존한 뒤 기본값으로 시작한다.
            // (진행도를 0으로 덮어쓰지 않는다 — 원본은 백업 키에 남는다.)
            Debug.LogError($"[OutgameSaveManager] 로드 실패, 원본을 백업하고 기본값으로 시작합니다: {t_e}");
            s_repository.Save(CORRUPT_KEY, t_json);
            Data = new UserSaveData();
        }
    }

    /// <summary>현재 값을 저장소에 기록한다. 값을 바꾼 곳(재화·소유 등)에서 호출한다.</summary>
    public static void Save()
    {
        Data.version = UserSaveData.VERSION;
        s_repository.Save(SAVE_KEY, JsonUtility.ToJson(Data));
    }
}
