using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 아웃게임 세이브 매니저. 저장 매체는 IRepository로 교체한다.
public static class DataSaveManager
{
    const string SAVE_KEY = "outgame_save";
    const string CORRUPT_KEY = "outgame_save_corrupt";
    const string VERSION_BACKUP_KEY_PREFIX = "outgame_save_v";

    static IRepository s_repository = new JsonFileRepository();
    static bool s_saveBlocked;

    // 쓰기 직렬화 사슬. await가 프레임을 가르므로 두 쓰기가 겹치면
    // 늦게 시작한 쪽이 먼저 끝나 구 스냅샷이 파일을 덮을 수 있다.
    static UniTask s_writeChain = UniTask.CompletedTask;

    public static event Action<string> OnSaved;

    public static UserSaveData Data { get; private set; } = CreateCurrentData();
    public static bool CloudUploadAllowed { get; private set; } = true;
    public static bool IsSaveBlocked => s_saveBlocked;

    // 쓰기가 진행 중인가(대기 중 같은 커맨드가 두 번 들어오는 것을 막는 판정)
    public static bool IsWriting { get; private set; }

    // 저장 매체 교체. Load 이전에 호출한다.
    public static void SetRepository(IRepository _repository)
    {
        if (_repository != null) s_repository = _repository;
    }

    internal static UniTask<bool> HasLocalSaveAsync() => s_repository.HasAsync(SAVE_KEY);

    // 부팅 시 한 번 호출한다. 원격보다 최신인 로컬 데이터는 보존하고 쓰기를 막는다.
    public static async UniTask LoadAsync()
    {
        s_saveBlocked = false;
        CloudUploadAllowed = true;

        string t_json = await s_repository.LoadAsync(SAVE_KEY);
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
                string t_backupKey = VERSION_BACKUP_KEY_PREFIX + t_loadedVersion;
                await s_repository.SaveAsync(t_backupKey, t_json);

                Debug.LogWarning(
                    $"[DataSaveManager] Save v{t_loadedVersion} is newer than client v{UserSaveData.VERSION}. " +
                    $"Local save and cloud upload are blocked. Backup: {t_backupKey}");

                s_saveBlocked = true;
                CloudUploadAllowed = false;
                return;
            }

