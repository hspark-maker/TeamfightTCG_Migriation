using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class TurnBannerUI : MonoBehaviour
{
    [SerializeField] RectTransform bannerRect;
    [SerializeField] float enterDuration = 0.45f;
    [SerializeField] float driftDuration = 0.55f;
    [SerializeField] float driftX        = 80f;
    [SerializeField] float exitDuration  = 0.22f;
    [SerializeField] float offscreenX    = 1200f;

    public async UniTask Play()
    {
        TurnState.InputAllowed = false;
        gameObject.SetActive(true);

        this.bannerRect.DOKill();
        this.bannerRect.anchoredPosition = new Vector2(this.offscreenX, this.bannerRect.anchoredPosition.y);

        // 오른쪽에서 중앙으로 — OutBack으로 살짝 오버슈트 후 안착
        await this.bannerRect.DOAnchorPosX(0f, this.enterDuration)
            .SetEase(Ease.OutBack).ToUniTask();

        // 가운데에서 아주 천천히 좌측으로 흘러가기
        await this.bannerRect.DOAnchorPosX(-this.driftX, this.driftDuration)
            .SetEase(Ease.Linear).ToUniTask();

        // 지수 가속으로 빠르게 화면 밖으로
        await this.bannerRect.DOAnchorPosX(-this.offscreenX, this.exitDuration)
            .SetEase(Ease.InExpo).ToUniTask();

        gameObject.SetActive(false);
    }
}
