using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>선공/후공 결정 연출. 코인이 축을 중심으로 빙글빙글 돌다 결과 면으로 '탁' 안착.
/// 앞면(front)=선공, 뒷면(back)=후공. **연출 전용** — 실제 선/후공 판정은 호출측(TurnRunner 등)이
/// 하고 결과(bool)를 <see cref="Play"/>에 넘긴다. 결정론/네트워크 무접촉(표시만).
/// TurnBannerUI 선례를 따른 코드 연출(스케일 팝 + 회전 + 스프라이트 스왑).</summary>
public class CoinFlipUI : MonoBehaviour
{
    [Header("연출 대상")]
    [SerializeField] Transform target;      // 회전·스케일 대상(보통 코인 이미지, 비우면 self)
    [SerializeField] Image     coinImage;   // 면 스프라이트 교체 대상

    [Header("면 스프라이트")]
    [SerializeField] Sprite frontSprite;    // 앞면 = 선공
    [SerializeField] Sprite backSprite;     // 뒷면 = 후공

    [Header("회전축")]
    [SerializeField] bool flipVertical = true;   // true=X축(위아래 뒤집기), false=Y축(좌우 회전)

    [Header("타이밍")]
    [SerializeField] int   halfTurns    = 6;      // 반바퀴(180°) 횟수 = 면 전환 횟수(많을수록 빨리 돎)
    [SerializeField] float spinDuration = 1.1f;   // 전체 회전 시간
    [SerializeField] float popScale     = 1.18f;  // 안착 '탁' 팝 배율
    [SerializeField] float popDuration  = 0.12f;

    Vector3 baseScale;
    bool    cached;

    void Awake()
    {
        if (this.target == null) this.target = this.transform;
        this.baseScale = this.target.localScale;
        this.cached = true;
    }

    /// <summary>코인 플립 연출 재생. <paramref name="_front"/>=true면 앞면(선공)에서 안착.
    /// 완료 시 회전은 0으로 리셋되고 결과 면 스프라이트가 남는다.</summary>
    public async UniTask Play(bool _front)
    {
        if (!this.cached) { this.baseScale = this.target.localScale; this.cached = true; }

        this.target.DOKill();
        this.target.localScale       = this.baseScale;
        this.target.localEulerAngles = Vector3.zero;

        // 반바퀴 패리티로 최종 면 결정: 짝수 반바퀴=앞면(0/360°), 홀수=뒷면(180°). 원하는 면에 맞게 보정.
        int t_half = Mathf.Max(2, this.halfTurns);
        if ((t_half % 2 == 0) != _front) t_half++;
        float t_endDeg = t_half * 180f;

        // 회전 중 현재 각도로 앞/뒷면 스왑(축을 정면에서 볼 때 = 앞면).
        Vector3 t_endEuler = this.flipVertical ? new Vector3(t_endDeg, 0f, 0f) : new Vector3(0f, t_endDeg, 0f);
        await this.target.DOLocalRotate(t_endEuler, this.spinDuration, RotateMode.FastBeyond360)
            .SetEase(Ease.OutCubic)
            .OnUpdate(() =>
            {
                Vector3 t_e = this.target.localEulerAngles;
                float t_ang = Mathf.Repeat(this.flipVertical ? t_e.x : t_e.y, 360f);
                bool t_showFront = t_ang < 90f || t_ang >= 270f;   // 정면 향할 때 앞면
                SetFace(t_showFront);
            })
            .SetLink(this.target.gameObject)
            .ToUniTask();

        // 회전 리셋 + 결과 면 확정(정면 표시).
        this.target.localEulerAngles = Vector3.zero;
        SetFace(_front);

        // '탁' 안착: 살짝 커졌다 원복(OutBack 오버슈트).
        await this.target.DOScale(this.baseScale * this.popScale, this.popDuration).SetEase(Ease.OutBack)
            .SetLink(this.target.gameObject).ToUniTask();
        await this.target.DOScale(this.baseScale, this.popDuration).SetEase(Ease.OutQuad)
            .SetLink(this.target.gameObject).ToUniTask();
    }

    void SetFace(bool _front)
    {
        if (this.coinImage == null) return;
        Sprite t_s = _front ? this.frontSprite : this.backSprite;
        if (t_s != null && this.coinImage.sprite != t_s) this.coinImage.sprite = t_s;
    }

#if UNITY_EDITOR
    [ContextMenu("Test Flip → Front(선공)")] void TestFront() => Play(true).Forget();
    [ContextMenu("Test Flip → Back(후공)")]  void TestBack()  => Play(false).Forget();
#endif
}
