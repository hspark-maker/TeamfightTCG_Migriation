using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 매칭 화면의 안무(대기 → 발견 → 대치). MonoBehaviour가 아니라 셸이 필드로 소유한다
// (ScreenDimTint·RewardRevealFx와 같은 계열).
//
// ⚠ 오브젝트 참조를 뺀 모든 필드에 C# 이니셜라이저로 기본값을 준다. 기존 프리팹 YAML에는 이 필드들이 아직 없어
//   역직렬화가 건드리지 않고, 그래서 이니셜라이저 값이 그대로 살아난다 — 배선 없이도 도는 값이 여기 적힌 값이다.
[Serializable]
public class MatchmakingFx
{
    [Header("대기(스캔)")]
    [Tooltip("빈 상대 틀을 훑는 띠의 그림. 비우면 스캔 축을 통째로 건너뛴다 — 대기가 점(...)만 남는다.\n" +
             "  에셋 후보: Assets/Sprites/CardPack/Glow_Radial.")]
    [SerializeField] Sprite scanSprite;

    [Tooltip("띠의 색. 상대 자리가 아직 '비어 있다'로 읽혀야 하므로 차가운 쪽이 맞는다.")]
    [SerializeField] Color scanColor = new Color(0.55f, 0.85f, 1f, 1f);

    [Tooltip("한 번 훑는 데 걸리는 초. 0.6 아래로 내리면 훑는 게 아니라 깜빡이는 것으로 읽힌다.")]
    [Min(0.1f)] [SerializeField] float scanPeriod = 0.9f;

    [Tooltip("띠의 두께(px). 틀 높이의 절반쯤이 적당하다 — 얇으면 선이 되고 두꺼우면 그냥 밝아진다.")]
    [Min(1f)] [SerializeField] float scanThickness = 110f;

    [Range(0f, 1f)] [SerializeField] float scanAlpha = 0.5f;

    [Header("발견(슬램)")]
    [Tooltip("상대 카드가 꽂히기 전 배율. 이 배율은 t=0에 즉시 적용되고 트윈은 회복만 한다 — " +
             "눈이 봐야 하는 것은 커지는 과정이 아니라 이미 큰 것이 내려꽂히는 순간이다.")]
    [Min(1f)] [SerializeField] float slamScale = 1.3f;

    [Tooltip("꽂히기 전 떠 있는 높이(px).")]
    [SerializeField] float slamRise = 64f;

    [Tooltip("내려꽂히는 시간. 0.1을 넘기면 '내려온다'가 되어 타격이 사라진다.")]
    [Min(0.01f)] [SerializeField] float slamDuration = 0.08f;

    [Tooltip("꽂히는 프레임에 화면 전체가 받는 킥(배율). 0이면 화면이 반응하지 않는다.")]
    [Min(0f)] [SerializeField] float rootKick = 0.05f;

    [Tooltip("꽂히는 순간 딤이 진해지는 정도(-1 가장 어둡게 ~ 0 평상). 어두워졌다 돌아오는 이 왕복이 충격을 대신한다.")]
    [Range(-1f, 0f)] [SerializeField] float foundDimPunch = -0.55f;

    [Tooltip("이름·랭크가 순서대로 들어오는 간격(초). 0이면 둘이 동시에 뜬다 — 읽는 순서가 사라진다.")]
    [Min(0f)] [SerializeField] float infoStagger = 0.05f;

    [Tooltip("이름·랭크가 옆에서 밀려오는 거리(px).")]
    [SerializeField] float infoSlide = 36f;

    [Tooltip("꽂히는 프레임의 화면 섬광. 색을 흰색으로 두면 매칭 화면이 하얗게 날아간다 — 어두운 화면엔 색을 얹는다.")]
    [SerializeField] ScreenFlashCover foundFlash = new ScreenFlashCover
    {
        rise = 0.03f, hold = 0.01f, fall = 0.3f, peak = 0.42f,
        color = new Color(0.55f, 0.78f, 1f, 1f),
        burstColor = new Color(0.8f, 0.92f, 1f, 1f),
        burstStartScale = 0.2f, burstEndScale = 1.4f, burstFall = 0.4f,
    };

