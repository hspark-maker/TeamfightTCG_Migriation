using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 이메일 로그인·가입 화면. 두 동작을 버튼 둘로 나누지 않고 <b>모드 하나</b>로 다룬다 —
// 나눠 두면 Enter 제출이 어느 쪽인지 정할 수 없어, 가입하려던 입력이 로그인으로 나가 실패한다(실제로 그랬다).
//
// 제출 버튼은 언제나 loginButton 하나이고, signUpButton 은 그 버튼이 무엇을 제출할지 바꾸는 전환 링크다.
public sealed class LoginEmailPanel : MonoBehaviour
{
    [SerializeField] TMP_InputField emailField;
    [SerializeField] TMP_InputField passwordField;

    [Tooltip("제출 버튼. 현재 모드에 따라 로그인 또는 가입을 보낸다.")]
    [SerializeField] Button loginButton;
    [SerializeField] TextMeshProUGUI loginButtonLabel;

    [Tooltip("게스트로 시작. 미배선이면 이 화면은 이메일 로그인만 제공한다.")]
    [SerializeField] Button anonymousButton;

    [Tooltip("로그인 ↔ 가입 전환 링크. 미배선이면 이 화면은 로그인 전용이 된다.")]
    [SerializeField] Button signUpButton;

    const string SubmitLoginLabel = "LOGIN";
    const string SubmitSignUpLabel = "SIGN UP";
    const string ToggleToSignUpLabel = "Sign Up";
    const string ToggleToLoginLabel = "Login";
    const int MinPasswordLength = 6;

    bool signingIn;

    // 제출이 가입인가. false 면 로그인이다. Enter 제출도 이 값을 따른다.
    bool signUpMode;

    // 전환 링크의 글자. 버튼이 TMP 텍스트 자신에 붙어 있어도, 자식에 있어도 잡힌다.
    TextMeshProUGUI m_signUpLabel;

    void Awake()
    {
        // 정렬 층(UiSortingOrder.SignIn)과 GraphicRaycaster 는 프리팹에 저작돼 있다 —
        // 부트 로딩 커버(1000)보다 위여야 하고, 레이캐스터가 없으면 보이기만 하고 탭이 안 먹는다.

        // 배선은 숨길 때도 반드시 한다 — 계정 버튼으로 나중에 다시 열리는데,
        // 그때는 Awake 가 이미 지나가서 여기서 걸지 않으면 죽은 화면이 뜬다.
        this.loginButton.onClick.AddListener(Submit);
        this.passwordField.onSubmit.AddListener(SubmitFromKeyboard);
        if (this.anonymousButton != null) this.anonymousButton.onClick.AddListener(ContinueAsGuest);
        if (this.signUpButton != null)
        {
            this.signUpButton.onClick.AddListener(ToggleSignUpMode);
            m_signUpLabel = this.signUpButton.GetComponentInChildren<TextMeshProUGUI>(true);
        }
    }

    /// <summary>로딩 화면의 계정 버튼이 여는 입구.</summary>
    public void Open()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    void OnEnable()
    {
        // 열 때마다 로그인 모드로 되돌린다 — 직전에 가입을 누르고 닫았다고 다음에 가입으로 열리면
        // 유저가 무엇을 제출하는지 모른 채 비밀번호를 넣게 된다.
        this.signUpMode = false;
        SetBusy(false);

        // 관문에 화면이 섰다고 알린다. Awake 가 아니라 여기인 이유: 이 화면은 씬에 <b>비활성</b>으로
        // 저작돼 있고, 비활성 오브젝트는 Awake 가 돌지 않아 자기 존재를 알릴 방법이 없다.
        // 누가 켜는지는 LoadingCoverView 가 정한다.
        SignInGate.MarkPanelReady();
    }

    void OnDestroy()
    {
        this.loginButton.onClick.RemoveListener(Submit);
        this.passwordField.onSubmit.RemoveListener(SubmitFromKeyboard);
        if (this.anonymousButton != null) this.anonymousButton.onClick.RemoveListener(ContinueAsGuest);
        if (this.signUpButton != null) this.signUpButton.onClick.RemoveListener(ToggleSignUpMode);
    }

