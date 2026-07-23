using System;
using UnityEngine;

// 아웃게임 세이브 매니저 — IRepository를 통해 UserSaveData를 로드/저장한다.
// 소비자는 Data로 현재 값에 접근하고, 바꾼 뒤 Save()를 부른다.
public static class DataSaveManager
{
    const string SAVE_KEY    = "outgame_save";
    const string CORRUPT_KEY = "outgame_save_corrupt";

    // 저장 매체 (repository 교체 지점).
    static IRepository s_repository = new JsonFileRepository();

    public static UserSaveData Data { get; private set; } = new UserSaveData();

    // 저장 매체를 교체한다(Load 이전에 호출).
    public static void SetRepository(IRepository _repository)
    {
        if (_repository != null) s_repository = _repository;
    }

    // 부트 시 1회 호출(소비자보다 먼저). 세이브가 없으면 기본값으로 시작.
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
            // 손상된 세이브: 원본을 corrupt 키에 백업하고 기본값으로 시작(진행도 0 덮어쓰기 금지).
            Debug.LogError($"[DataSaveManager] 로드 실패, 원본 백업 후 기본값으로 시작: {t_e}");
            s_repository.Save(CORRUPT_KEY, t_json);
            Data = new UserSaveData();
        }
    }

    // 현재 값을 저장소에 기록. 값을 바꾼 곳에서 호출한다.
    public static void Save()
    {
        Data.version = UserSaveData.VERSION;
        s_repository.Save(SAVE_KEY, JsonUtility.ToJson(Data));
    }
}
