sealed class PlayerSaveFirebaseModule : IFirebaseModule
{
    readonly bool uploadAllowed;

    internal PlayerSaveFirebaseModule(bool _uploadAllowed)
    {
        this.uploadAllowed = _uploadAllowed;
    }

    public void Initialize(in FirebaseContext _context)
        => PlayerSaveSync.Initialize(in _context, this.uploadAllowed);

    public void RetryPending() => PlayerSaveSync.RetryPending();
    public void FlushPending() => PlayerSaveSync.FlushPending();
    public void Shutdown() => PlayerSaveSync.Shutdown();
}
