using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 랭크 보상 오버레이의 등장·퇴장 안무. MonoBehaviour가 아니라 뷰가 필드로 소유한다
// (PopupTransition·ScreenDimTint와 같은 계열 — 씬 저작 뷰는 베이스 클래스로 묶기 어렵다).
//
// ⚠ 모든 필드에 C# 이니셜라이저로 기본값을 준다. 기존 프리팹 YAML에는 이 필드들이 아직 없어 역직렬화가
//   건드리지 않고, 그래서 이니셜라이저 값이 그대로 살아난다 — "아무것도 배선하지 않아도 도는 값"이 여기 적힌 값이다.
[System.Serializable]
public class RewardRevealFx
{
    [Header("빛")]
    [Tooltip("방사광 묶음(RayGlow/RayBurst의 부모). 미배선이면 빛 축을 통째로 건너뛴다.")]
    [SerializeField] RectTransform rayRoot;

    [Tooltip("빗살. 알파는 CanvasGroup이 아니라 색으로 다룬다 — 팝업 루트의 CanvasGroup과 곱해져 등장 구간이 흐려진다.")]
    [SerializeField] Graphic rayBurst;
    [Tooltip("빗살 아래 깔리는 훈김.")]
    [SerializeField] Graphic rayGlow;

    [Range(0f, 1f)] [SerializeField] float burstAlpha = 1f;
    [Range(0f, 1f)] [SerializeField] float glowAlpha  = 0.75f;

    [Tooltip("빛이 시작하는 배율. 1에 가까울수록 '이미 열려 있던 빛'으로 읽힌다.")]
    [Min(0f)] [SerializeField] float rayStartScale   = 0.85f;
    [Min(0f)] [SerializeField] float rayGrowDuration = 0.22f;
    [Min(0f)] [SerializeField] float rayFadeDuration = 0.12f;

    [Tooltip("한 바퀴 도는 데 걸리는 초. 짧으면 '바람개비'가 된다 — 20초 아래로 내리지 말 것. 0이면 회전 없음.")]
    [Min(0f)] [SerializeField] float raySpinPeriod = 24f;

    [Tooltip("열리면서 목표보다 더 밀려나는 양. 되돌아오는 이 한 번이 충격파를 대신한다 — 0이면 그냥 커지기만 한다.")]
    [Min(0f)] [SerializeField] float rayPunch = 0.15f;
    [Tooltip("밀려난 빛이 제자리로 돌아오는 시간.")]
    [Min(0f)] [SerializeField] float rayPunchSettle = 0.18f;

    [Header("팝콘")]
    [Tooltip("파편이 뿌려질 레이어. 아이콘보다 '위'에 그려지는 노드여야 터진 것으로 읽힌다. " +
             "미배선이면 이 축을 통째로 건너뛴다.")]
    [SerializeField] RectTransform confettiRoot;

    [Tooltip("조각 그림. 비면 파편 축을 통째로 건너뛴다. 조각마다 돌아가며 쓰므로 " +
             "모양이 섞일수록 '색종이'가 되고, 한 종류만 넣으면 같은 그림 여러 장으로 읽힌다. " +
             "에셋 후보: Sprites/CardPack의 Shine_Star, Glow_Radial.")]
    [SerializeField] Sprite[] confettiSprites;

    [Tooltip("조각 색. 스프라이트와 따로 돌아간다(개수가 서로 달라야 같은 조합이 반복되지 않는다). " +
             "비우면 흰색.")]
    [SerializeField] Color[] confettiColors =
    {
        new Color(1f, 0.86f, 0.36f),   // 금
        new Color(1f, 1f, 1f),         // 흰
        new Color(0.62f, 0.86f, 1f),   // 하늘
    };

    [Min(0)] [SerializeField] int   confettiCount = 24;
    [Min(1f)] [SerializeField] float confettiSize = 56f;
    [Tooltip("사방으로 벌어지는 거리(px).")]
    [Min(0f)] [SerializeField] float confettiSpread = 380f;
    [Tooltip("솟은 자리에서 더 떨어지는 거리(px). 화면 밖까지 떨어뜨려야 '치우지 않아도' 사라진 것으로 읽힌다.")]
    [Min(0f)] [SerializeField] float confettiDrop = 420f;
    [Tooltip("조각별 출발이 어긋나는 최대 폭(초). 0이면 전부 동시에 튄다 — 폭발이지 팝콘이 아니다.")]
    [Min(0f)] [SerializeField] float confettiStagger = 0.12f;
    [Tooltip("파편이 터지는 시각. 아이콘이 착지하는 프레임에 맞춰야 '부딪혀서 튀었다'로 읽힌다.")]
    [Min(0f)] [SerializeField] float confettiAt = 0.1f;

