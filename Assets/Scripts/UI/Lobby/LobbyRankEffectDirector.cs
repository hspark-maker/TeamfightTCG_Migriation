using System.Collections;
using DG.Tweening;
using UnityEngine;

// 전투에서 넘어온 랭크 정산을 로비에서 한 번 보여준다(포인트 조각 → 승급 순서).
// 재화 획득(LobbyGainEffectDirector)과 따로 서는 이유: 랭크는 커버가 걷힌 뒤 시작해야 하고, 재화 쪽은 지금도 커버 아래에서 시작한다.
// 각 단계를 한 시퀀스에 중첩하지 않고 순서대로 재생한다 — RankHud가 자기 연출을 스스로 Kill하므로(탭 전환 등)
// 중첩된 하위를 밖에서 죽이면 부모 시퀀스가 어긋난다.
public class LobbyRankEffectDirector : MonoBehaviour
{
    [Header("배선")]
    [Tooltip("포인트 조각 분출기. 반드시 GainEffectLayer 하위에 둘 것 — 탭과 함께 꺼지는 노드에 두면 OnDisable이 비행 중 조각을 걷어간다.")]
    [SerializeField] CoinBurstEffect pointBurst;

    [Tooltip("조각 스프라이트. 전투 결과 팝업의 랭크 줄과 같은 별을 쓴다(방금 본 것이 로비로 이어지게).")]
    [SerializeField] Sprite pieceSprite;

    [Header("포인트 획득")]
    [Tooltip("조각이 튀어 오르는 방향(도). 90이면 배지 바로 위 — 아래(별 방향)로 두면 곧 켜질 별이 가려진다.")]
    [SerializeField] float angleStart = 90f;
    [Tooltip("조각이 튀어 오르는 거리(px). 실제 도달은 이 값의 0.7배다.")]
    [SerializeField] float scatterRadius = 170f;
    [Tooltip("조각이 튀어 오르는 시간.")]
    [SerializeField] float scatterDuration = 0.16f;
    [Tooltip("조각이 배지로 빨려드는 시간. 튀어오름과 합쳐 획득 연출 전체 길이(0.4초)가 된다.")]
    [SerializeField] float gatherDuration = 0.24f;

    [Header("공통")]
    [Tooltip("커버가 걷힌 뒤 시작까지의 뜸. 상단바 코인이 먼저 착지하도록 비켜 준다.")]
    [SerializeField] float startDelay = 0.15f;

    [Tooltip("승급 오버레이가 걷힌 뒤 랭크 보상 목록이 뜨기까지의 뜸. 두 화면이 맞물려 뜨면 한 화면이 갈아끼워진 것처럼 읽힌다.")]
    [SerializeField] float rewardPanelDelay = 0.25f;

    // 조각은 하나뿐이다 — 매 전투마다 보는 연출이라 여러 개로 쪼개면 사건이 밍밍해진다.
    // 얼마나 올랐는지는 게이지가 한 번에 전진하는 폭이 답한다(조각 수가 아니다).
    const int PIECE_COUNT = 1;

    // 커버 아래에서 미리 세워 둔 티어 변화 연출(승급·강등, 재생 대기 중). 조립하는 순간 표시가 과거로 되돌아간다.
    Sequence m_tierChange;

    static LobbyRankEffectDirector s_instance;

    // 이번 씬의 랭크 연출이 이미 끝났는가. 이벤트만으로는 늦게 온 쪽이 신호를 놓친다 —
    // 나중에 매치 탭으로 들어오는 표시가 뒤늦게라도 물어볼 수 있게 래치로 남긴다.
    static bool s_finished;

    /// <summary>랭크 연출이 끝났다. <b>보여줄 것이 없어 그냥 지나간 경우도 포함</b>해서 알린다 —
    /// 이 뒤에 이어 붙는 안내(온보딩 4챕터)가 신호를 놓치면 그 자리에서 영영 멈춘다.</summary>
    public static event System.Action OnAnyFinished;

