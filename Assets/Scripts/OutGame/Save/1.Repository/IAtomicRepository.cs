using Cysharp.Threading.Tasks;

// 기록을 원자적으로 교체할 수 있는 저장 매체(교체 전 값은 백업 키로 남는다)
public interface IAtomicRepository : IRepository
{
    UniTask<ESaveWriteResult> ReplaceWithBackupAsync(string _key, string _value, string _backupKey);
}
