using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
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

    [Header("결과 텍스트")]
    [SerializeField] TMP_Text resultText;          // 비우면 런타임 생성
    [SerializeField] string   frontText = "선공";   // 앞면
    [SerializeField] string   backText  = "후공";   // 뒷면

    Vector3 baseScale;
    bool    cached;

    void Awake()
    {
        if (this.target == null) this.target = this.transform;
        this.baseScale = this.target.localScale;
        this.cached = true;
        if (this.resultText == null) BuildResultText();
    }

    /// <summary>코인 플립 연출 재생. <paramref name="_front"/>=true면 앞면(선공)에서 안착.
    /// 완료 시 회전은 0으로 리셋되고 결과 면 스프라이트가 남는다.</summary>
    public async UniTask Play(bool _front)
    {
        if (!this.cached) { this.baseScale = this.target.localScale; this.cached = true; }

        this.target.DOKill();
        this.target.localScale       = this.baseScale;
        this.target.localEulerAngles = Vector3.zero;
        HideResult();

        // 항상 짝수 반바퀴 → 정면(upright, 0°)에서 종료. 뒷면이 180°(뒤집힌 채)로 끝나 튀는 문제 방지.
        int t_half = Mathf.Max(2, this.halfTurns);
        if (t_half % 2 != 0) t_half++;
        float t_endDeg   = t_half * 180f;
        float t_lastEdge = t_endDeg - 90f;   // 마지막 edge-on(면이 가려지는 순간) — 이후 결과 면으로 미리 스왑

        // 값 트윈으로 누적 각도를 직접 추적(마지막 바퀴 판정용). 회전 + 면 스왑을 매 프레임 적용.
        float t_deg = 0f;
        await DOTween.To(() => t_deg, x =>
            {
                t_deg = x;
                this.target.localEulerAngles = this.flipVertical ? new Vector3(x, 0f, 0f) : new Vector3(0f, x, 0f);
                bool t_showFront;
                if (x >= t_lastEdge) t_showFront = _front;   // 마지막 바퀴: 결과 면 미리 스왑(edge-on에서 교체 → 안 보임)
                else { float t_a = Mathf.Repeat(x, 360f); t_showFront = t_a < 90f || t_a >= 270f; }
                SetFace(t_showFront);
            }, t_endDeg, this.spinDuration)
            .SetEase(Ease.OutCubic)
            .SetLink(this.target.gameObject)
            .ToUniTask();

        // 회전 리셋(정면) + 결과 면 확정.
        this.target.localEulerAngles = Vector3.zero;
        SetFace(_front);
        RevealResult(_front);   // 선공/후공 텍스트 표시

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

    // 결과 텍스트가 미배선이면 코인 아래에 런타임 생성(씬 배선 불필요).
    void BuildResultText()
    {
        var t_go = new GameObject("CoinResultText", typeof(RectTransform));
        t_go.transform.SetParent(this.transform, false);
        var t_rt = (RectTransform)t_go.transform;
        t_rt.anchorMin = t_rt.anchorMax = new Vector2(0.5f, 0.5f);
        t_rt.pivot = new Vector2(0.5f, 0.5f);
        t_rt.anchoredPosition = new Vector2(0f, -240f);
        t_rt.sizeDelta = new Vector2(600f, 160f);
        var t_cg = t_go.AddComponent<CanvasGroup>();
        t_cg.alpha = 0f; t_cg.blocksRaycasts = false; t_cg.interactable = false;
        var t_txt = t_go.AddComponent<TextMeshProUGUI>();
        TutorialUIStyle.ApplyFont(t_txt);
        t_txt.alignment    = TextAlignmentOptions.Center;
        t_txt.fontSize     = 96f;
        t_txt.fontStyle    = FontStyles.Bold;
        t_txt.color        = Color.white;
        t_txt.raycastTarget = false;
        this.resultText = t_txt;
    }

    void HideResult()
    {
        if (this.resultText == null) return;
        this.resultText.transform.DOKill();
        var t_cg = this.resultText.GetComponent<CanvasGroup>();
        if (t_cg != null) { t_cg.DOKill(); t_cg.alpha = 0f; }
    }

    // 착지 시 선공/후공 표시(페이드+팝). 코인 pop과 동기.
    void RevealResult(bool _front)
    {
        if (this.resultText == null) return;
        this.resultText.text = _front ? this.frontText : this.backText;
        Transform t_tr = this.resultText.transform;
        t_tr.DOKill();
        t_tr.localScale = Vector3.one * 0.8f;
        t_tr.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetLink(this.resultText.gameObject);
        var t_cg = this.resultText.GetComponent<CanvasGroup>();
        if (t_cg != null) { t_cg.DOKill(); t_cg.alpha = 0f; t_cg.DOFade(1f, 0.2f).SetLink(this.resultText.gameObject); }
    }

#if UNITY_EDITOR
    [ContextMenu("Test Flip → Front(선공)")] void TestFront() => Play(true).Forget();
    [ContextMenu("Test Flip → Back(후공)")]  void TestBack()  => Play(false).Forget();
#endif
}
