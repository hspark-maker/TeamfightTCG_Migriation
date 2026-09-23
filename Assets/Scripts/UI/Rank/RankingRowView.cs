using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 서버 순위·프로필을 저작된 행에 표시한다.
public class RankingRowView : MonoBehaviour, IUIInitializable
{
    [SerializeField] TMP_Text rankText;
    [SerializeField] TMP_Text nicknameText;
    [SerializeField] TMP_Text tierNameText;
    [SerializeField] TMP_Text pointsText;
    [SerializeField] Image badgeImage;
    [SerializeField] Image avatarImage;
    [SerializeField] Image frameImage;
    [SerializeField] Image crownImage;
    [SerializeField] Sprite[] crowns;
    [SerializeField] Image backgroundImage;
    [SerializeField] Color selfColor = new Color(0.8825388f, 1f, 0.8066038f, 1f);
    Sprite m_defaultAvatar;
    Sprite m_defaultFrame;
    Color m_defaultFrameColor;
    Color m_defaultBackgroundColor;
    bool m_initialized;

    void Awake() => InitializeUI();

    public void InitializeUI()
    {
        if (m_initialized) return;
        m_initialized = true;
        if (avatarImage != null) m_defaultAvatar = avatarImage.sprite;
        if (frameImage != null)
        {
            m_defaultFrame = frameImage.sprite;
            m_defaultFrameColor = frameImage.color;
        }
        if (backgroundImage != null) m_defaultBackgroundColor = backgroundImage.color;
    }

    internal void Bind(RankLeaderboardEntry _entry)
    {
        var t_info = RankManager.GetInfoAt(_entry.Points);
        Bind(_entry.Rank, _entry.Nickname, t_info.DisplayName, t_info.Badge, _entry.Points, _entry.IsSelf);
        if (avatarImage != null)
        {
            var t_config = ProfileManager.Config;
            string t_id = string.IsNullOrEmpty(_entry.AvatarId) && t_config != null
                ? t_config.DefaultAvatarId : _entry.AvatarId;
            avatarImage.sprite = t_config != null && t_config.TryGetAvatar(t_id, out var t_avatar)
                ? t_avatar.SmallOrLarge : m_defaultAvatar;
        }
        if (frameImage != null)
        {
            var t_config = ProfileManager.Config;
            string t_id = string.IsNullOrEmpty(_entry.FrameId) && t_config != null
                ? t_config.DefaultFrameId : _entry.FrameId;
            if (t_config != null && t_config.TryGetFrame(t_id, out var t_frame))
            {
                frameImage.sprite = t_frame.sprite;
                frameImage.color = t_frame.color;
            }
            frameImage.enabled = frameImage.sprite != null;
        }
        if (crownImage != null && crowns != null)
        {
            bool t_show = _entry.Rank >= 1 && _entry.Rank <= crowns.Length;
            crownImage.gameObject.SetActive(t_show);
            if (t_show) crownImage.sprite = crowns[_entry.Rank - 1];
        }
    }

    public void Bind(int _rank, string _nickname, string _tierName, Sprite _badge, long _points, bool _isSelf)
    {
        InitializeUI();
        if (frameImage != null)
        {
            frameImage.sprite = m_defaultFrame;
            frameImage.color = m_defaultFrameColor;
            frameImage.enabled = m_defaultFrame != null;
        }
        if (backgroundImage != null) backgroundImage.color = _isSelf ? selfColor : m_defaultBackgroundColor;
        if (rankText != null) rankText.text = _rank > 0 ? _rank.ToString() : "-";
        if (nicknameText != null)
        {
            nicknameText.richText = false;
            nicknameText.text = _isSelf ? $"{_nickname} (나)" : _nickname;
        }
        if (tierNameText != null) tierNameText.text = _tierName;
        if (pointsText != null) pointsText.text = $"{_points:N0} P";
        if (badgeImage != null)
        {
            badgeImage.sprite = _badge;
            badgeImage.enabled = _badge != null;
        }
    }
}
