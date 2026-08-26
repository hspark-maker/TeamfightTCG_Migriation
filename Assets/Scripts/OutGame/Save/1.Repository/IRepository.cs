// 세이브 저장 매체 추상화(키-값)
public interface IRepository
{
    bool Has(string _key);

    string Load(string _key);

    void Save(string _key, string _value);

    void Delete(string _key);
}
