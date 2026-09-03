using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 프로필 버튼이 처음 여는 판. 프로필 요약을 보여 주고 갈래를 고르게 한다 —
/// 편집(<see cref="ProfileEditPanel"/>)·설정(<see cref="SettingsPanel"/>)으로 나가는 길이 여기 모인다.
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

    [Tooltip("설정 화면으로 가는 버튼.")]
    [SerializeField] Button settingButton;

    [Tooltip("닫기 버튼.")]
    [SerializeField] Button closeButton;

    [Tooltip("바깥 암막 클릭 판정. 닫기와 같은 동작이다.")]
    [SerializeField] Button dimButton;

    // 풀 계약. 표시값을 밖에서 받지 않으므로 할 일이 없다.
    public override void Initialization(UIData _data) { }

    public override void Show()
    {
        this.isShow = true;
        gameObject.SetActive(true);
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
        if (this.settingButton != null) this.settingButton.onClick.AddListener(OpenSettings);
        if (this.closeButton   != null) this.closeButton.onClick.AddListener(Hide);
        if (this.dimButton     != null) this.dimButton.onClick.AddListener(Hide);
    }

    protected override void OnDestroy()
    {
        if (this.editButton    != null) this.editButton.onClick.RemoveListener(OpenProfileEdit);
        if (this.settingButton != null) this.settingButton.onClick.RemoveListener(OpenSettings);
        if (this.closeButton   != null) this.closeButton.onClick.RemoveListener(Hide);
        if (this.dimButton     != null) this.dimButton.onClick.RemoveListener(Hide);

        base.OnDestroy();
    }

    // 이 판은 닫는다 — 편집 팝업이 그 위에 겹쳐 뜨면 뒤로가기의 목적지가 둘이 된다.
    void OpenProfileEdit()
    {
        Hide();
        UIPoolManager.Instance?.AddOrUpdateUI<ProfileEditPanel>();
    }

    void OpenSettings()
    {
        Hide();
        UIPoolManager.Instance?.AddOrUpdateUI<SettingsPanel>();
    }
}
