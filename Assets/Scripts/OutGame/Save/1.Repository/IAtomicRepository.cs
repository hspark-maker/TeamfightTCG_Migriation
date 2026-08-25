public interface IAtomicRepository : IRepository
{
    void ReplaceWithBackup(string _key, string _value, string _backupKey);
}
