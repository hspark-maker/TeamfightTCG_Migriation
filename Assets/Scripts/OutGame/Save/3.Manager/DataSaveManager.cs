using System;
using UnityEngine;

// 아웃게임 세이브 매니저 — IRepository로 UserSaveData 로드/저장
public static class DataSaveManager
{
    const string SAVE_KEY    = "outgame_save";
    const string CORRUPT_KEY = "outgame_save_corrupt";

    static IRepository s_repository = new JsonFileRepository();

    // 현재 세이브 값 — 바꾼 뒤 Save() 호출
    public static UserSaveData Data { get; private set; } = new UserSaveData();

    // 저장 매체 교체(Load 이전에 호출)
    public static void SetRepository(IRepository _repository)
    {
        if (_repository != null) s_repository = _repository;
    }

    // 부트 시 1회 호출(소비자보다 먼저) — 없거나 손상 시 기본값으로 시작
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
            Debug.LogError($"[DataSaveManager] 로드 실패, 원본 백업 후 기본값으로 시작: {t_e}");
            s_repository.Save(CORRUPT_KEY, t_json);
            Data = new UserSaveData();
        }
    }

    // 현재 값을 저장소에 기록
    public static void Save()
    {
        Data.version = UserSaveData.VERSION;
        s_repository.Save(SAVE_KEY, JsonUtility.ToJson(Data));
    }
}
