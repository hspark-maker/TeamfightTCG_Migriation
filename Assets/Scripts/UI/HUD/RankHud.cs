using System.Collections.Generic;
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

    [Tooltip("조각이 꽂힌 프레임부터 게이지가 목표치까지 가는 시간. 전진은 한 번뿐이라 이 값이 곧 '차오르는 시간' 전부다.\n" +
             "승급 연출에서 별을 한 칸씩 채우는 시간도 같은 값을 쓴다 — 조각 간격(pipStep)보다 짧게 둔다.")]
    [SerializeField] float gaugeAdvanceDuration = 0.25f;

    [Header("승급 연출")]
    [Tooltip("연출을 시작하기 전 뜸. 화면이 눈에 들어온 뒤에 별이 채워져야 '변했다'가 보인다.")]
    [SerializeField] float enterDelay = 0.35f;

    [Tooltip("마디를 되짚어 내려갈 때(별이 다시 모자라짐) 별이 움츠러드는 세기. 전진 쪽 점등은 '별 점등' 항목이 맡는다.")]
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

    [Header("포인트 손실 반응")]
    [Tooltip("게이지가 줄어든 자리까지 뒤로 미끄러지는 시간. 획득은 느리고 시끄럽게, 손실은 빠르고 조용하게 — 짧게 둔다.")]
    [SerializeField] float lossSlideDuration = 0.2f;

    // 활성 인스턴스(연출 호출자가 찾는 창구). 로비에 하나뿐이지만 탭 토글로 꺼지므로 활성분만 든다.
    static RankHud s_instance;

    // 최초 렌더를 Start로 미루기 위한 표식 — RankConfig 주입(DataLibrary.Awake)보다 OnEnable이 먼저 돌 수 있다.
    bool m_started;

    // 진행 중 티어 변화 연출. 살아있는 동안 Render가 표시를 최종값으로 덮지 않는다(연출이 과거 상태에서 출발한다).
    Sequence m_tierSeq;

    // 포인트 손실 반응. 배지 색·스케일을 함께 잡으므로 획득 플래시와 겹쳐 돌지 않게 따로 든다.
    Sequence m_lossSeq;

    // 조각이 꽂힐 때의 밝아짐.
    Tween m_flashTween;

    // 별 하나가 켜지는 한 박(플래시 + 팝 + 고스트 + 넘겨받기). 다음 별이 켜지면 앞 박을 걷고 다시 세운다.
    Sequence m_starSeq;

    // 켜진 별 자리에 띄운 고스트들. 시퀀스가 어디서 끊겨도 여기로 걷는다.
    readonly List<GameObject> m_ghosts = new List<GameObject>();

    // 별 채움·밑판의 저작 색. 연출이 물들인 뒤 되돌릴 기준이다.
    Color[] m_starFillColors;
    Color[] m_starPlateColors;

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

    /// <summary>조각이 배지에 꽂힌 프레임. 배지와 채워지는 별이 함께 튀고, 게이지가 목표치까지 한 번에 전진한다.</summary>
    public void PlayGainImpact()
    {
        UiPunch.Play(this.BadgeRect, this.gainImpactPunch);
        UiPunch.Play(this.PipTransform(this.FillingStarIndex()), this.gainStarPunch);

        // 한 판에 반 칸씩 움직이는 값이라 잘게 쪼개면 사건이 밍밍해진다 — 조각 하나에 전진 한 번이다.
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

        this.CacheStarColors();
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
        // 후퇴는 조용해야 한다 — 되짚어 내려간 자리를 축하할 이유가 없다.
        if (!_forward)
        {
            UiPunch.Play(this.PipTransform(_index), -this.pipPunch);
            return;
        }

        this.PlayStarLit(_index);
    }

    // 별이 꽉 차는 프레임에 사건을 몰아넣는다(흰 플래시 + 1.5배에서 내려앉기 + 고스트 확산),
    // 한 박 뒤 다음 별이 시선을 넘겨받는다. 마지막 별은 넘길 곳이 없어 여운만 남긴다.
    void PlayStarLit(int _index)
    {
        this.KillStarBeat();

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

        this.SpawnStarGhost(t_gauge, _index);
        this.StageHandoff(t_gauge, _index + 1);

        this.m_starSeq.OnKill(() =>
        {
            this.m_starSeq = null;
            this.ClearGhosts();
        });
    }

    // 켜진 별 자리에 밑판 스프라이트를 복제해 띄우고 퍼지며 사라지게 한다(전용 링 텍스처가 없어 별 모양 그대로 쓴다).
    void SpawnStarGhost(RankStarGauge _gauge, int _index)
    {
        var t_plate = _gauge.StarPlate(_index);
        var t_rect  = _gauge.StarRect(_index);
        if (t_plate == null || t_rect == null || t_rect.parent == null) return;

        var t_go = new GameObject("StarGhost", typeof(RectTransform), typeof(CanvasRenderer),
                                  typeof(Image), typeof(LayoutElement));
        this.m_ghosts.Add(t_go);

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

        this.m_starSeq.Join(t_ghost.DOScale(this.ghostScale, this.ghostDuration).SetEase(Ease.OutQuad));
        this.m_starSeq.Join(t_img.DOFade(0f, this.ghostDuration).SetEase(Ease.OutQuad));
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
    void RestorePipScales()
    {
        this.KillStarBeat();

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

    // 점등 한 박을 걷고 남은 고스트를 지운다(참조를 먼저 비워 OnKill이 되돌아 들어오지 않게 한다).
    void KillStarBeat()
    {
        var t_seq = this.m_starSeq;
        this.m_starSeq = null;
        if (t_seq != null) t_seq.Kill();

        this.ClearGhosts();
    }

    void ClearGhosts()
    {
        for (int t_i = 0; t_i < this.m_ghosts.Count; t_i++)
        {
            if (this.m_ghosts[t_i] == null) continue;
            Destroy(this.m_ghosts[t_i]);
        }

        this.m_ghosts.Clear();
    }
}
