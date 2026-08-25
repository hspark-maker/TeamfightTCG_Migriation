using System;
using System.Collections.Generic;
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
    [SerializeField] TMP_Text descriptionLabel;
    [Tooltip("갤러리에서의 차례(1부터). 저작 순서를 그대로 읽는다.")]
    [SerializeField] TMP_Text orderLabel;
    [SerializeField] GameObject progressRow;
    [SerializeField] AlbumGaugeView gauge = new AlbumGaugeView();
    [SerializeField] AlbumChestView chest = new AlbumChestView();
    [SerializeField] GameObject doneRow;

    [Tooltip("잠긴 테마 셀의 밝기. 흑백만으로도 잠김이 읽히므로 너무 내리지 말 것(0이면 셀이 사라진다).")]
    [SerializeField] float lockedAlpha = 0.6f;

    AlbumTheme m_theme;
    bool       m_anchored;   // 안내 타깃으로 등록된 상태. 남의 등록을 날리지 않으려고 자기 것만 해제한다

    GameObject                m_lockBadge;
    bool                      m_lockBadgeMissing;   // 카탈로그 미배선 경고·재시도는 1회로 끝낸다
    CanvasGroup               m_group;
    List<UiGrayscale.Toned>   m_toned;

    public void Bind(AlbumTheme _theme, int _order, Action<AlbumTheme> _onOpen, bool _tutorialTarget = false)
    {
        m_theme = _theme;

        ApplyLocked(_theme.IsLocked);

        // 차례와 소개는 잠긴 테마에도 그대로 보인다 — 잠김은 흑백·자물쇠가 말한다
        if (orderLabel != null) orderLabel.text = _order > 0 ? _order.ToString() : string.Empty;
        if (descriptionLabel != null) descriptionLabel.text = _theme.Description;

        if (_theme.IsLocked)
        {
            if (nameLabel != null) nameLabel.text = _theme.DisplayName;
            if (progressRow != null) progressRow.SetActive(false);
            if (doneRow != null) doneRow.SetActive(false);
            chest.Bind(default(AlbumRewardInfo), null);
            ApplyTutorialAnchor(false);
            return;
        }

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

    // 준비 중 테마의 잠김 룩 — 셀 전체 탈채도 + 썸네일 위 자물쇠(FeatureLockView·TournamentNodeView와 같은 관용구).
    // 차단(interactable)도 여기서 세운다 — 이 셀에는 잠김을 세우는 다른 계산식이 없어 서로 덮어쓸 일이 없다.
    void ApplyLocked(bool _locked)
    {
        // 다시 칠하기 전에 항상 저작값으로 되돌린다 — 셀은 재사용되고 Bind가 반복 호출된다.
        UiGrayscale.Restore(this.m_toned);
        this.m_toned = null;

        if (thumbButton != null) thumbButton.interactable = !_locked;

        if (!_locked)
        {
            if (this.m_lockBadge != null) this.m_lockBadge.SetActive(false);
            if (this.m_group != null) this.m_group.alpha = 1f;
            return;
        }

        EnsureLockBadge();

        if (this.m_lockBadge != null)
        {
            this.m_lockBadge.SetActive(true);
            this.m_lockBadge.transform.SetAsLastSibling();
        }

        if (this.m_group == null) this.m_group = gameObject.GetComponent<CanvasGroup>();
        if (this.m_group == null) this.m_group = gameObject.AddComponent<CanvasGroup>();
        this.m_group.alpha = this.lockedAlpha;

        // 자물쇠는 탈채도에서 뺀다 — 잠김을 말하는 표식이 저 혼자 회색이면 읽히지 않는다.
        this.m_toned = UiGrayscale.Apply(gameObject, this.m_lockBadge != null ? this.m_lockBadge.transform : null);
    }

    // 자물쇠는 셀 프리팹에 없다 — 동기 UI 카탈로그의 공용 배지를 썸네일 위에 1회 꽂아 재사용한다.
    void EnsureLockBadge()
    {
        if (this.m_lockBadge != null || this.m_lockBadgeMissing) return;

        var t_parent = thumbButton != null ? thumbButton.transform as RectTransform : transform as RectTransform;
        if (t_parent == null)
        {
            this.m_lockBadgeMissing = true;
            return;
        }

        var t_prefab = SyncUiPrefabs.Get(ESyncUiPrefab.LockBadge);
        if (t_prefab == null)
        {
            this.m_lockBadgeMissing = true;
            Debug.LogWarning($"[AlbumThemeCellView] 동기 UI 카탈로그 자물쇠 미배선 — 잠긴 테마가 흑백으로만 보입니다.", this);
            return;
        }

        this.m_lockBadge      = Instantiate(t_prefab, t_parent, false);
        this.m_lockBadge.name = "LockBadge";
    }

    void ClaimReward()
    {
        if (m_theme == null) return;

        // 팝업을 띄우기 전에 막는다 — 지급은 [획득]에서 일어난다.
        if (!AlbumRewardManager.CanClaimTheme(m_theme)) return;

        AlbumRewardClaimFlow.Open($"{m_theme.DisplayName} 완성!",
                                  m_theme.Rewards,
                                  () => AlbumRewardManager.ClaimTheme(m_theme));
    }
}
