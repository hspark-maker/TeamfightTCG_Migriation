using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 랭크 보상 한 행(RankRewardRow 프리팹 루트에 부착).
// 티어 인덱스만 들고 표시값은 매번 RankRewardManager.GetInfo로 다시 받는다(행이 스냅샷을 캐싱하면 수령 후 stale).
public class RankRewardRowView : MonoBehaviour
{
    [SerializeField] Image badgeImage;       // 티어 배지(미저작이면 프리팹 기본 유지)
    [SerializeField] TMP_Text tierNameText;  // 티어 표시명
    [SerializeField] Button rewardBox;       // 보상 박스 = 수령 요청 버튼

    [Tooltip("보상 칸(아이콘 + 수량). 저작한 보상이 칸 수보다 적으면 남는 칸은 꺼진다.")]
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;

    [Header("상태 노드(선택 — 미배선 시 null 가드)")]
    [SerializeField] GameObject highlight;   // 수령 가능한 행 중 최상위 1개만(밀려 쌓인 행은 버튼만 활성)
    [SerializeField] GameObject claimedMark; // 수령 완료(체크)
    [SerializeField] GameObject lockDim;     // 미달성(자물쇠)
    [SerializeField] GameObject chevron;     // 행 사이 장식(마지막 행만 비활성)
    [SerializeField] CanvasGroup canvasGroup;

    [Tooltip("수령 완료 행의 알파. 미달성은 lockDim 노드가 따로 덮으므로 여기서 겹쳐 딤하지 않는다.")]
    [SerializeField] float claimedAlpha = 0.6f;

    [Header("최상위 수령 가능 연출")]
    [Tooltip("최상위 행의 링이 상시 깜빡이는 최저 알파.")]
    [SerializeField] float readyPulseMinAlpha = 0.45f;
    [SerializeField] float readyPulseDuration = 0.8f;

    static bool s_overflowWarned;

    // 표시 대상 티어. -1 = 미바인딩(Refresh 무시).
    int m_tierIndex = -1;

    Action<int> m_onClick;

    // 최상위 행의 상시 펄스.
    Tween m_pulseTween;

    // 깜빡임 대상. highlight 노드에서 1회만 찾아 캐싱한다.
    Image m_highlightImage;

    // 티어 인덱스 배선 + 리스너 1회 등록(재빌드마다 중복 방지). _isLast면 쉐브론을 끈다.
    public void Bind(int _tierIndex, bool _isLast, Action<int> _onClick)
    {
        this.m_tierIndex = _tierIndex;
        this.m_onClick = _onClick;

        if (this.rewardBox != null)
        {
            this.rewardBox.onClick.RemoveAllListeners();
            this.rewardBox.onClick.AddListener(this.OnRewardBoxClicked);
        }

        if (this.chevron != null) this.chevron.SetActive(!_isLast);

        this.Refresh();
    }

    // 상태 표시 갱신. 수령 통지(OnChanged)마다 컨트롤러가 전 행에 호출한다.
    public void Refresh()
    {
        if (this.m_tierIndex < 0) return;

        var t_info = RankRewardManager.GetInfo(this.m_tierIndex);

        // 배지 미저작(null)이면 프리팹에 배선된 기존 스프라이트를 그대로 둔다.
        if (this.badgeImage != null && t_info.Badge != null) this.badgeImage.sprite = t_info.Badge;
        if (this.tierNameText != null) this.tierNameText.text = t_info.DisplayName;
        this.BindRewardSlots(t_info.Rewards);

        bool t_claimable = t_info.State == ERankRewardState.Claimable;
        bool t_claimed = t_info.State == ERankRewardState.Claimed;

        if (this.rewardBox != null) this.rewardBox.interactable = t_claimable;
        if (this.claimedMark != null) this.claimedMark.SetActive(t_claimed);
        if (this.lockDim != null) this.lockDim.SetActive(t_info.State == ERankRewardState.Locked);
        if (this.canvasGroup != null) this.canvasGroup.alpha = t_claimed ? this.claimedAlpha : 1f;

        if (this.highlight != null) this.highlight.SetActive(t_info.IsTopClaimable);

        this.UpdateReadyPulse(t_info.IsTopClaimable);
    }

    // 패널을 닫거나 행이 꺼질 때 잔여 트윈이 링 알파를 중간값에 남기지 않게 정리한다.
    void OnDisable()
    {
        this.KillReadyPulse();
        this.RestoreRowVisual();
        this.Refresh();
    }

    void BindRewardSlots(IReadOnlyList<RankReward> _rewards)
    {
        if (this.rewardSlots == null) return;

        for (int t_i = 0; t_i < this.rewardSlots.Length; t_i++)
        {
            if (this.rewardSlots[t_i] == null) continue;

            if (t_i < _rewards.Count) this.rewardSlots[t_i].Bind(_rewards[t_i].Icon, _rewards[t_i].Gain.Amount);
            else this.rewardSlots[t_i].Hide();
        }

        // 저작 문제라 행마다 찍으면 소음이다 — 세션에 한 번이면 족하다.
        if (_rewards.Count > this.rewardSlots.Length && !s_overflowWarned)
        {
            s_overflowWarned = true;
            Debug.LogWarning($"[RankRewardRowView] 티어 보상 {_rewards.Count}건이 슬롯 {this.rewardSlots.Length}칸을 초과 — 앞칸만 표시한다.", this);
        }
    }

    // 수령 요청은 패널로 올린다(행은 팝업·지급을 모른다).
    void OnRewardBoxClicked()
    {
        if (this.m_tierIndex < 0) return;
        this.m_onClick?.Invoke(this.m_tierIndex);
    }

    // 최상위 수령 가능 행만 상시로 깜빡인다.
    void UpdateReadyPulse(bool _isTopClaimable)
    {
        // 꺼지는 중(OnDisable→Refresh)에는 켜지 않는다 — 보이지도 않는 트윈이 남는다.
        if (!_isTopClaimable || !this.isActiveAndEnabled)
        {
            this.KillReadyPulse();
            return;
        }

        if (this.IsReadyPulsePlaying) return;   // 재시작하면 OnChanged마다 알파가 처음으로 튄다.

        var t_image = this.HighlightImage;
        if (t_image == null) return;

        this.m_pulseTween = t_image.DOFade(this.readyPulseMinAlpha, this.readyPulseDuration)
                                   .SetLoops(-1, LoopType.Yoyo)
                                   .SetLink(this.gameObject);
    }

    bool IsReadyPulsePlaying => this.m_pulseTween != null && this.m_pulseTween.IsActive();

    Image HighlightImage
    {
        get
        {
            if (this.m_highlightImage == null && this.highlight != null)
                this.m_highlightImage = this.highlight.GetComponent<Image>();
            return this.m_highlightImage;
        }
    }

    void KillReadyPulse()
    {
        if (this.m_pulseTween == null) return;
        this.m_pulseTween.Kill();
        this.m_pulseTween = null;
        this.RestoreRowVisual();   // 중간 알파에 굳은 링이 다음 상태로 넘어가지 않게.
    }

    void RestoreRowVisual()
    {
        var t_image = this.HighlightImage;
        if (t_image == null) return;

        var t_color = t_image.color;
        t_color.a = 1f;
        t_image.color = t_color;
    }
}
