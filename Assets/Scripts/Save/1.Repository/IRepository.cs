/// <summary>
/// 세이브 저장 매체 추상화. 직렬화된 문자열을 키에 저장/조회하는 키-값 계약이다.
/// PlayerPrefs·파일·클라우드 등 구현만 갈아끼우면 저장 방식을 바꿀 수 있다.
/// </summary>
public interface IRepository
{
    bool Has(string _key);
    
    string Load(string _key);
    
    void Save(string _key, string _value);
    
    void Delete(string _key);
}
