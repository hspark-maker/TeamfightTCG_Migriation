using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 앨범 완성 보상 상자 한 개(3계층 공용) — 상태별 표시와 수령 클릭만 담당, 지급 판정은 콜백 몫
[System.Serializable]
public class AlbumChestView
{
    [SerializeField] Button button;
    [SerializeField] GameObject claimedMark;
    [SerializeField] float pulseScale = 1.08f;
    [SerializeField] float pulseDuration = 0.5f;

    [Header("상자 그림 (선택 — 미배선이면 프리팹 그림 그대로)")]
    [Tooltip("상자 그림을 그리는 Image. 아래 두 스프라이트와 함께 배선해야 열림/닫힘이 바뀐다.")]
    [SerializeField] Image  chestImage;
    [Tooltip("아직 못 받았거나 잠긴 상태의 닫힌 상자.")]
    [SerializeField] Sprite closedSprite;
    [Tooltip("수령을 마친 열린 상자.")]
    [SerializeField] Sprite openedSprite;

    [Header("받을 수 있을 때 도는 빛 (선택)")]
    [Tooltip("상자 뒤에서 도는 빛. 받을 수 있을 때만 켜진다.")]
    [SerializeField] RectTransform glow;
    [Tooltip("빛이 한 바퀴 도는 시간(초).")]
    [SerializeField] float glowSpinSeconds = 6f;

    Tween m_pulse;
    Tween m_spin;

    // 코인 연출 출발점
    public RectTransform Rect => button != null ? button.transform as RectTransform : null;

    public void Bind(in AlbumRewardInfo _info, Action _onClaim)
    {
        if (button == null) return;

        // SetLink는 파괴에만 반응해 상태 전환을 못 쫓는다 — 재Bind마다 죽이고 스케일 원복
        m_pulse?.Kill();
        m_pulse = null;
        button.transform.localScale = Vector3.one;

        m_spin?.Kill();
        m_spin = null;

        // 보상 미저작이면 상자 자체를 걷는다(default 스냅샷의 Rewards null 포함). 재Bind가 복원 경로
        if (_info.Rewards == null || _info.Rewards.Count == 0)
        {
            button.gameObject.SetActive(false);
            if (glow != null) glow.gameObject.SetActive(false);   // 상자가 없으면 뒤 빛도 남으면 안 된다
            return;
        }
        button.gameObject.SetActive(true);

        bool t_claimable = _info.State == EAlbumRewardState.Claimable;
        bool t_claimed   = _info.State == EAlbumRewardState.Claimed;

        // 보상 아이콘은 꽂지 않는다 — 그러면 상자가 코인/보석으로 바뀐다. 바꾸는 건 열림/닫힘 두 장뿐이고,
        // 둘 다 배선됐을 때만 손댄다(한쪽만 있으면 되돌아올 그림이 없어 상자가 그 상태로 눌러붙는다).
        if (chestImage != null && closedSprite != null && openedSprite != null)
            chestImage.sprite = t_claimed ? openedSprite : closedSprite;

        if (claimedMark != null) claimedMark.SetActive(t_claimed);

        ShowGlow(t_claimable);

        button.interactable = t_claimable;
        button.onClick.RemoveAllListeners();
        if (_onClaim != null) button.onClick.AddListener(() => _onClaim());

        if (t_claimable)
            m_pulse = button.transform.DOScale(pulseScale, pulseDuration)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
                .SetLink(button.gameObject, LinkBehaviour.KillOnDisable);   // 탭 전환 중 백그라운드 트윈 방지
    }

    /// <summary>받을 수 있을 때만 뒤 빛을 켜고 돌린다. 각도는 켤 때 0으로 되돌린다 —
    /// 지난 상태에서 멈춘 각도로 다시 시작하면 상자마다 빛이 제각각 기운 채 선다.</summary>
    void ShowGlow(bool _on)
    {
        if (glow == null) return;

        glow.gameObject.SetActive(_on);
        if (!_on) return;

        glow.localRotation = Quaternion.identity;

        if (glowSpinSeconds <= 0f) return;

        m_spin = glow.DOLocalRotate(new Vector3(0f, 0f, -360f), glowSpinSeconds, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Restart)
            .SetLink(glow.gameObject, LinkBehaviour.KillOnDisable);
    }
}