    /// <summary>게스트로 시작. 계정을 여기서 만들지 않는다 — 관문이 열리면 부트의 인증 경로가
    /// 기기에 남은 익명 계정을 복원하고, 없을 때만 새로 발급한다. 그 자격증명은 Firebase SDK 가
    /// 기기에 들고 있으므로 다음 실행에도 같은 계정으로 들어온다.</summary>
    void ContinueAsGuest()
    {
        if (this.signingIn) return;

        SetBusy(true, "Starting...");
        SignInGate.Complete(ESignInMethod.Anonymous);
        gameObject.SetActive(false);
    }

    // 제출할 내용을 바꾼다. 여기서 계정을 만들지 않는다 — 만드는 것은 제출 버튼의 일이다.
    void ToggleSignUpMode()
    {
        if (this.signingIn) return;

        this.signUpMode = !this.signUpMode;
        SetBusy(false);
    }

    void SubmitFromKeyboard(string _) => Submit();

    void Submit() => RunEmailFlowAsync(this.signUpMode).Forget();

    // 로그인과 가입은 부르는 API 와 문구만 다르다. 성공 후 처리(비번 비우기·관문 확정·부트 재기동)가
    // 같으므로 한 자리에 둔다 — 나뉘어 있으면 한쪽만 고쳐 어긋난다.
    async UniTaskVoid RunEmailFlowAsync(bool _createAccount)
    {
        if (this.signingIn) return;

        string t_email = this.emailField.text.Trim();
        string t_password = this.passwordField.text;
        if (string.IsNullOrEmpty(t_email) || string.IsNullOrEmpty(t_password))
        {
            SetBusy(false, "Enter email & password");
            return;
        }

        // Firebase 는 6자 미만을 거절한다. 왕복을 태우면 code=WeakPassword 로만 돌아와
        // 화면이 "무엇이 잘못됐는지"를 못 말한다.
        if (_createAccount && t_password.Length < MinPasswordLength)
        {
            SetBusy(false, $"Password: {MinPasswordLength}+ chars");
            return;
        }

        SetBusy(true, _createAccount ? "Creating..." : "Signing in...");

        FirebaseAuthService t_auth = FirebaseAuthService.Instance;
        bool t_ok = _createAccount
            ? await t_auth.CreateAccountWithEmailAndPasswordAsync(t_email, t_password)
            : await t_auth.SignInWithEmailAndPasswordAsync(t_email, t_password);
        if (!this) return;

        if (!t_ok)
        {
            // 실패 사유는 FirebaseAuthService 가 사람이 읽을 문장으로 로그에 남긴다.
            SetBusy(false, _createAccount ? "Sign up failed" : "Login failed");
            return;
        }

        this.passwordField.text = string.Empty;
        SignInGate.Complete(ESignInMethod.Email);
        gameObject.SetActive(false);

        // 이미 다른 계정으로 세이브를 채택한 세션이면 갈아끼울 수 없다 — 부트를 처음부터 다시 태운다.
        // 관문이 아직 안 열린 첫 실행에서는 아무 일도 하지 않는다(그쪽은 부트가 이어서 돈다).
        GameManager.RestartForAccountChange();
    }

    // _label 을 주면 그 문구를 쓰고, 없으면 현재 모드의 기본 문구로 되돌린다.
    void SetBusy(bool _busy, string _label = null)
    {
        this.signingIn = _busy;
        this.loginButton.interactable = !_busy;
        this.emailField.interactable = !_busy;
        this.passwordField.interactable = !_busy;
        if (this.anonymousButton != null) this.anonymousButton.interactable = !_busy;
        if (this.signUpButton != null) this.signUpButton.interactable = !_busy;

        this.loginButtonLabel.text = _label ?? (this.signUpMode ? SubmitSignUpLabel : SubmitLoginLabel);

        // 전환 링크는 "지금 모드"가 아니라 "누르면 갈 곳"을 말한다.
        if (m_signUpLabel != null)
            m_signUpLabel.text = this.signUpMode ? ToggleToLoginLabel : ToggleToSignUpLabel;
    }
}
