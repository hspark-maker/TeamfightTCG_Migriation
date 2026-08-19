using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// 로비 랭크 표시(배지 = 등급, 별 = 단계 안 진행 — 한 칸이 1승이고 네 칸이 차면 다음 단계, 텍스트 = 티어명).
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
    [Tooltip("단계 안 진행을 그리는 게이지(RankProgressGauge 구현이면 무엇이든).\n" +
             "별이 얼마나 찼는지도, 언제 꽉 차는지도 모두 이 게이지가 낸다 — 진행을 그리는 축은 이것 하나뿐이다.\n" +
             "미배선이면 진행 표시 없이 배지·이름만 갱신된다.")]
    [FormerlySerializedAs("arc")]
    [SerializeField] RankProgressGauge gauge;

    [Tooltip("조각이 꽂힌 프레임부터 게이지가 목표치까지 가는 시간. 전진은 한 번뿐이라 이 값이 곧 '차오르는 시간' 전부다.\n" +
             "첫 진입 연출에서 별을 한 칸씩 채우는 시간도 같은 값을 쓴다 — 조각 간격(pipStep)보다 짧게 둔다.")]
    [SerializeField] float gaugeAdvanceDuration = 0.25f;

    [Header("티어 변화 연출")]
    [Tooltip("첫 진입(언랭크 → 첫 등급)에서 승급 오버레이가 걷힌 뒤, 별 줄 네 칸이 나타나기 시작하기까지의 뜸.\n" +
             "화면이 눈에 들어온 뒤에 나타나야 '생겼다'가 보인다.")]
    [SerializeField] float enterDelay = 0.35f;

    [Tooltip("나타나는 별 칸이 출발하는 배율. 여기서 1로 튀어 오른다(OutBack).")]
    [SerializeField] float pipsAppearScale = 0.4f;
    [Tooltip("별 칸 하나가 제 크기로 올라서는 시간.")]
    [SerializeField] float pipsAppearDuration = 0.18f;
    [Tooltip("별 칸끼리의 간격. 0이면 네 칸이 한 번에 나타난다 — 줄로 읽히려면 조금 어긋나야 한다.")]
    [SerializeField] float pipsAppearStep = 0.07f;

    [Tooltip("마디를 되짚어 내려갈 때(별이 다시 모자라짐) 별이 움츠러드는 세기. 전진 쪽 점등은 '별 점등' 항목이 맡는다.")]
    [SerializeField] float pipPunch = UiPunch.DEFAULT_SCALE;

    [Tooltip("별 하나가 꽉 차고 다음으로 넘어가는 간격. 펀치 길이(0.3초)보다 짧으면 연출이 잘린다.")]
    [SerializeField] float pipStep = 0.35f;

    [Tooltip("마지막 별이 찬 뒤의 여운.")]
    [SerializeField] float finishDelay = 0.6f;

    [Header("포인트 획득 반응")]
    [Tooltip("조각이 꽂히는 프레임의 배지 펀치 세기. 한 판에 한 번뿐이라 크게 준다.")]
    [SerializeField] float gainImpactPunch = 0.3f;
    [Tooltip("같은 프레임에 '지금 채워지는 별'이 튀는 세기. 조각 → 별로 인과가 이어지게 하는 값이라 배지보다 작게.")]
    [SerializeField] float gainStarPunch = 0.2f;
    [SerializeField] Color gainFlashColor = Color.white;
    [SerializeField] float gainFlashDuration = 0.18f;

    [Header("별 점등")]
    [Tooltip("별이 꽉 차는 프레임에 채움이 한 번 튀는 색. 여기서 곧바로 저작 색(골드)으로 돌아온다.")]
    [SerializeField] Color starFlashColor = Color.white;
    [Tooltip("흰 플래시가 골드로 돌아오는 시간. 짧을수록 '한 프레임 번쩍'에 가깝다.")]
    [SerializeField] float starFlashDuration = 0.1f;
    [Tooltip("켜지는 별이 출발하는 배율. 여기서 1로 내려앉는다.")]
    [SerializeField] float starPopScale = 1.5f;
    [SerializeField] float starPopDuration = 0.12f;
    [Tooltip("켜진 별 자리에 남는 고스트(밑판 스프라이트 복제)가 퍼져 나가는 배율.")]
    [SerializeField] float ghostScale = 2.2f;
    [Tooltip("고스트가 퍼지며 사라지는 시간.")]
    [SerializeField] float ghostDuration = 0.25f;

    [Tooltip("켜진 뒤 다음 별이 시선을 넘겨받기까지의 뜸. 사건이 겹치지 않게 한 박 띄운다.")]
    [SerializeField] float handoffDelay = 0.15f;
    [Tooltip("넘겨받는 별이 잠깐 커지는 배율. 상태가 아니라 순간이다 — 곧바로 1로 돌아온다.")]
    [SerializeField] float handoffScale = 1.15f;
    [Tooltip("넘겨받는 별이 밝아졌다 원복하고 배율이 돌아오는 시간.")]
    [SerializeField] float handoffDuration = 0.2f;
    [Tooltip("넘겨받는 별의 밑판이 잠깐 물드는 색. 원복은 저작 색으로 돌아간다.")]
    [SerializeField] Color handoffColor = Color.white;

    [Header("승급전 대기")]
    [Tooltip("별 넷이 꽉 찬 뒤의 '승급전' 상태 표현(배지 뒤 광선 · 문구 · 배지 호흡). 값은 전부 여기 안에 있다.")]
    [SerializeField] RankPromoStandby promoStandby = new RankPromoStandby();

    [Tooltip("고스트가 배지로 출발하는 프레임에 별 넷이 함께 맥동하는 세기.")]
    [SerializeField] float promoStarPulse = 0.22f;

    [Tooltip("배지로 수렴하는 고스트가 줄어드는 배율. 1보다 작아야 '빨려들었다'로 읽힌다.")]
    [SerializeField] float promoGhostScale = 0.35f;

    [Header("포인트 손실 반응")]
    [Tooltip("게이지가 줄어든 자리까지 뒤로 미끄러지는 시간. 획득은 느리고 시끄럽게, 손실은 빠르고 조용하게 — 짧게 둔다.")]
    [SerializeField] float lossSlideDuration = 0.2f;

    // 활성 인스턴스(연출 호출자가 찾는 창구). 로비에 하나뿐이지만 탭 토글로 꺼지므로 활성분만 든다.
    static RankHud s_instance;

    // 최초 렌더를 Start로 미루기 위한 표식 — RankConfig 주입(DataLibrary.Awake)보다 OnEnable이 먼저 돌 수 있다.
    bool m_started;

    // 진행 중 티어 변화 연출. 살아있는 동안 Render가 표시를 최종값으로 덮지 않는다(연출이 과거 상태에서 출발한다).
    Sequence m_tierSeq;

    // 승급 오버레이가 화면을 덮을 때까지 표시를 옛 등급에 묶어 두는 잠금. 푸는 자리는 ApplyTierInstant 하나다.
    bool m_holdTier;

    // 별 줄만 따로 묶어 두는 잠금(첫 진입 전용). 오버레이가 덮인 프레임에 배지는 갈아끼우되 별 줄은 감춘 채 남긴다 —
    // 드러나는 자리는 오버레이가 걷힌 뒤(BuildFirstEntryReveal)다.
    bool m_holdPips;

    // 포인트 손실 반응(게이지가 뒤로 미끄러지는 것뿐). 획득 쪽 반응과 겹쳐 돌지 않게 따로 든다.
    Sequence m_lossSeq;

    // 조각이 꽂힐 때의 밝아짐.
    Tween m_flashTween;

    // 별 하나가 켜지는 한 박(플래시 + 팝 + 고스트 + 넘겨받기). 다음 별이 켜지면 앞 박을 걷고 다시 세운다.
    Sequence m_starSeq;

    // 켜진 별 자리에 띄운 고스트들. 시퀀스가 어디서 끊겨도 여기로 걷는다.
    readonly List<GameObject> m_ghosts = new List<GameObject>();

    // 승급전 진입 연출(별 → 배지). 별 점등 한 박(m_starSeq)보다 오래 살아남아야 해서 따로 든다.
    Sequence m_promoSeq;

    // 배지로 수렴하는 고스트들. 점등 쪽 고스트와 섞으면 별 정리(RestorePipScales)가 비행 중인 이것들까지 걷는다.
    readonly List<GameObject> m_promoGhosts = new List<GameObject>();

    // 이번 정산이 승급전 대기로 넘어가는가. PrepareProgress가 세우고 마지막 별이 찰 때 한 번 소비한다 —
    // 평범한 렌더로는 서지 않으므로 로비를 다시 열어도 진입 연출이 두 번 재생되지 않는다.
    bool m_promoEnterDue;

    // 별 채움·밑판의 저작 색. 연출이 물들인 뒤 되돌릴 기준이다.
    Color[] m_starFillColors;
    Color[] m_starPlateColors;

    // 조각 연출이 출발한 비율과 도착 비율. 조각이 꽂히는 프레임에 이 구간을 한 번에 건넌다.
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
    /// 단계가 갈리는 판이면 도착지는 새 단계의 자리(0)가 아니라 옛 단계의 승격선(1)이다. 그 뒤는 티어 변화 연출이 이어받는다.</summary>
    public void PrepareProgress(long _points)
    {
        var t_prev = RankManager.GetInfoAt(_points);
        var t_now  = RankManager.GetInfo();

        // 단계가 오르면 새 단계의 진행은 0이라, 최종값을 그대로 쓰면 게이지가 역행해 별 넷이 우수수 꺼진다(승격인데 후퇴 연출).
        // 채우는 것까지가 이 구간의 몫이고, 비우는 것은 BuildTierUp이 스냅으로 한다.
        this.m_gainFrom = GaugeRatioOf(t_prev);
        this.m_gainTo   = t_now.TierIndex != t_prev.TierIndex ? 1f : GaugeRatioOf(t_now);

        this.SetGaugeRatio(this.m_gainFrom);

        // 전투 직전엔 아직 승급전이 아니었다 — 대기 표시를 걷어 두고, 마지막 별이 차는 프레임에 진입 연출로 켠다.
        // 단계·등급이 갈리는 판도 도착지가 1이지만 그 결과는 대기가 아니라 승격·승급이라 여기 걸리지 않는다.
        this.m_promoEnterDue = RankManager.IsPromoPending && this.m_gainFrom < 1f && this.m_gainTo >= 1f;
        if (this.m_promoEnterDue) this.promoStandby.SetStandby(false);
    }

    /// <summary>조각이 배지에 꽂힌 프레임. 배지와 채워지는 별이 함께 튀고, 게이지가 목표치까지 한 번에 전진한다.</summary>
    public void PlayGainImpact()
    {
        UiPunch.Play(this.BadgeRect, this.gainImpactPunch);
        UiPunch.Play(this.PipTransform(this.FillingStarIndex()), this.gainStarPunch);

        // 한 판에 한 칸씩 움직이는 값이라 잘게 쪼개면 사건이 밍밍해진다 — 조각 하나에 전진 한 번이다.
        if (this.gauge != null) this.gauge.TweenTo(this.m_gainTo, this.gaugeAdvanceDuration);

        if (this.badgeImage == null) return;

        this.KillFlash();
        this.m_flashTween = this.badgeImage.DOColor(this.gainFlashColor, this.gainFlashDuration * 0.5f)
                                           .SetLoops(2, LoopType.Yoyo)
                                           .SetLink(this.gameObject)
                                           // 스케일은 건드리지 않는다 — 같은 배지에서 도는 UiPunch를 중간에 밟는다.
                                           .OnKill(() => { this.m_flashTween = null; this.RestoreBadgeColor(); });
    }

    /// <summary>
    /// 포인트가 깎인 순간. 게이지가 뒤로 미끄러지는 것만 보여준다 — 패배 팝업에서 이미 본 손실이라
    /// 배지를 식히거나 움츠러뜨려 두 번 때리지 않는다. 재생은 호출자 몫(BuildTierUp과 같은 규약).
    /// </summary>
    public Sequence BuildLossReaction()
    {
        this.KillReactions();

        this.m_lossSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 게이지는 요요로 되돌아오지 않는다 — 줄어든 자리가 곧 지금 값이다.
        // 꽉 찬 별은 바닥이 현재 단계 진입선이라 꺼지지 않는다. 채우던 중인 별만 줄어든다.
        if (this.gauge != null)
        {
            Tween t_gauge = this.gauge.TweenTo(GaugeRatioOf(RankManager.GetInfo()), this.lossSlideDuration);
            if (t_gauge != null) this.m_lossSeq.Join(t_gauge);
        }

        this.m_lossSeq.OnKill(() => this.m_lossSeq = null);

        return this.m_lossSeq;
    }

    /// <summary>
    /// 티어가 오른 순간을 그린다. 표시는 이미 최종 티어이므로 _prevTierIndex 상태에서 출발해 지금으로 건너온다.
    /// 재생은 호출자 몫 — 끝난 뒤에 무엇을 이을지는 부르는 쪽이 정한다.
    /// _prevTierIndex가 음수면 첫 진입(언랭크 → 첫 티어)으로 본다.
    /// 표시를 옛 상태에 묶어 두는 것이 전부다 — 티어가 오르는 사건은 단계든 등급이든 전면 오버레이가 맡고,
    /// 덮인 프레임에 <see cref="ApplyTierInstant"/>가 최종값으로 갈아끼운다.
    /// </summary>
    public Sequence BuildTierUp(int _prevTierIndex)
    {
        this.KillTierChange();

        this.m_tierSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 출발점 = 오르기 직전 화면. 되돌리기는 시퀀스가 아니라 조립 시점에 즉시 건다 —
        // 앞 단계(포인트 조각)가 도는 동안 새 별이 이미 차 있으면 "포인트가 차서 별이 찼다"는 인과가 깨진다.
        // m_tierSeq 대입 뒤여야 Render의 연출 가드가 이 상태를 최종값으로 덮지 않는다.
        // 게이지 값은 여기서 손대지 않는다 — 전투 직전 비율은 PrepareProgress가 이미 세워 뒀다(그리는 축은 하나다).
        // 첫 진입(_prevTierIndex < 0)의 출발점은 언랭크다 — 별 줄이 꺼진 채 시작해 오버레이가 걷힌 뒤에 드러난다.
        this.SetPipsVisible(_prevTierIndex >= 0);

        // 티어가 오르는 판은 여기서 아무것도 그리지 않는다 — 단계 상승(브론즈 1 → 브론즈 2)이든 등급 승급(브론즈 → 실버)이든
        // 그 사건의 주인은 전면 오버레이(RankPromoteOverlay) 하나다.
        // 표시를 옛 티어로 되돌린 채 묶어 두고, 오버레이가 화면을 덮은 프레임에 ApplyTierInstant가 최종값으로 갈아끼운다.
        if (_prevTierIndex >= 0)
        {
            this.RenderTier(RankRewardManager.GetInfo(_prevTierIndex));
            this.m_holdTier = true;
        }
        // 첫 진입(언랭크 → 첫 등급)도 사건의 주인은 같은 오버레이다 — 게임에서 처음 얻는 등급이라 오히려 제일 큰 판이다.
        // 여기서는 언랭크 화면으로 되돌려 묶어 두기만 한다. 배지가 갈리는 것을 안 보여주면 첫 등급이 '이미 그랬던 것'으로 읽힌다.
        // 별 줄은 배지와 따로 묶는다 — 오버레이가 걷힌 뒤에 드러나는 것이 이 사건의 마지막 박이다.
        else
        {
            RankManager.GetUnrankedDisplay(out string t_unrankedName, out Sprite t_unrankedBadge);
            this.RenderTier(t_unrankedName, t_unrankedBadge);

            this.m_holdTier = true;
            this.m_holdPips = true;
        }

        // 여운 없이 끝내면 마지막 별이 튀는 도중에 연출이 잘린 것처럼 보인다.
        this.m_tierSeq.AppendInterval(this.finishDelay);

        // 어떤 이유로 끊겨도 표시가 중간 상태로 굳지 않게 한다(연출 가드를 먼저 풀고 정상 규칙으로 되돌린다).
        this.m_tierSeq.OnKill(() =>
        {
            this.m_tierSeq = null;
            this.RestorePipScales();
            this.Render();
        });

        return this.m_tierSeq;
    }

    /// <summary>연출 없이 지금 랭크의 최종값(배지·티어명·게이지)으로 표시를 세운다.
    /// 승급 오버레이가 화면을 덮은 프레임에 디렉터가 부른다 — 갈아끼우는 것이 보이지 않는 유일한 자리다.
    /// _keepPipsHidden이면 별 줄만 감춘 채 남긴다(첫 진입 전용 — 드러나는 자리는 <see cref="BuildFirstEntryReveal"/>).
    /// 기본값이 '감춤 해제'인 것이 안전망이다: 오버레이를 못 세우는 길로 빠져도 표시가 별 줄 없이 고착되지 않는다.</summary>
    public void ApplyTierInstant(bool _keepPipsHidden = false)
    {
        this.m_holdTier = false;
        if (!_keepPipsHidden) this.m_holdPips = false;

        this.Render();
    }

    /// <summary>첫 진입에서 오버레이가 걷힌 뒤의 마지막 박 — 별 줄이 드러난다.
    /// 브론즈 1은 채울 칸이 0이라 '차오름'으로는 아무것도 안 보인다. 네 칸이 차례로 나타나는 것 자체가 이 박의 내용이고,
    /// 뜻은 "이제부터 이걸 채운다"다. 진행이 0이 아닌 첫 진입이 생기면 뒤이어 실제 진행까지 찬다.
    /// 재생은 호출자 몫(BuildTierUp과 같은 규약).</summary>
    public Sequence BuildFirstEntryReveal()
    {
        this.KillTierChange();

        var t_info = RankManager.GetInfo();

        this.m_tierSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 오버레이가 걷힌 화면이 눈에 들어올 시간을 먼저 준다 — 걷히자마자 나타나면 오버레이의 잔상에 묻힌다.
        this.m_tierSeq.AppendInterval(this.enterDelay);

        // 묶음을 푸는 것과 켜는 것을 시퀀스 안에서 한다 — 조립 시점에 풀면 오버레이가 걷히기 전에 별 줄이 드러난다.
        // 켜는 프레임에 곧바로 출발 배율까지 눌러 둔다. 한 프레임이라도 제 크기로 서면 '나타남'이 아니라 '깜빡임'이 된다.
        this.m_tierSeq.AppendCallback(() =>
        {
            this.m_holdPips = false;
            this.SetPipsVisible(true);
            this.SetGaugeRatio(0f);
            this.SetPipScales(this.pipsAppearScale);
        });

        this.StagePipsAppear(this.m_tierSeq);
        this.StageFirstEntry(this.m_tierSeq, t_info, RankConfig.WinsPerDivision);

        this.m_tierSeq.AppendInterval(this.finishDelay);

        this.m_tierSeq.OnKill(() =>
        {
            this.m_tierSeq = null;
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

        this.CacheStarColors();
        this.promoStandby.Capture();
    }

    // 연출이 물들이기 전의 별 색을 떠 둔다(어디서 끊겨도 여기로 되돌린다).
    void CacheStarColors()
    {
        var t_star = this.gauge as RankStarGauge;
        if (t_star == null) return;

        int t_count = t_star.StarCount;
        this.m_starFillColors  = new Color[t_count];
        this.m_starPlateColors = new Color[t_count];

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            var t_fill  = t_star.StarFill(t_i);
            var t_plate = t_star.StarPlate(t_i);

            this.m_starFillColors[t_i]  = t_fill  != null ? t_fill.color  : Color.white;
            this.m_starPlateColors[t_i] = t_plate != null ? t_plate.color : Color.white;
        }
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

        // 꺼지는 동안 트윈만 남으면 다음 활성화가 중간 상태를 물려받는다(고스트도 여기서 걷힌다).
        this.KillTierChange();
        this.KillReactions();
        this.RestorePipScales();

        // 대기 표시는 상태라 다시 켜지지만, 무한 루프와 런타임 생성물은 여기서 반드시 걷는다.
        // 다음 활성화의 Render가 SetStandby로 제 상태를 즉시 되세운다.
        this.KillPromoEnter();
        this.promoStandby.Reset();
        this.m_promoEnterDue = false;

        // 오버레이를 못 보고 꺼지는 길(탭 전환·씬 언로드)에서 표시가 옛 등급에 고착되지 않게 묶음을 푼다.
        this.m_holdTier = false;
        this.m_holdPips = false;

        if (this.gauge != null) this.gauge.Stop();
    }

    void Render()
    {
        // 연출 중에는 손대지 않는다 — 과거 상태에서 출발하는 연출을 최종값으로 덮어버린다.
        if (this.m_tierSeq != null && this.m_tierSeq.IsActive()) return;

        // 승급 오버레이가 화면을 덮기 전까지 표시는 옛 등급에 묶여 있다 — 푸는 자리는 ApplyTierInstant 하나다.
        if (this.m_holdTier) return;

        var t_info = RankManager.GetInfo();

        this.RenderTier(t_info.DisplayName, t_info.Badge);

        // 미도달(언랭크)은 아직 한 칸도 딛지 않았다 — 티어 인덱스는 0이지만 별 줄 자체를 감춘다.
        // 첫 진입 연출 중에는 이미 도달했어도 감춘 채로 둔다(m_holdPips).
        this.SetPipsVisible(!t_info.IsUnranked && !this.m_holdPips);
        this.SetGaugeRatio(GaugeRatioOf(t_info));

        // 승급전은 사건이 아니라 상태다 — 로비에 들어올 때마다 여기서 즉시(연출 없이) 되세운다.
        // 진입 연출이 도는 중이면 이미 대기 상태라 이 호출이 그 위를 덮지 않는다.
        this.promoStandby.SetStandby(RankManager.IsPromoPending);
    }

    // 게이지에 먹일 비율. 진행을 그리는 값은 전부 여기를 지난다.
    // 승급전 대기의 실제 포인트는 '등급 천장 - 1'이라 TierProgress가 0.975로 나오지만, 기획상 그 상태는 "별 넷이 꽉 참"이다 —
    // 이 보정은 여기 한 지점뿐이다(과거 포인트를 그린 스냅샷은 보정하지 않는다).
    static float GaugeRatioOf(in RankInfo _info)
    {
        if (_info.IsUnranked) return 0f;
        if (_info.Points == RankManager.Points && RankManager.IsPromoPending) return 1f;

        return _info.TierProgress;
    }

    // 채움 머리가 마디를 지나는 그 프레임의 반응(전진 = 별이 꽉 찼다, 후퇴 = 다시 모자라다).
    // 점등은 따로 있는 상태가 아니라 fillAmount == 1 그 자체다 — 여기서 별을 켜고 끄지 않는다.
    void OnMarkerCrossed(int _index, bool _forward)
    {
        // 후퇴는 조용해야 한다 — 되짚어 내려간 자리를 축하할 이유가 없다.
        if (!_forward)
        {
            UiPunch.Play(this.PipTransform(_index), -this.pipPunch);
            return;
        }

        this.PlayStarLit(_index);
        this.TryPlayPromoEnter(_index);
    }

    // 마지막 별이 전진으로 찬 프레임 = 승급전 대기로 넘어가는 순간. 시선을 별에서 배지로 넘기는 것이 이 연출의 전부다.
    void TryPlayPromoEnter(int _index)
    {
        if (!this.m_promoEnterDue || _index != RankConfig.WinsPerDivision - 1) return;

        // 한 번 소비하면 끝이다 — 승급전을 치르고 나면 IsPromoPending이 false가 되어 Render가 알아서 끈다.
        this.m_promoEnterDue = false;

        this.KillPromoEnter();

        this.m_promoSeq = this.promoStandby.BuildEnter(this.BadgeRect);
        this.StagePromoSuck(this.m_promoSeq);
        this.m_promoSeq.SetLink(this.gameObject)
                       .OnKill(() =>
                       {
                           this.m_promoSeq = null;
                           this.ClearGhosts(this.m_promoGhosts);
                       });
    }

    // 별 넷이 함께 맥동하고, 각 별의 고스트가 배지로 수렴하며 줄어들고 사라진다.
    void StagePromoSuck(Sequence _seq)
    {
        var t_gauge = this.gauge as RankStarGauge;
        var t_badge = this.BadgeRect;
        if (t_gauge == null || t_badge == null) return;

        float t_at  = this.promoStandby.SuckAt;
        float t_dur = this.promoStandby.SuckDuration;

        for (int t_i = 0; t_i < t_gauge.StarCount; t_i++)
        {
            int t_index = t_i;   // 클로저가 반복마다 새 변수를 잡아야 마지막 별만 맥동하지 않는다
            _seq.InsertCallback(t_at, () => UiPunch.Play(this.PipTransform(t_index), this.promoStarPulse));

            var t_img = this.SpawnStarGhost(t_gauge, t_i, this.m_promoGhosts);
            if (t_img == null) continue;

            Color t_lit  = t_img.color;
            Color t_gone = t_lit;
            t_gone.a     = 0f;
            t_img.color  = t_gone;   // 출발 전까지는 보이지 않는다 — 밑판 복제라 그냥 두면 채워진 별을 가린다

            // 부모가 다른 사각으로 건너가므로 자리는 월드로 잡는다.
            _seq.Insert(t_at, t_img.rectTransform.DOMove(t_badge.position, t_dur).SetEase(Ease.InQuad));
            _seq.Insert(t_at, t_img.rectTransform.DOScale(this.promoGhostScale, t_dur).SetEase(Ease.InQuad));
            _seq.Insert(t_at, t_img.DOColor(t_gone, t_dur).From(t_lit, setImmediately: false).SetEase(Ease.InQuad));
        }
    }

    // 별이 꽉 차는 프레임에 사건을 몰아넣는다(흰 플래시 + 1.5배에서 내려앉기 + 고스트 확산),
    // 한 박 뒤 다음 별이 시선을 넘겨받는다. 마지막 별은 넘길 곳이 없어 여운만 남긴다.
    void PlayStarLit(int _index)
    {
        // 한 번에 두 마디를 뛰어넘으면 앞 별의 박이 아직 돌고 있다 — 배율·색이 중간값으로 굳지 않게 먼저 되돌린다.
        this.RestorePipScales();

        var t_gauge = this.gauge as RankStarGauge;
        if (t_gauge == null) return;

        var t_rect = t_gauge.StarRect(_index);
        if (t_rect == null) return;

        this.m_starSeq = DOTween.Sequence().SetLink(this.gameObject);

        var t_fill = t_gauge.StarFill(_index);
        if (t_fill != null)
        {
            t_fill.DOKill();
            t_fill.color = this.starFlashColor;
            this.m_starSeq.Join(t_fill.DOColor(this.StarFillColor(_index), this.starFlashDuration));
        }

        // 조각이 꽂힐 때 걸어 둔 펀치가 아직 돌고 있으면 배율이 겹친다 — 걷고 나서 내려앉힌다.
        t_rect.DOKill();
        t_rect.localScale = Vector3.one * this.starPopScale;
        this.m_starSeq.Join(t_rect.DOScale(1f, this.starPopDuration).SetEase(Ease.OutQuad));

        this.StageGhostSpread(t_gauge, _index);
        this.StageHandoff(t_gauge, _index + 1);

        this.m_starSeq.OnKill(() =>
        {
            this.m_starSeq = null;
            this.ClearGhosts(this.m_ghosts);
        });
    }

    // 켜진 별 자리에 남은 고스트가 퍼지며 사라진다(점등의 잔향).
    void StageGhostSpread(RankStarGauge _gauge, int _index)
    {
        var t_img = this.SpawnStarGhost(_gauge, _index, this.m_ghosts);
        if (t_img == null) return;

        this.m_starSeq.Join(t_img.rectTransform.DOScale(this.ghostScale, this.ghostDuration).SetEase(Ease.OutQuad));
        this.m_starSeq.Join(t_img.DOFade(0f, this.ghostDuration).SetEase(Ease.OutQuad));
    }

    // 별 밑판 스프라이트를 복제한 고스트를 별 자리에 띄운다(전용 링 텍스처가 없어 별 모양 그대로 쓴다).
    // 어디로 어떻게 보낼지는 부르는 쪽이 정한다 — 퍼뜨리거나(점등) 배지로 수렴시키거나(승급전 진입).
    Image SpawnStarGhost(RankStarGauge _gauge, int _index, List<GameObject> _bin)
    {
        var t_plate = _gauge.StarPlate(_index);
        var t_rect  = _gauge.StarRect(_index);
        if (t_plate == null || t_rect == null || t_rect.parent == null) return null;

        var t_go = new GameObject("StarGhost", typeof(RectTransform), typeof(CanvasRenderer),
                                  typeof(Image), typeof(LayoutElement));
        _bin.Add(t_go);

        var t_ghost = (RectTransform)t_go.transform;
        t_ghost.SetParent(t_rect.parent, false);

        // 별 줄이 레이아웃으로 정렬돼 있으면 새 형제가 칸을 밀어낸다 — 계산에서 빼 둔다.
        t_go.GetComponent<LayoutElement>().ignoreLayout = true;

        t_ghost.anchorMin        = t_rect.anchorMin;
        t_ghost.anchorMax        = t_rect.anchorMax;
        t_ghost.pivot            = t_rect.pivot;
        t_ghost.anchoredPosition = t_rect.anchoredPosition;
        t_ghost.sizeDelta        = t_rect.sizeDelta;
        t_ghost.SetAsLastSibling();

        var t_img = t_go.GetComponent<Image>();
        t_img.sprite         = t_plate.sprite;
        t_img.color          = t_plate.color;
        t_img.preserveAspect = t_plate.preserveAspect;
        t_img.raycastTarget  = false;

        return t_img;
    }

    // 다음 별이 시선을 넘겨받는 한 박. 크기 강조는 상태가 아니라 순간이라 곧바로 원복한다.
    void StageHandoff(RankStarGauge _gauge, int _index)
    {
        var t_rect = _gauge.StarRect(_index);
        if (t_rect == null) return;

        this.m_starSeq.Insert(this.handoffDelay,
                              // setImmediately를 끄지 않으면 조립하는 순간 커져 버려 한 박 내내 부풀어 있는다.
                              t_rect.DOScale(1f, this.handoffDuration)
                                    .From(Vector3.one * this.handoffScale, false)
                                    .SetEase(Ease.OutQuad));

        var t_plate = _gauge.StarPlate(_index);
        if (t_plate == null) return;

        t_plate.DOKill();
        t_plate.color = this.StarPlateColor(_index);
        this.m_starSeq.Insert(this.handoffDelay,
                              t_plate.DOColor(this.handoffColor, this.handoffDuration * 0.5f)
                                     .SetLoops(2, LoopType.Yoyo));
    }

    // 지금 채워지고 있는 별. 조각이 꽂히는 순간 움직이기 시작하는 칸이라 여기를 함께 튀긴다.
    int FillingStarIndex()
    {
        var t_star = this.gauge as RankStarGauge;
        if (t_star == null || t_star.StarCount == 0) return 0;

        return Mathf.Clamp(Mathf.FloorToInt(this.m_gainFrom * t_star.StarCount), 0, t_star.StarCount - 1);
    }

    Color StarFillColor(int _index)
        => this.m_starFillColors != null && _index < this.m_starFillColors.Length
         ? this.m_starFillColors[_index] : Color.white;

    Color StarPlateColor(int _index)
        => this.m_starPlateColors != null && _index < this.m_starPlateColors.Length
         ? this.m_starPlateColors[_index] : Color.white;

    // 별 K가 꽉 차는 비율들. 사건은 "별이 다 찼다"이지 "채우기 시작했다"가 아니라 구간의 끝을 잡는다.
    static float[] BuildMarkerRatios()
    {
        int t_stars = RankConfig.WinsPerDivision;

        var t_ratios = new float[t_stars];
        for (int t_i = 0; t_i < t_stars; t_i++) t_ratios[t_i] = (float)(t_i + 1) / t_stars;

        return t_ratios;
    }

    // 별 줄 네 칸이 차례로 제 크기로 올라선다. 채움이 아니라 '그릇이 생긴다'를 그리는 자리다.
    void StagePipsAppear(Sequence _seq)
    {
        float t_at = _seq.Duration(false);

        for (int t_i = 0; t_i < RankConfig.WinsPerDivision; t_i++)
        {
            Transform t_pip = this.PipTransform(t_i);
            if (t_pip == null) continue;

            _seq.Insert(t_at + t_i * this.pipsAppearStep,
                        t_pip.DOScale(1f, this.pipsAppearDuration).SetEase(Ease.OutBack));
        }

        // Insert는 이어붙이는 머리를 밀지 않는다 — 뒤에 오는 채움이 나타남 위로 겹치지 않게 여기서 밀어 준다.
        _seq.AppendInterval((RankConfig.WinsPerDivision - 1) * this.pipsAppearStep + this.pipsAppearDuration);
    }

    // 별 칸 전체를 한 배율로 눌러 둔다(나타남의 출발점). 되돌림은 RestorePipScales가 맡는다.
    void SetPipScales(float _scale)
    {
        for (int t_i = 0; t_i < RankConfig.WinsPerDivision; t_i++)
        {
            Transform t_pip = this.PipTransform(t_i);
            if (t_pip != null) t_pip.localScale = Vector3.one * _scale;
        }
    }

    // 첫 진입(언랭크 → 첫 등급)의 별 채움. 등급 승급(브론즈 → 실버)은 이 길로 오지 않는다 —
    // 그 사건의 주인은 전면 오버레이(RankPromoteOverlay) 하나다.
    void StageFirstEntry(Sequence _seq, in RankInfo _info, int _stars)
    {
        // in 파라미터는 람다가 잡을 수 없다 — 콜백이 쓸 값만 먼저 떠 둔다.
        float t_progress = GaugeRatioOf(_info);

        // 조각이 밀어 둔 게이지를 출발선으로 되감는다 — 다 채운 채로 시작하면 채워지는 그림이 없다.
        // 무통지 스냅이다(통지하면 마디가 우수수 역행하며 방금 켜진 펀치를 되감는다).
        _seq.AppendCallback(() => this.SetGaugeRatio(0f));

        // 도달한 진행까지 한 칸씩 쌓는다. 마지막 토막은 실제 진행까지만 찬다 — 도달값보다 더 채우면 거짓말이 된다.
        // 브론즈 1은 진행이 0이라 채울 칸이 없다. 별 줄이 나타나는 것 자체가 그 판의 내용이다.
        for (int t_i = 0; t_i < _stars && (float)t_i / _stars < t_progress; t_i++)
            this.StageStarFill(_seq, Mathf.Min((float)(t_i + 1) / _stars, t_progress));
    }

    // 게이지가 _ratio까지 차오르는 단위 동작(별 한 칸 분량). 꽉 차는 순간의 펀치는 마디 통과 통지가 낸다.
    void StageStarFill(Sequence _seq, float _ratio)
    {
        _seq.AppendCallback(() =>
        {
            // 언랭크에서 올라오는 첫 진입은 별 줄이 꺼진 채 출발한다 — 켜는 것이 채움보다 먼저여야 한다.
            this.SetPipsVisible(true);

            if (this.gauge != null) this.gauge.TweenTo(_ratio, this.gaugeAdvanceDuration);
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

    // 승급전 진입 연출만 걷는다. 대기 '상태'는 여기서 끄지 않는다 — 끄고 켜는 축은 Render 하나다.
    void KillPromoEnter()
    {
        var t_seq = this.m_promoSeq;
        this.m_promoSeq = null;
        if (t_seq != null) t_seq.Kill();

        this.ClearGhosts(this.m_promoGhosts);
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

    // 별 펀치·점등은 시퀀스 밖에서도 도는 트윈이라, 연출이 중간에 끊기면 배율·색이 중간값으로 굳고 고스트가 남는다.
    // 점등 한 박을 걷을 때 참조를 먼저 비운다 — 시퀀스의 OnKill이 여기로 되돌아 들어오지 않게.
    void RestorePipScales()
    {
        var t_seq = this.m_starSeq;
        this.m_starSeq = null;
        if (t_seq != null) t_seq.Kill();

        this.ClearGhosts(this.m_ghosts);

        var t_star = this.gauge as RankStarGauge;
        if (t_star == null) return;

        for (int t_i = 0; t_i < t_star.StarCount; t_i++)
        {
            var t_rect = t_star.StarRect(t_i);
            if (t_rect != null)
            {
                t_rect.DOKill();
                t_rect.localScale = Vector3.one;
            }

            var t_fill = t_star.StarFill(t_i);
            if (t_fill != null)
            {
                t_fill.DOKill();
                t_fill.color = this.StarFillColor(t_i);
            }

            var t_plate = t_star.StarPlate(t_i);
            if (t_plate == null) continue;

            t_plate.DOKill();
            t_plate.color = this.StarPlateColor(t_i);
        }
    }

    void ClearGhosts(List<GameObject> _bin)
    {
        for (int t_i = 0; t_i < _bin.Count; t_i++)
        {
            if (_bin[t_i] == null) continue;
            Destroy(_bin[t_i]);
        }

        _bin.Clear();
    }
}
