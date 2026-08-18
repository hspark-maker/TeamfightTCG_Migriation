using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 승급전 대기의 **상태 표현**(배지 뒤 광선 · "승급전" 문구 · 배지 호흡). 한 번 터지고 잊히는 연출이 아니라,
// 로비에 들어올 때마다 배지에서 "너 지금 승급전이야"가 계속 읽혀야 하는 상태다 — 그래서 상태(SetStandby)와
// 그 상태로 넘어가는 사건(BuildEnter)을 나눠 든다.
//
// 판때기는 전부 프리팹 저작이고 코드는 알파·배율·회전만 민다(CardEvolveRays·ScreenDimTint와 같은 규약).
// 재생하지 않고 호출자 시퀀스로 돌려준다 — 수명·링크는 부르는 쪽(RankHud)이 진다.
[Serializable]
public class RankPromoStandby
{
    [Header("배선")]
    [Tooltip("배지 뒤에서 뻗는 광선 판들. 비면 광선 축을 통째로 건너뛴다.\n" +
             "\n" +
             "저작 규약 세 가지 (어기면 광선으로 안 읽힌다):\n" +
             "  · 피벗은 (0.5, 0) — 뿌리가 배지 중심이고 거기서 바깥으로 뻗는다.\n" +
             "  · 폭은 길이의 0.12 안팎. 0.2를 넘기면 광선이 아니라 꽃잎이 된다.\n" +
             "  · 각도를 등간격으로 두지 말 것 — 나란하면 바람개비로 읽힌다.\n" +
             "배지보다 **앞선 형제**여야 배지를 가리지 않는다(uGUI는 나중 형제를 위에 그린다).")]
    [SerializeField] Graphic[] rays;

    [Tooltip("\"승급전\" 문구 노드. 비면 문구 축을 건너뛴다. 자리는 저작값 그대로 쓴다 — 코드는 내려꽂는 동안만 민다.")]
    [SerializeField] RectTransform label;

    [Tooltip("문구의 CanvasGroup. label과 같은 노드에 붙이면 된다. 비면 문구가 늘 보이는 채로 자리만 움직인다.")]
    [SerializeField] CanvasGroup labelGroup;

    [Tooltip("호흡시킬 배지 사각. RankHud가 그리는 배지와 같은 노드다 — " +
             "로비 재진입(연출 없는 경로)에는 배지를 넘겨줄 자리가 없어 여기서 든다.")]
    [SerializeField] RectTransform badge;

    [Tooltip("고스트가 닿는 프레임에 짧게 킥을 먹일 뿌리(RankInfo). 비면 배지의 부모를 쓴다.")]
    [SerializeField] RectTransform kickRoot;

    [Header("정적 · 흡수")]
    [Tooltip("마지막 별이 켜지고 나서 아무 일도 일어나지 않는 시간. **이 빈 박이 다음 걸 만든다 — 줄이지 마라.**")]
    [SerializeField] float silence = 0.3f;

    [Tooltip("별의 고스트가 배지로 빨려드는 시간(가속). 이 끝이 곧 모든 사건이 겹치는 한 프레임이다.")]
    [SerializeField] float suckDuration = 0.25f;

    [Header("충격 — 고스트가 배지에 닿는 한 프레임")]
    [Tooltip("배지가 부푸는 정도(1 기준 추가 배율). 0.4면 1.4배까지 튄다.")]
    [SerializeField] float badgePunch = 0.4f;
    [SerializeField] float badgePunchDuration = 0.3f;

    [Tooltip("배지 위에만 터지는 국소 섬광의 세기. 화면 전체를 물들이지 않는다 — 그 자리는 승급 오버레이 몫이다.")]
    [Range(0f, 1f)]
    [SerializeField] float flashAlpha = 0.85f;
    [SerializeField] float flashDuration = 0.24f;

    [Tooltip("뿌리(RankInfo)가 받는 킥. 배지 펀치보다 훨씬 작아야 '배지가 튀었다'로 읽힌다.")]
    [SerializeField] float kickPunch = 0.05f;
    [SerializeField] float kickDuration = 0.25f;