            if (Data.version < UserSaveData.VERSION)
            {
                int t_loadedVersion = Data.version;
                string t_backupKey = VERSION_BACKUP_KEY_PREFIX + t_loadedVersion;
                await s_repository.SaveAsync(t_backupKey, t_json);

                Debug.LogWarning(
                    $"[DataSaveManager] Save v{t_loadedVersion} reset for client v{UserSaveData.VERSION}. " +
                    $"Cloud upload is blocked for this session. Backup: {t_backupKey}");

                Data = CreateCurrentData();
                CloudUploadAllowed = false;
            }
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[DataSaveManager] Load failed. Backing up source and starting with defaults: {t_exception}");
            await s_repository.SaveAsync(CORRUPT_KEY, t_json);
            Data = CreateCurrentData();
            CloudUploadAllowed = false;
        }
    }

    public static UniTask<ESaveWriteResult> SaveAsync() => Serialize(WriteAsync);

    /// <summary>기다리지 않고 즉시 기록한다. 앱 종료 경로 전용 —
    /// 파일 매체에서만 성립하며, 다른 매체면 경고 후 아무것도 하지 않는다.</summary>
    internal static ESaveWriteResult SaveBlocking()
    {
        if (s_saveBlocked)
        {
            Debug.LogWarning("[DataSaveManager] Save blocked because the loaded save is newer than this client.");
            return ESaveWriteResult.Blocked;
        }

        if (s_repository is not JsonFileRepository t_fileRepository)
        {
            Debug.LogWarning("[DataSaveManager] 종료 경로 동기 저장은 파일 매체에서만 지원한다 — 이번 쓰기는 건너뛴다.");
            return ESaveWriteResult.IoFailed;
        }

        Data.version = UserSaveData.VERSION;
        string t_json = JsonUtility.ToJson(Data);
        ESaveWriteResult t_result = t_fileRepository.SaveBlocking(SAVE_KEY, t_json);
        if (t_result == ESaveWriteResult.Success) OnSaved?.Invoke(t_json);

        return t_result;
    }

    public static string CreateSnapshot()
    {
        Data.version = UserSaveData.VERSION;
        return JsonUtility.ToJson(Data);
    }

    internal static UniTask<string> LoadSyncMetadataAsync(string _key) => s_repository.LoadAsync(_key);

    internal static async UniTask SaveSyncMetadataAsync(string _key, string _json)
    {
        await s_repository.SaveAsync(_key, _json);
    }

    /// <summary>원격 payload를 로컬에 적용한다. 실패하면 원본을 되돌린 뒤 사유를 담아 돌려준다.
    /// 게임 쪽 쓰기와 같은 사슬에서 돌아야 한다 — 아니면 검증용 재로드 사이에 다른 쓰기가 끼어든다.</summary>
    internal static UniTask<SaveApplyReport> ApplyRemoteAsync(string _payload, string _backupKey)
        => Serialize(() => ApplyRemoteCoreAsync(_payload, _backupKey));

    internal static UniTask<SaveApplyReport> SaveRemoteConflictAsync(string _key, string _json)
        => Serialize(() => SaveRemoteConflictCoreAsync(_key, _json));

    static async UniTask<ESaveWriteResult> WriteAsync()
    {
        if (s_saveBlocked)
        {
            Debug.LogWarning("[DataSaveManager] Save blocked because the loaded save is newer than this client.");
            return ESaveWriteResult.Blocked;
        }

        Data.version = UserSaveData.VERSION;
        string t_json = JsonUtility.ToJson(Data);
        ESaveWriteResult t_result = await s_repository.SaveAsync(SAVE_KEY, t_json);
        if (t_result == ESaveWriteResult.Success) OnSaved?.Invoke(t_json);

        return t_result;
    }

    static async UniTask<SaveApplyReport> ApplyRemoteCoreAsync(string _payload, string _backupKey)
    {
        if (s_repository is not IAtomicRepository t_atomicRepository)
            return SaveApplyReport.Fail("Active repository does not support atomic replacement.");

        string t_original = await s_repository.LoadAsync(SAVE_KEY);
        try
        {
            UserSaveData t_expectedData = JsonUtility.FromJson<UserSaveData>(_payload);
            string t_expected = JsonUtility.ToJson(t_expectedData);

            if (await t_atomicRepository.ReplaceWithBackupAsync(SAVE_KEY, _payload, _backupKey) != ESaveWriteResult.Success)
                throw new InvalidOperationException("Atomic replace failed while applying the remote payload.");

            await LoadAsync();
            if (s_saveBlocked || CreateSnapshot() != t_expected)
                throw new InvalidOperationException("Reloaded save does not match the remote payload.");

            return SaveApplyReport.Ok();
        }
        catch (Exception t_exception)
        {
            string t_error = t_exception.Message;
            try
            {
                if (string.IsNullOrEmpty(t_original))
                {
                    await s_repository.DeleteAsync(SAVE_KEY);
                }
                else if (await t_atomicRepository.ReplaceWithBackupAsync(
                             SAVE_KEY, t_original, _backupKey + "_failed_apply") != ESaveWriteResult.Success)
                {
                    throw new InvalidOperationException("Atomic replace failed while restoring the original save.");
                }

                await LoadAsync();

                string t_restored = await s_repository.LoadAsync(SAVE_KEY);
                if (t_restored != t_original || s_saveBlocked)
                    throw new InvalidOperationException(
                        $"Original save restore verification failed. Recovery backup: {_backupKey}.");
            }
            catch (Exception t_restoreException)
            {
                t_error += " Restore failed: " + t_restoreException.Message;
            }

            return SaveApplyReport.Fail(t_error);
        }
    }

    static async UniTask<SaveApplyReport> SaveRemoteConflictCoreAsync(string _key, string _json)
    {
        try
        {
            await s_repository.SaveAsync(_key, _json);
            if (await s_repository.LoadAsync(_key) != _json)
                throw new InvalidOperationException("Conflict backup verification failed.");

            return SaveApplyReport.Ok();
        }
        catch (Exception t_exception)
        {
            return SaveApplyReport.Fail(t_exception.Message);
        }
    }

    // 앞선 쓰기가 끝난 뒤에 시작하도록 사슬에 잇는다.
    static async UniTask<T> Serialize<T>(Func<UniTask<T>> _work)
    {
        UniTask t_previous = s_writeChain;
        var t_completion = new UniTaskCompletionSource();
        s_writeChain = t_completion.Task;

        try
        {
            await t_previous;
        }
        catch (Exception)
        {
            // 앞선 쓰기의 실패는 그 호출자가 받는다 — 사슬은 끊지 않는다.
        }

        IsWriting = true;
        try
        {
            return await _work();
        }
        finally
        {
            IsWriting = false;
            t_completion.TrySetResult();
        }
    }

    static UserSaveData CreateCurrentData()
    {
        return new UserSaveData { version = UserSaveData.VERSION };
    }
}
