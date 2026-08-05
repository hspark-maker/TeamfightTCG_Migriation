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
    [SerializeField] Image backgroundImage;                                   // ScreenDim이 없는 테스트 씬 fallback
    [SerializeField] Color backgroundColor  = new Color(0f, 0f, 0f, 0.78f);
    [SerializeField] float backgroundFade   = 0.25f;
    [SerializeField] Image glowImage;                                         // 비우면 런타임 생성(방사형 글로우)
    [SerializeField] Color frontGlowColor   = new Color(0.30f, 0.65f, 1f,    0.85f);  // 선공 = 파랑
    [SerializeField] Color backGlowColor    = new Color(1f,    0.28f, 0.22f, 0.85f);  // 후공 = 빨강
    [SerializeField] float glowSize         = 900f;
    [SerializeField] float glowFade         = 0.28f;
    [SerializeField] float glowPulseScale   = 1.10f;   // 안착 후 숨쉬기 배율
    [SerializeField] float glowPulseTime    = 0.9f;

    [Header("반짝임 (안착 직후 1회)")]
    [SerializeField] float shineTilt     = 25f;     // 띠 기울기(도). 0이면 수직 띠
    [SerializeField] float shineWidth    = 0.22f;   // 코인 폭 대비 띠 두께
    [SerializeField] float shineDuration = 0.5f;    // 위→아래로 훑는 시간
    [SerializeField] float shineAlpha    = 0.9f;    // 띠 밝기(반사광이라 코인보다 진하다)

    Vector3 baseScale;
    bool    cached;

    Image shineImage;   // 런타임 생성 — 코인 실루엣 안에서만 보이는 사선 흰 띠

    static Sprite s_radialSprite;   // 런타임 생성 방사형 글로우 스프라이트(전 인스턴스 공용)

    void Awake()
    {
        if (this.target == null) this.target = this.transform;
        this.baseScale = this.target.localScale;
        this.cached = true;
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
        HideShine();        // 이전 판 반짝임이 남아 회전 중에 번쩍이지 않게
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

        // 금속 반사 1회. 팝이 **끝난 뒤** — 커지는 중에 겹치면 둘 다 묻힌다.
        await PlayShine();
    }

    void SetFace(bool _front)
    {
        if (this.coinImage == null) return;
        Sprite t_s = _front ? this.frontSprite : this.backSprite;
        if (t_s != null && this.coinImage.sprite != t_s) this.coinImage.sprite = t_s;
    }

    // ── 반짝임 ────────────────────────────────────────────────────
    // 사선 흰 띠가 코인 위를 위→아래로 한 번 지나간다(시너지 엠블럼 DropAndShine과 같은 그림).
    // 띠는 코인 실루엣 **안에서만** 보여야 금속 반사로 읽힌다 — UI는 SpriteMask가 안 먹으므로 Mask(스텐실)를 쓴다.
    //
    // **Mask를 코인 Image에 직접 걸면 안 된다** — 그러면 코인의 다른 자식(결과 텍스트 등)까지 실루엣 밖이
    // 통째로 잘려 사라진다. 그래서 코인 스프라이트를 한 장 더 깐 전용 마스크 GO를 만들고 띠만 그 자식에 둔다
    // (showMaskGraphic=false — 스텐실만 쓰고 그림은 안 그린다. 코인 본체는 원래 Image가 계속 그린다).
    Image shineMaskImage;

    void EnsureShine()
    {
        if (this.shineImage != null || this.coinImage == null) return;

        var t_maskGo = new GameObject("CoinShineMask", typeof(RectTransform));
        t_maskGo.transform.SetParent(this.coinImage.transform, false);
        t_maskGo.transform.SetAsFirstSibling();   // 결과 텍스트 등 뒤 형제보다 먼저 그려지게(텍스트가 위)

        var t_maskRt = (RectTransform)t_maskGo.transform;
        t_maskRt.anchorMin = Vector2.zero;        // 코인 rect에 그대로 맞춘다
        t_maskRt.anchorMax = Vector2.one;
        t_maskRt.offsetMin = t_maskRt.offsetMax = Vector2.zero;

        this.shineMaskImage = t_maskGo.AddComponent<Image>();
        this.shineMaskImage.raycastTarget = false;
        t_maskGo.AddComponent<Mask>().showMaskGraphic = false;

        var t_go = new GameObject("CoinShine", typeof(RectTransform));
        t_go.transform.SetParent(t_maskGo.transform, false);

        this.shineImage = t_go.AddComponent<Image>();
        this.shineImage.sprite        = ShineBandSprite.Get();
        this.shineImage.raycastTarget = false;
        this.shineImage.color         = new Color(1f, 1f, 1f, 0f);
        t_maskGo.SetActive(false);
    }

    /// <summary>반짝임 1회. 팝(안착)이 **끝난 뒤** 불린다 — 커지는 중에 겹치면 둘 다 묻힌다.
    /// 호출부가 await 해야 연출이 잘리지 않는다(끝나면 코인을 내리는 흐름이라).</summary>
    async UniTask PlayShine()
    {
        EnsureShine();
        if (this.shineImage == null) return;

        // 마스크 실루엣 = 지금 보이는 코인 면. SetFace가 스프라이트를 갈아끼우므로 재생 시점에 맞춘다.
        this.shineMaskImage.sprite = this.coinImage.sprite;

        // 크기는 재생할 때마다 코인 rect에서 다시 잡는다 — 해상도/레이아웃으로 코인이 커져도 띠가 어긋나지 않게.
        Rect t_coin = ((RectTransform)this.coinImage.transform).rect;
        var  t_rt   = (RectTransform)this.shineImage.transform;

        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);
        // 기울여도 코인을 완전히 가로지르도록 대각선 길이보다 길게.
        t_rt.sizeDelta = new Vector2(t_coin.width * this.shineWidth,
                                     Mathf.Sqrt(t_coin.width * t_coin.width + t_coin.height * t_coin.height) * 1.4f);
        t_rt.localRotation = Quaternion.Euler(0f, 0f, this.shineTilt);

        float t_travel = t_coin.height * 0.95f;   // 코인 위에서 시작해 아래로 빠져나갈 만큼
        t_rt.anchoredPosition = new Vector2(0f, t_travel);

        var t_go = this.shineMaskImage.gameObject;
        t_go.SetActive(true);
        t_rt.DOKill();
        this.shineImage.DOKill();
        this.shineImage.color = new Color(1f, 1f, 1f, 0f);

        float t_dur = Mathf.Max(0.05f, this.shineDuration);

        // 밝기는 양 끝에서 죽는다 — 들어올 때/나갈 때 띠가 잘려 보이지 않게.
        DOTween.Sequence().SetLink(t_go)
            .Append(this.shineImage.DOFade(this.shineAlpha, t_dur * 0.3f))
            .AppendInterval(t_dur * 0.3f)
            .Append(this.shineImage.DOFade(0f, t_dur * 0.4f));

        await t_rt.DOAnchorPosY(-t_travel, t_dur).SetEase(Ease.InOutSine).SetLink(t_go).ToUniTask();
        if (t_go != null) t_go.SetActive(false);
    }

    void HideShine()
    {
        if (this.shineImage == null) return;
        this.shineImage.DOKill();
        this.shineImage.transform.DOKill();
        this.shineImage.color = new Color(1f, 1f, 1f, 0f);
        if (this.shineMaskImage != null) this.shineMaskImage.gameObject.SetActive(false);
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
        if (ScreenDim.IsAvailable) ScreenDim.Show(this, this.backgroundColor.a, true, this.backgroundFade);
        else
        {
            if (this.backgroundImage == null)
            {
                this.backgroundImage = BuildBackdrop("CoinFlipBackground", 4000f, null);
                if (this.glowImage != null)
                    this.backgroundImage.transform.SetSiblingIndex(this.glowImage.transform.GetSiblingIndex());
            }
            var t_backgroundGo = this.backgroundImage.gameObject;
            t_backgroundGo.SetActive(true);
            this.backgroundImage.raycastTarget = true;
            this.backgroundImage.DOKill();
            this.backgroundImage.color = new Color(this.backgroundColor.r, this.backgroundColor.g, this.backgroundColor.b, 0f);
            this.backgroundImage.DOFade(this.backgroundColor.a, this.backgroundFade).SetLink(t_backgroundGo);
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
        HideShine();
        ScreenDim.Hide(this);
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
