using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 로비 랭크 표시(배지 = 등급, 핍 = 등급 안 단계, 텍스트 = 티어명).
// 랭크는 전투 씬에서만 변하므로 변경 이벤트 없이 표시 시점에 RankManager를 재조회한다.
// 포인트 수치는 화면에 두지 않는다 — 증감은 배지 반응으로만 알린다(조립은 LobbyRankEffectDirector).
public class RankHud : MonoBehaviour
{
    [SerializeField] Image badgeImage;   // 티어 배지
    [SerializeField] TMP_Text descText;  // 티어 표시명("브론즈 1")

    [Header("단계 핍")]
    [Tooltip("핍을 묶는 노드(RankPips). 언랭크면 통째로 끈다 — 빈 별 네 칸은 '아직 아무것도 아님'을 알리는 데 방해만 된다.\n" +
             "미배선이면 끄지 않고 빈 칸만 그린다.")]
    [SerializeField] GameObject pipsRoot;

    [Tooltip("등급 안 단계를 표시하는 칸(왼쪽부터). 개수는 RankConfig.DivisionsPerGrade와 맞춘다 — 모자라면 그만큼만 그린다.")]
    [SerializeField] Image[] divisionPips;
    [SerializeField] Sprite pipOn;
    [SerializeField] Sprite pipOff;

    [Header("진행 호")]
    [Tooltip("배지를 감싸는 진행 게이지. pipsRoot 아래 별들보다 앞선 형제로 둘 것 — 그래야 별 뒤에 깔리고, 언랭크에서 별 줄과 함께 꺼진다.\n" +
             "미배선이면 호 없이 종전대로 동작한다.")]
    [SerializeField] RankProgressArc arc;

    [Tooltip("조각 하나가 꽂힐 때 호가 다음 눈금까지 가는 시간. 조각 간격보다 짧게 둔다 — 길면 다음 조각이 앞 트윈을 밟는다.")]
    [SerializeField] float arcStepDuration = 0.18f;

    [Header("승급 연출")]
    [Tooltip("연출을 시작하기 전 뜸. 화면이 눈에 들어온 뒤에 별이 켜져야 '변했다'가 보인다.")]
    [SerializeField] float enterDelay = 0.35f;

    [SerializeField] float pipPunch = UiPunch.DEFAULT_SCALE;

    [Tooltip("핍 하나가 켜지고(꺼지고) 다음으로 넘어가는 간격. 펀치 길이(0.3초)보다 짧으면 연출이 잘린다.")]
    [SerializeField] float pipStep = 0.35f;

    [Tooltip("마지막 별이 켜진 뒤의 여운.")]
    [SerializeField] float finishDelay = 0.6f;
    [Tooltip("핍이 다 꺼진 뒤 배지가 갈리기까지의 뜸(강등 전용 — 승급은 배지 안무가 박자를 갖는다).")]
    [SerializeField] float badgeSwapDelay = 0.15f;

    [Tooltip("등급 승급 때 켜져 있던 별이 한꺼번에 꺼지며 움츠러드는 세기.")]
    [SerializeField] float pipBlowOut = 0.1f;

    [Tooltip("등급이 갈리는 순간의 배지 안무(파열 → 강림). 값은 전부 여기 안에 있다.")]
    [SerializeField] RankPromoteEffect promote = new RankPromoteEffect();

    [Header("포인트 획득 반응")]
    [Tooltip("조각이 닿을 때마다 배지가 튀는 세기. 여러 번 겹치므로 작게 준다.")]
    [SerializeField] float gainPunch = 0.12f;
    [Tooltip("마지막 조각이 닿을 때 한 번 크게.")]
    [SerializeField] float gainFinalPunch = 0.3f;
    [SerializeField] Color gainFlashColor = Color.white;
    [SerializeField] float gainFlashDuration = 0.18f;

