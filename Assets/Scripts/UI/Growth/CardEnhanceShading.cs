using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 강화 연출에서 카드 표면이 내는 빛(AllIn1SpriteShader UiMask 변형). 시간축은 모른다 —
// 축 값을 셰이더 프로퍼티로 옮기기만 하고, 언제 얼마나 밀지는 CardEnhanceRitualView가 정한다.
//
// 본체에 얹는 축은 전부 UV와 무관한 것들이다 — 카드가 여러 이미지로 쪼개져 있어도 재질 사본 하나를
// 함께 쓰면 조각나지 않는다. UV 의존 축(FADE·SHINE)은 이미지 한 장짜리 덮개에서만 쓴다.
//
// ⚠ 셰이더 키워드는 런타임에 켜지 않는다(shader_feature — 빌드에서 스트립되고 첫 EnableKeyword가 렉을 만든다).
[Serializable]
public class CardEnhanceShading
{
    [Header("카드 본체")]
    [Tooltip("⚠ TMP 텍스트는 넣지 않는다 — 자체 재질을 쓰므로 덮어쓰면 글자가 깨진다.")]
    [SerializeField] Graphic[] cardSurfaces;                                    // 본체 이미지들(Frame·Portrait·장식) — 재질 사본 하나를 공유
    [SerializeField] Material  bodyMaterial;                                    // CardRitualBody. GLOW·GREYSCALE·HITEFFECT·INNEROUTLINE·SHAKEUV 필요

    [Header("덮개 — 글자까지 삼키는 한 겹")]
    [Tooltip("⚠ CardUIView의 맨 마지막 자식이어야 한다 — 글자·아이콘 위에 오지 않으면 그것들만 남아 뜬다.")]
    [SerializeField] Graphic  floodCover;                                       // 카드 실루엣을 덮는 판(알파 0·Raycast 끔). 미배선이면 글자가 안 덮인다
    [SerializeField] Material coverMaterial;                                    // CardRitualEmber. FADE 필요 — 미배선이면 얼룩 없이 균일하게 걷힌다

    [Header("표면을 훑는 빛 (선택)")]
    [Tooltip("⚠ 색은 검정·알파 1·가산 블렌드여야 한다 — SHINE은 밑판 색에 빛을 더하는 축이다.")]
    [SerializeField] Graphic  gleamCover;                                       // floodCover와 같은 스프라이트·같은 자리, 그 다음 자식
    [SerializeField] Material gleamMaterial;                                    // CardRitualGleam. SHINE 필요
    [Range(0f, 20f)]
    [SerializeField] float    gleamGlow = 3f;                                   // 빛줄기 세기(_ShineGlow) — 재질 저작값을 매번 덮어쓴다

    [Header("온도 — 열이 어떤 빛으로 보이는가")]
    [SerializeField] Color emberColor    = new Color(1f, 0.50f, 0.15f, 1f);     // 달아오르기 시작할 때(잉걸)
    [SerializeField] Color whiteHotColor = new Color(1f, 0.93f, 0.78f, 1f);     // 정점(백열) — 여기까지 올라야 다음이 '터진다'로 읽힌다
    [SerializeField] float heatGlow      = 1.6f;                                // 정점의 발광(_Glow). 1을 넘기면 카드 색이 날아간다
    [Range(0f, 1f)]
    [SerializeField] float rimStrength   = 0.75f;                               // 정점의 테두리 불(_InnerOutlineAlpha)
    [Range(0f, 2f)]
    [SerializeField] float pixelShake    = 0.35f;                               // 정점의 픽셀 진동(_ShakeUv). 크면 이웃 그림이 새어든다

    // 셰이더가 '아직 멀쩡함'으로 보는 _FadeAmount. 재질의 _FadeBurnTransition(0.28)보다 낮아야
    // 평상시 덮개가 새지 않는다(셰이더가 [_FadeAmount, +transition] 구간을 걸쳐 지운다).
    const float FadeIdle = -0.3f;

