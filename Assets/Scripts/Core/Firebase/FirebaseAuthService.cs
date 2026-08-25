using System;
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

    FirebaseAuthService() { }

    public async UniTask InitializeAsync()
    {
        if (this.State == EFirebaseAuthState.Initializing ||
            (this.State == EFirebaseAuthState.SignedIn && this.IsCurrentUserActive))
            return;

        SetState(EFirebaseAuthState.Initializing);

        try
        {
            DependencyStatus t_dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync().AsUniTask();
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
                AuthResult t_result = await this.auth.SignInAnonymouslyAsync().AsUniTask();
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
            SetFailure(EFirebaseAuthState.Failed, _exception.GetBaseException().Message);
        }
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
