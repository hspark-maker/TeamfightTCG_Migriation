using Cysharp.Threading.Tasks;

// 아웃게임 세이브를 FirebaseManager 수명주기에 붙이는 어댑터.
sealed class PlayerSaveFirebaseModule : IFirebaseModule
{
    public void Initialize(in FirebaseContext _context) => PlayerSaveCloud.Initialize(in _context);

    public void RetryPending() => PlayerSaveCloud.RetryPending();

    public UniTask FlushPendingAsync() => PlayerSaveCloud.FlushAsync();

    public void Shutdown() => PlayerSaveCloud.Shutdown();
}