    [Header("광선 점화")]
    [Tooltip("점화 직전 광선의 길이 배율(저작 대비). 여기서 뻗어 나온다.")]
    [SerializeField] float igniteSeedLength = 0.3f;

    [Tooltip("광선이 저작 길이까지 뻗는 시간.")]
    [SerializeField] float igniteDuration = 0.14f;

    [Tooltip("점화 순간 잠깐 과다 노출되는 밝기. 여기서 저작 알파로 정착한다.")]
    [Range(0f, 1f)]
    [SerializeField] float igniteOverAlpha = 1f;

    [Tooltip("과다 노출이 저작 알파로 가라앉는 시간.")]
    [SerializeField] float igniteSettle = 0.2f;

    [Header("문구")]
    [Tooltip("충격 프레임에서 문구가 내려꽂히기까지의 뜸.")]
    [SerializeField] float labelDelay = 0.15f;

    [Tooltip("문구가 출발하는 높이(px, 저작 자리 기준 위쪽).")]
    [SerializeField] float labelDropHeight = 40f;

    [Tooltip("내려꽂히는 시간. 짧게 둔다 — 굴리지 말고 도착하는 순간 이미 찍혀 있어야 한다.")]
    [SerializeField] float labelDrop = 0.14f;

    [Header("상태 유지 루프")]
    [Tooltip("광선 뭉치가 한 바퀴 도는 각도. 부호로 방향이 갈린다.")]
    [SerializeField] float spinDegrees = 360f;

    [Tooltip("한 바퀴에 걸리는 시간. 길수록 '떠 있다'에 가깝고, 짧으면 시선을 잡아먹는다.")]
    [SerializeField] float spinDuration = 20f;

    [Tooltip("배지 호흡의 최대 배율. 1.05를 넘기면 상태가 아니라 사건으로 읽힌다.")]
    [SerializeField] float breatheScale = 1.04f;

    [Tooltip("호흡 한 결의 시간(들숨 = 날숨).")]
    [SerializeField] float breatheDuration = 0.9f;

    RayPose[] m_poses;                  // 광선 판의 저작 자세
    float[]   m_rayAlphas;              // 광선 판의 저작 알파(점화가 정착할 목표)
    Vector2   m_labelPos;               // 문구의 저작 자리
    float     m_labelAlpha;             // 문구의 저작 알파
    bool      m_captured;

    Tween[]       m_spins;              // 무한 회전. 참조를 들고 있다가 Reset에서 걷는다
    Tween         m_breathe;            // 배지 호흡(무한)
    Image         m_flash;              // 배지 국소 섬광(런타임 생성물)
    RectTransform m_kick;               // 킥을 먹인 뿌리. 끊기면 배율이 중간값으로 굳는다
    RectTransform m_badge;              // 진입 연출이 겨눈 배지. 배선이 비었을 때의 폴백
    // 지금 대기 상태인가. 진입 연출이 조립되는 순간부터 참이다 — 뒤이어 도는 평범한 렌더가 그 위를 덮지 않게 하는 빗장.
    bool          m_on;

    /// <summary>진입 연출에서 고스트가 출발하는 시각. 별을 가진 쪽(RankHud)이 같은 시계에 맞춰 태운다.</summary>
    public float SuckAt => Mathf.Max(0f, this.silence);

    /// <summary>고스트가 배지까지 오는 시간. 이 끝이 모든 사건이 겹치는 충격 프레임이다.</summary>
    public float SuckDuration => Mathf.Max(0.05f, this.suckDuration);

    /// <summary>저작 자세·알파를 1회 캡처하고 꺼진 상태로 둔다(저작은 보이게 해 두고 쓰므로 여기서 한 번 걷는다).</summary>
    public void Capture()
    {
        if (this.m_captured) return;
        this.m_captured = true;

        if (this.rays != null)
        {
            this.m_poses     = new RayPose[this.rays.Length];
            this.m_rayAlphas = new float[this.rays.Length];

            for (int t_i = 0; t_i < this.rays.Length; t_i++)
            {
                if (this.rays[t_i] == null) continue;

                this.m_poses[t_i]     = new RayPose(this.rays[t_i].rectTransform);
                this.m_rayAlphas[t_i] = this.rays[t_i].color.a;
            }
        }

        if (this.label != null) this.m_labelPos = this.label.anchoredPosition;
        this.m_labelAlpha = this.labelGroup != null ? this.labelGroup.alpha : 1f;

        this.ApplyOff();
    }

