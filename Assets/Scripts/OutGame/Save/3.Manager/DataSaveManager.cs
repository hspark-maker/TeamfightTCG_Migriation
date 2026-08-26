using System;
using UnityEngine;

// 아웃게임 세이브 매니저. 저장 매체는 IRepository로 교체한다.
public static class DataSaveManager
{
    const string SAVE_KEY = "outgame_save";
    const string CORRUPT_KEY = "outgame_save_corrupt";
    const string VERSION_BACKUP_KEY_PREFIX = "outgame_save_v";

    static IRepository s_repository = new JsonFileRepository();
    static bool s_saveBlocked;

    public static event Action<string> OnSaved;

    public static UserSaveData Data { get; private set; } = CreateCurrentData();
    public static bool CloudUploadAllowed { get; private set; } = true;
    public static bool IsSaveBlocked => s_saveBlocked;
    internal static bool HasLocalSave => s_repository.Has(SAVE_KEY);

    // 저장 매체 교체. Load 이전에 호출한다.
    public static void SetRepository(IRepository _repository)
    {
        if (_repository != null) s_repository = _repository;
    }

    // 부팅 시 한 번 호출한다. 원격보다 최신인 로컬 데이터는 보존하고 쓰기를 막는다.
    public static void Load()
    {
        s_saveBlocked = false;
        CloudUploadAllowed = true;

        string t_json = s_repository.Load(SAVE_KEY);
        if (string.IsNullOrEmpty(t_json))
        {
            Data = CreateCurrentData();
            return;
        }

        try
        {
            Data = JsonUtility.FromJson<UserSaveData>(t_json) ?? CreateCurrentData();
            if (Data.version > UserSaveData.VERSION)
            {
                int t_loadedVersion = Data.version;
                string t_backupKey = $"{VERSION_BACKUP_KEY_PREFIX}{t_loadedVersion}";
                s_repository.Save(t_backupKey, t_json);

                Debug.LogWarning(
                    $"[DataSaveManager] Save v{t_loadedVersion} is newer than client v{UserSaveData.VERSION}. " +
                    $"Local save and cloud upload are blocked. Backup: '{t_backupKey}'");

                s_saveBlocked = true;
                CloudUploadAllowed = false;
                return;
            }

            if (Data.version < UserSaveData.VERSION)
            {
                int t_loadedVersion = Data.version;
                string t_backupKey = $"{VERSION_BACKUP_KEY_PREFIX}{t_loadedVersion}";
                s_repository.Save(t_backupKey, t_json);

                Debug.LogWarning(
                    $"[DataSaveManager] Save v{t_loadedVersion} reset for client v{UserSaveData.VERSION}. " +
                    $"Cloud upload is blocked for this session. Backup: '{t_backupKey}'");

                Data = CreateCurrentData();
                CloudUploadAllowed = false;
            }
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[DataSaveManager] Load failed. Backing up source and starting with defaults: {t_exception}");
            s_repository.Save(CORRUPT_KEY, t_json);
            Data = CreateCurrentData();
            CloudUploadAllowed = false;
        }
    }

    public static void Save()
    {
        if (s_saveBlocked)
        {
            Debug.LogWarning("[DataSaveManager] Save blocked because the loaded save is newer than this client.");
            return;
        }

        Data.version = UserSaveData.VERSION;
        string t_json = JsonUtility.ToJson(Data);
        s_repository.Save(SAVE_KEY, t_json);
        OnSaved?.Invoke(t_json);
    }

    public static string CreateSnapshot()
    {
        Data.version = UserSaveData.VERSION;
        return JsonUtility.ToJson(Data);
    }

    internal static string LoadSyncMetadata(string _key)
    {
        return s_repository.Load(_key);
    }

    internal static void SaveSyncMetadata(string _key, string _json)
    {
        s_repository.Save(_key, _json);
    }

    internal static bool TryApplyRemote(
        string _payload,
        string _backupKey,
        out string _error)
    {
        _error = string.Empty;
        if (s_repository is not IAtomicRepository t_atomicRepository)
        {
            _error = "Active repository does not support atomic replacement.";
            return false;
        }

        string t_original = s_repository.Load(SAVE_KEY);
        try
        {
            UserSaveData t_expectedData = JsonUtility.FromJson<UserSaveData>(_payload);
            string t_expected = JsonUtility.ToJson(t_expectedData);

            t_atomicRepository.ReplaceWithBackup(SAVE_KEY, _payload, _backupKey);
            Load();
            if (s_saveBlocked || CreateSnapshot() != t_expected)
                throw new InvalidOperationException("Reloaded save does not match the remote payload.");

            return true;
        }
        catch (Exception t_exception)
        {
            _error = t_exception.Message;
            try
            {
                if (string.IsNullOrEmpty(t_original))
                    s_repository.Delete(SAVE_KEY);
                else
                    t_atomicRepository.ReplaceWithBackup(SAVE_KEY, t_original, _backupKey + "_failed_apply");
                Load();

                string t_restored = s_repository.Load(SAVE_KEY);
                if (t_restored != t_original || s_saveBlocked)
                    throw new InvalidOperationException(
                        $"Original save restore verification failed. Recovery backup: '{_backupKey}'.");
            }
            catch (Exception t_restoreException)
            {
                _error += $" Restore failed: {t_restoreException.Message}";
            }

            return false;
        }
    }

    internal static bool TrySaveRemoteConflict(string _key, string _json, out string _error)
    {
        _error = string.Empty;
        try
        {
            s_repository.Save(_key, _json);
            if (s_repository.Load(_key) != _json)
                throw new InvalidOperationException("Conflict backup verification failed.");
            return true;
        }
        catch (Exception t_exception)
        {
            _error = t_exception.Message;
            return false;
        }
    }

    static UserSaveData CreateCurrentData()
    {
        return new UserSaveData { version = UserSaveData.VERSION };
    }
}