    [Header("포인트 손실 반응")]
    [SerializeField] float lossDuration = 0.45f;
    [SerializeField] float lossShrink = 0.08f;
    [Tooltip("배지가 잠깐 식는 색. 붉은 경고가 아니라 채도가 빠지는 쪽 — 패배를 두 번 때리지 않는다.")]
    [SerializeField] Color lossColor = new Color(0.6f, 0.6f, 0.7f);

    [Header("강등 연출")]
    [Tooltip("별 하나가 꺼지고 다음으로 넘어가는 간격. 승급(pipStep)보다 짧게 둔다 — 잃는 장면을 길게 끌지 않는다.")]
    [SerializeField] float demotePipStep = 0.22f;

    [Tooltip("꺼지는 별이 움츠러드는 세기. 켜질 때의 펀치와 반대 방향으로 들어간다.")]
    [SerializeField] float demotePipShrink = 0.18f;

    [Tooltip("등급이 갈릴 때만 쓰는 배지 흔들림 세기(px). 별 하나 제거에는 쓰지 않는다 — 강도는 빈도를 따른다.")]
    [SerializeField] float badgeShakeStrength = 18f;
    [SerializeField] float badgeShakeDuration = 0.45f;

    [Tooltip("등급이 갈리는 순간 배지가 잠깐 잠기는 색.")]
    [SerializeField] Color demoteBadgeColor = new Color(0.45f, 0.42f, 0.5f);

    // 활성 인스턴스(연출 호출자가 찾는 창구). 로비에 하나뿐이지만 탭 토글로 꺼지므로 활성분만 든다.
    static RankHud s_instance;

    // 최초 렌더를 Start로 미루기 위한 표식 — RankConfig 주입(DataLibrary.Awake)보다 OnEnable이 먼저 돌 수 있다.
    bool m_started;

    // 진행 중 티어 변화 연출(승급·강등 공용 — 둘이 함께 돌 일이 없고 Render의 연출 가드는 하나여야 한다).
    // 살아있는 동안 Render가 표시를 최종값으로 덮지 않는다(연출이 과거 상태에서 출발한다).
    Sequence m_tierSeq;

    // 포인트 손실 반응. 배지 색·스케일을 함께 잡으므로 획득 플래시와 겹쳐 돌지 않게 따로 든다.
    Sequence m_lossSeq;

    // 마지막 조각이 꽂힐 때의 밝아짐. 손실 반응과 같은 색을 쓴다.
    Tween m_flashTween;

    // 조각 연출이 출발한 비율과 도착 비율. 조각이 꽂힐 때마다 이 사이를 등분해 전진한다.
    float m_gainFrom;
    float m_gainTo;

    // 배지의 저작 색. 연출이 어디서 끊겨도 여기로 되돌린다(티어업이 스프라이트를 갈아도 색은 그대로다).
    Color m_badgeBaseColor = Color.white;

    // 강등 흔들림이 되돌릴 배지 자리. 흔들림이 중간에 끊기면 배지가 어긋난 채 굳는다.
    Vector2 m_badgeBasePos;

    /// <summary>조각이 튀어나올 원점이자 도착지. 연출 디렉터가 쓴다.</summary>
    public RectTransform BadgeRect => this.badgeImage != null ? (RectTransform)this.badgeImage.transform : null;

    /// <summary>활성 랭크 HUD를 얻는다. 꺼져 있으면 false(연출만 건너뛰면 된다).</summary>
    public static bool TryGet(out RankHud _hud)
    {
        _hud = s_instance;
        if (_hud == null)
        {
            s_instance = null;   // 파괴됐는데 OnDisable이 오지 않은 잔재를 걷는다(CurrencyHud.TryGet과 같은 규율).
            return false;
        }

        return true;
    }

