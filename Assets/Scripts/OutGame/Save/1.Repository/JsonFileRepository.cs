using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

// IRepository의 JSON 파일 구현 — 키마다 persistentDataPath/{subFolder}/{key}.json
public class JsonFileRepository : IAtomicRepository
{
    readonly string m_directory;

    // 세이브 파일 폴더 경로
    public string DirectoryPath => m_directory;

    public JsonFileRepository(string _subFolder = "Save")
    {
        m_directory = Path.Combine(Application.persistentDataPath, _subFolder);
    }

    public UniTask<bool> HasAsync(string _key) => UniTask.FromResult(File.Exists(PathOf(_key)));

    public UniTask<string> LoadAsync(string _key)
    {
        var t_path = PathOf(_key);
        return UniTask.FromResult(File.Exists(t_path) ? File.ReadAllText(t_path) : "");
    }

    public UniTask<ESaveWriteResult> SaveAsync(string _key, string _value)
        => UniTask.FromResult(SaveBlocking(_key, _value));

    /// <summary>기다리지 않고 즉시 기록한다. 앱 종료 경로 전용 —
    /// 종료 콜백은 비동기 완료를 기다려주지 않아 여기서만 추상화를 우회한다.</summary>
    public ESaveWriteResult SaveBlocking(string _key, string _value)
    {
        try
        {
            Directory.CreateDirectory(m_directory);
            File.WriteAllText(PathOf(_key), _value);
            return ESaveWriteResult.Success;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[JsonFileRepository] '{_key}' 기록 실패: {t_exception.Message}");
            return ESaveWriteResult.IoFailed;
        }
    }

    public UniTask DeleteAsync(string _key)
    {
        var t_path = PathOf(_key);
        if (File.Exists(t_path)) File.Delete(t_path);
        return UniTask.CompletedTask;
    }

    // 실패를 예외가 아니라 반환값으로 알린다 — SaveAsync와 규약이 갈리면 호출부가 한쪽을 놓친다.
    public UniTask<ESaveWriteResult> ReplaceWithBackupAsync(string _key, string _value, string _backupKey)
    {
        string t_path = PathOf(_key);
        string t_backupPath = PathOf(_backupKey);
        string t_tempPath = t_path + ".tmp";
        try
        {
            Directory.CreateDirectory(m_directory);

            using (var t_stream = new FileStream(t_tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var t_writer = new StreamWriter(t_stream))
            {
                t_writer.Write(_value);
                t_writer.Flush();
                t_stream.Flush(true);
            }

            if (File.Exists(t_path))
            {
                File.Copy(t_path, t_backupPath, true);
                File.Replace(t_tempPath, t_path, null);
            }
            else
            {
                File.Move(t_tempPath, t_path);
            }
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[JsonFileRepository] '{_key}' 원자 교체 실패: {t_exception.Message}");
            return UniTask.FromResult(ESaveWriteResult.IoFailed);
        }
        finally
        {
            if (File.Exists(t_tempPath)) File.Delete(t_tempPath);
        }

        return UniTask.FromResult(ESaveWriteResult.Success);
    }

    string PathOf(string _key)
    {
        var t_safe = _key.Replace('/', '_').Replace('\\', '_');
        return Path.Combine(m_directory, t_safe + ".json");
    }
}
