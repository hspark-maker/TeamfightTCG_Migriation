using UnityEngine;

/// <summary>
/// IRepository의 PlayerPrefs 구현. 문자열 값을 PlayerPrefs 키에 저장한다.
/// </summary>
public class PlayerPrefsRepository : IRepository
{
    public bool Has(string _key) => PlayerPrefs.HasKey(_key);

    public string Load(string _key) => PlayerPrefs.GetString(_key, "");

    public void Save(string _key, string _value)
    {
        PlayerPrefs.SetString(_key, _value);
        PlayerPrefs.Save();
    }

    public void Delete(string _key)
    {
        PlayerPrefs.DeleteKey(_key);
        PlayerPrefs.Save();
    }
}
