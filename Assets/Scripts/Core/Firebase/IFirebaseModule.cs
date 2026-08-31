using Cysharp.Threading.Tasks;

public interface IFirebaseModule
{
    void Initialize(in FirebaseContext _context);
    void RetryPending();
    UniTask FlushPendingAsync();
    void Shutdown();
}
