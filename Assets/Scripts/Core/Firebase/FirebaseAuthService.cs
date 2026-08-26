using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Auth;

public sealed class FirebaseAuthService
{
    public static FirebaseAuthService Instance { get; } = new FirebaseAuthService();

    public event Action OnStateChanged;

    public EFirebaseAuthState State { get; private set; } = EFirebaseAuthState.Uninitialized;
    public string UserId { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public bool IsCurrentUserActive => this.auth?.CurrentUser != null &&
                                       this.auth.CurrentUser.UserId == this.UserId;

    FirebaseAuth auth;
    bool stateChangedSubscribed;
    string sessionUserId = string.Empty;
    Task initializationTask;
    int generation;

    FirebaseAuthService() { }

    public UniTask InitializeAsync()
    {
        if (this.State == EFirebaseAuthState.SignedIn && this.IsCurrentUserActive)
            return UniTask.CompletedTask;
        if (this.initializationTask != null && !this.initializationTask.IsCompleted)
            return this.initializationTask.AsUniTask();

        SetState(EFirebaseAuthState.Initializing);
        this.initializationTask = InitializeCoreAsync(this.generation);
        return this.initializationTask.AsUniTask();
    }

    async Task InitializeCoreAsync(int _generation)
    {
        try
        {
            DependencyStatus t_dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (_generation != this.generation) return;
            if (t_dependencyStatus != DependencyStatus.Available)
            {
                SetFailure(EFirebaseAuthState.Unavailable, $"Firebase dependencies unavailable: {t_dependencyStatus}");
                return;
            }

            this.auth = FirebaseAuth.DefaultInstance;
            SubscribeStateChanged();
            FirebaseUser t_user = this.auth.CurrentUser;
            if (t_user == null)
            {
                AuthResult t_result = await this.auth.SignInAnonymouslyAsync();
                if (_generation != this.generation) return;
                t_user = t_result.User;
            }

            if (t_user == null || string.IsNullOrEmpty(t_user.UserId))
            {
                SetFailure(EFirebaseAuthState.Failed, "Firebase anonymous sign-in returned no user.");
                return;
            }

            if (!CanAcceptUser(t_user.UserId))
                return;

            this.UserId = t_user.UserId;
            this.LastError = string.Empty;
            SetState(EFirebaseAuthState.SignedIn);
        }
        catch (Exception _exception)
        {
            if (_generation != this.generation) return;
            SetFailure(EFirebaseAuthState.Failed, _exception.GetBaseException().Message);
        }
    }

    internal void Shutdown()
    {
        this.generation++;
        if (this.stateChangedSubscribed && this.auth != null)
            this.auth.StateChanged -= HandleAuthStateChanged;
        this.stateChangedSubscribed = false;
        this.auth = null;
        this.initializationTask = null;
        this.sessionUserId = string.Empty;
        this.UserId = string.Empty;
        this.LastError = string.Empty;
        this.State = EFirebaseAuthState.Uninitialized;
        this.OnStateChanged = null;
    }

    void SetFailure(EFirebaseAuthState _state, string _error)
    {
        this.LastError = _error;
        SetState(_state);
    }

    void SubscribeStateChanged()
    {
        if (this.stateChangedSubscribed) return;
        this.auth.StateChanged += HandleAuthStateChanged;
        this.stateChangedSubscribed = true;
    }

    void HandleAuthStateChanged(object _sender, EventArgs _args)
    {
        FirebaseUser t_user = this.auth?.CurrentUser;
        if (t_user == null)
        {
            if (!string.IsNullOrEmpty(this.sessionUserId))
            {
                SetFailure(EFirebaseAuthState.Failed, "Firebase account signed out. Restart is required.");
                return;
            }

            this.UserId = string.Empty;
            this.LastError = string.Empty;
            SetState(EFirebaseAuthState.Uninitialized);
            return;
        }

        if (!CanAcceptUser(t_user.UserId))
            return;

        this.UserId = t_user.UserId;
        this.LastError = string.Empty;
        SetState(EFirebaseAuthState.SignedIn);
    }

    bool CanAcceptUser(string _userId)
    {
        if (string.IsNullOrEmpty(this.sessionUserId))
        {
            this.sessionUserId = _userId;
            return true;
        }

        if (this.sessionUserId == _userId)
            return true;

        SetFailure(EFirebaseAuthState.Failed, "Firebase account changed. Restart is required.");
        return false;
    }

    void SetState(EFirebaseAuthState _state)
    {
        this.State = _state;
        this.OnStateChanged?.Invoke();
    }
}