    [Header("대치(충돌)")]
    [Tooltip("부딪히기 전 뒤로 물러나는 거리(px). 이 예비동작이 없으면 두 카드가 그냥 가까워질 뿐이다.")]
    [Min(0f)] [SerializeField] float windUpDistance = 26f;

    [Min(0.01f)] [SerializeField] float windUpDuration = 0.1f;

    [Tooltip("부딪히는 데 걸리는 시간. 물러나는 시간보다 반드시 짧아야 '때렸다'로 읽힌다.")]
    [Min(0.01f)] [SerializeField] float impactDuration = 0.08f;

    [Min(0.01f)] [SerializeField] float settleDuration = 0.26f;

    [Tooltip("VS가 튀어나오기 전 각도(도). 충돌의 결과로 튀어나온 것처럼 보이게 하는 축이다.")]
    [SerializeField] float vsSpin = -14f;

    [Min(0f)] [SerializeField] float vsOvershoot = 0.4f;
    [Min(0.01f)] [SerializeField] float vsPopDuration = 0.14f;

    [Range(-1f, 0f)] [SerializeField] float versusDimPunch = -0.35f;

    [Tooltip("충돌 프레임의 섬광. 발견보다 옅어야 한다 — 같은 세기면 두 사건이 한 덩어리로 뭉친다.")]
    [SerializeField] ScreenFlashCover versusFlash = new ScreenFlashCover
    {
        rise = 0.02f, hold = 0f, fall = 0.22f, peak = 0.3f,
        color = new Color(1f, 0.92f, 0.72f, 1f),
        burstColor = new Color(1f, 0.9f, 0.65f, 1f),
        burstStartScale = 0.3f, burstEndScale = 1.5f, burstFall = 0.32f,
    };

    [Header("딤")]
    [Tooltip("화면을 덮은 어둠(Dimed). 미배선이면 딤 축이 통째로 무시된다 — 전환이 걷어낼 대상도 여기서 온다.")]
    [SerializeField] ScreenDimTint dim = new ScreenDimTint();

    // 스캔은 상대를 구할 때까지 도는 상주 트윈이라 시퀀스 밖에서 돈다 — StopScan이 반드시 걷는다.
    Tween  m_scanTween;
    Image  m_scanBand;

    /// <summary>전환(MatchHandoffFx)이 이어서 걷어낼 딤. 진실원을 둘로 만들지 않으려 여기서 빌려준다.</summary>
    public ScreenDimTint Dim => this.dim;

    /// <summary>발견 안무가 끝나는 시각 — 셸의 뜸(foundHold)이 이보다 짧으면 대치가 안무를 잘라먹는다.</summary>
    public float FoundDuration => this.slamDuration + this.infoStagger * 2f + 0.2f;

    /// <summary>대치 안무가 끝나는 시각.</summary>
    public float VersusDuration => this.windUpDuration + this.impactDuration + this.settleDuration;

    public void Capture()
    {
        this.dim.Capture();
    }

