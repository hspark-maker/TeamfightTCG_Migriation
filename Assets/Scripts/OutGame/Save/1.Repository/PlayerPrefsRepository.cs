using Cysharp.Threading.Tasks;
using UnityEngine;

// IRepository의 PlayerPrefs 구현
public class PlayerPrefsRepository : IAtomicRepository
{
    public UniTask<bool> HasAsync(string _key) => UniTask.FromResult(PlayerPrefs.HasKey(_key));

    public UniTask<string> LoadAsync(string _key) => UniTask.FromResult(PlayerPrefs.GetString(_key, ""));

    public UniTask<ESaveWriteResult> SaveAsync(string _key, string _value)
    {
        Write(_key, _value);
        return UniTask.FromResult(ESaveWriteResult.Success);
    }

    public UniTask DeleteAsync(string _key)
    {
        PlayerPrefs.DeleteKey(_key);
        PlayerPrefs.Save();
        return UniTask.CompletedTask;
    }

    // 원자성이 없다 — 두 번의 쓰기 사이에 죽으면 백업만 남는다(파일 구현과 달리 교체 보장이 없다).
    public UniTask<ESaveWriteResult> ReplaceWithBackupAsync(string _key, string _value, string _backupKey)
    {
        if (PlayerPrefs.HasKey(_key)) Write(_backupKey, PlayerPrefs.GetString(_key, ""));
        Write(_key, _value);
        return UniTask.FromResult(ESaveWriteResult.Success);
    }

    static void Write(string _key, string _value)
    {
        PlayerPrefs.SetString(_key, _value);
        PlayerPrefs.Save();
    }
}