    /// <summary>연출을 _points(전투 직전) 시점에서 출발시킨다. 조각이 채워 갈 구간도 여기서 정한다 —
    /// 등급이 갈리는 판이면 도착지는 새 등급의 자리가 아니라 옛 등급의 승급선(1)이다. 그 뒤는 파열이 이어받는다.</summary>
    public void PrepareProgress(long _points)
    {
        var t_prev = RankManager.GetInfoAt(_points);
        var t_now  = RankManager.GetInfo();

        this.m_gainFrom = t_prev.GradeProgress;
        this.m_gainTo   = t_now.Grade != t_prev.Grade ? 1f : t_now.GradeProgress;

        this.SetArc(this.m_gainFrom);
    }

    /// <summary>조각 하나가 배지에 꽂혔다(_arrived = 1부터 세는 누적, _total = 전체).
    /// 마지막 조각이면 크게 튀고 한 번 밝아진다.</summary>
    public void PlayGainImpact(int _arrived, int _total)
    {
        bool t_final = _arrived >= _total;

        UiPunch.Play(this.BadgeRect, t_final ? this.gainFinalPunch : this.gainPunch);

        // 조각이 꽂힌 만큼만 호가 전진한다 — 한 번에 도착시키면 "무엇 때문에 찼는지"가 사라진다.
        if (this.arc != null && _total > 0)
            this.arc.TweenTo(Mathf.Lerp(this.m_gainFrom, this.m_gainTo, (float)_arrived / _total),
                             this.arcStepDuration);

        if (!t_final || this.badgeImage == null) return;

        this.KillFlash();
        this.m_flashTween = this.badgeImage.DOColor(this.gainFlashColor, this.gainFlashDuration * 0.5f)
                                           .SetLoops(2, LoopType.Yoyo)
                                           .SetLink(this.gameObject)
                                           // 스케일은 건드리지 않는다 — 같은 배지에서 도는 UiPunch를 중간에 밟는다.
                                           .OnKill(() => { this.m_flashTween = null; this.RestoreBadgeColor(); });
    }

    /// <summary>
    /// 포인트가 깎인 순간. 배지가 잠깐 식었다 돌아온다 — 패배 팝업에서 이미 본 손실이라 여기서 또 때리지 않는다.
    /// 재생은 호출자 몫(BuildTierUp과 같은 규약).
    /// </summary>
    public Sequence BuildLossReaction()
    {
        this.KillReactions();

        this.m_lossSeq = DOTween.Sequence().SetLink(this.gameObject);

        if (this.badgeImage != null)
            this.m_lossSeq.Join(this.badgeImage.DOColor(this.lossColor, this.lossDuration * 0.5f)
                                               .SetLoops(2, LoopType.Yoyo));

        // 호는 요요로 되돌아오지 않는다 — 줄어든 자리가 곧 지금 값이다(배지만 식었다 돌아온다).
        if (this.arc != null)
        {
            Tween t_arc = this.arc.TweenTo(RankManager.GetInfo().GradeProgress, this.lossDuration);
            if (t_arc != null) this.m_lossSeq.Join(t_arc);
        }

        var t_rect = this.BadgeRect;
        if (t_rect != null)
            this.m_lossSeq.Join(t_rect.DOScale(1f - this.lossShrink, this.lossDuration * 0.5f)
                                      .SetLoops(2, LoopType.Yoyo)
                                      .SetEase(Ease.OutQuad));

        // 어떤 이유로 끊겨도 색·스케일이 중간값에 굳지 않게 한다(스케일을 잡는 유일한 반응이라 여기서만 되돌린다).
        this.m_lossSeq.OnKill(() =>
        {
            this.m_lossSeq = null;
            this.RestoreBadgeColor();

            var t_badge = this.BadgeRect;
            if (t_badge != null) t_badge.localScale = Vector3.one;
        });

        return this.m_lossSeq;
    }

