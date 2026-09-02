using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 잠김 판이 걷히는 순간의 연출 — 빛이 자물쇠로 모였다가, 자물쇠가 터지며 판이 사라진다.
// 잠김 판 노드에 붙인다(키워드 섹션·시너지 섹션 각각 한 장).
[RequireComponent(typeof(RectTransform))]
public class SectionUnlockFx : MonoBehaviour
{
    [Tooltip("빛이 모이는 목적지(자물쇠 아이콘). 미배선이면 판 한가운데로 모인다.")]
    [SerializeField] RectTransform lockMark;

    [Tooltip("걷힐 덮개. 미배선이면 이 노드의 Graphic.")]
    [SerializeField] Graphic plate;

    [Tooltip("빛 알갱이 스프라이트. 미배선이면 빛 없이 자물쇠 강조와 걷힘만 돈다.")]
    [SerializeField] Sprite moteSprite;

    [Tooltip("자물쇠 뒤에서 비치는 빛(자물쇠보다 앞 형제여선 안 된다). 미배선이면 이 축만 빠진다.")]
    [SerializeField] Graphic backLight;

    [Header("모이는 빛")]
    [SerializeField] int   moteCount      = 10;
    [SerializeField] float moteSize       = 64f;
    [Tooltip("빛이 처음 흩어지는 거리(px). 자물쇠에서 이만큼 퍼졌다가 도로 빨려 든다.")]
    [SerializeField] float scatterRadius  = 200f;
    [SerializeField] float gatherDuration = 0.42f;
    [Tooltip("빛 한 알씩 출발이 밀리는 간격. 0이면 전부 동시에 모여 한 방으로 끝난다.")]
    [SerializeField] float moteInterval   = 0.05f;

    [Header("자물쇠 뒤 빛")]
    [Tooltip("빛이 모이는 동안 뒷빛이 차오르는 최대 알파.")]
    [Range(0f, 1f)]
    [SerializeField] float backLightAlpha = 0.85f;
    [Tooltip("차올랐을 때의 크기 배율. 저작 크기에서 여기까지 커진다.")]
    [SerializeField] float backLightScale = 1f;
    [Tooltip("터지는 순간 한 번 더 커지는 배율(위 배율에 곱한다).")]
    [SerializeField] float backLightBurstScale = 1.5f;
    [Tooltip("도는 속도(초당 각도). 0이면 돌지 않는다.")]
    [SerializeField] float backLightSpin = 40f;

    [Header("풀리는 순간")]
    [Tooltip("빛이 닿을 때마다 자물쇠가 튀는 세기.")]
    [SerializeField] float markPunch     = 0.25f;
    [Tooltip("자물쇠가 커지며 사라지고 판이 걷히는 시간.")]
    [SerializeField] float breakDuration = 0.3f;
    [Tooltip("터질 때 자물쇠가 커지는 배율.")]
    [SerializeField] float breakScale    = 1.6f;

    CoinBurstEffect m_burst;
    Sequence        m_seq;

    // 저작값. 진입 시점 값을 기준으로 삼으면 앞 연출의 어긋남이 이월된다.
    float   m_plateAlpha0 = 1f;
    float   m_markAlpha0  = 1f;
    Vector3 m_markScale0  = Vector3.one;
    float   m_backAlpha0;
    Vector3 m_backScale0  = Vector3.one;

    Graphic       Plate      => this.plate != null ? this.plate : GetComponent<Graphic>();
    RectTransform Mark       => this.lockMark != null ? this.lockMark : (RectTransform)transform;
    Graphic       MarkGraphic => this.Mark != null ? this.Mark.GetComponent<Graphic>() : null;

    /// <summary>판을 연출로 걷고 노드를 비활성으로 남긴다. 꺼져 있으면 아무것도 하지 않는다.</summary>
    public Tween Play()
    {
        if (!gameObject.activeInHierarchy) return null;

        KillRunning();
        RestoreAuthored();   // 앞 연출이 잘린 자리에서 출발하지 않게

        RectTransform t_mark  = this.Mark;
        Graphic       t_glyph = this.MarkGraphic;
        Graphic       t_plate = this.Plate;
        float         t_break = Mathf.Max(0.02f, this.breakDuration);

        var t_seq = DOTween.Sequence().SetLink(gameObject);

        Sequence t_gather = BuildGather(t_mark);
        if (t_gather != null) t_seq.Append(t_gather);

        // 뒷빛은 모임과 같은 구간을 덮어야 "빛이 자물쇠로 몰린다"로 읽힌다.
        float t_gatherLen = t_gather != null ? Mathf.Max(0.02f, t_gather.Duration()) : 0f;
        StageBackLight(t_seq, t_gatherLen, t_break);

        t_seq.Append(t_mark.DOScale(this.m_markScale0 * this.breakScale, t_break).SetEase(Ease.OutCubic));
        if (t_glyph != null) t_seq.Join(t_glyph.DOFade(0f, t_break).SetEase(Ease.InQuad));
        if (t_plate != null) t_seq.Join(t_plate.DOFade(0f, t_break).SetEase(Ease.InQuad));

        // 잘려도 판은 꺼지고 값은 저작값으로 돌아가게 마무리는 한 곳에서.
        t_seq.OnKill(() =>
        {
            this.m_seq = null;
            if (this == null) return;

            RestoreAuthored();
            gameObject.SetActive(false);
        });

        this.m_seq = t_seq;
        return t_seq;
    }

