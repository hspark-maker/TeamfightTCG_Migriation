using UnityEngine;

// IRepository의 PlayerPrefs 구현
public class PlayerPrefsRepository : IAtomicRepository
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

    public void ReplaceWithBackup(string _key, string _value, string _backupKey)
    {
        if (Has(_key)) Save(_backupKey, Load(_key));
        Save(_key, _value);
    }
}