    /// <summary>
    /// 티어가 오른 순간을 그린다. 표시는 이미 최종 티어이므로 _prevTierIndex 상태에서 출발해 지금으로 건너온다.
    /// 재생은 호출자 몫 — 끝난 뒤에 무엇을 이을지는 부르는 쪽이 정한다.
    /// _prevTierIndex가 음수면 첫 진입(언랭크 → 첫 티어)으로 본다.
    /// </summary>
    public Sequence BuildTierUp(int _prevTierIndex)
    {
        this.KillTierChange();

        int t_divisions = RankConfig.DivisionsPerGrade;
        var t_info = RankManager.GetInfo();

        int t_prevDivision = _prevTierIndex < 0 ? 0 : _prevTierIndex % t_divisions + 1;
        int t_prevGrade    = _prevTierIndex < 0 ? -1 : _prevTierIndex / t_divisions;
        bool t_gradeUp     = t_info.TierIndex / t_divisions != t_prevGrade;

        this.m_tierSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 출발점 = 오르기 직전 화면. 되돌리기는 시퀀스가 아니라 조립 시점에 즉시 건다 —
        // 앞 단계(포인트 조각)가 도는 동안 새 핍이 이미 켜져 있으면 "포인트가 차서 별이 켜졌다"는 인과가 깨진다.
        // m_tierSeq 대입 뒤여야 Render의 연출 가드가 이 상태를 최종값으로 덮지 않는다.
        // 첫 진입(_prevTierIndex < 0)의 출발점은 언랭크다 — 핍 줄이 꺼진 채 시작해 첫 칸이 켜질 때 함께 나타난다.
        this.SetPipsVisible(_prevTierIndex >= 0);
        this.RenderPips(t_prevDivision);

        if (t_gradeUp)
        {
            // 첫 진입은 되돌릴 '이전 등급'이 없다 — 언랭크 배지로 되돌려야 그것이 파열하고 첫 등급이 강림한다.
            // 안 되돌리면 이미 떠 있는 새 배지가 터지고 같은 배지가 다시 내려온다.
            if (_prevTierIndex >= 0) this.RenderTier(RankRewardManager.GetInfo(_prevTierIndex));
            else
            {
                RankManager.GetUnrankedDisplay(out string t_unrankedName, out Sprite t_unrankedBadge);
                this.RenderTier(t_unrankedName, t_unrankedBadge);
            }
        }

        // 옛 상태를 한 박자 보여준 뒤에 바꾼다 — 곧장 켜면 무엇이 달라졌는지 못 본다.
        this.m_tierSeq.AppendInterval(this.enterDelay);

        // 같은 등급 안 상승(브론즈 1 → 2)도 같은 길을 탄다 — 새로 도달한 칸을 순서대로 켠다(두 칸 이상 뛰어도 다 켜진다).
        if (t_gradeUp) this.StageGradeUp(this.m_tierSeq, t_info, t_prevDivision, t_divisions);
        else
            for (int t_i = t_prevDivision; t_i < t_info.Division && t_i < t_divisions; t_i++)
                this.StagePipOn(this.m_tierSeq, t_i);

        // 여운 없이 끝내면 마지막 별이 튀는 도중에 연출이 잘린 것처럼 보인다.
        this.m_tierSeq.AppendInterval(this.finishDelay);

        // 어떤 이유로 끊겨도 표시가 중간 상태로 굳지 않게 한다(연출 가드를 먼저 풀고 정상 규칙으로 되돌린다).
        // Restore가 배지의 자리·배율과 런타임 생성물을 함께 걷는다.
        this.m_tierSeq.OnKill(() =>
        {
            this.m_tierSeq = null;
            this.promote.Restore();
            this.RestorePipScales();
            this.Render();
        });

        return this.m_tierSeq;
    }