    /// <summary>남은 구간을 최종 상태로 끌어당긴다(탭 스킵). 돌고 있지 않으면 false.</summary>
    public bool RequestSkip()
    {
        Sequence t_seq = this.m_seq;
        if (t_seq == null || !t_seq.IsActive()) return false;

        t_seq.Complete(true);
        return true;
    }

    void Awake()
    {
        Graphic t_plate = this.Plate;
        if (t_plate != null) this.m_plateAlpha0 = t_plate.color.a;

        Graphic t_glyph = this.MarkGraphic;
        if (t_glyph != null) this.m_markAlpha0 = t_glyph.color.a;

        if (this.Mark != null) this.m_markScale0 = this.Mark.localScale;

        if (this.backLight != null)
        {
            this.m_backAlpha0 = this.backLight.color.a;
            this.m_backScale0 = this.backLight.transform.localScale;
        }
    }

    void OnDisable()
    {
        // 연출 도중 꺼지면 마무리 콜백이 오지 않아 알갱이와 트윈이 남는다.
        KillRunning();
        RestoreAuthored();
    }

    /// <summary>자물쇠 뒤 빛을 시퀀스에 얹는다. 모이는 동안 차오르고, 터질 때 한 번 더 부풀었다 꺼진다.</summary>
    void StageBackLight(Sequence _seq, float _gatherLen, float _breakLen)
    {
        if (this.backLight == null) return;

        var t_rt = (RectTransform)this.backLight.transform;
        this.backLight.gameObject.SetActive(true);

        float t_rise = Mathf.Max(0.02f, _gatherLen);
        _seq.Insert(0f, this.backLight.DOFade(this.backLightAlpha, t_rise).SetEase(Ease.OutQuad));
        _seq.Insert(0f, t_rt.DOScale(this.m_backScale0 * this.backLightScale, t_rise).SetEase(Ease.OutQuad));

        // 저작값이 속도(초당 각도)라 구간 길이로 환산한다.
        float t_total = t_rise + _breakLen;
        if (!Mathf.Approximately(this.backLightSpin, 0f))
            _seq.Insert(0f, t_rt.DOLocalRotate(new Vector3(0f, 0f, this.backLightSpin * t_total), t_total,
                                               RotateMode.LocalAxisAdd).SetEase(Ease.Linear));

        _seq.Insert(t_rise, t_rt.DOScale(this.m_backScale0 * this.backLightScale * this.backLightBurstScale, _breakLen)
                                .SetEase(Ease.OutCubic));
        _seq.Insert(t_rise, this.backLight.DOFade(0f, _breakLen).SetEase(Ease.InQuad));
    }

    // 빛 알갱이. 자물쇠 자리에서 퍼졌다가 그 자리로 다시 모인다(출발 == 목적지).
    Sequence BuildGather(RectTransform _mark)
    {
        if (this.moteSprite == null || this.moteCount <= 0) return null;

        CoinBurstEffect t_burst = EnsureBurst();
        t_burst.Configure(this.moteSprite, _mark, _mark, this.moteCount,
                          _angleStart: 0f, _angleSpan: 360f,
                          _scatterRadius: this.scatterRadius, _gatherDuration: this.gatherDuration,
                          _coinSize: this.moteSize, _coinInterval: this.moteInterval);

        return t_burst.BuildBurst((_arrived, _total) =>
                                      UiPunch.Play(_mark, this.markPunch, this.gatherDuration * 0.5f));
    }

    CoinBurstEffect EnsureBurst()
    {
        if (this.m_burst != null) return this.m_burst;

        // 알갱이는 이 노드 기준 anchoredPosition으로 나므로 원점·피벗을 부모와 맞춰야 궤적이 성립한다.
        var t_go = new GameObject("UnlockBurst", typeof(RectTransform), typeof(CoinBurstEffect));
        var t_rt = (RectTransform)t_go.transform;
        t_rt.SetParent(transform, false);
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta     = Vector2.zero;
        t_rt.localPosition = Vector3.zero;
        t_rt.localScale    = Vector3.one;

        this.m_burst = t_go.GetComponent<CoinBurstEffect>();
        return this.m_burst;
    }

    void RestoreAuthored()
    {
        RectTransform t_mark = this.Mark;
        if (t_mark != null)
        {
            t_mark.DOKill();
            t_mark.localScale = this.m_markScale0;
        }

        Graphic t_glyph = this.MarkGraphic;
        if (t_glyph != null) SetAlpha(t_glyph, this.m_markAlpha0);

        Graphic t_plate = this.Plate;
        if (t_plate != null) SetAlpha(t_plate, this.m_plateAlpha0);

        if (this.backLight != null)
        {
            Transform t_back = this.backLight.transform;
            t_back.DOKill();
            t_back.localScale    = this.m_backScale0;
            t_back.localRotation = Quaternion.identity;
            SetAlpha(this.backLight, this.m_backAlpha0);
        }
    }

    static void SetAlpha(Graphic _g, float _a)
    {
        _g.DOKill();
        Color t_c = _g.color;
        _g.color = new Color(t_c.r, t_c.g, t_c.b, _a);
    }

    void KillRunning()
    {
        Sequence t_seq = this.m_seq;
        this.m_seq = null;
        if (t_seq != null && t_seq.IsActive()) t_seq.Kill();
    }
}
