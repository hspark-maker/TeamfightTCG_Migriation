using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 앨범 갤러리의 테마 셀 하나(Cell_00 부착). 수령 후 상태 갱신은 OnChanged가 부모 Refresh로 처리
public class AlbumThemeCellView : MonoBehaviour
{
    [SerializeField] Button thumbButton;
    [SerializeField] Image thumbIcon;
    [SerializeField] TMP_Text nameLabel;
    [SerializeField] GameObject progressRow;
    [SerializeField] AlbumGaugeView gauge = new AlbumGaugeView();
    [SerializeField] AlbumChestView chest = new AlbumChestView();
    [SerializeField] GameObject doneRow;

    AlbumTheme m_theme;

    public void Bind(AlbumTheme _theme, Action<AlbumTheme> _onOpen)
    {
        m_theme = _theme;

        if (thumbIcon != null && _theme.Icon != null) thumbIcon.sprite = _theme.Icon;
        if (nameLabel != null) nameLabel.text = _theme.DisplayName;

        var t_info = AlbumRewardManager.GetThemeInfo(_theme);
        gauge.Set(t_info.Owned, t_info.Total);

        // Claimable은 progressRow 유지 — 상자 펄스가 수령을 유도한다
        bool t_done = t_info.State == EAlbumRewardState.Claimed;
        if (progressRow != null) progressRow.SetActive(!t_done);
        if (doneRow != null) doneRow.SetActive(t_done);

        chest.Bind(t_info, ClaimReward);

        if (thumbButton != null)
        {
            thumbButton.onClick.RemoveAllListeners();
            thumbButton.onClick.AddListener(() => _onOpen?.Invoke(m_theme));
        }
    }

    void Awake()
    {
        // 런타임 RemoveAllListeners는 퍼시스턴트를 못 지운다 — 목업 onClick은 배선 단계에서 지워야 한다
        if (thumbButton != null && thumbButton.onClick.GetPersistentEventCount() > 0)
            Debug.LogWarning("[AlbumThemeCellView] 목업 퍼시스턴트 onClick이 남아 있다 — 프리팹에서 제거할 것.", this);
    }

    void ClaimReward()
    {
        if (m_theme == null) return;

        var t_rewards = m_theme.Rewards;   // Claim 전에 캡처
        if (!AlbumRewardManager.ClaimTheme(m_theme)) return;

        if (!CurrencyGainEffectPlayer.TryGet(this, out var t_player)) return;

        var t_bucket = new CurrencyGainBucket();
        for (int t_i = 0; t_i < t_rewards.Count; t_i++)
            t_bucket.Add(t_rewards[t_i].currency, t_rewards[t_i].amount);
        t_player.Play(chest.Rect, t_bucket);
    }
}