    /// <summary>
    /// 티어가 내려간 순간을 그린다. 표시는 이미 최종 티어이므로 _prevTierIndex 상태에서 출발해 지금으로 내려온다.
    /// 재생은 호출자 몫(BuildTierUp과 같은 규약).
    /// 강도는 빈도를 따른다 — 별 하나 제거는 조용히, 등급이 갈릴 때만 배지를 흔들어 크게 알린다.
    /// </summary>
    public Sequence BuildTierDown(int _prevTierIndex)
    {
        this.KillTierChange();

        int t_divisions = RankConfig.DivisionsPerGrade;
        var t_info = RankManager.GetInfo();

        int t_prevDivision = _prevTierIndex % t_divisions + 1;
        bool t_gradeDown   = t_info.TierIndex / t_divisions != _prevTierIndex / t_divisions;

        this.m_tierSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 흔들림이 되돌아올 자리는 여기서 잡는다 — Awake는 레이아웃 전이라 배지가 아직 제자리가 아닐 수 있다.
        var t_badgeRect = this.BadgeRect;
        if (t_badgeRect != null) this.m_badgeBasePos = t_badgeRect.anchoredPosition;

        // 출발점 = 떨어지기 직전 화면(BuildTierUp과 같은 규율 — 조립 시점에 즉시 되돌린다).
        this.RenderPips(t_prevDivision);
        if (t_gradeDown) this.RenderTier(RankRewardManager.GetInfo(_prevTierIndex));

        this.m_tierSeq.AppendInterval(this.enterDelay);

        // 등급이 그대로면 잃은 칸만, 갈릴 거면 켜져 있던 칸을 전부 — 뒤에서부터 하나씩 꺼진다.
        int t_stop = t_gradeDown ? 0 : t_info.Division;
        for (int t_i = t_prevDivision - 1; t_i >= t_stop; t_i--)
            this.StagePipOff(this.m_tierSeq, t_i);

        if (t_gradeDown) this.StageGradeDown(this.m_tierSeq, t_info);

        this.m_tierSeq.AppendInterval(this.finishDelay);

        this.m_tierSeq.OnKill(() =>
        {
            this.m_tierSeq = null;
            this.RestoreBadgeColor();
            this.RestoreBadgePosition();
            this.RestorePipScales();
            this.Render();
        });

        return this.m_tierSeq;
    }

    void Awake()
    {
        // 연출이 되돌릴 기준색. 배지 스프라이트는 티어 변화로 갈리지만 색은 저작값 그대로다.
        if (this.badgeImage != null) this.m_badgeBaseColor = this.badgeImage.color;
    }

    void Start()
    {
        this.m_started = true;
        this.Render();
    }

    // 탭 재진입(SetActive 토글)만 처리. 첫 활성화는 Start가 담당.
    void OnEnable()
    {
        s_instance = this;

        if (!this.m_started) return;
        this.Render();
    }

    void OnDisable()
    {
        if (s_instance == this) s_instance = null;

        // 꺼지는 동안 트윈만 남으면 다음 활성화가 중간 상태를 물려받는다.
        this.KillTierChange();
        this.KillReactions();

        if (this.arc != null) this.arc.Stop();
    }

    void Render()
    {
        // 연출 중에는 손대지 않는다 — 과거 상태에서 출발하는 연출을 최종값으로 덮어버린다.
        if (this.m_tierSeq != null && this.m_tierSeq.IsActive()) return;

        var t_info = RankManager.GetInfo();

        this.RenderTier(t_info.DisplayName, t_info.Badge);

        // 미도달(언랭크)은 아직 한 칸도 딛지 않았다 — 티어 인덱스는 0이지만 핍 줄 자체를 감춘다.
        this.SetPipsVisible(!t_info.IsUnranked);
        this.RenderPips(t_info.IsUnranked ? 0 : t_info.Division);
        this.SetArc(t_info.IsUnranked ? 0f : t_info.GradeProgress);
    }