    /// <summary>연출 없이 즉시 상태를 반영한다. 로비 재진입이 지나는 길이다.</summary>
    public void SetStandby(bool _on)
    {
        if (!this.m_captured || this.m_on == _on) return;

        this.m_on = _on;

        if (_on)
        {
            this.ApplyOn();
            this.StartSpin();
            this.StartBreathe();
            return;
        }

        this.KillLoops();
        this.ApplyOff();
    }

    /// <summary>승급전 대기로 넘어가는 진입 안무. 재생·링크는 호출자 몫(프로젝트 규약).
    /// 마지막 별의 점등은 이미 났다 — 여기서 다시 내지 않는다.</summary>
    public Sequence BuildEnter(RectTransform _badge)
    {
        // 조립하는 순간부터 대기 상태다 — 뒤이어 도는 평범한 렌더가 SetStandby로 연출 위를 덮지 않게 한다.
        this.m_on = true;

        if (_badge != null) this.m_badge = _badge;

        RectTransform t_badge = this.Badge;
        var t_seq = DOTween.Sequence();

        // 정적 → 흡수 → 그 끝의 한 프레임에 전부 몰아넣는다. 사건을 시간축에 흩지 않는다.
        float t_impact = this.SuckAt + this.SuckDuration;

        this.StageBadge(t_seq, t_badge, t_impact);
        this.StageRays(t_seq, t_impact);
        this.StageLabel(t_seq, t_impact + Mathf.Max(0f, this.labelDelay));

        // 상태로 넘어가는 자리. 호흡은 펀치가 끝난 뒤여야 같은 배율을 두 트윈이 밀지 않는다.
        t_seq.InsertCallback(t_impact, this.StartSpin);
        t_seq.InsertCallback(t_impact + this.badgePunchDuration, this.StartBreathe);

        return t_seq;
    }

    /// <summary>루프를 걷고 저작 상태로 되돌린다. 어디서 끊겨도 남지 않게.</summary>
    public void Reset()
    {
        this.m_on = false;

        this.KillLoops();
        this.ApplyOff();

        if (this.m_flash != null)
        {
            this.m_flash.DOKill();
            UnityEngine.Object.Destroy(this.m_flash.gameObject);
            this.m_flash = null;
        }

        if (this.m_kick == null) return;

        this.m_kick.DOKill();
        this.m_kick.localScale = Vector3.one;
        this.m_kick = null;
    }

    // 배지 펀치 + 국소 섬광 + 뿌리 킥. 셋 다 같은 시각이라 하나의 타격으로 읽힌다.
    void StageBadge(Sequence _seq, RectTransform _badge, float _at)
    {
        if (_badge == null) return;

        _seq.Insert(_at, _badge.DOPunchScale(Vector3.one * this.badgePunch, this.badgePunchDuration,
                                             vibrato: 2, elasticity: 0.8f));

        var t_flash = this.EnsureFlash(_badge);
        if (t_flash != null)
        {
            // setImmediately: false — 지금 흰 판이 켜지면 정적이어야 할 앞 구간이 밝아진다.
            _seq.Insert(_at, t_flash.DOColor(new Color(1f, 1f, 1f, 0f), this.flashDuration)
                                    .From(new Color(1f, 1f, 1f, this.flashAlpha), setImmediately: false)
                                    .SetEase(Ease.OutQuad));
        }

        this.m_kick = this.kickRoot != null ? this.kickRoot : _badge.parent as RectTransform;
        if (this.m_kick == null) return;

        _seq.Insert(_at, this.m_kick.DOPunchScale(Vector3.one * this.kickPunch, this.kickDuration,
                                                  vibrato: 2, elasticity: 0.8f));
    }