    static readonly int P_Glow              = Shader.PropertyToID("_Glow");
    static readonly int P_GlowColor         = Shader.PropertyToID("_GlowColor");
    static readonly int P_InnerOutlineAlpha = Shader.PropertyToID("_InnerOutlineAlpha");
    static readonly int P_InnerOutlineColor = Shader.PropertyToID("_InnerOutlineColor");
    static readonly int P_ShakeUvX          = Shader.PropertyToID("_ShakeUvX");
    static readonly int P_ShakeUvY          = Shader.PropertyToID("_ShakeUvY");
    static readonly int P_GreyscaleBlend    = Shader.PropertyToID("_GreyscaleBlend");
    static readonly int P_HitEffectBlend    = Shader.PropertyToID("_HitEffectBlend");
    static readonly int P_HitEffectColor    = Shader.PropertyToID("_HitEffectColor");
    static readonly int P_FadeAmount        = Shader.PropertyToID("_FadeAmount");
    static readonly int P_ShineLocation     = Shader.PropertyToID("_ShineLocation");
    static readonly int P_ShineWidth        = Shader.PropertyToID("_ShineWidth");
    static readonly int P_ShineGlow         = Shader.PropertyToID("_ShineGlow");

    Material m_bodyMat;                     // 재질 사본. 자산을 직접 밀면 같은 셰이더를 쓰는 다른 화면까지 달아오른다
    Material m_coverMat;
    Material m_gleamMat;

    // 축의 현재값. 재질에서 되읽지 않는다 — 미배선이어도 축은 이어져야 뒤 구간의 출발점이 흔들리지 않는다.
    float m_heat;
    float m_shake;
    float m_grey;
    float m_blind;
    float m_coverAlpha;
    float m_snuff;
    float m_gleamAt;
    Color m_blindColor = Color.white;

    /// <summary>달아오를 면이 하나라도 배선돼 있는가.</summary>
    public bool HasSurface => this.m_bodyMat != null || this.floodCover != null;

    /// <summary>덮개를 얼룩덜룩 갉아 없앨 수 있는가(FADE 재질 배선 여부).</summary>
    public bool CanSnuff => this.m_coverMat != null;

    /// <summary>표면을 훑는 빛을 쏠 수 있는가.</summary>
    public bool HasGleam => this.m_gleamMat != null && this.gleamCover != null;

    public Color Ember    => this.emberColor;
    public Color WhiteHot => this.whiteHotColor;

    /// <summary>잉걸~백열 사이의 빛색.</summary>
    public Color LightAt(float _t) => Color.Lerp(this.emberColor, this.whiteHotColor, _t);

    // ── 축 ───────────────────────────────────────────────

    /// <summary>0 평상 ~ +1 백열. 흩어진 프로퍼티를 한 축으로 묶어 구간마다 하나만 밀면 되게 한다.
    /// ⚠ 카드 색(_Color)은 건드리지 않는다 — 톤이 밀리면 "달아오른다"가 아니라 "다른 카드로 바뀌었다"가 된다.</summary>
    public float Heat
    {
        get => this.m_heat;
        set
        {
            this.m_heat = value;

            if (this.m_bodyMat == null) return;

            float t_hot   = Mathf.Clamp01(value);
            Color t_light = LightAt(t_hot);

            this.m_bodyMat.SetColor(P_GlowColor, t_light);
            this.m_bodyMat.SetColor(P_InnerOutlineColor, t_light);

            // 제곱으로 민다 — 선형이면 앞 절반에서 이미 밝아져 정점이 밋밋해진다.
            this.m_bodyMat.SetFloat(P_Glow, t_hot * t_hot * this.heatGlow);
            this.m_bodyMat.SetFloat(P_InnerOutlineAlpha, t_hot * this.rimStrength);
        }
    }

