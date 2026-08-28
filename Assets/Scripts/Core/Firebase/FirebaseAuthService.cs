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
    bool changingAnonymousAccount;
    bool suppressAuthStateChanges;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 실제 메일이 나가지 않는 예약 도메인(RFC 2606)이라 오타로도 남의 주소를 건드리지 않는다.
    const string TEST_ACCOUNT_EMAIL_DOMAIN = "example.com";
    const string TEST_ACCOUNT_EMAIL_PREFIX = "mp-";
    const string TEST_ACCOUNT_PASSWORD_PREFIX = "TestPw!9-";
#endif

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public async UniTask<bool> SignInAsNewAnonymousAsync()
    {
        if (this.changingAnonymousAccount) return false;
        if (ContentProfileConfig.Active.RunMode != EContentRunMode.Test)
        {
            UnityEngine.Debug.LogWarning("[FirebaseAuth] 새 익명 계정 발급은 Test 런모드에서만 허용됩니다.");
            return false;
        }

        if (this.auth == null)
        {
            await InitializeAsync();
            if (this.auth == null || !this.IsCurrentUserActive) return false;
        }

        this.changingAnonymousAccount = true;
        this.suppressAuthStateChanges = true;
        try
        {
            this.generation++;
            this.initializationTask = null;
            this.sessionUserId = string.Empty;
            this.UserId = string.Empty;
            this.LastError = string.Empty;

            this.auth.SignOut();
            AuthResult t_result = await this.auth.SignInAnonymouslyAsync();
            FirebaseUser t_user = t_result.User;
            if (t_user == null || string.IsNullOrEmpty(t_user.UserId))
            {
                SetFailure(EFirebaseAuthState.Failed, "Firebase anonymous sign-in returned no user.");
                return false;
            }

            if (!CanAcceptUser(t_user.UserId)) return false;

            this.UserId = t_user.UserId;
            this.LastError = string.Empty;
            this.suppressAuthStateChanges = false;
            SetState(EFirebaseAuthState.SignedIn);
            UnityEngine.Debug.Log($"[FirebaseAuth] 새 테스트 익명 계정으로 로그인했습니다(uid={this.UserId}).");
            return true;
        }
        catch (Exception _exception)
        {
            SetFailure(EFirebaseAuthState.Failed, _exception.GetBaseException().Message);
            return false;
        }
        finally
        {
            this.changingAnonymousAccount = false;
        }
    }

    /// <summary>지정한 테스트 계정 id로 로그인한다. 같은 id면 항상 같은 uid가 나온다.
    /// 익명 로그인은 기기당 persistence 슬롯이 하나뿐이라 한 PC에서 두 클라이언트를 서로 다른 계정으로
    /// 띄울 수 없다 — 그래서 id로 이메일/비밀번호를 만들어 쓴다. Firebase 콘솔에서 이메일/비밀번호
    /// 공급자를 켜 두어야 하며, test 프로젝트 전용이다.</summary>
    public async UniTask<bool> SignInAsTestAccountAsync(string _accountId)
    {
        if (this.changingAnonymousAccount) return false;
        if (string.IsNullOrWhiteSpace(_accountId))
        {
            UnityEngine.Debug.LogWarning("[FirebaseAuth] 테스트 계정 id가 비어 있습니다.");
            return false;
        }
        if (ContentProfileConfig.Active.RunMode != EContentRunMode.Test)
        {
            UnityEngine.Debug.LogWarning("[FirebaseAuth] 테스트 계정 로그인은 Test 런모드에서만 허용됩니다.");
            return false;
        }

        // 익명 전환과 달리 현재 로그인 상태를 요구하지 않는다 — auth 객체만 서 있으면 갈아탈 수 있다.
        if (this.auth == null)
        {
            await InitializeAsync();
            if (this.auth == null) return false;
        }

        string t_slug = TestAccountSlug(_accountId);
        string t_email = $"{TEST_ACCOUNT_EMAIL_PREFIX}{t_slug}@{TEST_ACCOUNT_EMAIL_DOMAIN}";
        string t_password = $"{TEST_ACCOUNT_PASSWORD_PREFIX}{t_slug}";

        this.changingAnonymousAccount = true;
        this.suppressAuthStateChanges = true;
        try
        {
            this.generation++;
            this.initializationTask = null;
            this.sessionUserId = string.Empty;
            this.UserId = string.Empty;
            this.LastError = string.Empty;

            this.auth.SignOut();

            FirebaseUser t_user = await SignInOrCreateTestUserAsync(t_email, t_password);
            if (t_user == null || string.IsNullOrEmpty(t_user.UserId))
            {
                SetFailure(EFirebaseAuthState.Failed, $"Test account sign-in returned no user (id={_accountId}).");
                return false;
            }

            if (!CanAcceptUser(t_user.UserId)) return false;

            this.UserId = t_user.UserId;
            this.LastError = string.Empty;
            SetState(EFirebaseAuthState.SignedIn);
            UnityEngine.Debug.Log($"[FirebaseAuth] 테스트 계정 '{_accountId}'로 로그인했습니다(uid={this.UserId}).");
            return true;
        }
        catch (Exception _exception)
        {
            // 코드를 같이 찍는다 — "An internal error has occurred."만 보면 공급자 비활성인지
            // 네트워크 문제인지 구분이 안 돼 추적이 길어진다.
            string t_code = TryAuthError(_exception, out AuthError t_authError)
                ? t_authError.ToString()
                : "Unknown";
            SetFailure(
                EFirebaseAuthState.Failed,
                $"테스트 계정 로그인 실패(code={t_code}): {_exception.GetBaseException().Message}. " +
                "Firebase 콘솔에서 Authentication > Sign-in method > 이메일/비밀번호가 켜져 있는지 확인해라.");
            return false;
        }
        finally
        {
            // 실패해도 억제를 풀어야 이후 인증 상태 변화가 다시 관측된다.
            this.suppressAuthStateChanges = false;
            this.changingAnonymousAccount = false;
        }
    }

    // 처음 쓰는 id면 계정이 없다 — 없을 때만 만든다.
    //
    // "없다"를 에러 코드로 못 가른다는 게 이 메서드의 전제다. 프로젝트에 이메일 열거 방지가 켜져 있으면
    // 서버가 계정 없음과 비밀번호 불일치를 INVALID_LOGIN_CREDENTIALS 하나로 합쳐 돌려주고,
    // Unity SDK는 그걸 AuthError.Failure로 뭉갠다 — UserNotFound만 보면 생성 분기가 영영 안 선다.
    // 그래서 순서를 뒤집는다: 비밀번호 불일치가 확실한 경우만 빼고 일단 만들어 보고,
    // 이미 있는 계정이면(EmailAlreadyInUse) 그때 원래 로그인 실패를 그대로 올린다.
    async UniTask<FirebaseUser> SignInOrCreateTestUserAsync(string _email, string _password)
    {
        Exception t_signInFailure;
        try
        {
            AuthResult t_result = await this.auth.SignInWithEmailAndPasswordAsync(_email, _password);
            return t_result.User;
        }
        catch (Exception t_exception)
        {
            if (IsWrongPassword(t_exception)) throw;
            t_signInFailure = t_exception;
        }

        try
        {
            AuthResult t_created = await this.auth.CreateUserWithEmailAndPasswordAsync(_email, _password);
            return t_created.User;
        }
        catch (Exception t_exception) when (IsEmailAlreadyInUse(t_exception))
        {
            // 계정은 있는데 로그인이 안 됐다 = 자격 증명 문제. 생성 실패가 아니라 로그인 실패를 보여줘야 원인이 보인다.
            throw t_signInFailure;
        }
    }

    static bool IsWrongPassword(Exception _exception)
        => TryAuthError(_exception, out AuthError t_error) && t_error == AuthError.WrongPassword;

    static bool IsEmailAlreadyInUse(Exception _exception)
        => TryAuthError(_exception, out AuthError t_error) && t_error == AuthError.EmailAlreadyInUse;

    // Firebase는 실제 원인을 AggregateException 안쪽에 넣어 던진다 — 사슬을 끝까지 훑어야 코드가 나온다.
    static bool TryAuthError(Exception _exception, out AuthError _error)
    {
        _error = AuthError.Failure;
        for (Exception t_current = _exception; t_current != null; t_current = t_current.InnerException)
        {
            if (t_current is FirebaseException t_firebase)
            {
                _error = (AuthError)t_firebase.ErrorCode;
                return true;
            }
        }
        return false;
    }

    // 이메일 주소로 쓸 수 있는 문자만 남긴다 — id에 공백·한글이 들어와도 로그인이 깨지지 않게.
    static string TestAccountSlug(string _accountId)
    {
        System.Text.StringBuilder t_builder = new System.Text.StringBuilder(_accountId.Length);
        foreach (char t_char in _accountId.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(t_char) && t_char < 128) t_builder.Append(t_char);
            else if (t_char == '-' || t_char == '_') t_builder.Append(t_char);
        }
        return t_builder.Length == 0 ? "default" : t_builder.ToString();
    }
#endif

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
        this.changingAnonymousAccount = false;
        this.suppressAuthStateChanges = false;
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
        // 로그인 실패는 여기서만 알 수 있다. 안 찍으면 소비자 쪽에서 UNAUTHENTICATED 같은
        // 2차 증상으로만 드러나 원인 추적이 길어진다.
        UnityEngine.Debug.LogWarning($"[FirebaseAuth] {_state}: {_error}");
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
        if (this.suppressAuthStateChanges) return;

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
