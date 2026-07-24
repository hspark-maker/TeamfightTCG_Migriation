using System.IO;
using UnityEngine;

// IRepository의 JSON 파일 구현(디버깅용). 키마다 persistentDataPath/{subFolder}/{key}.json 파일로 저장해
// 내용을 직접 열어 보고 수정할 수 있다.
public class JsonFileRepository : IRepository
{
    readonly string m_directory;

    // 세이브 파일들이 저장되는 폴더 경로(디버깅 편의).
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

    // 키 → 파일 경로. 폴더 구분자만 안전하게 치환한다.
    string PathOf(string _key)
    {
        var t_safe = _key.Replace('/', '_').Replace('\\', '_');
        return Path.Combine(m_directory, t_safe + ".json");
    }
}