    /// <summary>픽셀 진동 0~1.</summary>
    public float Shake
    {
        get => this.m_shake;
        set
        {
            this.m_shake = value;

            if (this.m_bodyMat == null) return;

            this.m_bodyMat.SetFloat(P_ShakeUvX, value * this.pixelShake);
            this.m_bodyMat.SetFloat(P_ShakeUvY, value * this.pixelShake * 0.6f);
        }
    }

    /// <summary>회색화 0~1.</summary>
    public float Grey
    {
        get => this.m_grey;
        set
        {
            this.m_grey = value;
            if (this.m_bodyMat != null) this.m_bodyMat.SetFloat(P_GreyscaleBlend, value);
        }
    }

    /// <summary>카드 면을 덮는 백열 0~1. 1이면 흰 실루엣만 남는다.</summary>
    public float Blind
    {
        get => this.m_blind;
        set
        {
            this.m_blind = value;
            if (this.m_bodyMat != null) this.m_bodyMat.SetFloat(P_HitEffectBlend, value);
        }
    }

    /// <summary>덮개의 짙기 0~1. 글자·아이콘까지 삼키는 것은 이 축이다.</summary>
    public float Cover
    {
        get => this.m_coverAlpha;
        set
        {
            this.m_coverAlpha = value;
            SetAlpha(this.floodCover, value);
        }
    }

    /// <summary>0이면 덮개가 멀쩡하고 1이면 다 꺼졌다. 셰이더의 유휴값이 음수라 그 구간을 감춘 0~1이다.</summary>
    public float Snuff
    {
        get => this.m_snuff;
        set
        {
            this.m_snuff = value;
            if (this.m_coverMat != null) this.m_coverMat.SetFloat(P_FadeAmount, Mathf.Lerp(FadeIdle, 1f, value));
        }
    }

    /// <summary>본체의 백열과 덮개는 같은 빛이다 — 색을 따로 두면 경계에서 두 장으로 갈라져 보인다.
    /// 덮개의 알파는 <see cref="Cover"/>가 소유하므로 여기서 건드리지 않는다.</summary>
    public Color BlindColor
    {
        get => this.m_blindColor;
        set
        {
            this.m_blindColor = value;

            if (this.m_bodyMat != null) this.m_bodyMat.SetColor(P_HitEffectColor, value);

            if (this.floodCover == null) return;

            value.a = this.floodCover.color.a;
            this.floodCover.color = value;
        }
    }

    /// <summary>0이면 빛줄기가 닿기 전, 1이면 다 지나간 뒤. 양끝을 띠 폭만큼 밖으로 뺀다 —
    /// 안 그러면 반쯤 걸친 채 나타나거나 끝자락이 카드 위에 남는다.</summary>
    public float Gleam
    {
        get => this.m_gleamAt;
        set
        {
            this.m_gleamAt = value;

            if (this.m_gleamMat == null) return;

            float t_width = this.m_gleamMat.GetFloat(P_ShineWidth);

            this.m_gleamMat.SetFloat(P_ShineGlow, this.gleamGlow);
            this.m_gleamMat.SetFloat(P_ShineLocation, Mathf.Lerp(-t_width, 1f + t_width, value));
        }
    }

    // getter는 트윈이 시작할 때 한 번 읽힌다 — 그래서 앞 구간이 남긴 값에서 이어 출발한다.
    public Tween TweenHeat(float _to, float _dur)  => DOTween.To(() => this.Heat,  _v => this.Heat  = _v, _to, _dur);
    public Tween TweenShake(float _to, float _dur) => DOTween.To(() => this.Shake, _v => this.Shake = _v, _to, _dur);
    public Tween TweenGrey(float _to, float _dur)  => DOTween.To(() => this.Grey,  _v => this.Grey  = _v, _to, _dur);
    public Tween TweenBlind(float _to, float _dur) => DOTween.To(() => this.Blind, _v => this.Blind = _v, _to, _dur);
    public Tween TweenCover(float _to, float _dur) => DOTween.To(() => this.Cover, _v => this.Cover = _v, _to, _dur);
    public Tween TweenSnuff(float _to, float _dur) => DOTween.To(() => this.Snuff, _v => this.Snuff = _v, _to, _dur);
    public Tween TweenGleam(float _to, float _dur) => DOTween.To(() => this.Gleam, _v => this.Gleam = _v, _to, _dur);

