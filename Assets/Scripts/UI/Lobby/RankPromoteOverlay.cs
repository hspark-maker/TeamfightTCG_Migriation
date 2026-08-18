using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 등급 승급(승급전 승리)을 화면 전체를 멈춰 세우는 **한 개의 사건**으로 세우는 오버레이.
//
// 배지 안에서 여덟 단계로 흩어져 벌어지던 것을 여기로 옮긴다 — 약한 사건 여덟 개보다 강한 사건 하나가 크다.
// 그래서 안무의 모든 축(섬광·링·광선·킥·배지)이 **같은 한 프레임**에 겹친다. 시간축에 흩지 말 것.
//
// 암전 → 정적 → 꽂힘 → 충격 → 여운의 순서이고, 정적(silence)이 이 안무의 절반이다.
// 어두운 화면에 아무것도 없는 그 빈 박이 다음 프레임을 만든다.
//
// 판때기는 전부 프리팹 저작이고 코드는 알파·배율·회전만 민다(RankPromoStandby·CardEvolveRays와 같은 규약).
// 씬에 저작하지 않고 Addressables 타입 색인에서 독립 Canvas로 세운다(UnlockIntroOverlay와 같은 규약).
public class RankPromoteOverlay : SingletonOverlay<RankPromoteOverlay>
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [Tooltip("화면 어디를 눌러도 받는 투명 버튼. 안무 도중에 눌리면 건너뛰고 곧바로 닫힌다.")]
    [SerializeField] Button tapButton;

    [Tooltip("암전 위에 서는 내용 전체(배지·광선·링·등급명)를 묶는 그룹.\n" +
             "암전만 먼저 깔려야 정적이 성립하므로 여기 알파는 0에서 출발해 배지가 꽂히는 프레임에 켜진다.")]
    [SerializeField] CanvasGroup contentGroup;

    [Tooltip("충격 프레임에 킥을 먹일 뿌리. 미배선이면 contentGroup 노드를 쓴다.")]
    [SerializeField] RectTransform kickRoot;

    [Tooltip("도달한 등급의 배지. 이 노드가 2.2배에서 제자리로 꽂힌다.")]
    [SerializeField] Image badgeImage;

    [Tooltip("도달한 등급 이름. **배지가 꽂히기 전에 이미 찍혀 있다** — 굴리거나 뒤늦게 페이드인시키지 않는다.")]
    [SerializeField] TMP_Text tierNameText;

    [Tooltip("충격 프레임에 퍼져 사라지는 버스트 링 한 장. 비면 링 축을 통째로 건너뛴다.\n" +
             "배지보다 앞선 형제여야 배지를 가리지 않는다(uGUI는 나중 형제를 위에 그린다).")]
    [SerializeField] Graphic burstRing;

    [Tooltip("배지 뒤에서 뻗는 광선 판들. 비면 광선 축을 통째로 건너뛴다.\n" +
             "\n" +
             "저작 규약 세 가지 (어기면 광선으로 안 읽힌다 — RankPromoStandby와 같다):\n" +
             "  · 피벗은 (0.5, 0) — 뿌리가 배지 중심이고 거기서 바깥으로 뻗는다.\n" +
             "  · 폭은 길이의 0.12 안팎. 0.2를 넘기면 광선이 아니라 꽃잎이 된다.\n" +
             "  · 각도를 등간격으로 두지 말 것 — 나란하면 바람개비로 읽힌다.\n" +
             "배지보다 **앞선 형제**여야 배지를 가리지 않는다.")]
    [SerializeField] Graphic[] rays;

    [Tooltip("\"탭하여 계속\" 안내 그룹. 안무가 다 끝난 뒤에만 뜬다 — 처음부터 보이면 사건을 안 보고 넘긴다.")]
    [SerializeField] CanvasGroup hintGroup;

    [Header("연출")]
    [Tooltip("암전 자체. openDuration이 곧 급암전 시간이다 — 0.1을 넘기면 '멈춰 세웠다'가 아니라 '어두워진다'로 읽힌다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("암전이 끝나고 배지가 꽂히기까지, 어두운 화면에 **아무것도 없는** 시간.\n" +
             "**이 빈 박이 다음 걸 만든다 — 줄이지 마라.**")]
    [SerializeField] float silence = 0.17f;

    [Tooltip("배지가 출발하는 배율. 화면 밖에서 날아드는 크기라 2를 밑돌면 '꽂혔다'가 안 읽힌다.")]
    [SerializeField] float slamFromScale = 2.2f;

    [Tooltip("배지가 제자리에 꽂히는 시간(가속). 짧게 둔다 — 이 끝이 모든 사건이 겹치는 한 프레임이다.")]
    [SerializeField] float slamDuration = 0.08f;

    [Header("충격 — 배지가 꽂히는 한 프레임")]
    [Tooltip("화면 전체를 덮는 섬광 한 벌.\n" +
             "**burstSprite를 반드시 채울 것**(후보: Sprites/CardPack/Glow_Radial) — 배경이 어두운 자리라 " +
             "빛이 들어가야 살고, 비면 단색 흰 판만 지나간다.")]
    [SerializeField] ScreenFlashCover flash = new ScreenFlashCover();

    [Tooltip("링이 출발하는 배율(저작 크기 대비).")]
    [SerializeField] float ringFromScale = 0.2f;
    [Tooltip("링이 퍼져 나가 사라지는 배율(저작 크기 대비). 1보다 커야 '터졌다'로 읽힌다.")]
    [SerializeField] float ringToScale = 1.7f;
    [Tooltip("링이 퍼지며 사라지는 시간.")]
    [SerializeField] float ringDuration = 0.4f;

    [Tooltip("화면이 받는 킥(내용 뿌리 펀치). 작게 둔다 — 크면 배지가 아니라 화면이 주인공이 된다.")]
    [SerializeField] float kickPunch = 0.06f;
    [SerializeField] float kickDuration = 0.3f;

    [Tooltip("배지가 눌렸다 돌아오는 정도. 부풀리는 것이 아니라 눌리는 것이라 부호가 반대다.")]
    [SerializeField] float badgeSquash = 0.12f;
    [SerializeField] float badgeSquashDuration = 0.26f;

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

    [Header("여운")]
    [Tooltip("광선 뭉치가 한 바퀴 도는 각도. 부호로 방향이 갈린다.")]
    [SerializeField] float spinDegrees = 360f;
    [Tooltip("한 바퀴에 걸리는 시간. 길수록 '떠 있다'에 가깝고, 짧으면 시선을 잡아먹는다.")]
    [SerializeField] float spinDuration = 20f;
    [Tooltip("배지 호흡의 최대 배율. 1.05를 넘기면 여운이 아니라 새 사건으로 읽힌다.")]
    [SerializeField] float breatheScale = 1.04f;
    [Tooltip("호흡 한 결의 시간(들숨 = 날숨).")]
    [SerializeField] float breatheDuration = 0.9f;

    [Header("안내")]
    [Tooltip("충격 프레임부터 \"탭하여 계속\"이 뜨기까지의 뜸. 여운을 보는 시간이다.")]
    [SerializeField] float hintDelay = 0.87f;
    [SerializeField] float hintFade = 0.2f;

    // 진행 중 안무. 건너뛰기·닫기가 도중에 와도 저작 상태로 되돌린 뒤 이어가야 한다.
    Sequence m_choreo;

    // 닫힘 콜백. 한 번 쓰면 비워 연타를 막는다.
    Action m_onClose;

    // 무한 루프 트윈(광선 회전·배지 호흡). 참조를 들고 있다가 정리에서 걷는다.
    Tween[] m_spins;
    Tween   m_breathe;

    // 저작 상태 1회 캡처. 안무가 미는 값의 원본이자 점화가 정착할 목표다.
    Vector3[] m_rayScales;
    Vector3[] m_rayEulers;
    float[]   m_rayAlphas;
    Vector3   m_ringScale = Vector3.one;
    float     m_ringAlpha = 1f;
    float     m_hintAlpha = 1f;
    bool      m_captured;

    /// <summary>승급 오버레이를 얻는다. 평소 꺼져 있는 노드라 이미 선 것을 찾을 때는 비활성까지 뒤진다
    /// (UnlockIntroOverlay와 같은 규약).</summary>
    public static bool TryGet(out RankPromoteOverlay _overlay)
        => TryGetOrCreate(RuntimeOverlayPrefabs.Get<RankPromoteOverlay>, out _overlay);

    /// <summary>도달한 등급 _to를 전면에 세우고 탭을 기다린다.
    /// _onCovered는 암전이 완전히 덮인 프레임에 정확히 한 번 온다 — 로비 표시를 갈아끼우는 자리다
    /// (PackPurchaseImpact.Play의 덮임 통지와 같은 규약). 건너뛰기로 안무가 잘려도 반드시 한 번은 온다.
    /// _onClose는 걷힌 뒤 정확히 한 번 온다.
    /// 안무 도중이라도 탭 한 번이면 최종 상태로 점프한 뒤 닫힌다 — 두 번 봐야 하는 화면이 아니다.</summary>
    public void Show(RankTier _to, Action _onCovered, Action _onClose)
    {
        // 직전 표시의 안무를 걷는다 — 시퀀스에 중첩된 트윈은 대상의 DOKill이 잡지 못해 새 안무와 같은 노드를 함께 민다.
        KillChoreo();
        Capture();

        this.m_onClose = _onClose;

        if (this.badgeImage != null && _to.Badge != null) this.badgeImage.sprite = _to.Badge;
        if (this.tierNameText != null) this.tierNameText.text = _to.DisplayName;

        if (this.tapButton != null)
        {
            this.tapButton.onClick.RemoveAllListeners();   // 재표시마다 중복 등록 방지
            this.tapButton.onClick.AddListener(OnTapped);
        }

        IsOpen = true;
        SetVisible(true);

        // 손은 처음부터 열어 둔다 — 이 화면의 유일한 문이라, 안무가 어디서 끊겨도 잠긴 모달로 남지 않는다.
        SetInputEnabled(true);

        // 덮임 통지는 정확히 1회. 시간축과 중단 안전망 양쪽에서 부르므로 여기서 잠근다 —
        // 건너뛰기로 안무가 잘려도 이것이 빠지면 로비가 옛 등급에 고착된다.
        bool t_fired = false;
        void Fire()
        {
            if (t_fired) return;
            t_fired = true;
            _onCovered?.Invoke();
        }

        this.m_choreo = BuildChoreography(Fire);
        this.m_choreo.OnKill(Fire);   // 정상 종료든 중단이든 여기로 온다.
        this.m_choreo.Play();
    }

    /// <summary>밖에서 걷는다(화면이 통째로 넘어가는 경로). 콜백은 흘리지 않는다 —
    /// 이 길로 닫는 쪽은 이미 자기 흐름을 쥐고 있다.</summary>
    public void Hide()
    {
        this.m_onClose = null;

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        KillChoreo();
        ResetChoreography();
        SetVisible(false);

        if (t_wasOpen) RaiseClosed();
    }

    // Show를 거치지 않고 뜨는 경로(부모가 다시 켜짐)에서도 문이 잠기지 않게 열어 둔다.
    void OnEnable()
    {
        SetInputEnabled(true);
    }

    // 오버레이는 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리를 여기서 위임한다.
    void OnDisable()
    {
        this.transition.HandleDisabled(ResolveTarget());
        KillChoreo();
        ResetChoreography();

        // 꺼진 화면은 떠 있는 것이 아니다. Hide를 거치지 않고 꺼지는 경로(부모 비활성·씬 언로드)에서
        // 이 플래그가 남으면 "로비 표면이 보이는가" 판정이 영영 false가 된다.
        IsOpen = false;
    }

    // 탭 한 번이 곧 끝이다. 안무 중이면 최종 상태로 점프한 뒤 닫는다.
    void OnTapped()
    {
        // 콜백을 먼저 비워 연타로 두 번 흐르는 경로를 막는다.
        var t_callback = this.m_onClose;
        this.m_onClose = null;
        if (t_callback == null) return;

        SetInputEnabled(false);

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        KillChoreo();
        ResetChoreography();
        SetVisible(false);

        if (t_wasOpen) RaiseClosed();

        // 넘겨주기는 정리가 다 끝난 뒤다 — 받는 쪽이 이 화면의 상태를 다시 물어볼 수 있어야 한다.
        t_callback.Invoke();
    }

    // 암전 → 정적 → 꽂힘 → 충격 → 여운 → 안내. 충격 축은 전부 t_impact 한 시각에 몰려 있다.
    Sequence BuildChoreography(TweenCallback _onCovered)
    {
        PrimeChoreography();

        var t_seq = DOTween.Sequence().SetLink(gameObject);

        // 덮인 시각은 암전 자신이 낸다 — 이 값을 따로 재면 아직 비치는 화면 위에서 로비 표시가 갈린다.
        float t_covered = Mathf.Max(0f, this.transition.OpenDuration);
        float t_slamAt  = t_covered + Mathf.Max(0f, this.silence);
        float t_slam    = Mathf.Max(0.02f, this.slamDuration);
        float t_impact  = t_slamAt + t_slam;

        // 암전이 완전히 덮인 프레임. 뒤가 정적 구간이라 여기서 갈아끼우는 것은 보이지 않는다.
        t_seq.InsertCallback(t_covered, _onCovered);

        RectTransform t_badge = this.badgeImage != null ? this.badgeImage.rectTransform : null;

        // 내용은 배지가 날아드는 프레임에 통째로 켠다 — 등급명이 배지보다 늦게 뜨면 사건이 둘로 갈린다.
        if (this.contentGroup != null)
            t_seq.InsertCallback(t_slamAt, () => this.contentGroup.alpha = 1f);

        // 가속으로 꽂힌다. OutBack으로 되튀기면 '내려앉았다'가 되어 타격이 죽는다.
        if (t_badge != null)
            t_seq.Insert(t_slamAt, t_badge.DOScale(1f, t_slam).SetEase(Ease.InQuad));

        StageImpact(t_seq, t_impact, t_badge);
        StageRays(t_seq, t_impact);
        StageAfterglow(t_seq, t_impact);
        StageHint(t_seq, t_impact + Mathf.Max(0f, this.hintDelay));

        t_seq.OnComplete(() => this.m_choreo = null);
        return t_seq;
    }

    // 섬광 · 링 · 킥 · 배지 눌림. 넷이 같은 시각이라 하나의 타격으로 읽힌다.
    void StageImpact(Sequence _seq, float _at, RectTransform _badge)
    {
        if (ScreenFlash.TryGet(out ScreenFlash t_flash))
        {
            Sequence t_cover = t_flash.BuildCover(this.flash);
            if (t_cover != null) _seq.Insert(_at, t_cover);
        }

        if (this.burstRing != null)
        {
            RectTransform t_ring = this.burstRing.rectTransform;
            float         t_dur  = Mathf.Max(0.05f, this.ringDuration);

            _seq.InsertCallback(_at, () =>
            {
                t_ring.localScale = this.m_ringScale * this.ringFromScale;
                SetAlpha(this.burstRing, this.m_ringAlpha);
            });

            _seq.Insert(_at, t_ring.DOScale(this.m_ringScale * this.ringToScale, t_dur).SetEase(Ease.OutQuad));
            _seq.Insert(_at, this.burstRing.DOFade(0f, t_dur).SetEase(Ease.OutQuad));
        }

        RectTransform t_kick = ResolveKickRoot();
        if (t_kick != null)
            _seq.InsertCallback(_at, () => UiPunch.Play(t_kick, this.kickPunch, this.kickDuration));

        if (_badge == null) return;

        // 부호가 음수라 부푸는 것이 아니라 눌린다.
        _seq.Insert(_at, _badge.DOPunchScale(Vector3.one * -this.badgeSquash, this.badgeSquashDuration,
                                             vibrato: 2, elasticity: 0.8f));
    }

    // 씨앗 길이에서 저작 길이까지 뻗으며 과다 노출됐다가 저작 알파로 정착한다(RankPromoStandby와 같은 문법).
    void StageRays(Sequence _seq, float _at)
    {
        if (this.rays == null || this.m_rayScales == null) return;

        float t_flare = Mathf.Max(0.05f, this.igniteDuration) * 0.35f;

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            Graphic t_ray = this.rays[t_i];
            if (t_ray == null) continue;

            RectTransform t_rt   = t_ray.rectTransform;
            Vector3       t_lit  = this.m_rayScales[t_i];
            Vector3       t_seed = Vector3.Scale(t_lit, new Vector3(1f, this.igniteSeedLength, 1f));
            float         t_a    = this.m_rayAlphas[t_i];

            _seq.InsertCallback(_at, () =>
            {
                t_rt.localScale = t_seed;
                SetAlpha(t_ray, 0f);
            });

            _seq.Insert(_at, t_rt.DOScale(t_lit, Mathf.Max(0.05f, this.igniteDuration)).SetEase(Ease.OutCubic));
            _seq.Insert(_at, t_ray.DOFade(this.igniteOverAlpha, t_flare).SetEase(Ease.OutQuad));
            _seq.Insert(_at + t_flare, t_ray.DOFade(t_a, Mathf.Max(0.05f, this.igniteSettle)).SetEase(Ease.InQuad));
        }
    }

    // 여운으로 넘어가는 자리. 호흡은 눌림이 끝난 뒤여야 같은 배율을 두 트윈이 밀지 않는다.
    void StageAfterglow(Sequence _seq, float _at)
    {
        _seq.InsertCallback(_at, StartSpin);
        _seq.InsertCallback(_at + Mathf.Max(0f, this.badgeSquashDuration), StartBreathe);
    }

    void StageHint(Sequence _seq, float _at)
    {
        if (this.hintGroup == null) return;

        _seq.Insert(_at, this.hintGroup.DOFade(this.m_hintAlpha, Mathf.Max(0.01f, this.hintFade)));
    }

    // 안무 직전 상태. 암전 말고는 아무것도 화면에 없어야 한다.
    void PrimeChoreography()
    {
        KillLoops();

        if (this.contentGroup != null) this.contentGroup.alpha = 0f;

        if (this.badgeImage != null)
        {
            RectTransform t_badge = this.badgeImage.rectTransform;
            t_badge.DOKill();
            t_badge.localScale = Vector3.one * Mathf.Max(0.01f, this.slamFromScale);
        }

        if (this.burstRing != null)
        {
            this.burstRing.DOKill();
            this.burstRing.rectTransform.localScale = this.m_ringScale;
            SetAlpha(this.burstRing, 0f);
        }

        if (this.rays != null && this.m_rayScales != null)
            for (int t_i = 0; t_i < this.rays.Length; t_i++)
            {
                if (this.rays[t_i] == null) continue;

                this.rays[t_i].DOKill();
                ApplyRayPose(t_i);
                SetAlpha(this.rays[t_i], 0f);
            }

        if (this.hintGroup != null)
        {
            this.hintGroup.DOKill();
            this.hintGroup.alpha = 0f;
        }

        RectTransform t_kick = ResolveKickRoot();
        if (t_kick == null) return;

        t_kick.DOKill();
        t_kick.localScale = Vector3.one;
    }

    // 최종 상태(= 저작 상태)로 점프한다. 건너뛰기·닫기·비활성 어디로 나가도 중간값이 남지 않게.
    void ResetChoreography()
    {
        KillLoops();

        if (this.contentGroup != null) this.contentGroup.alpha = 1f;

        if (this.badgeImage != null)
        {
            RectTransform t_badge = this.badgeImage.rectTransform;
            t_badge.DOKill();
            t_badge.localScale = Vector3.one;
        }

        // 링은 이미 터져 사라진 뒤가 최종 상태다 — 저작 알파로 되돌리면 정지한 링이 화면에 남는다.
        if (this.burstRing != null)
        {
            this.burstRing.DOKill();
            this.burstRing.rectTransform.localScale = this.m_ringScale;
            SetAlpha(this.burstRing, 0f);
        }

        if (this.rays != null && this.m_rayScales != null)
            for (int t_i = 0; t_i < this.rays.Length; t_i++)
            {
                if (this.rays[t_i] == null) continue;

                this.rays[t_i].DOKill();
                ApplyRayPose(t_i);
                SetAlpha(this.rays[t_i], this.m_rayAlphas[t_i]);
            }

        if (this.hintGroup != null)
        {
            this.hintGroup.DOKill();
            this.hintGroup.alpha = this.m_hintAlpha;
        }

        RectTransform t_kick = ResolveKickRoot();
        if (t_kick == null) return;

        t_kick.DOKill();
        t_kick.localScale = Vector3.one;
    }

    // 저작 자세·알파를 1회 캡처한다. 첫 Show가 값을 밀기 전이어야 원본이 잡힌다.
    void Capture()
    {
        if (this.m_captured) return;
        this.m_captured = true;

        int t_count = this.rays != null ? this.rays.Length : 0;

        this.m_rayScales = new Vector3[t_count];
        this.m_rayEulers = new Vector3[t_count];
        this.m_rayAlphas = new float[t_count];

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            if (this.rays[t_i] == null) continue;

            RectTransform t_rt = this.rays[t_i].rectTransform;

            this.m_rayScales[t_i] = t_rt.localScale;
            this.m_rayEulers[t_i] = t_rt.localEulerAngles;
            this.m_rayAlphas[t_i] = this.rays[t_i].color.a;
        }

        if (this.burstRing != null)
        {
            this.m_ringScale = this.burstRing.rectTransform.localScale;
            this.m_ringAlpha = this.burstRing.color.a;
        }

        if (this.hintGroup != null) this.m_hintAlpha = this.hintGroup.alpha;
    }

    // 광선 뭉치가 천천히 돈다. 저작 각도에서 절대값으로 목표를 잡는다 — 상대 회전은 반복할수록 밀린다.
    void StartSpin()
    {
        if (this.rays == null || this.m_rayEulers == null) return;

        KillSpins();
        this.m_spins = new Tween[this.rays.Length];

        float t_dur = Mathf.Max(0.1f, this.spinDuration);

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            Graphic t_ray = this.rays[t_i];
            if (t_ray == null) continue;

            Vector3 t_to = this.m_rayEulers[t_i] + new Vector3(0f, 0f, this.spinDegrees);

            this.m_spins[t_i] = t_ray.rectTransform
                                     .DOLocalRotate(t_to, t_dur, RotateMode.FastBeyond360)
                                     .SetEase(Ease.Linear)
                                     .SetLoops(-1, LoopType.Restart)
                                     .SetLink(t_ray.gameObject);
        }
    }

    // 배지 호흡. 끝이 없으므로 참조를 들고 있다가 정리에서 걷는다.
    void StartBreathe()
    {
        if (this.badgeImage == null) return;

        KillBreathe();

        RectTransform t_badge = this.badgeImage.rectTransform;

        t_badge.localScale = Vector3.one;
        this.m_breathe = t_badge.DOScale(this.breatheScale, Mathf.Max(0.1f, this.breatheDuration))
                                .SetEase(Ease.InOutSine)
                                .SetLoops(-1, LoopType.Yoyo)
                                .SetLink(t_badge.gameObject);
    }

    void KillChoreo()
    {
        if (this.m_choreo != null && this.m_choreo.IsActive()) this.m_choreo.Kill();
        this.m_choreo = null;
    }

    void KillLoops()
    {
        KillSpins();
        KillBreathe();
    }

    void KillSpins()
    {
        if (this.m_spins == null) return;

        for (int t_i = 0; t_i < this.m_spins.Length; t_i++)
            if (this.m_spins[t_i] != null) this.m_spins[t_i].Kill();

        this.m_spins = null;
    }

    void KillBreathe()
    {
        if (this.m_breathe == null) return;

        this.m_breathe.Kill();
        this.m_breathe = null;
    }

    void ApplyRayPose(int _index)
    {
        RectTransform t_rt = this.rays[_index].rectTransform;

        t_rt.localScale       = this.m_rayScales[_index];
        t_rt.localEulerAngles = this.m_rayEulers[_index];
    }

    void SetInputEnabled(bool _enabled)
    {
        if (this.tapButton != null) this.tapButton.interactable = _enabled;
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : gameObject;

    RectTransform ResolveKickRoot()
        => this.kickRoot != null
            ? this.kickRoot
            : this.contentGroup != null ? this.contentGroup.transform as RectTransform : null;

    static void SetAlpha(Graphic _g, float _a)
    {
        if (_g == null) return;

        Color t_c = _g.color;
        t_c.a    = _a;
        _g.color = t_c;
    }
}
