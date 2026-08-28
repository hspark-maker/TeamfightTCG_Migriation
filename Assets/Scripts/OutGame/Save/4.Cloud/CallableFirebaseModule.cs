using Cysharp.Threading.Tasks;

// callable 창구를 FirebaseManager 수명주기에 붙이는 어댑터.
internal sealed class CallableFirebaseModule : IFirebaseModule
{
    readonly string m_emulatorOrigin;

    FunctionsCallableService m_service;

    internal CallableFirebaseModule(string _emulatorOrigin)
    {
        m_emulatorOrigin = _emulatorOrigin;
    }

    public void Initialize(in FirebaseContext _context)
    {
        m_service = new FunctionsCallableService(m_emulatorOrigin);
        ServerSaveCommands.SetService(m_service);
    }

    // callable은 보류분을 쌓지 않는다 — 재시도의 주체는 호출한 도메인이다.
    public void RetryPending() { }

    public UniTask FlushPendingAsync() => UniTask.CompletedTask;

    public void Shutdown()
    {
        m_service?.Shutdown();
        m_service = null;
        ServerSaveCommands.SetService(null);
    }
}