    public Tween TweenBlindColor(Color _to, float _dur)
        => DOTween.To(() => this.BlindColor, _v => this.BlindColor = _v, _to, _dur);

    // ── 수명 ─────────────────────────────────────────────

    // 사본을 한 번만 만들어 그대로 둔다. 평상값이 전부 중립이라 연출 밖에서는 기본 UI 재질과 구분되지 않는다.
    public void Attach()
    {
        if (this.m_bodyMat == null && this.bodyMaterial != null && this.cardSurfaces != null)
        {
            this.m_bodyMat = new Material(this.bodyMaterial) { name = this.bodyMaterial.name + " (ritual)" };

            foreach (Graphic t_g in this.cardSurfaces)
            {
                if (t_g != null) t_g.material = this.m_bodyMat;
            }
        }

        // 덮개는 본체와 다른 재질을 쓴다 — 본체가 못 쓰는 UV 의존 축(FADE)이 여기서만 성립한다.
        if (this.m_coverMat == null && this.coverMaterial != null && this.floodCover != null)
        {
            this.m_coverMat = new Material(this.coverMaterial) { name = this.coverMaterial.name + " (ritual)" };

            this.floodCover.material = this.m_coverMat;
        }

        // 빛줄기는 유휴 위치가 중립이 아니다(저작값은 카드 한복판) — 얹자마자 범위 밖으로 밀어 둔다.
        if (this.m_gleamMat == null && this.gleamMaterial != null && this.gleamCover != null)
        {
            this.m_gleamMat = new Material(this.gleamMaterial) { name = this.gleamMaterial.name + " (ritual)" };

            this.gleamCover.material = this.m_gleamMat;
            this.Gleam = 0f;
            SetAlpha(this.gleamCover, 0f);
        }
    }

    public void Release()
    {
        if (this.m_bodyMat  != null) UnityEngine.Object.Destroy(this.m_bodyMat);
        if (this.m_coverMat != null) UnityEngine.Object.Destroy(this.m_coverMat);
        if (this.m_gleamMat != null) UnityEngine.Object.Destroy(this.m_gleamMat);

        this.m_bodyMat  = null;
        this.m_coverMat = null;
        this.m_gleamMat = null;
    }

    /// <summary>빛줄기를 쏘기 직전. 밑판은 검정·가산이라 켜져도 보이는 것이 없다 — 빛을 실을 알파만 세운다.</summary>
    public void BeginGleam()
    {
        this.Gleam = 0f;
        SetAlpha(this.gleamCover, 1f);
    }

    /// <summary>다 지나간 판은 도로 내린다 — 켜 둔 채면 다음 열기에 띠가 걸린 채로 뜬다.</summary>
    public void EndGleam()
    {
        SetAlpha(this.gleamCover, 0f);
    }

    /// <summary>축을 전부 평상으로. 다음 연출이 중간값에서 출발하지 않게.</summary>
    public void Neutralize()
    {
        EndGleam();
        this.Gleam = 0f;

        this.Heat  = 0f;
        this.Shake = 0f;
        this.Grey  = 0f;
        this.Blind = 0f;

        // 알파를 먼저 내리고 잠식을 되돌린다 — 순서가 뒤집히면 지워졌던 덮개가 한 프레임 되살아난다.
        this.Cover = 0f;
        this.Snuff = 0f;

        this.BlindColor = Color.white;
    }

    static void SetAlpha(Graphic _g, float _a)
    {
        if (_g == null) return;

        Color t_c = _g.color;
        t_c.a = _a;
        _g.color = t_c;
    }
}
