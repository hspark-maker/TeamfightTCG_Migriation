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

    FirebaseAuth auth;

    FirebaseAuthService() { }

    public async UniTask InitializeAsync()
    {
        if (this.State == EFirebaseAuthState.Initializing || this.State == EFirebaseAuthState.SignedIn)
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
        this.UserId = string.Empty;
        this.LastError = _error;
        SetState(_state);
    }

    void SetState(EFirebaseAuthState _state)
    {
        this.State = _state;
        this.OnStateChanged?.Invoke();
    }
}
