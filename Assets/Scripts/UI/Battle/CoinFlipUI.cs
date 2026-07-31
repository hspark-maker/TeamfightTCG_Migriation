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

    [Header("배경 / 글로우")]
    [SerializeField] Image backgroundImage;                                   // 비우면 런타임 생성(전체화면 딤)
    [SerializeField] Color backgroundColor  = new Color(0f, 0f, 0f, 0.78f);
    [SerializeField] float backgroundFade   = 0.25f;
    [SerializeField] Image glowImage;                                         // 비우면 런타임 생성(방사형 글로우)
    [SerializeField] Color frontGlowColor   = new Color(0.30f, 0.65f, 1f,    0.85f);  // 선공 = 파랑
    [SerializeField] Color backGlowColor    = new Color(1f,    0.28f, 0.22f, 0.85f);  // 후공 = 빨강
    [SerializeField] float glowSize         = 900f;
    [SerializeField] float glowFade         = 0.28f;
    [SerializeField] float glowPulseScale   = 1.10f;   // 안착 후 숨쉬기 배율
    [SerializeField] float glowPulseTime    = 0.9f;

    Vector3 baseScale;
    bool    cached;

    static Sprite s_radialSprite;   // 런타임 생성 방사형 글로우 스프라이트(전 인스턴스 공용)

    void Awake()
    {
        if (this.target == null) this.target = this.transform;
        this.baseScale = this.target.localScale;
        this.cached = true;
        if (this.backgroundImage == null) this.backgroundImage = BuildBackdrop("CoinFlipBackground", 4000f, null);
        if (this.glowImage       == null) this.glowImage       = BuildBackdrop("CoinFlipGlow", this.glowSize, RadialSprite());
        HideBackdrop();
    }

    void OnDisable() => HideBackdrop();   // 코인 GO 비활성(TurnRunner) 시 부모에 붙은 배경/글로우도 같이 정리.

    /// <summary>코인 플립 연출 재생. <paramref name="_front"/>=true면 앞면(선공)에서 안착.
    /// 완료 시 회전은 0으로 리셋되고 결과 면 스프라이트가 남는다.</summary>
    public async UniTask Play(bool _front)
    {
        if (!this.cached) { this.baseScale = this.target.localScale; this.cached = true; }

        this.target.DOKill();
        this.target.localScale       = this.baseScale;
        this.target.localEulerAngles = Vector3.zero;
        HideResult();
        ShowBackground();   // 딤 배경 페이드인(글로우는 결과 확정 후)

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
        RevealGlow(_front);     // 선공=파랑 / 후공=빨강 글로우

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

    // ── 배경 / 글로우 ─────────────────────────────────────────────
    // 코인(this.transform)은 회전·스케일 대상이라 자식으로 붙이면 같이 돈다.
    // 부모의 자식으로 만들고 코인보다 앞 sibling index에 두어 항상 뒤에 그려지게 한다.
    Image BuildBackdrop(string _name, float _size, Sprite _sprite)
    {
        Transform t_parent = this.transform.parent != null ? this.transform.parent : this.transform;
        var t_go = new GameObject(_name, typeof(RectTransform));
        t_go.transform.SetParent(t_parent, false);
        var t_rt = (RectTransform)t_go.transform;
        t_rt.anchorMin = t_rt.anchorMax = new Vector2(0.5f, 0.5f);
        t_rt.pivot     = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta = new Vector2(_size, _size);
        // 코인이 부모 기준 오프셋을 가질 수 있으므로 코인 위치에 맞춘다(글로우가 코인 뒤에 오도록).
        if (t_parent != this.transform && this.transform is RectTransform t_self)
            t_rt.anchoredPosition = t_self.anchoredPosition;
        else t_rt.anchoredPosition = Vector2.zero;

        var t_img = t_go.AddComponent<Image>();
        t_img.sprite        = _sprite;
        t_img.raycastTarget = false;
        t_img.color         = new Color(1f, 1f, 1f, 0f);

        if (t_parent != this.transform) t_go.transform.SetSiblingIndex(this.transform.GetSiblingIndex());
        return t_img;
    }

    // 중심 흰색 → 가장자리 투명 방사형 그라디언트. 별도 아트 에셋 없이 글로우 표현.
    static Sprite RadialSprite()
    {
        if (s_radialSprite != null) return s_radialSprite;
        const int c_size = 128;
        var t_tex = new Texture2D(c_size, c_size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var t_px  = new Color[c_size * c_size];
        float t_r = c_size * 0.5f;
        for (int i = 0; i < c_size; i++)
            for (int j = 0; j < c_size; j++)
            {
                float t_d = Vector2.Distance(new Vector2(j + 0.5f, i + 0.5f), new Vector2(t_r, t_r)) / t_r;
                float t_a = Mathf.Clamp01(1f - t_d);
                t_a *= t_a;   // 제곱 falloff — 중심 집중, 가장자리 부드럽게
                t_px[i * c_size + j] = new Color(1f, 1f, 1f, t_a);
            }
        t_tex.SetPixels(t_px);
        t_tex.Apply();
        s_radialSprite = Sprite.Create(t_tex, new Rect(0f, 0f, c_size, c_size), new Vector2(0.5f, 0.5f));
        return s_radialSprite;
    }

    void ShowBackground()
    {
        if (this.backgroundImage != null)
        {
            var t_go = this.backgroundImage.gameObject;
            t_go.SetActive(true);
            this.backgroundImage.DOKill();
            this.backgroundImage.color = new Color(this.backgroundColor.r, this.backgroundColor.g, this.backgroundColor.b, 0f);
            this.backgroundImage.DOFade(this.backgroundColor.a, this.backgroundFade).SetLink(t_go);
        }
        if (this.glowImage != null)
        {
            this.glowImage.transform.DOKill();
            this.glowImage.DOKill();
            this.glowImage.gameObject.SetActive(false);
        }
    }

    void RevealGlow(bool _front)
    {
        if (this.glowImage == null) return;
        Color t_c = _front ? this.frontGlowColor : this.backGlowColor;
        var t_go = this.glowImage.gameObject;
        t_go.SetActive(true);
        this.glowImage.DOKill();
        this.glowImage.transform.DOKill();
        this.glowImage.color         = new Color(t_c.r, t_c.g, t_c.b, 0f);
        this.glowImage.transform.localScale = Vector3.one * 0.75f;
        this.glowImage.DOFade(t_c.a, this.glowFade).SetLink(t_go);
        this.glowImage.transform.DOScale(1f, this.glowFade).SetEase(Ease.OutBack).SetLink(t_go)
            .OnComplete(() => this.glowImage.transform
                .DOScale(this.glowPulseScale, this.glowPulseTime)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(t_go));
    }

    void HideBackdrop()
    {
        if (this.backgroundImage != null)
        {
            this.backgroundImage.DOKill();
            this.backgroundImage.color = new Color(this.backgroundColor.r, this.backgroundColor.g, this.backgroundColor.b, 0f);
            this.backgroundImage.gameObject.SetActive(false);
        }
        if (this.glowImage != null)
        {
            this.glowImage.DOKill();
            this.glowImage.transform.DOKill();
            this.glowImage.transform.localScale = Vector3.one;
            this.glowImage.gameObject.SetActive(false);
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Test Flip → Front(선공)")] void TestFront() => Play(true).Forget();
    [ContextMenu("Test Flip → Back(후공)")]  void TestBack()  => Play(false).Forget();
#endif
}
