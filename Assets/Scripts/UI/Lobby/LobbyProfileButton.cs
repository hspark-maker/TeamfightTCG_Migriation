using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 로비의 프로필 버튼(아바타·프레임·닉네임 표시 + 프로필 요약 판 열기).
///
/// ProfileManager.OnChanged를 구독하는 이유: 이 판들은 풀의 uiRoot에서 로비 위를 덮으므로
/// 팝업이 닫혀도 이 버튼이 속한 로비 탭의 OnEnable이 오지 않는다 — 통지가 유일한 갱신 신호다.
///
/// 버튼 onClick을 인스펙터로 배선하지 않는 이유는 LobbySettingsButton과 같다: 팝업은 씬 오브젝트가
/// 아니라 풀에서 꺼내 띄우는 것이라 인스펙터에서 가리킬 대상이 없다.
public class LobbyProfileButton : MonoBehaviour
{
    [SerializeField] Button button;
    [Tooltip("판·얼굴·링 한 덩어리.")]
    [SerializeField] ProfileAvatarView avatarView;
    [Tooltip("옵션 — 미배선이면 그림만 갱신한다.")]
    [SerializeField] TMP_Text nicknameText;

    void Awake()
    {
        if (this.button == null) this.button = GetComponent<Button>();
        if (this.button != null) this.button.onClick.AddListener(this.Open);
    }

    void OnDestroy()
    {
        if (this.button != null) this.button.onClick.RemoveListener(this.Open);
    }

    void OnEnable()
    {
        ProfileManager.OnChanged += this.Refresh;
        this.Refresh();
    }

    void OnDisable()
    {
        ProfileManager.OnChanged -= this.Refresh;
    }

    void Refresh()
    {
        if (this.avatarView != null) this.avatarView.Render(ProfileManager.CurrentLook);

        if (this.nicknameText != null) this.nicknameText.text = ProfileManager.Nickname;
    }

    /// 프로필 요약 판을 먼저 연다 — 편집으로 가는 길은 그 판의 버튼이 쥔다(LobbySettingPanel).
    /// 이미 떠 있으면 UIPoolManager가 맨 앞으로 올리고 Show를 다시 태운다(중복 생성 없음).
    void Open() => UIPoolManager.Instance?.AddOrUpdateUI<LobbySettingPanel>();
}
