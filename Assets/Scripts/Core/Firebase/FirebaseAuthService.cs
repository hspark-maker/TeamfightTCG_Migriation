using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

public sealed class FirebaseAuthService
{
    const string AuthBackendPrefsKey = "firebase.authBackend";
    const string LiveBackendName = "live";

    // 백엔드별로 마지막 익명 uid를 남긴다. 되로그인은 불가하지만 콘솔에서 그 계정의 문서를 찾을 수는 있다.
    const string UidPrefsKeyPrefix = "firebase.authUid.";

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
    string emulatorHost = string.Empty;
    int emulatorPort;
    Task initializationTask;
    int generation;

    FirebaseAuthService() { }

    /// <summary>익명 로그인이 향할 Auth 에뮬레이터를 지정한다(빈 호스트면 실서버). 첫 인증 시도 전에 불러야 한다.</summary>
    internal void UseEmulator(string _host, int _port)
    {
        this.emulatorHost = string.IsNullOrWhiteSpace(_host) ? string.Empty : _host.Trim();
        this.emulatorPort = _port;
    }

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
            ApplyBackend();
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

    // 에뮬레이터 배선 + 백엔드 전환 정리. 구독 전에 불려야 여기서 나는 로그아웃이 세션 종료로 오인되지 않는다.
    void ApplyBackend()
    {
        string t_backend = string.IsNullOrEmpty(this.emulatorHost)
            ? LiveBackendName
            : $"{this.emulatorHost}:{this.emulatorPort}";

        if (!string.IsNullOrEmpty(this.emulatorHost))
        {
            this.auth.UseEmulator(this.emulatorHost, this.emulatorPort);
            Debug.LogWarning($"[FirebaseAuth] Signing in against the auth emulator at {t_backend}.");
        }

        // 기기에 남은 익명 계정은 그 계정을 발급한 백엔드에서만 유효하다 — 에뮬레이터와 실서버를 오가면
        // 반대 축의 uid가 되살아나 토큰 검증부터 실패한다. 백엔드가 바뀐 첫 부트에서만 비우고, 같은 축이면 uid를 유지한다.
        string t_previous = PlayerPrefs.GetString(AuthBackendPrefsKey, LiveBackendName);
        if (t_previous == t_backend) return;

        // 익명 uid는 로그아웃하면 다시 로그인할 방법이 없다 — 그 계정의 세이브 문서를 콘솔에서 찾아낼
        // 마지막 단서가 이 로그와 prefs 한 줄뿐이다.
        string t_abandoned = this.auth.CurrentUser != null ? this.auth.CurrentUser.UserId : string.Empty;
        if (!string.IsNullOrEmpty(t_abandoned))
        {
            PlayerPrefs.SetString(UidPrefsKeyPrefix + t_previous, t_abandoned);
            Debug.LogWarning(
                $"[FirebaseAuth] Backend changed '{t_previous}' -> '{t_backend}'. " +
                $"Abandoning the anonymous account {t_abandoned}; it can never be signed in again. " +
                $"Recorded at PlayerPrefs[{UidPrefsKeyPrefix}{t_previous}].");
        }

        this.auth.SignOut();
        PlayerPrefs.SetString(AuthBackendPrefsKey, t_backend);
        PlayerPrefs.Save();
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
