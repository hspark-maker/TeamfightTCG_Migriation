using UnityEngine;

// IRepository의 PlayerPrefs 구현
public class PlayerPrefsRepository : IAtomicRepository
{
    public bool Has(string _key) => LocalPrefs.HasKey(_key);

    public string Load(string _key) => LocalPrefs.GetString(_key, "");

    public void Save(string _key, string _value)
    {
        LocalPrefs.SetString(_key, _value);
        LocalPrefs.Save();
    }

    public void Delete(string _key)
    {
        LocalPrefs.DeleteKey(_key);
        LocalPrefs.Save();
    }

    public void ReplaceWithBackup(string _key, string _value, string _backupKey)
    {
        if (Has(_key)) Save(_backupKey, Load(_key));
        Save(_key, _value);
    }
}