    /// <summary>이 씬에 랭크 연출 디렉터가 있는가. 없으면 기다릴 신호도 없다는 뜻이다.</summary>
    public static bool Exists => s_instance != null;

    /// <summary>랭크 연출이 아직 도는 중인가(= <see cref="OnAnyFinished"/> 전). 연출이 끝나기를 기다렸다가
    /// 표시를 바꾸는 쪽이 읽는다 — 구독보다 먼저 끝나 신호를 놓쳐도 이 값으로 따라잡을 수 있다.</summary>
    public static bool Playing => s_instance != null && !s_finished;

    void Awake()
    {
        s_instance = this;
        s_finished = false;
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    void Start()
    {
        this.StartCoroutine(this.PlayWhenReady());
    }

    void OnDisable()
    {
        // 재생에 닿지 못한 채 꺼지면 정지한 시퀀스가 RankHud.Render의 연출 가드를 영영 막아 표시가 과거에 고착된다.
        if (this.m_tierChange == null) return;

        this.m_tierChange.Kill();
        this.m_tierChange = null;
    }

    IEnumerator PlayWhenReady()
    {
        // 끝을 알리는 자리는 여기 하나다 — 보여줄 것이 없어 중간에 빠져나가는 길이 여럿이라,
        // 각 return마다 알리면 언젠가 한 곳을 빠뜨린다(그 길로 나가면 기다리던 안내가 영영 멈춘다).
        try
        {
            // 탭 선택(LobbyTabController.Start)과 레이아웃이 끝나야 배지 좌표가 확정된다(LobbyGainEffectDirector와 같은 이유).
            yield return null;
            Canvas.ForceUpdateCanvases();

            // 연출할 자리가 없어도 소비한다 — 남기면 다음 전투 결과에 옛 소식이 병합돼 두 배로 계산된다.
            if (!RankResultHandoff.TryConsume(out var t_result)) yield break;

            // 배지 연출은 랭크 탭이 떠 있을 때만 성립한다. 여기서 통째로 빠져나가면 안 된다 —
            // 승급 오버레이는 어느 탭에 있든 떠야 하는 화면이라, 배지가 없다는 이유로 함께 사라지면
            // 메타에서 제일 큰 사건이 탭 위치에 따라 조용히 스킵된다.
            if (RankHud.TryGet(out var t_hud)) yield return this.PlayHudEffects(t_hud, t_result);
            else                               yield return new WaitWhile(() => LoadingCoverView.IsCovering);

            yield return this.PlayPromote(t_result);
        }
        finally
        {
            // 오버레이가 뜨지 않는 길(디버그 티어 이동 등)로 빠져도 표시가 옛 등급에 고착되지 않게 여기서 확정한다.
            if (RankHud.TryGet(out RankHud t_hud)) t_hud.ApplyTierInstant();

            // 래치를 먼저 세운다 — 이 알림을 받아 표시를 다시 그리는 쪽이 Playing을 곧바로 되물을 수 있어야 한다.
            s_finished = true;
            OnAnyFinished?.Invoke();
        }
    }

    // 배지에서 벌어지는 몫(포인트 조각 → 별 → 티어 변화). 랭크 탭이 떠 있을 때만 지나는 길이다.
    IEnumerator PlayHudEffects(RankHud _hud, RankApplyResult _result)
    {
        // 진행 호도 전투 직전 위치에서 출발해야 한다. Delta는 클램프 뒤 실증감이라 현재 포인트에서 빼면 그때 값이다.
        _hud.PrepareProgress(RankManager.Points - _result.Delta);

        // 티어 변화는 커버 아래에서 세워 둔다 — 조립 시점에 별·배지가 전투 직전으로 되돌아가야
        // 커버가 걷히는 순간 유저가 처음 보는 화면이 "변하기 전"이 된다.
        // 강등은 세우지 않는다 — 포인트 바닥이 현재 단계 진입선이라 티어가 내려가는 일 자체가 없다.
        if (_result.IsTierUp)
        {
            this.m_tierChange = _hud.BuildTierUp(_result.PrevTierIndex);
            this.m_tierChange.Pause();
        }

        yield return new WaitWhile(() => LoadingCoverView.IsCovering);

        if (this.startDelay > 0f) yield return new WaitForSeconds(this.startDelay);

        yield return this.PlayPointChange(_hud, _result);

        // 포인트가 다 찬 뒤에 별이 켜진다 — 순서가 뒤집히면 "왜 올랐는지"가 사라진다.
        var t_seq = this.m_tierChange;
        this.m_tierChange = null;
        if (t_seq == null) yield break;

        t_seq.Play();

        // 별이 다 켜질 때까지 기다린다. 화면에 보이는 것은 전과 같고 코루틴이 끝나는 시점만 정확해진다 —
        // 뒤에 이어 붙는 안내가 승급 연출 위에 겹쳐 뜨지 않으려면 이 끝이 진짜 끝이어야 한다.
        yield return t_seq.WaitForKill();
    }

    // 티어가 오른 판을 전면 오버레이로 세우고, 닫힐 때까지 기다린다.
    // 단계 상승(브론즈 1 → 브론즈 2)·등급 승급(승급전 승리)·첫 진입(언랭크 → 브론즈 1)이 모두 같은 길로 온다 —
    // 별 넷을 채워야 오는 자리라 네 판에 한 번뿐이고, 티어마다 받을 보상이 걸려 있어 화면을 세울 값이 있다.
    IEnumerator PlayPromote(RankApplyResult _result)
    {
        if (!_result.IsTierUp) yield break;

        // 첫 진입은 승급전을 거치지 않지만, 게임에서 처음 얻는 등급이라 오히려 제일 큰 판이다.
        bool t_firstEntry = _result.PrevTierIndex < 0;

        if (!RankManager.TryGetTier(_result.TierIndex, out RankTier t_tier)) yield break;
        if (!RankPromoteOverlay.TryGet(out RankPromoteOverlay t_overlay)) yield break;

        bool t_closed = false;
        t_overlay.Show(t_tier,
                       // 암전이 덮은 프레임에 로비 표시를 새 티어로 갈아끼운다(별 줄도 여기서 비워진다) — 배지 안무는 여기서 돌지 않는다.
                       // 첫 진입만 별 줄을 감춘 채 남긴다. 그 줄이 드러나는 것이 오버레이 다음 박이다.
                       _onCovered: () => { if (RankHud.TryGet(out RankHud t_hud)) t_hud.ApplyTierInstant(t_firstEntry); },
                       _onClose: () => t_closed = true);

        // 화면이 걷힐 때까지 기다린다 — 이 뒤가 곧 랭크 연출의 끝(OnAnyFinished)이라,
        // 여기서 안 기다리면 튜토리얼 안내가 오버레이 위에 겹친다.
        // IsOpen도 함께 본다: 콜백을 거치지 않고 꺼지는 길(부모 비활성)에서 여기 걸리면 뒤따르는 안내가 영영 멈춘다.
        yield return new WaitUntil(() => t_closed || !RankPromoteOverlay.IsOpen);

        // 보상이 먼저다 — 오버레이가 걷힌 그 자리에서 곧바로 이어져야 "승급했으니 이걸 받아라"로 읽힌다.
        // 배지·별 연출 뒤로 밀면 두 사건 사이가 벌어져 목록이 따로 뜬 화면처럼 보인다.
        yield return this.PlayRewardPanel();

        yield return this.PlayFirstEntryReveal(t_firstEntry);
    }

    // 승급 오버레이를 실제로 본 판에만 이어 붙는 보상 목록.
    // 새 티어의 보상은 소식을 들은 그 자리에서 받는 것이 자연스럽다 — 배지만 보고 목록을 따로 찾아가게 두지 않는다.
    // 열지 않는 길이 셋 있다: 기능이 아직 잠겨 있거나(튜토리얼 진행 중), 받을 것이 없거나, 풀이 없을 때.
    IEnumerator PlayRewardPanel()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.RankReward)) yield break;
        if (RankRewardManager.TopClaimableIndex < 0) yield break;
        if (UIPoolManager.Instance == null) yield break;

        if (this.rewardPanelDelay > 0f) yield return new WaitForSeconds(this.rewardPanelDelay);

        RankRewardPanel t_panel = UIPoolManager.Instance.AddOrUpdateUI<RankRewardPanel>();
        if (t_panel == null) yield break;

        // 닫힐 때까지 기다린다 — 이 뒤가 곧 랭크 연출의 끝(OnAnyFinished)이라, 안 기다리면 튜토리얼 안내가 목록 위에 겹친다.
        // 비활성으로 빠지는 길도 닫힘으로 본다: 여기서 영영 멈추면 뒤따르는 안내가 통째로 잠긴다.
        yield return new WaitWhile(() => t_panel != null && t_panel.isActiveAndEnabled && t_panel.isShow);
    }

    // 첫 진입의 마지막 박 — 오버레이가 걷힌 자리에 별 줄이 드러난다.
    // 어느 길로 빠져도 표시는 finally의 ApplyTierInstant()가 최종 상태로 확정한다.
    IEnumerator PlayFirstEntryReveal(bool _firstEntry)
    {
        if (!_firstEntry) yield break;
        if (!RankHud.TryGet(out RankHud t_hud)) yield break;

        Sequence t_seq = t_hud.BuildFirstEntryReveal();
        if (t_seq == null) yield break;

        t_seq.Play();
        yield return t_seq.WaitForKill();
    }

    // 증감 반응 1개를 재생하고 끝날 때까지 기다린다. 완료가 아니라 Kill을 기다린다 — 도중에 끊겨도 티어 연출로 넘어간다.
    IEnumerator PlayPointChange(RankHud _hud, RankApplyResult _result)
    {
        if (_result.Delta == 0) yield break;

        // 증감 부호와 티어 변화 방향이 어긋나면(여러 판이 합쳐진 결과) 티어 쪽이 지배적인 소식이라 조각은 생략한다.
        if (_result.Delta > 0 && _result.IsTierDown) yield break;

        // 강등이 뒤따르면 손실 반응도 생략한다 — 배지가 두 번 식는다.
        if (_result.Delta < 0 && (_result.IsTierUp || _result.IsTierDown)) yield break;

        var t_seq = _result.Delta > 0 ? this.BuildGain(_hud) : _hud.BuildLossReaction();
        if (t_seq == null) yield break;

        t_seq.Play();
        yield return t_seq.WaitForKill();
    }

    // 조각 하나가 배지 자리에서 튀어 제자리로 빨려든다(재화가 수치 자리에서 튀는 것과 같은 문법).
    Sequence BuildGain(RankHud _hud)
    {
        if (this.pointBurst == null)
        {
            Debug.LogWarning("[LobbyRankEffectDirector] pointBurst 미배선 — 포인트 획득 연출을 건너뛴다.");
            return null;
        }

        // 조각이 하나라 부채꼴 폭은 의미가 없다 — 0으로 두면 angleStart 방향 그대로 튄다.
        this.pointBurst.Configure(this.pieceSprite, _hud.BadgeRect, _hud.BadgeRect, PIECE_COUNT,
                                  this.angleStart, 0f, this.scatterRadius, this.gatherDuration,
                                  _scatterDuration: this.scatterDuration);

        // 스프라이트가 비어 있으면 BuildBurst가 빈 시퀀스로 즉시 통지한다 — 조각 없이 배지 펀치만 남는다.
        return this.pointBurst.BuildBurst((_arrived, _total) => _hud.PlayGainImpact());
    }
}