    [Tooltip("사방으로 솟는 데 걸리는 시간.")]
    [Min(0f)] [SerializeField] float confettiRise = 0.34f;
    [Tooltip("떨어지는 데 걸리는 시간. 조각은 이 구간에서 지워진다 — 짧으면 화면 밖에 닿기 전에 사라진다.")]
    [Min(0f)] [SerializeField] float confettiFall = 0.55f;
    [Tooltip("생겨나며 커지는 시간.")]
    [Min(0f)] [SerializeField] float confettiPop = 0.12f;
    [Tooltip("나는 동안 도는 각도. 0이면 회전 없음.")]
    [SerializeField] float confettiSpin = 220f;

    [Tooltip("등장 순간 딤이 한 번 밝아지는 정도. 화면이 반응해야 혼자 받는 느낌이 안 든다. 0이면 없음.")]
    [Range(0f, 1f)] [SerializeField] float introDimPunch = 0.25f;

    [Header("보상")]
    [Tooltip("보상 칸을 담은 가로 레이아웃. 개수에 따라 배율만 바뀐다.")]
    [SerializeField] RectTransform rewardRow;

    [Tooltip("보상이 1종일 때의 배율.")]
    [Min(0.1f)] [SerializeField] float soloScale = 1f;
    [Tooltip("2종일 때의 배율. 1로 두면 행이 화면 폭의 80%를 넘겨 '나열'로 읽힌다.")]
    [Min(0.1f)] [SerializeField] float duoScale = 0.72f;
    [Tooltip("3종 이상일 때의 배율(앨범 전체 완성이 여기 걸린다). 칸이 늘수록 더 줄여야 폭이 유지된다.")]
    [Min(0.1f)] [SerializeField] float trioScale = 0.6f;

    [Tooltip("아이콘·수량이 튀어나오는 시각(초).")]
    [Min(0f)] [SerializeField] float punchAt = 0.04f;

    [Tooltip("아이콘이 부풀어 있는 정도. 이 배율은 t=0에 즉시 적용되고 트윈은 회복만 한다 — " +
             "눈이 봐야 하는 것은 부풀어 오르는 과정이 아니라 이미 맞은 뒤의 회복이다.")]
    [Min(0f)] [SerializeField] float iconOvershoot = 0.35f;
    [Min(0f)] [SerializeField] float iconDuration  = 0.24f;

    [Min(0f)] [SerializeField] float amountOvershoot = 0.35f;
    [Min(0f)] [SerializeField] float amountDuration  = 0.22f;

    [Header("문구·버튼")]
    [SerializeField] RectTransform titleRect;
    [Min(0f)] [SerializeField] float titleAt       = 0.06f;
    [Min(0f)] [SerializeField] float titleDuration = 0.12f;

    [Tooltip("[획득] 버튼. 저작 위치를 한 번만 캡처해 원위치로 삼으므로 레이아웃 그룹 아래에 두지 말 것 — " +
             "리빌드 전 좌표가 원위치로 굳어 매 등장마다 그 자리로 스냅한다.")]
    [SerializeField] RectTransform buttonRect;
    [Min(0f)] [SerializeField] float buttonAt       = 0.06f;
    [Min(0f)] [SerializeField] float buttonDuration = 0.16f;
    [Tooltip("버튼이 아래에서 밀려 올라오는 거리(px). 화면 크기에서 계산하지 않는다 — 첫 프레임엔 rect가 0이다.")]
    [SerializeField] float buttonRise = 40f;

    [Header("퇴장(획득)")]
    [Tooltip("빨려들기 직전 아이콘이 한 번 부푸는 배율.")]
    [Min(0f)] [SerializeField] float launchScale = 1.2f;
    [Tooltip("부푸는 데 걸리는 초. 코인 분출이 시작되는 시각이기도 하다 — 0으로 두면 예전처럼 클릭 즉시 코인이 나간다.")]
    [Min(0f)] [SerializeField] float launchRise = 0.06f;
    [Min(0f)] [SerializeField] float launchDuration = 0.12f;

    [Tooltip("빛이 밀려나며 사라지는 배율.")]
    [Min(0f)] [SerializeField] float rayExitScale = 1.5f;

