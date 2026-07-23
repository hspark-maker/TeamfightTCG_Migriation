using UnityEngine;

/// <summary>
/// ISaveStorage의 PlayerPrefs 구현. 문자열 값을 PlayerPrefs 키에 저장한다.
/// 저장 방식을 바꾸려면 이 클래스 대신 다른 ISaveStorage 구현을 주입하면 된다.
/// </summary>
public class PlayerPrefsSaveStorage : ISaveStorage
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
