using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

public sealed class FirebaseAuthService
{
    // 인증 부기는 LocalPrefs 에 둔다 — PlayerPrefs 는 ParrelSync 클론과 원본이 공유하므로
    // 한쪽의 백엔드 전환이 다른 쪽 익명 계정까지 버리게 만든다. LocalPrefs 는 DevAccountScope 폴더로 갈린다.
    const string AuthBackendPrefsKey = "firebase.authBackend";
    const string LiveBackendName = "live";

    // 백엔드별로 마지막 익명 uid를 남긴다. 되로그인은 불가하지만 콘솔에서 그 계정의 문서를 찾을 수는 있다.
    const string UidPrefsKeyPrefix = "firebase.authUid.";
    // 이메일 로그인으로 버려진 익명 uid 자리. 백엔드 전환 기록과 같은 접두사를 쓴다.
    const string AbandonedOnSignInKey = "abandonedOnEmailSignIn";

    // 기기에 남은 계정의 복원을 기다리는 창(50ms x 30 = 1.5초). 부트 인증 예산
    // (FirebaseTimeouts.AuthAndReadMilliseconds) 안에서 끝나야 하므로 늘릴 때는 그쪽을 함께 본다.
    const int RestorePollIntervalMilliseconds = 50;
    const int RestorePollAttempts = 30;

    public static FirebaseAuthService Instance { get; } = new FirebaseAuthService();

    public event Action OnStateChanged;

    public EFirebaseAuthState State { get; private set; } = EFirebaseAuthState.Uninitialized;

    /// <summary>Firebase 네이티브 SDK 적재가 이 프로세스에서 이미 끝났는가. 소비자는 이걸로 콜드 부트와
    /// 데워진 뒤를 갈라 인증 대기 예산을 고른다(<see cref="FirebaseTimeouts.SdkColdStartMilliseconds"/>).</summary>
    public static bool DependenciesReady { get; private set; }

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

    /// <summary>이미 있는 이메일 계정으로 로그인한다.</summary>
    public UniTask<bool> SignInWithEmailAndPasswordAsync(string _email, string _password)
        => SwitchToEmailAccountAsync(_email, _password, false);

    /// <summary>이메일 계정을 새로 만들고 그 계정으로 들어간다.
    /// 이미 있는 이메일이면 실패한다 — 그 경우는 로그인이 맞는 동작이라 조용히 갈아타지 않는다.</summary>
    public UniTask<bool> CreateAccountWithEmailAndPasswordAsync(string _email, string _password)
        => SwitchToEmailAccountAsync(_email, _password, true);

    // 로그인과 가입은 마지막 한 호출만 다르고 나머지(세대 증가·세션 uid 초기화·익명 폐기 기록·
    // 상태 억제)가 전부 같다. 절차를 두 벌로 두면 한쪽만 고쳐 어긋난다 — 실제로 그렇게 어긋난 적이 있다.
    async UniTask<bool> SwitchToEmailAccountAsync(string _email, string _password, bool _createAccount)
    {
        if (this.changingAnonymousAccount) return false;

        // 빈 입력은 인증 실패가 아니라 화면의 문제다. 여기서 State 를 Failed 로 내리면
        // OnStateChanged 를 타고 PlayerSaveCloud·CloudSyncStatusWatcher 가 세션 사고로 읽는다.
        if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrEmpty(_password)) return false;