    [Tooltip("획득 순간 딤이 밝아지는 정도. 알파는 그대로고 색만 밀린다.")]
    [Range(0f, 1f)] [SerializeField] float dimPunch = 0.4f;

    // 무한 회전. 시퀀스에 넣으면 시퀀스가 영영 끝나지 않으므로 따로 돌린다.
    Tween m_spin;

    // 이번 등장이 세운 조각. 안무가 잘려도 허공에 굳지 않게 Reset이 걷는다(CoinBurstEffect.ClearCoins와 같은 규칙).
    readonly List<GameObject> m_confetti = new List<GameObject>();

    // 마지막으로 안무한 칸. 퇴장·원복이 같은 대상을 집어야 한다.
    IReadOnlyList<CurrencyRewardSlotView> m_slots;

    // 지연 확보·저작값 캡처. 중간값을 기준으로 잡으면 반복할수록 밀린다.
    CanvasGroup m_titleGroup;
    CanvasGroup m_buttonGroup;
    Vector2     m_buttonHome;
    bool        m_homeCaptured;

    /// <summary>아이콘이 빨려들기 시작하는 시각 — 코인 분출을 여기에 맞춰야 "아이콘이 코인으로 분해됐다"로 읽힌다.</summary>
    public float LaunchAt => this.launchRise;

    /// <summary>등장이 끝나는 시각 — 입력을 다시 여는 쪽이 값을 또 들고 있지 않게.</summary>
    public float IntroDuration
    {
        get
        {
            // 파편은 여기에 들어가지 않는다 — 조각이 다 떨어질 때까지 손을 막으면 1초 가까이 기다리게 된다.
            float t_end = this.rayGrowDuration + this.rayPunchSettle;
            t_end = Mathf.Max(t_end, this.punchAt + this.iconDuration);
            t_end = Mathf.Max(t_end, this.punchAt + this.amountDuration);
            t_end = Mathf.Max(t_end, this.titleAt + this.titleDuration);
            t_end = Mathf.Max(t_end, this.buttonAt + this.buttonDuration);
            return t_end;
        }
    }

    /// <summary>보상 개수에 맞춘 배율. 칸 정렬은 레이아웃 그룹이 이미 가운데로 모아 준다.</summary>
    public void ApplyCount(int _count)
    {
        if (this.rewardRow == null) return;

        float t_scale = _count <= 1 ? this.soloScale
                      : _count == 2 ? this.duoScale
                                    : this.trioScale;

        this.rewardRow.localScale = Vector3.one * t_scale;
    }

    /// <summary>
    /// 등장 안무를 만들어 돌려준다(재생은 호출자). 켜져 있는 칸만 안무한다.
    /// 딤을 함께 받는 이유는 퇴장과 같다 — 화면이 한 번 반응해야 혼자 받는 그림이 안 된다.
    /// </summary>
    public Sequence BuildIntro(IReadOnlyList<CurrencyRewardSlotView> _slots, ScreenDimTint _dim)
    {
        this.m_slots = _slots;
        this.CaptureHome();

        var t_seq = DOTween.Sequence();

        this.StageRays(t_seq);
        this.StageSlots(t_seq);
        this.StageConfetti(t_seq);
        this.StageChrome(t_seq);

        if (_dim != null && this.introDimPunch > 0f) InsertDimPunch(t_seq, _dim, this.introDimPunch);

        return t_seq;
    }

    // 밝아졌다 평상으로. 퇴장의 dimPunch와 같은 모양이지만 이쪽이 먼저 온다.
    static void InsertDimPunch(Sequence _seq, ScreenDimTint _dim, float _level)
    {
        _seq.Insert(0f, _dim.TweenLevel(_level, 0.06f).SetEase(Ease.OutQuad));
        _seq.Insert(0.06f, _dim.TweenLevel(0f, 0.28f).SetEase(Ease.OutQuad));
    }

