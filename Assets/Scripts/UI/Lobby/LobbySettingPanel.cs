using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 프로필 버튼이 처음 여는 판. 프로필 요약을 보여 주고 갈래를 고르게 한다 —
/// 프로필 편집(<see cref="ProfileEditPanel"/>) 진입과 환경설정을 함께 제공한다.
///
/// <para>예전에는 프로필 버튼이 편집 팝업을 곧바로 열었다. 그 자리에 이 판을 끼운 것이라
/// 편집으로 가는 길은 사라지지 않고 여기 버튼 하나로 옮겨 왔다.</para>
///
/// <para>풀(UIPoolManager)이 수명을 쥔다. 표시값은 판 안의 <see cref="LobbyProfileButton"/>·
/// <c>ProfileAvatarView</c>가 <c>ProfileManager</c>에서 스스로 당기므로 UIData 를 받지 않는다.</para></summary>
public class LobbySettingPanel : PooledUIBase
{
    [Tooltip("프로필 편집으로 가는 버튼. 미배선이면 그 길만 없다 — 판은 그대로 뜬다.")]
    [SerializeField] Button editButton;

    [Tooltip("닫기 버튼.")]
    [SerializeField] Button closeButton;

    [Tooltip("바깥 암막 클릭 판정. 닫기와 같은 동작이다.")]
    [SerializeField] Button dimButton;

    [Header("연결된 서비스")]
    [SerializeField] RectTransform serviceRoot;
    [SerializeField] AccountServiceCellView serviceCellPrefab;
    [SerializeField] AccountServiceCellView addServiceCell;

    AccountServiceCellView serviceCell;

    bool confirmingLogout;
    bool loggingOut;

    // 풀 계약. 표시값을 밖에서 받지 않으므로 할 일이 없다.
    public override void Initialization(UIData _data) { }

    public override void Show()
    {
        this.isShow = true;
        gameObject.SetActive(true);
        RefreshAccount();
    }

    public override void Hide()
    {
        this.isShow = false;
        gameObject.SetActive(false);
    }

    protected override void Awake()
    {
        base.Awake();

        // 인스펙터로 걸지 않는 이유는 LobbyProfileButton과 같다 — 가리킬 대상이 풀에서 세워지는 화면이라
        // 저작 시점에는 존재하지 않는다.
        if (this.editButton    != null) this.editButton.onClick.AddListener(OpenProfileEdit);
        if (this.closeButton   != null) this.closeButton.onClick.AddListener(Hide);
        if (this.dimButton     != null) this.dimButton.onClick.AddListener(Hide);
        FirebaseAuthService.Instance.OnStateChanged += RefreshAccount;
    }

    protected override void OnDestroy()
    {
        if (this.editButton    != null) this.editButton.onClick.RemoveListener(OpenProfileEdit);
        if (this.closeButton   != null) this.closeButton.onClick.RemoveListener(Hide);
        if (this.dimButton     != null) this.dimButton.onClick.RemoveListener(Hide);
        FirebaseAuthService.Instance.OnStateChanged -= RefreshAccount;

        base.OnDestroy();
    }

    // 이 판은 닫는다 — 편집 팝업이 그 위에 겹쳐 뜨면 뒤로가기의 목적지가 둘이 된다.
    void OpenProfileEdit()
    {
        ProfileEditPanel t_panel = UIPoolManager.Instance?.AddOrUpdateUI<ProfileEditPanel>(new UIData
        {
            onHide = () =>
            {
                if (this != null) this.Show();
            }
        });
        if (t_panel != null) Hide();
    }

    void RefreshAccount()
    {
        // 다른 목록 패널처럼 프리팹을 한 번 생성하고, 풀 재개방 시 같은 셀을 재사용한다.
        if (this.serviceCell == null && this.serviceRoot != null && this.serviceCellPrefab != null)
        {
            this.serviceCell = Instantiate(this.serviceCellPrefab, this.serviceRoot);
            this.serviceCell.transform.SetAsFirstSibling();
        }
        if (this.serviceCell == null) return;

        FirebaseAuthService t_auth = FirebaseAuthService.Instance;
        bool t_active = t_auth.IsCurrentUserActive;
        this.serviceCell.Bind(EAccountServiceCellState.Connected,
            null,
            t_active && !this.loggingOut && !GameManager.IsLoggingOut, ConfirmLogout);
        this.serviceCell.gameObject.SetActive(t_active);
        if (this.addServiceCell != null)
            this.addServiceCell.Bind(EAccountServiceCellState.Add, null, true, null);
    }

    void ConfirmLogout()
    {
        if (this.confirmingLogout || this.loggingOut || GameManager.IsLoggingOut ||
            !FirebaseAuthService.Instance.IsCurrentUserActive) return;

        this.confirmingLogout = true;
        SimpleYNPopup t_popup = UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = FirebaseAuthService.Instance.IsAnonymous
                ? "게스트 계정에서 로그아웃하면 현재 진행도를 다시 불러올 수 없습니다.\n로그아웃할까요?"
                : "로그아웃하고 로그인 화면으로 돌아갈까요?",
            yesText = "로그아웃",
            noText = "취소",
            yesAction = () => LogoutAsync().Forget(),
            onHide = () => this.confirmingLogout = false,
        });
        if (t_popup == null) this.confirmingLogout = false;
    }

    async UniTask LogoutAsync()
    {
        if (this.loggingOut || GameManager.IsLoggingOut) return;
        this.loggingOut = true;
        RefreshAccount();
        // 확인 팝업이 자신의 Hide를 끝낸 다음 대기/실패 팝업을 연다.
        await UniTask.Yield();
        Hide();
        object t_owner = new object();
        bool t_success = false;
        ServerWaitOverlay.Hold(t_owner);
        try
        {
            t_success = await GameManager.LogoutAsync();
        }
        catch (Exception t_exception)
        {
            Debug.LogException(t_exception);
        }
        finally
        {
            ServerWaitOverlay.Release(t_owner);
            this.loggingOut = false;
        }

        if (!t_success)
        {
            if (this != null) Show();
            NetworkFailurePopup.Show("로그아웃을 완료하지 못했습니다.");
        }
    }
}