        this.changingAnonymousAccount = true;
        this.suppressAuthStateChanges = true;
        try
        {
            this.generation++;
            this.initializationTask = null;
            this.sessionUserId = string.Empty;
            this.UserId = string.Empty;
            this.LastError = string.Empty;

            // SDK만 세운다. InitializeAsync 를 타면 복원할 계정이 없을 때 익명 계정을 발급하는데,
            // 바로 아래 SignOut 이 그것을 버려 콘솔에 주인 없는 계정만 남는다.
            if (!(await PrepareAuthAsync(this.generation)).ok) return false;

            RecordAbandonedAnonymousAccount();
            this.auth.SignOut();

            string t_trimmed = _email.Trim();
            AuthResult t_result = _createAccount
                ? await this.auth.CreateUserWithEmailAndPasswordAsync(t_trimmed, _password)
                : await this.auth.SignInWithEmailAndPasswordAsync(t_trimmed, _password);

            FirebaseUser t_user = t_result.User;
            if (t_user == null || string.IsNullOrEmpty(t_user.UserId))
            {
                SetFailure(EFirebaseAuthState.Failed,
                    _createAccount ? "Firebase account creation returned no user."
                                   : "Firebase email sign-in returned no user.");
                return false;
            }

            if (!CanAcceptUser(t_user.UserId)) return false;

            this.UserId = t_user.UserId;
            this.LastError = string.Empty;
            SetState(EFirebaseAuthState.SignedIn);
            Debug.Log(_createAccount
                ? $"[FirebaseAuth] 이메일 계정을 새로 만들었습니다(uid={this.UserId})."
                : $"[FirebaseAuth] 이메일 계정으로 로그인했습니다(uid={this.UserId}).");
            return true;
        }
        catch (Exception _exception)
        {
            bool t_known = TryAuthError(_exception, out AuthError t_authError);
            string t_code = t_known ? t_authError.ToString() : "Unknown";

            SetFailure(
                EFirebaseAuthState.Failed,
                $"{(_createAccount ? "이메일 가입" : "이메일 로그인")} 실패(code={t_code}): " +
                $"{DescribeEmailFailure(t_authError, _createAccount)} " +
                $"[{_exception.GetBaseException().Message}]");
            return false;
        }
        finally
        {
            this.suppressAuthStateChanges = false;
            this.changingAnonymousAccount = false;
        }
    }

    // ── 이메일 계정 전환의 공용 헬퍼 ─────────────────────────────────────
    // 개발 전용 블록(#if) **밖**에 있어야 한다 — 이메일 로그인·가입은 프로덕션 경로이고,
    // 안에 두면 에디터에서는 멀쩡한데 릴리스 플레이어 빌드만 CS0103 으로 깨진다(실제로 그랬다).

    /// <summary>익명 계정을 버리기 직전에 그 uid 를 남긴다.
    ///
    /// <para>익명 uid 는 로그아웃하면 다시 로그인할 방법이 없다 — 이 계정의 세이브 문서를 콘솔에서
    /// 찾아낼 마지막 단서가 이 로그와 prefs 한 줄뿐이다. 계정 <b>연결</b>(LinkWithCredential)이 아니라
    /// 전환이라 진행도가 그대로 끊기므로, 흔적 없이 버리지 않는다.</para></summary>
    void RecordAbandonedAnonymousAccount()
    {
        FirebaseUser t_current = this.auth?.CurrentUser;
        if (t_current == null || !t_current.IsAnonymous || string.IsNullOrEmpty(t_current.UserId)) return;

        LocalPrefs.SetString(UidPrefsKeyPrefix + AbandonedOnSignInKey, t_current.UserId);
        LocalPrefs.Save();
        Debug.LogWarning(
            $"[FirebaseAuth] 이메일 로그인을 위해 익명 계정 {t_current.UserId} 를 버린다 — 다시 로그인할 수 없다. " +
            $"기록 위치 LocalPrefs[{UidPrefsKeyPrefix}{AbandonedOnSignInKey}].");
    }

    /// <summary>SDK 코드를 사람이 고칠 수 있는 문장으로 바꾼다.
    ///
    /// <para><see cref="AuthError.Failure"/> 를 따로 잡는 이유: 프로젝트에 이메일 열거 방지가 켜져 있으면
    /// 서버가 <i>계정 없음</i>과 <i>비밀번호 불일치</i>를 INVALID_LOGIN_CREDENTIALS 하나로 합쳐 돌려주고,
    /// Unity SDK 는 그걸 Failure + "An internal error has occurred." 로 뭉갠다 —
    /// 그대로 두면 화면이 원인을 한 글자도 말하지 못한다.</para></summary>
    static string DescribeEmailFailure(AuthError _error, bool _createAccount)
    {
        switch (_error)
        {
            case AuthError.EmailAlreadyInUse:
                return "이미 가입된 이메일입니다 — 로그인으로 들어가세요.";
            case AuthError.WrongPassword:
                return "비밀번호가 맞지 않습니다.";
            case AuthError.UserNotFound:
                return "가입되지 않은 이메일입니다 — 먼저 가입하세요.";
            case AuthError.InvalidEmail:
                return "이메일 형식이 올바르지 않습니다.";
            case AuthError.WeakPassword:
                return "비밀번호가 너무 짧습니다(6자 이상).";
            case AuthError.OperationNotAllowed:
                return "Firebase 콘솔에서 Authentication > Sign-in method > 이메일/비밀번호를 켜야 합니다.";
            case AuthError.NetworkRequestFailed:
                return "네트워크에 연결하지 못했습니다.";
            case AuthError.Failure:
                return _createAccount
                    ? "계정을 만들지 못했습니다 — 이메일/비밀번호 로그인이 켜져 있는지 확인하세요."
                    : "가입되지 않은 이메일이거나 비밀번호가 틀렸습니다.";
            default:
                return string.Empty;
        }
    }

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

            // 이메일 경로와 같은 이유로 SDK만 세운다(아래 SignOut 이 버릴 익명 계정을 만들지 않는다).
            if (!(await PrepareAuthAsync(this.generation)).ok) return false;

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

    /// <summary>Firebase Auth SDK 를 쓸 수 있는 상태까지만 만든다 — <b>로그인은 하지 않는다.</b>
    ///
    /// <para>부트(<see cref="InitializeCoreAsync"/>)와 계정 전환 경로가 공유한다. 전환 경로가
    /// <see cref="InitializeAsync"/> 를 타면 복원할 계정이 없을 때 익명 계정을 발급하고,
    /// 곧이어 SignOut 이 그것을 버려 다시 로그인할 수 없는 계정만 콘솔에 쌓인다.</para>
    ///
    /// <para>abandonedAccount = 백엔드가 바뀌어 기기의 익명 계정을 버렸다. 그때는 복원할 것이 없는 게
    /// 확정이라 부트가 기다리지 않는다.</para></summary>
    async UniTask<(bool ok, bool abandonedAccount)> PrepareAuthAsync(int _generation)
    {
        if (this.auth != null) return (true, false);

        var t_watch = System.Diagnostics.Stopwatch.StartNew();
        DependencyStatus t_dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
        long t_dependencyMilliseconds = t_watch.ElapsedMilliseconds;
        if (_generation != this.generation) return (false, false);
        if (t_dependencyStatus != DependencyStatus.Available)
        {
            SetFailure(EFirebaseAuthState.Unavailable, $"Firebase dependencies unavailable: {t_dependencyStatus}");
            return (false, false);
        }

        // 콜드 적재가 얼마나 걸렸는지 남기지 않으면, 인증 타임아웃이 네트워크 탓인지 SDK 적재 탓인지 사후에 못 가른다.
        DependenciesReady = true;
        Debug.Log($"[FirebaseAuth] SDK dependencies ready in {t_dependencyMilliseconds}ms.");

        this.auth = FirebaseAuth.DefaultInstance;
        bool t_abandoned = ApplyBackend();
        SubscribeStateChanged();
        return (true, t_abandoned);
    }

    async Task InitializeCoreAsync(int _generation)
    {
        try
        {
            (bool t_ready, bool t_abandonedAccount) = await PrepareAuthAsync(_generation);
            if (!t_ready) return;

            FirebaseUser t_user = t_abandonedAccount
                ? null
                : await WaitForPersistedUserAsync(_generation);
            if (_generation != this.generation) return;

            bool t_mintedAccount = false;
            if (t_user == null)
            {
                AuthResult t_result = await this.auth.SignInAnonymouslyAsync();
                if (_generation != this.generation) return;
                t_user = t_result.User;
                t_mintedAccount = true;
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

            // 계정이 새로 생긴 순간이 곧 이전 진행도가 끊긴 순간이다. 부트 경로는 uid를 어디에도 남기지
            // 않아, 이 두 줄이 없으면 콘솔의 익명 계정이 왜 늘었는지 사후에 가릴 방법이 없다.
            if (t_mintedAccount)
                Debug.LogWarning($"[FirebaseAuth] 복원할 계정이 없어 새 익명 계정을 발급했습니다(uid={this.UserId}).");
            else
                Debug.Log($"[FirebaseAuth] 기기에 남은 익명 계정으로 복원했습니다(uid={this.UserId}).");

            SetState(EFirebaseAuthState.SignedIn);
        }
        catch (Exception _exception)
        {
            if (_generation != this.generation) return;
            SetFailure(EFirebaseAuthState.Failed, _exception.GetBaseException().Message);
        }
    }

    // 기기에 남은 계정의 복원은 비동기다 — DefaultInstance 직후의 CurrentUser 한 번만 보고 판정하면
    // 아직 올라오지 않은 계정을 "없다"로 읽어 새 익명 계정을 발급하고, 그 순간 이전 진행도가 끊긴다.
    // 창을 다 쓰고도 비어 있을 때만 이 기기에 계정이 없는 것으로 본다.
    async UniTask<FirebaseUser> WaitForPersistedUserAsync(int _generation)
    {
        for (int t_attempt = 0; t_attempt < RestorePollAttempts; t_attempt++)
        {
            FirebaseUser t_user = this.auth?.CurrentUser;
            if (t_user != null && !string.IsNullOrEmpty(t_user.UserId)) return t_user;

            await UniTask.Delay(RestorePollIntervalMilliseconds, DelayType.Realtime);
            if (_generation != this.generation) return null;
        }

        return this.auth?.CurrentUser;
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
    // 백엔드가 바뀌어 계정을 버렸으면 true — 그때는 기기에 복원할 계정이 없는 게 확정이라 기다리지 않는다.
    bool ApplyBackend()
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
        string t_previous = LocalPrefs.GetString(AuthBackendPrefsKey, LiveBackendName);
        if (t_previous == t_backend) return false;

        // 익명 uid는 로그아웃하면 다시 로그인할 방법이 없다 — 그 계정의 세이브 문서를 콘솔에서 찾아낼
        // 마지막 단서가 이 로그와 prefs 한 줄뿐이다.
        string t_abandoned = this.auth.CurrentUser != null ? this.auth.CurrentUser.UserId : string.Empty;
        if (!string.IsNullOrEmpty(t_abandoned))
        {
            LocalPrefs.SetString(UidPrefsKeyPrefix + t_previous, t_abandoned);
            Debug.LogWarning(
                $"[FirebaseAuth] Backend changed '{t_previous}' -> '{t_backend}'. " +
                $"Abandoning the anonymous account {t_abandoned}; it can never be signed in again. " +
                $"Recorded at LocalPrefs[{UidPrefsKeyPrefix}{t_previous}].");
        }

        this.auth.SignOut();
        LocalPrefs.SetString(AuthBackendPrefsKey, t_backend);
        LocalPrefs.Save();
        return true;
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