    /// <summary>퇴장 안무를 호출자의 마스터 시퀀스에 얹는다 — 코인 분출과 같은 시간축에 놓여야 한다.</summary>
    public void BuildOutro(Sequence _seq, ScreenDimTint _dim)
    {
        if (_seq == null) return;

        this.KillSpin();

        // 눌린 반응은 버튼 자신이 낸다 — 화면이 걷히기 전에 손끝의 결과가 먼저 보여야 한다.
        if (this.buttonRect != null)
            _seq.InsertCallback(0f, () => UiPunch.Play(this.buttonRect, 0.18f, 0.18f));

        if (_dim != null)
        {
            _seq.Insert(0f, _dim.TweenLevel(this.dimPunch, 0.08f).SetEase(Ease.OutQuad));
            _seq.Insert(0.08f, _dim.TweenLevel(0f, 0.3f).SetEase(Ease.OutQuad));
        }

        // 빛은 밀려나며 걷힌다.
        if (this.rayRoot != null)
            _seq.Insert(this.launchRise, this.rayRoot.DOScale(this.rayExitScale, 0.2f).SetEase(Ease.OutQuad));

        this.InsertFadeOut(_seq, this.rayBurst, this.launchRise, 0.16f);
        this.InsertFadeOut(_seq, this.rayGlow,  this.launchRise, 0.16f);

        // 아이콘은 한 번 부풀었다 빨려든다 — 잠깐 뒤로 물리는 InBack이 분출의 수렴과 같은 결이다.
        if (this.m_slots != null)
        {
            for (int t_i = 0; t_i < this.m_slots.Count; t_i++)
            {
                if (!IsLive(this.m_slots[t_i])) continue;

                var t_icon   = this.m_slots[t_i].Icon;
                var t_amount = this.m_slots[t_i].Amount;

                if (t_icon != null)
                {
                    _seq.Insert(0f, t_icon.transform.DOScale(this.launchScale, this.launchRise).SetEase(Ease.OutQuad));
                    _seq.Insert(this.launchRise,
                                t_icon.transform.DOScale(0f, this.launchDuration).SetEase(Ease.InBack));
                    this.InsertFadeOut(_seq, t_icon, this.launchRise, this.launchDuration);
                }

                if (t_amount != null)
                {
                    _seq.Insert(this.launchRise,
                                t_amount.transform.DOScale(0f, this.launchDuration).SetEase(Ease.InBack));
                    this.InsertFadeOut(_seq, t_amount, this.launchRise, this.launchDuration);
                }
            }
        }

        if (this.m_titleGroup != null)
            _seq.Insert(this.launchRise, this.m_titleGroup.DOFade(0f, 0.1f).SetEase(Ease.InQuad));

        if (this.m_buttonGroup != null)
            _seq.Insert(this.launchRise, this.m_buttonGroup.DOFade(0f, 0.1f).SetEase(Ease.InQuad));
    }

    /// <summary>모든 축을 저작 상태로 되돌린다. 안무가 잘려도 중간값으로 굳지 않게.</summary>
    public void Reset()
    {
        this.KillSpin();
        this.ClearConfetti();

        if (this.rayRoot != null)
        {
            this.rayRoot.DOKill();
            this.rayRoot.localScale    = Vector3.one;
            this.rayRoot.localRotation = Quaternion.identity;
        }

        RestoreAlpha(this.rayBurst, this.burstAlpha);
        RestoreAlpha(this.rayGlow,  this.glowAlpha);

        if (this.m_slots != null)
        {
            for (int t_i = 0; t_i < this.m_slots.Count; t_i++)
            {
                if (this.m_slots[t_i] == null) continue;

                RestoreSlotGraphic(this.m_slots[t_i].Icon);
                RestoreSlotGraphic(this.m_slots[t_i].Amount);
            }
        }

        if (this.m_titleGroup != null)
        {
            this.m_titleGroup.DOKill();
            this.m_titleGroup.alpha = 1f;
        }

        if (this.m_buttonGroup != null)
        {
            this.m_buttonGroup.DOKill();
            this.m_buttonGroup.alpha = 1f;
        }

        if (this.buttonRect != null)
        {
            this.buttonRect.DOKill();
            this.buttonRect.localScale = Vector3.one;   // 퇴장 펀치가 배율을 남긴 채 끊길 수 있다
            if (this.m_homeCaptured) this.buttonRect.anchoredPosition = this.m_buttonHome;
        }
    }

