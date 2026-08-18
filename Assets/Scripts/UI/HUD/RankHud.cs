using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// 로비 랭크 표시(배지 = 등급, 별 = 등급 안 진행, 텍스트 = 티어명).
// 랭크는 전투 씬에서만 변하므로 변경 이벤트 없이 표시 시점에 RankManager를 재조회한다.
// 포인트 수치는 화면에 두지 않는다 — 증감은 배지 반응으로만 알린다(조립은 LobbyRankEffectDirector).
public class RankHud : MonoBehaviour
{
    [SerializeField] Image badgeImage;   // 티어 배지
    [SerializeField] TMP_Text descText;  // 티어 표시명("브론즈 1")

    [Header("단계 별")]
    [Tooltip("별을 묶는 노드(RankPips). 언랭크면 통째로 끈다 — 빈 별 네 칸은 '아직 아무것도 아님'을 알리는 데 방해만 된다.\n" +
             "미배선이면 끄지 않고 빈 칸만 그린다.")]
    [SerializeField] GameObject pipsRoot;

    [Header("진행 게이지")]
    [Tooltip("등급 안 진행을 그리는 게이지(RankProgressGauge 구현이면 무엇이든).\n" +
             "별이 얼마나 찼는지도, 언제 꽉 차는지도 모두 이 게이지가 낸다 — 진행을 그리는 축은 이것 하나뿐이다.\n" +
             "미배선이면 진행 표시 없이 배지·이름만 갱신된다.")]
    [FormerlySerializedAs("arc")]
    [SerializeField] RankProgressGauge gauge;

    [Tooltip("조각 하나가 꽂힐 때 게이지가 다음 눈금까지 가는 시간. 조각 간격보다 짧게 둔다 — 길면 다음 조각이 앞 트윈을 밟는다.")]
    [FormerlySerializedAs("arcStepDuration")]
    [SerializeField] float gaugeStepDuration = 0.18f;

    [Header("승급 연출")]
    [Tooltip("연출을 시작하기 전 뜸. 화면이 눈에 들어온 뒤에 별이 채워져야 '변했다'가 보인다.")]
    [SerializeField] float enterDelay = 0.35f;

    [SerializeField] float pipPunch = UiPunch.DEFAULT_SCALE;

    [Tooltip("별 하나가 꽉 차고 다음으로 넘어가는 간격. 펀치 길이(0.3초)보다 짧으면 연출이 잘린다.")]
    [SerializeField] float pipStep = 0.35f;

    [Tooltip("마지막 별이 찬 뒤의 여운.")]
    [SerializeField] float finishDelay = 0.6f;

    [Tooltip("등급 승급 때 차 있던 별이 한꺼번에 비워지며 움츠러드는 세기.")]
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

    // 활성 인스턴스(연출 호출자가 찾는 창구). 로비에 하나뿐이지만 탭 토글로 꺼지므로 활성분만 든다.
    static RankHud s_instance;

    // 최초 렌더를 Start로 미루기 위한 표식 — RankConfig 주입(DataLibrary.Awake)보다 OnEnable이 먼저 돌 수 있다.
    bool m_started;

    // 진행 중 티어 변화 연출. 살아있는 동안 Render가 표시를 최종값으로 덮지 않는다(연출이 과거 상태에서 출발한다).
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

        this.m_gainFrom = GaugeRatioOf(t_prev);
        this.m_gainTo   = t_now.Grade != t_prev.Grade ? 1f : GaugeRatioOf(t_now);