    /// <summary>빈 상대 틀을 훑기 시작한다. 대기 시간의 주인은 매치메이커라 길이를 모르므로 무한 반복이다.</summary>
    public void StartScan(RectTransform _frame)
    {
        this.StopScan();

        if (_frame == null || this.scanSprite == null) return;

        // 틀 안쪽 마스크에 붙여야 띠가 둥근 테두리를 넘지 않는다.
        var t_mask   = _frame.GetComponentInChildren<Mask>();
        var t_parent = t_mask != null ? (RectTransform)t_mask.transform : _frame;

        var t_go = new GameObject("ScanBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;

        t_rt.SetParent(t_parent, false);
        t_rt.anchorMin = new Vector2(0f, 0.5f);
        t_rt.anchorMax = new Vector2(1f, 0.5f);
        t_rt.pivot     = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta = new Vector2(0f, this.scanThickness);

        this.m_scanBand = t_go.GetComponent<Image>();
        this.m_scanBand.sprite         = this.scanSprite;
        this.m_scanBand.raycastTarget  = false;
        this.m_scanBand.preserveAspect = false;
        this.m_scanBand.color          = new Color(this.scanColor.r, this.scanColor.g, this.scanColor.b, 0f);

        float t_travel = Mathf.Max(_frame.rect.height, this.scanThickness * 2f) * 0.5f + this.scanThickness * 0.5f;
        float t_fade   = this.scanPeriod * 0.25f;

        // 출발 자리를 트윈 생성 전에 박는다 — DOTween이 그 값을 시작점으로 잡아 반복마다 위에서 다시 내려온다.
        t_rt.anchoredPosition = new Vector2(0f, t_travel);

        var t_seq = DOTween.Sequence().SetLink(t_go);
        t_seq.Append(t_rt.DOAnchorPosY(-t_travel, this.scanPeriod).SetEase(Ease.InOutSine));
        t_seq.Insert(0f, this.m_scanBand.DOFade(this.scanAlpha, t_fade).SetEase(Ease.OutQuad));
        t_seq.Insert(this.scanPeriod - t_fade, this.m_scanBand.DOFade(0f, t_fade).SetEase(Ease.InQuad));

        this.m_scanTween = t_seq.SetLoops(-1, LoopType.Restart);
    }

    public void StopScan()
    {
        this.m_scanTween?.Kill();
        this.m_scanTween = null;

        if (this.m_scanBand != null) UnityEngine.Object.Destroy(this.m_scanBand.gameObject);
        this.m_scanBand = null;
    }

    /// <summary>
    /// 상대가 꽂히는 안무를 만들어 돌려준다(재생은 호출자).
    /// 배율·위치는 t=0에 즉시 밀어 넣고 트윈은 회복만 한다 — 부풀어 오르는 과정을 보여주면 타격이 뭉개진다.
    /// </summary>
    public Sequence BuildFound(MatchProfileView _opponent, RectTransform _root)
    {
        var t_seq = DOTween.Sequence();

        if (_opponent == null) return t_seq;

        var t_card = _opponent.Rect;
        var t_home = t_card.anchoredPosition;

        t_card.DOKill();
        t_card.localScale       = Vector3.one * this.slamScale;
        t_card.anchoredPosition = t_home + new Vector2(0f, this.slamRise);

        t_seq.Insert(0f, t_card.DOScale(1f, this.slamDuration).SetEase(Ease.InQuad));
        t_seq.Insert(0f, t_card.DOAnchorPos(t_home, this.slamDuration).SetEase(Ease.InQuad));

        // 꽂히는 프레임에 전부 몰아 넣는다 — 시간축에 흩으면 약한 사건 넷이 되고, 겹치면 하나의 큰 사건이 된다.
        float t_hit = this.slamDuration;

        if (this.rootKick > 0f && _root != null)
            t_seq.InsertCallback(t_hit, () => UiPunch.Play(_root, this.rootKick, 0.24f));

        this.InsertDimPunch(t_seq, t_hit, this.foundDimPunch);
        this.InsertFlash(t_seq, t_hit, this.foundFlash);

        this.InsertInfo(t_seq, t_hit, _opponent.NicknameText, 0);
        this.InsertInfo(t_seq, t_hit, _opponent.RankNameText, 1);

        return t_seq;
    }

    /// <summary>
    /// 두 카드가 물러났다 부딪히고 VS가 튀어나오는 안무. 미는 방향은 호출자가 준 걸음(_step)이 정한다 —
    /// 어느 쪽이 위인지 이 클래스는 몰라도 된다.
    /// </summary>
    public Sequence BuildVersus(RectTransform _my,  Vector2 _myHome,
                                RectTransform _opp, Vector2 _oppHome,
                                Vector2 _step, RectTransform _vs)
    {
        var t_seq = DOTween.Sequence();

        this.StageClash(t_seq, _my,  _myHome,   _step);
        this.StageClash(t_seq, _opp, _oppHome, -_step);

        float t_hit = this.windUpDuration + this.impactDuration;

        this.InsertDimPunch(t_seq, t_hit, this.versusDimPunch);
        this.InsertFlash(t_seq, t_hit, this.versusFlash);

        if (_vs != null)
        {
            _vs.DOKill();
            _vs.localScale    = Vector3.one * (1f + this.vsOvershoot);
            _vs.localRotation = Quaternion.Euler(0f, 0f, this.vsSpin);

            // VS는 충돌의 결과로 튀어나온다 — 켜지는 시각이 부딪히는 프레임과 어긋나면 따로 논다.
            t_seq.Insert(t_hit, _vs.DOScale(1f, this.vsPopDuration).SetEase(Ease.OutBack));
            t_seq.Insert(t_hit, _vs.DOLocalRotate(Vector3.zero, this.vsPopDuration).SetEase(Ease.OutBack));
        }

        return t_seq;
    }

    /// <summary>모든 축을 저작 상태로 되돌린다. 안무가 잘려도 중간값으로 굳지 않게.</summary>
    public void Reset(MatchProfileView _my, MatchProfileView _opponent, RectTransform _root, RectTransform _vs)
    {
        this.StopScan();
        this.dim.Reset();

        RestoreCard(_my);
        RestoreCard(_opponent);

        if (_root != null)
        {
            _root.DOKill();
            _root.localScale = Vector3.one;
        }

        if (_vs == null) return;

        _vs.DOKill();
        _vs.localScale    = Vector3.one;
        _vs.localRotation = Quaternion.identity;
    }

    // 물러났다 부딪히고 제자리로. 홈 좌표는 호출자가 Awake에서 잡아 둔 값이라 반복해도 밀리지 않는다.
    void StageClash(Sequence _seq, RectTransform _rect, Vector2 _home, Vector2 _step)
    {
        if (_rect == null) return;

        _rect.DOKill();
        _rect.anchoredPosition = _home;

        Vector2 t_back = _step.sqrMagnitude > 0.0001f ? -_step.normalized * this.windUpDistance : Vector2.zero;

        _seq.Insert(0f, _rect.DOAnchorPos(_home + t_back, this.windUpDuration).SetEase(Ease.OutQuad));
        _seq.Insert(this.windUpDuration,
                    _rect.DOAnchorPos(_home + _step, this.impactDuration).SetEase(Ease.InQuad));
        _seq.Insert(this.windUpDuration + this.impactDuration,
                    _rect.DOAnchorPos(_home, this.settleDuration).SetEase(Ease.OutBack));
    }

    void InsertDimPunch(Sequence _seq, float _at, float _level)
    {
        if (Mathf.Approximately(_level, 0f)) return;

        _seq.Insert(_at, this.dim.TweenLevel(_level, 0.05f).SetEase(Ease.OutQuad));
        _seq.Insert(_at + 0.05f, this.dim.TweenLevel(0f, 0.3f).SetEase(Ease.OutQuad));
    }

    // 섬광은 자가설치 레이어라 배선할 자리가 없다 — 없으면 이 축만 조용히 빠진다.
    void InsertFlash(Sequence _seq, float _at, ScreenFlashCover _cover)
    {
        if (_cover == null || _cover.peak <= 0f) return;
        if (!ScreenFlash.TryGet(out var t_flash)) return;

        _seq.InsertCallback(_at, () =>
        {
            var t_cover = t_flash.BuildCover(_cover);
            t_cover?.Play();
        });
    }

    void InsertInfo(Sequence _seq, float _at, Graphic _target, int _order)
    {
        if (_target == null) return;

        var t_rt   = (RectTransform)_target.transform;
        var t_home = t_rt.anchoredPosition;

        t_rt.DOKill();
        _target.DOKill();

        t_rt.anchoredPosition = t_home + new Vector2(this.infoSlide, 0f);
        SetAlpha(_target, 0f);

        float t_start = _at + this.infoStagger * _order;

        _seq.Insert(t_start, t_rt.DOAnchorPos(t_home, 0.18f).SetEase(Ease.OutCubic));
        _seq.Insert(t_start, _target.DOFade(1f, 0.14f).SetEase(Ease.OutQuad));
    }

    static void RestoreCard(MatchProfileView _view)
    {
        if (_view == null) return;

        _view.Rect.DOKill();
        _view.Rect.localScale = Vector3.one;

        RestoreInfo(_view.NicknameText);
        RestoreInfo(_view.RankNameText);
    }

    // 좌표는 되돌리지 않는다 — 이 글자들은 셸이 홈을 들고 있지 않아, 되돌릴 기준이 트윈이 잡아 둔 값뿐이다.
    // DOKill(complete) 대신 알파만 세우는 이유도 같다.
    static void RestoreInfo(Graphic _target)
    {
        if (_target == null) return;

        _target.transform.DOKill(complete: true);
        _target.DOKill();
        SetAlpha(_target, 1f);
    }

    static void SetAlpha(Graphic _target, float _alpha)
    {
        var t_c = _target.color;
        t_c.a = _alpha;
        _target.color = t_c;
    }
}