    // 등급 승급: 구 배지가 파열하고 → 새 배지가 강림해 착지하고 → 그제서야 새 등급의 별이 채워진다.
    // 별을 뒤에서부터 하나씩 줄이지 않는다 — 오르는 순간에 '잃는 그림'을 끼우면 감정이 역행한다.
    void StageGradeUp(Sequence _seq, in RankInfo _info, int _prevDivision, int _divisions)
    {
        // in 파라미터는 람다가 잡을 수 없다 — 콜백이 쓸 값만 먼저 떠 둔다.
        string t_name = _info.DisplayName;
        Sprite t_badge = _info.Badge;
        int t_division = _info.Division;
        int t_prev = _prevDivision;
        float t_progress = _info.GradeProgress;

        // 호도 별과 같은 프레임에 새 등급의 출발선으로 되감긴다 — 옛 등급의 승급선에 머물면 다 채운 채로 보인다.
        this.promote.Build(_seq, this.badgeImage, this.descText, t_name, t_badge,
                           _onBurst: () => { this.BlowOutPips(t_prev); this.SetArc(t_progress); });

        // 새 등급의 1단계부터 다시 쌓기 시작한다(도달 단계가 2 이상인 경우도 순서대로 켠다).
        for (int t_i = 0; t_i < t_division && t_i < _divisions; t_i++)
            this.StagePipOn(_seq, t_i);
    }

    // 켜져 있던 별이 배지가 터지는 것과 같은 프레임에 한꺼번에 꺼진다(순서대로 지우면 그만큼 잃는 장면이 길어진다).
    void BlowOutPips(int _prevDivision)
    {
        for (int t_i = 0; t_i < _prevDivision; t_i++)
        {
            this.SetPip(t_i, false);
            UiPunch.Play(this.PipTransform(t_i), -this.pipBlowOut);
        }
    }

    // 핍 하나가 탁 켜지는 단위 동작. _index = 켜질 칸(0-based).
    void StagePipOn(Sequence _seq, int _index)
    {
        _seq.AppendCallback(() =>
        {
            // 언랭크에서 올라오는 첫 진입은 핍 줄이 꺼진 채 출발한다 — 켜는 것이 펀치보다 먼저여야 한다.
            this.SetPipsVisible(true);
            this.SetPip(_index, true);
            UiPunch.Play(this.PipTransform(_index), this.pipPunch);
        });
        _seq.AppendInterval(this.pipStep);
    }

    // 등급 강등: 배지가 흔들리며 아래 등급으로 갈리고, 새 등급의 별 4칸은 한 번에 켜진다.
    // 칸을 하나씩 켜는 건 승급의 문법이라 여기서 재사용하면 "별이 늘었다 = 올랐다"로 읽힌다.
    void StageGradeDown(Sequence _seq, in RankInfo _info)
    {
        // in 파라미터는 람다가 잡을 수 없다 — 콜백이 쓸 값만 먼저 떠 둔다(StageGradeUp과 같은 사정).
        string t_name = _info.DisplayName;
        Sprite t_badge = _info.Badge;
        int t_division = _info.Division;
        float t_progress = _info.GradeProgress;

        _seq.AppendInterval(this.badgeSwapDelay);
        _seq.AppendCallback(() =>
        {
            this.RenderTier(t_name, t_badge);
            this.RenderPips(t_division);
            this.SetArc(t_progress);
        });

        // 흔들림·어두워짐은 시퀀스 멤버로 단다 — 콜백 안에서 따로 띄우면 시퀀스가 죽어도 살아남아 배지가 어긋난 채 굳는다.
        // 배지가 미배선이면 둘 다 없다(핍만 갈린다). BadgeRect가 non-null이면 badgeImage도 non-null이다.
        var t_rect = this.BadgeRect;
        if (t_rect == null) return;

        // 콜백 뒤에는 Join이 아니라 Append로 붙인다 — 길이 0인 콜백은 시퀀스 길이를 늘리지 않아
        // Append가 곧 "콜백과 같은 시각"이고, Join의 기준점보다 이쪽이 분명하다.
        _seq.Append(t_rect.DOShakeAnchorPos(this.badgeShakeDuration, this.badgeShakeStrength, vibrato: 14));
        _seq.Join(this.badgeImage.DOColor(this.demoteBadgeColor, this.badgeShakeDuration * 0.5f)
                                 .SetLoops(2, LoopType.Yoyo));
    }

