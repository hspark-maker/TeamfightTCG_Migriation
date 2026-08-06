using System;
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
    [SerializeField] TMP_Text amountText;    // 보상 금액("x100")
    [SerializeField] Button rewardBox;       // 보상 박스 = 수령 요청 버튼

    [Header("상태 노드(선택 — 미배선 시 null 가드)")]
    [SerializeField] GameObject highlight;   // 수령 가능한 행 중 최상위 1개만(밀려 쌓인 행은 버튼만 활성)
    [SerializeField] GameObject claimedMark; // 수령 완료(체크)
    [SerializeField] GameObject lockDim;     // 미달성(자물쇠)
    [SerializeField] GameObject chevron;     // 행 사이 장식(마지막 행만 비활성)
    [SerializeField] CanvasGroup canvasGroup;

    [Tooltip("수령 완료 행의 알파. 미달성은 lockDim 노드가 따로 덮으므로 여기서 겹쳐 딤하지 않는다.")]
    [SerializeField] float claimedAlpha = 0.6f;

    [Header("티어 상승 연출")]
    [Tooltip("행 루트 스케일 펀치 세기(1 + 이 값). 레이아웃은 sizeDelta 기반이라 스케일은 행 간격에 영향을 주지 않는다.")]
    [SerializeField] float tierUpPunch = 0.08f;
    [SerializeField] float tierUpDuration = 1.2f;
    [Tooltip("깜빡임이 내려가는 최저 알파.")]
    [SerializeField] float tierUpMinAlpha = 0.2f;

    [Header("최상위 수령 가능 연출")]
    [Tooltip("최상위 행의 링이 상시 깜빡이는 최저 알파.")]
    [SerializeField] float readyPulseMinAlpha = 0.45f;
    [SerializeField] float readyPulseDuration = 0.8f;

    // 표시 대상 티어. -1 = 미바인딩(Refresh 무시).
    int m_tierIndex = -1;

    Action<int> m_onClick;

    // 진행 중 티어 상승 연출. 살아있는 동안 Refresh가 highlight 토글을 건너뛴다.
    Sequence m_tierUpSeq;

    // 최상위 행의 상시 펄스. 티어 상승 연출과 같은 알파를 쓰므로 둘이 동시에 돌지 않게 한다.
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
        if (this.amountText != null) this.amountText.text = $"x{t_info.Reward.Amount:N0}";

        bool t_claimable = t_info.State == ERankRewardState.Claimable;
        bool t_claimed = t_info.State == ERankRewardState.Claimed;

        if (this.rewardBox != null) this.rewardBox.interactable = t_claimable;
        if (this.claimedMark != null) this.claimedMark.SetActive(t_claimed);
        if (this.lockDim != null) this.lockDim.SetActive(t_info.State == ERankRewardState.Locked);
        if (this.canvasGroup != null) this.canvasGroup.alpha = t_claimed ? this.claimedAlpha : 1f;

        // 연출 중에는 링을 끄지 않는다 — 도중에 OnChanged가 오면(수령 등) 깜빡이던 링이 사라진다.
        if (this.highlight != null && !this.IsTierUpPlaying) this.highlight.SetActive(t_info.IsTopClaimable);

        this.UpdateReadyPulse(t_info.IsTopClaimable);
    }

    /// <summary>이번 전투로 새로 도달한 행임을 알리는 연출. 끝나면 Refresh로 원래 표시 규칙에 되돌린다.</summary>
    public void PlayTierUpEffect()
    {
        if (this.highlight == null) return;

        this.KillTierUpEffect();
        this.KillReadyPulse();   // 같은 링 알파를 쓴다 — 겹쳐 돌면 깜빡임이 서로를 덮는다.

        // 최상위 수령 대상이 아니어도 이 행만은 보이게 켠다 — "도달했다"를 알리는 게 목적이다.
        this.highlight.SetActive(true);

        this.m_tierUpSeq = DOTween.Sequence().SetLink(this.gameObject);
        this.m_tierUpSeq.Append(this.transform.DOPunchScale(Vector3.one * this.tierUpPunch,
                                                            this.tierUpDuration * 0.4f, 1, 0.6f));

        // 4루프 Yoyo = 깜빡임 2회. 펀치와 겹쳐 재생하고 총 길이는 tierUpDuration에 맞춘다.
        var t_image = this.HighlightImage;
        if (t_image != null)
            this.m_tierUpSeq.Join(t_image.DOFade(this.tierUpMinAlpha, this.tierUpDuration * 0.25f)
                                         .SetLoops(4, LoopType.Yoyo));

        this.m_tierUpSeq.OnComplete(() =>
        {
            this.m_tierUpSeq = null;   // Refresh의 연출 가드를 먼저 풀어야 링이 정상 규칙으로 돌아간다.
            this.RestoreRowVisual();
            this.Refresh();            // 최상위 행이면 여기서 상시 펄스로 이어진다.
        });
    }

    // 패널을 닫거나 행이 꺼질 때 잔여 트윈이 스케일·알파를 중간값에 남기지 않게 정리한다.
    // Kill로 연출 가드를 먼저 푼 뒤 Refresh해야, 연출이 강제로 켰던 링이 원래 규칙(최상위 여부)으로 되돌아간다.
    void OnDisable()
    {
        this.KillTierUpEffect();
        this.KillReadyPulse();
        this.RestoreRowVisual();
        this.Refresh();
    }

    // 수령 요청은 패널로 올린다(행은 팝업·지급을 모른다).
    void OnRewardBoxClicked()
    {
        if (this.m_tierIndex < 0) return;
        this.m_onClick?.Invoke(this.m_tierIndex);
    }

    // 최상위 수령 가능 행만 상시로 깜빡인다. 티어 상승 연출이 같은 알파를 쓰는 동안엔 손대지 않는다(끝나면 Refresh가 다시 부른다).
    void UpdateReadyPulse(bool _isTopClaimable)
    {
        if (this.IsTierUpPlaying) return;

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

    bool IsTierUpPlaying => this.m_tierUpSeq != null && this.m_tierUpSeq.IsActive();

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

    void KillTierUpEffect()
    {
        if (this.m_tierUpSeq == null) return;
        this.m_tierUpSeq.Kill();
        this.m_tierUpSeq = null;
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
        this.transform.localScale = Vector3.one;

        var t_image = this.HighlightImage;
        if (t_image == null) return;

        var t_color = t_image.color;
        t_color.a = 1f;
        t_image.color = t_color;
    }
}
