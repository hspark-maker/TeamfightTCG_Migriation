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

    Tween m_pulse;

    // 코인 연출 출발점
    public RectTransform Rect => button != null ? button.transform as RectTransform : null;

    public void Bind(in AlbumRewardInfo _info, Action _onClaim)
    {
        if (button == null) return;

        // SetLink는 파괴에만 반응해 상태 전환을 못 쫓는다 — 재Bind마다 죽이고 스케일 원복
        m_pulse?.Kill();
        m_pulse = null;
        button.transform.localScale = Vector3.one;

        // 보상 미저작이면 상자 자체를 걷는다(default 스냅샷의 Rewards null 포함). 재Bind가 복원 경로
        if (_info.Rewards == null || _info.Rewards.Count == 0)
        {
            button.gameObject.SetActive(false);
            return;
        }
        button.gameObject.SetActive(true);

        bool t_claimable = _info.State == EAlbumRewardState.Claimable;

        // 상자 그림은 프리팹 저작 그대로 둔다 — 보상 아이콘을 꽂으면 상자가 코인/보석으로 바뀐다

        if (claimedMark != null) claimedMark.SetActive(_info.State == EAlbumRewardState.Claimed);

        button.interactable = t_claimable;
        button.onClick.RemoveAllListeners();
        if (_onClaim != null) button.onClick.AddListener(() => _onClaim());

        if (t_claimable)
            m_pulse = button.transform.DOScale(pulseScale, pulseDuration)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
                .SetLink(button.gameObject, LinkBehaviour.KillOnDisable);   // 탭 전환 중 백그라운드 트윈 방지
    }
}