    // 핍 하나가 조용히 꺼지는 단위 동작. 켜질 때의 탁 튀는 손맛과 반대로 움츠러들었다 돌아온다.
    void StagePipOff(Sequence _seq, int _index)
    {
        _seq.AppendCallback(() =>
        {
            this.SetPip(_index, false);
            UiPunch.Play(this.PipTransform(_index), -this.demotePipShrink);
        });
        _seq.AppendInterval(this.demotePipStep);
    }

    void RenderTier(in RankRewardInfo _info) => this.RenderTier(_info.DisplayName, _info.Badge);

    // 배지 미저작(null)이면 씬에 배선된 기존 스프라이트를 그대로 둔다.
    void RenderTier(string _displayName, Sprite _badge)
    {
        if (this.badgeImage != null && _badge != null) this.badgeImage.sprite = _badge;
        if (this.descText != null && _displayName != null) this.descText.text = _displayName;
    }

    // _filled칸까지 켜고 나머지는 끈다.
    void RenderPips(int _filled)
    {
        if (this.divisionPips == null) return;

        for (int t_i = 0; t_i < this.divisionPips.Length; t_i++)
            this.SetPip(t_i, t_i < _filled);
    }

    void SetArc(float _ratio)
    {
        if (this.arc != null) this.arc.SetRatio(_ratio);
    }

    void SetPipsVisible(bool _visible)
    {
        if (this.pipsRoot == null) return;
        if (this.pipsRoot.activeSelf == _visible) return;

        this.pipsRoot.SetActive(_visible);
    }

    void SetPip(int _index, bool _on)
    {
        if (this.divisionPips == null || _index < 0 || _index >= this.divisionPips.Length) return;

        var t_pip = this.divisionPips[_index];
        if (t_pip == null) return;

        var t_sprite = _on ? this.pipOn : this.pipOff;
        if (t_sprite != null) t_pip.sprite = t_sprite;
    }

    Transform PipTransform(int _index)
    {
        if (this.divisionPips == null || _index < 0 || _index >= this.divisionPips.Length) return null;

        var t_pip = this.divisionPips[_index];
        return t_pip != null ? t_pip.transform : null;
    }

    void KillTierChange()
    {
        if (this.m_tierSeq == null) return;

        // Kill이 OnKill을 부르고 그쪽에서 참조를 비운다 — 여기서 먼저 비우면 Render의 연출 가드가 어긋난다.
        this.m_tierSeq.Kill();
        this.m_tierSeq = null;
    }

    // 배지 색·스케일을 잡는 연출을 모두 걷는다(각 OnKill이 저작 상태로 되돌린다).
    void KillReactions()
    {
        this.KillFlash();

        if (this.m_lossSeq == null) return;
        this.m_lossSeq.Kill();
        this.m_lossSeq = null;
    }

    void KillFlash()
    {
        if (this.m_flashTween == null) return;
        this.m_flashTween.Kill();
        this.m_flashTween = null;
    }

    void RestoreBadgeColor()
    {
        if (this.badgeImage != null) this.badgeImage.color = this.m_badgeBaseColor;
    }

    void RestoreBadgePosition()
    {
        var t_rect = this.BadgeRect;
        if (t_rect != null) t_rect.anchoredPosition = this.m_badgeBasePos;
    }

    // 핍 펀치는 시퀀스 멤버가 아니라 따로 뜬 트윈이라, 연출이 중간에 끊기면 줄어든(혹은 커진) 배율로 굳는다.
    void RestorePipScales()
    {
        if (this.divisionPips == null) return;

        for (int t_i = 0; t_i < this.divisionPips.Length; t_i++)
        {
            var t_pip = this.divisionPips[t_i];
            if (t_pip == null) continue;

            t_pip.transform.DOKill();
            t_pip.transform.localScale = Vector3.one;
        }
    }
}
