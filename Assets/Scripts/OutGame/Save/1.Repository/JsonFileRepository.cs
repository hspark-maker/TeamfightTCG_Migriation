using System.IO;
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

    public bool Has(string _key) => File.Exists(PathOf(_key));

    public string Load(string _key)
    {
        var t_path = PathOf(_key);
        return File.Exists(t_path) ? File.ReadAllText(t_path) : "";
    }

    public void Save(string _key, string _value)
    {
        Directory.CreateDirectory(m_directory);
        File.WriteAllText(PathOf(_key), _value);
    }

    public void Delete(string _key)
    {
        var t_path = PathOf(_key);
        if (File.Exists(t_path)) File.Delete(t_path);
    }

    public void ReplaceWithBackup(string _key, string _value, string _backupKey)
    {
        Directory.CreateDirectory(m_directory);

        string t_path = PathOf(_key);
        string t_backupPath = PathOf(_backupKey);
        string t_tempPath = t_path + ".tmp";
        try
        {
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
        finally
        {
            if (File.Exists(t_tempPath)) File.Delete(t_tempPath);
        }
    }

    string PathOf(string _key)
    {
        var t_safe = _key.Replace('/', '_').Replace('\\', '_');
        return Path.Combine(m_directory, t_safe + ".json");
    }
}
