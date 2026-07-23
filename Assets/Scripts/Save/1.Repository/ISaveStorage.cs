
public interface ISaveStorage
{
    bool Has(string _key);
    
    string Load(string _key);
    
    void Save(string _key, string _value);
    
    void Delete(string _key);
}
