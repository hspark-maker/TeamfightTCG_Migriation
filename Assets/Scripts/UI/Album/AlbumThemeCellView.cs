using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 앨범 갤러리의 테마 셀 하나(Cell_00 부착). 수령 후 상태 갱신은 OnChanged가 부모 Refresh로 처리
public class AlbumThemeCellView : MonoBehaviour
{
    [SerializeField] Button thumbButton;
    [SerializeField] Image thumbIcon;
    [SerializeField] Image thumbFrame;
    [SerializeField] Image namePlate;
    [SerializeField] TMP_Text nameLabel;
    [SerializeField] GameObject progressRow;
    [SerializeField] AlbumGaugeView gauge = new AlbumGaugeView();
    [SerializeField] AlbumChestView chest = new AlbumChestView();
    [SerializeField] GameObject doneRow;

    AlbumTheme m_theme;
    bool       m_anchored;   // 안내 타깃으로 등록된 상태. 남의 등록을 날리지 않으려고 자기 것만 해제한다

    public void Bind(AlbumTheme _theme, Action<AlbumTheme> _onOpen, bool _tutorialTarget = false)
    {
        m_theme = _theme;

        // 셀은 Cell_00 클론이라 저작이 없으면 9칸이 전부 같은 스킨이 된다 — null은 목업 보존이 아니라 "템플릿 색 그대로"
        if (thumbIcon != null && _theme.Icon != null) thumbIcon.sprite = _theme.Icon;
        if (thumbFrame != null && _theme.Frame != null) thumbFrame.sprite = _theme.Frame;
        if (namePlate != null && _theme.NamePlate != null) namePlate.sprite = _theme.NamePlate;
        if (nameLabel != null) nameLabel.text = _theme.DisplayName;

        var t_info = AlbumRewardManager.GetThemeInfo(_theme);

        // 삽입 연출 중 아직 안 꽂은 카드는 표시에서만 뺀다 — 총 게이지·페이지 게이지와 숫자가 갈리지 않게
        int t_hidden = AlbumInsertMask.HiddenCountIn(_theme);
        gauge.Set(t_info.Owned - t_hidden, t_info.Total);

        // Claimable은 progressRow 유지 — 상자 펄스가 수령을 유도한다
        bool t_done = t_hidden == 0 && t_info.State == EAlbumRewardState.Claimed;
        if (progressRow != null) progressRow.SetActive(!t_done);
        if (doneRow != null) doneRow.SetActive(t_done);

        // 마지막 칸을 꽂는 순간 상자가 나타나는 게 보상 신호다 — 그 전엔 감춘다
        if (t_hidden > 0)
        {
            var t_empty = default(AlbumRewardInfo);
            chest.Bind(t_empty, null);
        }
        else chest.Bind(t_info, ClaimReward);

        if (thumbButton != null)
        {
            thumbButton.onClick.RemoveAllListeners();
            thumbButton.onClick.AddListener(() => _onOpen?.Invoke(m_theme));
        }

        ApplyTutorialAnchor(_tutorialTarget);
    }

    void Awake()
    {
        // 런타임 RemoveAllListeners는 퍼시스턴트를 못 지운다 — 목업 onClick은 배선 단계에서 지워야 한다
        if (thumbButton != null && thumbButton.onClick.GetPersistentEventCount() > 0)
            Debug.LogWarning("[AlbumThemeCellView] 목업 퍼시스턴트 onClick이 남아 있다 — 프리팹에서 제거할 것.", this);
    }

    // 셀은 갤러리가 다시 그릴 때 꺼지거나 교체된다 — 죽은 칸을 가리키는 등록이 남지 않게 여기서 놓는다
    void OnDisable()
    {
        ApplyTutorialAnchor(false);
    }

    void ApplyTutorialAnchor(bool _on)
    {
        if (_on == m_anchored) return;
        m_anchored = _on;

        var t_rect = thumbButton != null ? thumbButton.transform as RectTransform : null;
        if (t_rect == null) return;

        if (_on) TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.AlbumThemeCell, t_rect, thumbButton);
        else     TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.AlbumThemeCell, t_rect);
    }

    // 상자 콜백은 동기 델리게이트라 대기를 여기서 끊는다(RewardClaimPopup의 버튼 핸들러와 같은 형태).
    void ClaimReward() => ClaimRewardAsync().Forget();

    async UniTaskVoid ClaimRewardAsync()
    {
        if (m_theme == null) return;

        // 팝업을 띄우기 전에 막는다 — 지급은 [획득]에서 일어난다.
        if (!AlbumRewardManager.CanClaimTheme(m_theme)) return;

        await AlbumRewardClaimFlow.Open($"{m_theme.DisplayName} 완성!",
                                        m_theme.Rewards,
                                        () => AlbumRewardManager.ClaimTheme(m_theme));
    }
}
