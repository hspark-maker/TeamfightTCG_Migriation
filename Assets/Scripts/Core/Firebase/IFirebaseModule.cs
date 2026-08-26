public interface IFirebaseModule
{
    void Initialize(in FirebaseContext _context);
    void RetryPending();
    void FlushPending();
    void Shutdown();
}