    void StageRays(Sequence _seq)
    {
        if (this.rayRoot == null) return;

        this.KillSpin();

        this.rayRoot.DOKill();
        this.rayRoot.localScale    = Vector3.one * this.rayStartScale;
        this.rayRoot.localRotation = Quaternion.identity;   // 직전 표시가 남긴 각도에서 이어 돌지 않게

        // 목표보다 더 밀고 나갔다 돌아온다. 이 왕복이 충격파 링을 대신한다 —
        // UI로 쓸 수 있는 링 스프라이트가 프로젝트에 없다(파티클 텍스처는 Image가 못 쓴다).
        _seq.Insert(0f, this.rayRoot.DOScale(1f + this.rayPunch, this.rayGrowDuration).SetEase(Ease.OutQuad));
        if (this.rayPunch > 0f)
            _seq.Insert(this.rayGrowDuration,
                        this.rayRoot.DOScale(1f, this.rayPunchSettle).SetEase(Ease.OutQuad));

        this.InsertFadeIn(_seq, this.rayBurst, this.burstAlpha, this.rayFadeDuration);
        this.InsertFadeIn(_seq, this.rayGlow,  this.glowAlpha,  this.rayFadeDuration);

        if (this.raySpinPeriod <= 0f) return;

        // 상주 회전이라 시퀀스 밖에서 돈다 — SetActive(false)로도 멈추지 않으므로 Reset이 반드시 걷는다.
        this.m_spin = this.rayRoot.DOLocalRotate(new Vector3(0f, 0f, -360f), this.raySpinPeriod,
                                                 RotateMode.FastBeyond360)
                                  .SetEase(Ease.Linear)
                                  .SetLoops(-1, LoopType.Restart)
                                  .SetLink(this.rayRoot.gameObject);
    }

    void StageSlots(Sequence _seq)
    {
        if (this.m_slots == null) return;

        for (int t_i = 0; t_i < this.m_slots.Count; t_i++)
        {
            if (!IsLive(this.m_slots[t_i])) continue;

            this.StagePunch(_seq, this.m_slots[t_i].Icon,   this.iconOvershoot,   this.iconDuration);
            this.StagePunch(_seq, this.m_slots[t_i].Amount, this.amountOvershoot, this.amountDuration);
        }
    }

    // 아이콘이 착지하는 프레임에 파편이 튄다. 시간이 어긋나면 '터져서 튀었다'가 아니라 '따로 논다'가 된다.
    void StageConfetti(Sequence _seq)
    {
        this.ClearConfetti();

        if (this.confettiRoot == null || this.confettiCount <= 0) return;
        if (this.confettiSprites == null || this.confettiSprites.Length == 0) return;

        var t_settings = new UiConfettiBurst.Settings(this.confettiCount, this.confettiSpread,
                                                      this.confettiRise, this.confettiDrop, this.confettiFall,
                                                      this.confettiPop, this.confettiStagger, this.confettiSpin);

        // 터짐의 중심은 아이콘이 서는 자리다. 파편 레이어와 보상 행은 부모가 달라 좌표를 옮겨야 한다.
        Vector2 t_from = this.rewardRow != null
                       ? UiGainBurst.ToLayerLocal(this.confettiRoot, this.rewardRow)
                       : Vector2.zero;

        _seq.Insert(this.confettiAt,
                    UiConfettiBurst.Build(this.confettiRoot, t_from, in t_settings,
                                          _spawn: _i => (RectTransform)this.CreatePiece(_i).transform,
                                          _despawn: _rt => { if (_rt != null) _rt.gameObject.SetActive(false); }));
    }