        this.SetGaugeRatio(this.m_gainFrom);
    }

    /// <summary>조각 하나가 배지에 꽂혔다(_arrived = 1부터 세는 누적, _total = 전체).
    /// 마지막 조각이면 크게 튀고 한 번 밝아진다.</summary>
    public void PlayGainImpact(int _arrived, int _total)
    {
        bool t_final = _arrived >= _total;

        UiPunch.Play(this.BadgeRect, t_final ? this.gainFinalPunch : this.gainPunch);

        // 조각이 꽂힌 만큼만 게이지가 전진한다 — 한 번에 도착시키면 "무엇 때문에 찼는지"가 사라진다.
        if (this.gauge != null && _total > 0)
            this.gauge.TweenTo(Mathf.Lerp(this.m_gainFrom, this.m_gainTo, (float)_arrived / _total),
                               this.gaugeStepDuration);

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

        // 게이지는 요요로 되돌아오지 않는다 — 줄어든 자리가 곧 지금 값이다(배지만 식었다 돌아온다).
        // 꽉 찬 별은 바닥이 현재 단계 진입선이라 꺼지지 않는다. 채우던 중인 별만 줄어든다.
        if (this.gauge != null)
        {
            Tween t_gauge = this.gauge.TweenTo(GaugeRatioOf(RankManager.GetInfo()), this.lossDuration);
            if (t_gauge != null) this.m_lossSeq.Join(t_gauge);
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
        // 앞 단계(포인트 조각)가 도는 동안 새 별이 이미 차 있으면 "포인트가 차서 별이 찼다"는 인과가 깨진다.
        // m_tierSeq 대입 뒤여야 Render의 연출 가드가 이 상태를 최종값으로 덮지 않는다.
        // 게이지 값은 여기서 손대지 않는다 — 전투 직전 비율은 PrepareProgress가 이미 세워 뒀다(그리는 축은 하나다).
        // 첫 진입(_prevTierIndex < 0)의 출발점은 언랭크다 — 별 줄이 꺼진 채 시작해 첫 칸이 찰 때 함께 나타난다.
        this.SetPipsVisible(_prevTierIndex >= 0);

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

        // 옛 상태를 한 박자 보여준 뒤에 바꾼다 — 등급이 갈리는 판에서만 필요하다.
        // 같은 등급 안 상승은 앞선 조각 단계가 이미 옛 상태에서 출발하는 것을 다 보여줬다.
        if (t_gradeUp) this.m_tierSeq.AppendInterval(this.enterDelay);

        // 같은 등급 안 상승(브론즈 1 → 2)의 별은 여기서 채우지 않는다 —
        // 조각이 게이지를 미는 동안 채움 머리가 그 별을 꽉 채운다. 여기서 또 채우면 시계가 둘이 된다.
        if (t_gradeUp) this.StageGradeUp(this.m_tierSeq, t_info, t_prevDivision, t_divisions);

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

    void Awake()
    {
        // 연출이 되돌릴 기준색. 배지 스프라이트는 티어 변화로 갈리지만 색은 저작값 그대로다.
        if (this.badgeImage != null) this.m_badgeBaseColor = this.badgeImage.color;

        // 별은 스스로 반응하지 않는다 — 채움 머리가 자기 마디를 지날 때만 튄다.
        if (this.gauge != null) this.gauge.SetThresholds(BuildMarkerRatios(), this.OnMarkerCrossed);
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

        if (this.gauge != null) this.gauge.Stop();
    }

    void Render()
    {
        // 연출 중에는 손대지 않는다 — 과거 상태에서 출발하는 연출을 최종값으로 덮어버린다.
        if (this.m_tierSeq != null && this.m_tierSeq.IsActive()) return;

        var t_info = RankManager.GetInfo();

        this.RenderTier(t_info.DisplayName, t_info.Badge);

        // 미도달(언랭크)은 아직 한 칸도 딛지 않았다 — 티어 인덱스는 0이지만 별 줄 자체를 감춘다.
        this.SetPipsVisible(!t_info.IsUnranked);
        this.SetGaugeRatio(GaugeRatioOf(t_info));
    }

    // 게이지에 먹일 비율. 진행을 그리는 값은 전부 여기를 지난다.
    // 승급전 대기의 실제 포인트는 '등급 천장 - 1'이라 GradeProgress가 0.95쯤 나오지만, 기획상 그 상태는 "별 넷이 꽉 참"이다 —
    // 이 보정은 여기 한 지점뿐이다(과거 포인트를 그린 스냅샷은 보정하지 않는다).
    static float GaugeRatioOf(in RankInfo _info)
    {
        if (_info.IsUnranked) return 0f;
        if (_info.Points == RankManager.Points && RankManager.IsPromoPending) return 1f;

        return _info.GradeProgress;
    }

    // 채움 머리가 마디를 지나는 그 프레임의 반응(전진 = 별이 꽉 찼다, 후퇴 = 다시 모자라다).
    // 점등은 따로 있는 상태가 아니라 fillAmount == 1 그 자체다 — 여기서 별을 켜고 끄지 않는다.
    void OnMarkerCrossed(int _index, bool _forward)
    {
        UiPunch.Play(this.PipTransform(_index), _forward ? this.pipPunch : -this.pipPunch);
    }

    // 별 K가 꽉 차는 비율들. 사건은 "별이 다 찼다"이지 "채우기 시작했다"가 아니라 구간의 끝을 잡는다.
    static float[] BuildMarkerRatios()
    {
        int t_divisions = RankConfig.DivisionsPerGrade;

        var t_ratios = new float[t_divisions];
        for (int t_i = 0; t_i < t_divisions; t_i++) t_ratios[t_i] = (float)(t_i + 1) / t_divisions;

        return t_ratios;
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
        float t_progress = GaugeRatioOf(_info);

        // 게이지는 배지가 터지는 프레임에 새 등급의 출발선으로 되감긴다 — 옛 등급의 승급선에 머물면 다 채운 채로 보인다.
        this.promote.Build(_seq, this.badgeImage, this.descText, t_name, t_badge,
                           _onBurst: () => this.BlowOutGauge(t_prev));

        // 새 등급의 1단계부터 다시 쌓는다. 마지막 토막은 실제 진행까지만 찬다 — 도달값보다 더 채우면 거짓말이 된다.
        for (int t_i = 0; t_i < t_division && t_i < _divisions; t_i++)
            this.StageStarFill(_seq, Mathf.Min((float)(t_i + 1) / _divisions, t_progress));
    }

    // 차 있던 별이 배지가 터지는 것과 같은 프레임에 한꺼번에 비워지며 움츠러든다(순서대로 지우면 그만큼 잃는 장면이 길어진다).
    void BlowOutGauge(int _prevDivision)
    {
        // 무통지 스냅이다 — 통지하면 마디가 우수수 역행하며 방금 켜진 펀치를 되감는다.
        this.SetGaugeRatio(0f);

        for (int t_i = 0; t_i < _prevDivision; t_i++)
            UiPunch.Play(this.PipTransform(t_i), -this.pipBlowOut);
    }

    // 게이지가 _ratio까지 차오르는 단위 동작(별 한 칸 분량). 꽉 차는 순간의 펀치는 마디 통과 통지가 낸다.
    void StageStarFill(Sequence _seq, float _ratio)
    {
        _seq.AppendCallback(() =>
        {
            // 언랭크에서 올라오는 첫 진입은 별 줄이 꺼진 채 출발한다 — 켜는 것이 채움보다 먼저여야 한다.
            this.SetPipsVisible(true);

            if (this.gauge != null) this.gauge.TweenTo(_ratio, this.gaugeStepDuration);
        });
        _seq.AppendInterval(this.pipStep);
    }

    void RenderTier(in RankRewardInfo _info) => this.RenderTier(_info.DisplayName, _info.Badge);

    // 배지 미저작(null)이면 씬에 배선된 기존 스프라이트를 그대로 둔다.
    void RenderTier(string _displayName, Sprite _badge)
    {
        if (this.badgeImage != null && _badge != null) this.badgeImage.sprite = _badge;
        if (this.descText != null && _displayName != null) this.descText.text = _displayName;
    }

    void SetGaugeRatio(float _ratio)
    {
        if (this.gauge != null) this.gauge.SetRatio(_ratio);
    }

    void SetPipsVisible(bool _visible)
    {
        if (this.pipsRoot == null) return;
        if (this.pipsRoot.activeSelf == _visible) return;

        this.pipsRoot.SetActive(_visible);
    }

    // 펀치할 별. 자리도 개수도 게이지가 안다 — HUD는 별 목록을 따로 들지 않는다.
    Transform PipTransform(int _index)
    {
        var t_star = this.gauge as RankStarGauge;
        return t_star != null ? t_star.StarRect(_index) : null;
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

    // 별 펀치는 시퀀스 멤버가 아니라 따로 뜬 트윈이라, 연출이 중간에 끊기면 줄어든(혹은 커진) 배율로 굳는다.
    void RestorePipScales()
    {
        var t_star = this.gauge as RankStarGauge;
        if (t_star == null) return;

        for (int t_i = 0; t_i < t_star.StarCount; t_i++)
        {
            var t_rect = t_star.StarRect(t_i);
            if (t_rect == null) continue;

            t_rect.DOKill();
            t_rect.localScale = Vector3.one;
        }
    }
}
