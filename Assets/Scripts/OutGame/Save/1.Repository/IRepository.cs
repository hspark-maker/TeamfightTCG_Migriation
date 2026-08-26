using Cysharp.Threading.Tasks;

// 세이브 저장 매체 추상화(키-값). 매체가 네트워크일 수 있어 전 메서드가 UniTask다.
public interface IRepository
{
    UniTask<bool> HasAsync(string _key);

    UniTask<string> LoadAsync(string _key);

    UniTask<ESaveWriteResult> SaveAsync(string _key, string _value);

    UniTask DeleteAsync(string _key);
}