    // 씨앗 길이에서 저작 길이까지 뻗으며 과다 노출됐다가 저작 알파로 정착한다.
    void StageRays(Sequence _seq, float _at)
    {
        if (this.rays == null || this.m_poses == null) return;

        float t_flare = Mathf.Max(0.05f, this.igniteDuration) * 0.35f;

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            Graphic t_ray = this.rays[t_i];
            if (t_ray == null) continue;

            RectTransform t_rt   = t_ray.rectTransform;
            RayPose       t_pose = this.m_poses[t_i];
            float         t_lit  = this.m_rayAlphas[t_i];
            Vector3       t_seed = Vector3.Scale(t_pose.Scale, new Vector3(1f, this.igniteSeedLength, 1f));

            _seq.InsertCallback(_at, () =>
            {
                t_pose.ApplyTo(t_rt);
                t_rt.localScale = t_seed;
                SetAlpha(t_ray, 0f);
            });

            _seq.Insert(_at, t_rt.DOScale(t_pose.Scale, Mathf.Max(0.05f, this.igniteDuration)).SetEase(Ease.OutCubic));
            _seq.Insert(_at, t_ray.DOFade(this.igniteOverAlpha, t_flare).SetEase(Ease.OutQuad));
            _seq.Insert(_at + t_flare, t_ray.DOFade(t_lit, Mathf.Max(0.05f, this.igniteSettle)).SetEase(Ease.InQuad));
        }
    }

    // 위에서 내려꽂히며 알파 in. 굴리지 않는다 — 도착하는 순간 이미 찍혀 있어야 한다.
    void StageLabel(Sequence _seq, float _at)
    {
        if (this.label == null) return;

        float t_drop = Mathf.Max(0.05f, this.labelDrop);

        _seq.Insert(_at, this.label.DOAnchorPos(this.m_labelPos, t_drop)
                                   .From(this.m_labelPos + Vector2.up * this.labelDropHeight, setImmediately: false)
                                   .SetEase(Ease.OutQuad));

        if (this.labelGroup == null) return;

        _seq.Insert(_at, this.labelGroup.DOFade(this.m_labelAlpha, t_drop * 0.6f)
                                        .From(0f, setImmediately: false));
    }

    // 저작 상태 그대로 켜 둔다(진입 연출이 끝난 자리와 같은 그림이라, 렌더가 덮어도 튀지 않는다).
    void ApplyOn()
    {
        this.RestoreRays(_lit: true);
        this.RestoreLabel(_visible: true);
    }

    void ApplyOff()
    {
        this.RestoreRays(_lit: false);
        this.RestoreLabel(_visible: false);

        var t_badge = this.Badge;
        if (t_badge == null) return;

        t_badge.DOKill();
        t_badge.localScale = Vector3.one;
    }

    void RestoreRays(bool _lit)
    {
        if (this.rays == null || this.m_poses == null) return;

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            if (this.rays[t_i] == null) continue;

            this.rays[t_i].DOKill();
            this.m_poses[t_i].ApplyTo(this.rays[t_i].rectTransform);
            SetAlpha(this.rays[t_i], _lit ? this.m_rayAlphas[t_i] : 0f);
        }
    }

    void RestoreLabel(bool _visible)
    {
        if (this.label != null)
        {
            this.label.DOKill();
            this.label.anchoredPosition = this.m_labelPos;
        }

        if (this.labelGroup == null) return;

        this.labelGroup.DOKill();
        this.labelGroup.alpha = _visible ? this.m_labelAlpha : 0f;
    }

    // 광선 뭉치가 천천히 돈다. 저작 각도에서 절대값으로 목표를 잡는다 — 상대 회전은 반복할수록 밀린다.
    void StartSpin()
    {
        if (this.rays == null || this.m_poses == null) return;

        this.KillSpins();
        this.m_spins = new Tween[this.rays.Length];

        float t_dur = Mathf.Max(0.1f, this.spinDuration);

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            Graphic t_ray = this.rays[t_i];
            if (t_ray == null) continue;

            Vector3 t_to = this.m_poses[t_i].Rotation.eulerAngles + new Vector3(0f, 0f, this.spinDegrees);

            this.m_spins[t_i] = t_ray.rectTransform
                                     .DOLocalRotate(t_to, t_dur, RotateMode.FastBeyond360)
                                     .SetEase(Ease.Linear)
                                     .SetLoops(-1, LoopType.Restart)
                                     .SetLink(t_ray.gameObject);
        }
    }

    // 배지 호흡. 상태라서 끝이 없다 — 참조를 들고 있다가 Reset에서 걷는다.
    void StartBreathe()
    {
        var t_badge = this.Badge;
        if (t_badge == null) return;

        this.KillBreathe();

        t_badge.localScale = Vector3.one;
        this.m_breathe = t_badge.DOScale(this.breatheScale, Mathf.Max(0.1f, this.breatheDuration))
                                .SetEase(Ease.InOutSine)
                                .SetLoops(-1, LoopType.Yoyo)
                                .SetLink(t_badge.gameObject);
    }

    void KillLoops()
    {
        this.KillSpins();
        this.KillBreathe();
    }

    void KillSpins()
    {
        if (this.m_spins == null) return;

        for (int t_i = 0; t_i < this.m_spins.Length; t_i++)
        {
            if (this.m_spins[t_i] != null) this.m_spins[t_i].Kill();
        }

        this.m_spins = null;
    }

    void KillBreathe()
    {
        if (this.m_breathe == null) return;

        this.m_breathe.Kill();
        this.m_breathe = null;

        var t_badge = this.Badge;
        if (t_badge != null) t_badge.localScale = Vector3.one;
    }

    // 배지 위에 정확히 겹치는 흰 복제본. 배지 스프라이트는 승급으로 갈리므로 부를 때마다 맞춰 준다.
    Image EnsureFlash(RectTransform _badge)
    {
        var t_source = _badge.GetComponent<Image>();
        if (t_source == null) return null;

        if (this.m_flash == null)
        {
            var t_go = new GameObject("PromoStandbyFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            var t_rt = (RectTransform)t_go.transform;
            t_rt.SetParent(_badge, false);
            t_rt.SetAsLastSibling();
            t_rt.anchorMin = Vector2.zero;
            t_rt.anchorMax = Vector2.one;
            t_rt.offsetMin = Vector2.zero;
            t_rt.offsetMax = Vector2.zero;

            this.m_flash = t_go.GetComponent<Image>();
            this.m_flash.raycastTarget = false;   // 연출 조각이 탭 터치를 가로채지 않게.
        }

        this.m_flash.sprite         = t_source.sprite;
        this.m_flash.type           = t_source.type;
        this.m_flash.preserveAspect = t_source.preserveAspect;
        this.m_flash.color          = new Color(1f, 1f, 1f, 0f);

        return this.m_flash;
    }

    // 호흡시킬 배지. 배선이 정본이고, 미배선이면 진입 연출이 넘겨준 것으로 버틴다.
    RectTransform Badge => this.badge != null ? this.badge : this.m_badge;

    static void SetAlpha(Graphic _g, float _a)
    {
        if (_g == null) return;

        Color t_c = _g.color;
        t_c.a    = _a;
        _g.color = t_c;
    }

    // 광선 판 하나의 저작 자세. 세 배열로 흩어 두면 인덱스가 어긋날 때 조용히 틀어진다(CardEvolveRays와 같은 규약).
    readonly struct RayPose
    {
        public readonly Vector2    Anchored;
        public readonly Vector3    Scale;
        public readonly Quaternion Rotation;

        public RayPose(RectTransform _rt)
        {
            this.Anchored = _rt.anchoredPosition;
            this.Scale    = _rt.localScale;
            this.Rotation = _rt.localRotation;
        }

        public void ApplyTo(RectTransform _rt)
        {
            _rt.anchoredPosition = this.Anchored;
            _rt.localScale       = this.Scale;
            _rt.localRotation    = this.Rotation;
        }
    }
}