    // ⚠ 가산 합성을 걸지 않는다. 가산은 알파가 RGB에 곱해지지 않아 DOFade로 지워지지 않는다 —
    //   조각이 낙하 끝에서 툭 사라진다. 빛(RayFill)만 가산으로 두고 파편은 평범하게 겹친다.
    GameObject CreatePiece(int _index)
    {
        var t_go = new GameObject("Confetti", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ((RectTransform)t_go.transform).sizeDelta = Vector2.one * this.confettiSize;

        // 모양과 색을 각자의 길이로 돌린다 — 같은 주기로 돌리면 "별은 늘 금색"처럼 조합이 굳는다.
        var t_img = t_go.GetComponent<Image>();
        t_img.sprite         = this.confettiSprites[_index % this.confettiSprites.Length];
        t_img.preserveAspect = true;
        t_img.raycastTarget  = false;   // 파편이 [획득] 터치를 가로채지 않게.
        t_img.color          = this.confettiColors == null || this.confettiColors.Length == 0
                             ? Color.white
                             : this.confettiColors[_index % this.confettiColors.Length];

        this.m_confetti.Add(t_go);
        return t_go;
    }

    void ClearConfetti()
    {
        for (int t_i = 0; t_i < this.m_confetti.Count; t_i++)
        {
            if (this.m_confetti[t_i] == null) continue;

            this.m_confetti[t_i].transform.DOKill();
            Object.Destroy(this.m_confetti[t_i]);
        }

        this.m_confetti.Clear();
    }

    // 충격은 t=0에 전부 들어간다. 커지는 구간을 트윈에 맡기면 그 시간만큼 타격이 뭉개져 "톡 부풀었다"로 읽힌다.
    void StagePunch(Sequence _seq, Graphic _target, float _overshoot, float _duration)
    {
        if (_target == null) return;

        RestoreSlotGraphic(_target);

        var t_tr = _target.transform;
        t_tr.localScale = Vector3.one * (1f + _overshoot);

        _seq.Insert(this.punchAt, t_tr.DOScale(1f, _duration).SetEase(Ease.OutQuint));
    }

    void StageChrome(Sequence _seq)
    {
        this.m_titleGroup = ResolveGroup(this.titleRect, this.m_titleGroup);
        if (this.m_titleGroup != null)
        {
            this.m_titleGroup.DOKill();
            this.m_titleGroup.alpha = 0f;
            _seq.Insert(this.titleAt, this.m_titleGroup.DOFade(1f, this.titleDuration).SetEase(Ease.OutCubic));
        }

        this.m_buttonGroup = ResolveGroup(this.buttonRect, this.m_buttonGroup);
        if (this.m_buttonGroup != null)
        {
            this.m_buttonGroup.DOKill();
            this.m_buttonGroup.alpha = 0f;
            _seq.Insert(this.buttonAt, this.m_buttonGroup.DOFade(1f, this.buttonDuration));
        }

        if (this.buttonRect == null || !this.m_homeCaptured) return;

        this.buttonRect.DOKill();
        this.buttonRect.localScale       = Vector3.one;   // 직전 퇴장의 펀치가 배율을 남긴 채 끊겼을 수 있다
        this.buttonRect.anchoredPosition = this.m_buttonHome - new Vector2(0f, this.buttonRise);
        _seq.Insert(this.buttonAt,
                    this.buttonRect.DOAnchorPos(this.m_buttonHome, this.buttonDuration).SetEase(Ease.OutBack));
    }

    void InsertFadeIn(Sequence _seq, Graphic _target, float _to, float _duration)
    {
        if (_target == null) return;

        _target.DOKill();
        SetAlpha(_target, 0f);
        _seq.Insert(0f, _target.DOFade(_to, _duration).SetEase(Ease.OutQuad));
    }

    void InsertFadeOut(Sequence _seq, Graphic _target, float _at, float _duration)
    {
        if (_target == null) return;

        _seq.Insert(_at, _target.DOFade(0f, _duration).SetEase(Ease.InQuad));
    }

    // 저작 위치는 한 번만 잡는다 — 이미 밀린 값을 다시 캡처하면 열 때마다 버튼이 아래로 내려앉는다.
    void CaptureHome()
    {
        if (this.m_homeCaptured || this.buttonRect == null) return;

        this.m_homeCaptured = true;
        this.m_buttonHome   = this.buttonRect.anchoredPosition;
    }

    void KillSpin()
    {
        this.m_spin?.Kill();
        this.m_spin = null;
    }

    // root를 배선하지 않은 칸도 Bind는 아이콘·수량을 채운다 — 안무에서만 빠지면 그 칸이 멈춰 보인다.
    static bool IsLive(CurrencyRewardSlotView _slot)
        => _slot != null && (_slot.Root == null ? _slot.Icon != null : _slot.Root.activeSelf);

    static CanvasGroup ResolveGroup(RectTransform _target, CanvasGroup _cached)
    {
        if (_cached != null || _target == null) return _cached;

        var t_group = _target.GetComponent<CanvasGroup>();
        return t_group != null ? t_group : _target.gameObject.AddComponent<CanvasGroup>();
    }

    // 안무가 배율을 건드리지 않은 축(광선)은 알파만 되돌린다 — 배율까지 밀면 저작한 크기가 파괴된다.
    static void RestoreAlpha(Graphic _target, float _alpha)
    {
        if (_target == null) return;

        _target.DOKill();
        SetAlpha(_target, _alpha);
    }

    static void RestoreSlotGraphic(Graphic _target)
    {
        if (_target == null) return;

        _target.DOKill();
        _target.transform.DOKill();
        _target.transform.localScale = Vector3.one;
        SetAlpha(_target, 1f);
    }

    static void SetAlpha(Graphic _target, float _alpha)
    {
        var t_c = _target.color;
        t_c.a = _alpha;
        _target.color = t_c;
    }
}
