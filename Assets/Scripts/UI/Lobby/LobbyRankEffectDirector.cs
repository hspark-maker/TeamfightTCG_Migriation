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
    [Tooltip("조각 수는 증감량을 따라간다 — 숫자를 지운 화면에서 '얼마나'를 비추는 유일한 단서다.")]
    [SerializeField] int pieceMin = 4;
    [SerializeField] int pieceMax = 10;

    [Tooltip("아래(핍 방향)를 비운 부채꼴. 조각이 핍을 덮으면 곧 켜질 별이 가려진다.")]
    [SerializeField] float angleStart = 300f;
    [SerializeField] float angleSpan = 300f;
    [SerializeField] float scatterRadius = 170f;
    [SerializeField] float gatherDuration = 0.32f;

    [Header("공통")]
    [Tooltip("커버가 걷힌 뒤 시작까지의 뜸. 상단바 코인이 먼저 착지하도록 비켜 준다.")]
    [SerializeField] float startDelay = 0.15f;

    // 커버 아래에서 미리 세워 둔 티어 변화 연출(승급·강등, 재생 대기 중). 조립하는 순간 표시가 과거로 되돌아간다.
    Sequence m_tierChange;

    static LobbyRankEffectDirector s_instance;

    /// <summary>랭크 연출이 끝났다. <b>보여줄 것이 없어 그냥 지나간 경우도 포함</b>해서 알린다 —
    /// 이 뒤에 이어 붙는 안내(온보딩 4챕터)가 신호를 놓치면 그 자리에서 영영 멈춘다.</summary>
    public static event System.Action OnAnyFinished;

    /// <summary>이 씬에 랭크 연출 디렉터가 있는가. 없으면 기다릴 신호도 없다는 뜻이다.</summary>
    public static bool Exists => s_instance != null;

    void Awake()
    {
        s_instance = this;
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
            if (!RankHud.TryGet(out var t_hud)) yield break;

            // 진행 호도 전투 직전 위치에서 출발해야 한다. Delta는 클램프 뒤 실증감이라 현재 포인트에서 빼면 그때 값이다.
            t_hud.PrepareProgress(RankManager.Points - t_result.Delta);

            // 티어 변화는 커버 아래에서 세워 둔다 — 조립 시점에 별·배지가 전투 직전으로 되돌아가야
            // 커버가 걷히는 순간 유저가 처음 보는 화면이 "변하기 전"이 된다.
            // 강등은 세우지 않는다 — 포인트 바닥이 현재 단계 진입선이라 티어가 내려가는 일 자체가 없다.
            if (t_result.IsTierUp)
            {
                this.m_tierChange = t_hud.BuildTierUp(t_result.PrevTierIndex);
                this.m_tierChange.Pause();
            }

            yield return new WaitWhile(() => LoadingCoverView.IsCovering);

            if (this.startDelay > 0f) yield return new WaitForSeconds(this.startDelay);

            yield return this.PlayPointChange(t_hud, t_result);

            // 포인트가 다 찬 뒤에 별이 켜진다 — 순서가 뒤집히면 "왜 올랐는지"가 사라진다.
            var t_seq = this.m_tierChange;
            this.m_tierChange = null;
            if (t_seq == null) yield break;

            t_seq.Play();

            // 별이 다 켜질 때까지 기다린다. 화면에 보이는 것은 전과 같고 코루틴이 끝나는 시점만 정확해진다 —
            // 뒤에 이어 붙는 안내가 승급 연출 위에 겹쳐 뜨지 않으려면 이 끝이 진짜 끝이어야 한다.
            yield return t_seq.WaitForKill();
        }
        finally
        {
            OnAnyFinished?.Invoke();
        }
    }

    // 증감 반응 1개를 재생하고 끝날 때까지 기다린다. 완료가 아니라 Kill을 기다린다 — 도중에 끊겨도 티어 연출로 넘어간다.
    IEnumerator PlayPointChange(RankHud _hud, RankApplyResult _result)
    {
        if (_result.Delta == 0) yield break;

        // 증감 부호와 티어 변화 방향이 어긋나면(여러 판이 합쳐진 결과) 티어 쪽이 지배적인 소식이라 조각은 생략한다.
        if (_result.Delta > 0 && _result.IsTierDown) yield break;

        // 강등이 뒤따르면 손실 반응도 생략한다 — 배지가 두 번 식는다.
        if (_result.Delta < 0 && (_result.IsTierUp || _result.IsTierDown)) yield break;

        var t_seq = _result.Delta > 0 ? this.BuildGain(_hud, _result.Delta) : _hud.BuildLossReaction();
        if (t_seq == null) yield break;

        t_seq.Play();
        yield return t_seq.WaitForKill();
    }

    // 조각이 배지 자리에서 튀어 제자리로 빨려든다(재화가 수치 자리에서 튀는 것과 같은 문법).
    Sequence BuildGain(RankHud _hud, long _delta)
    {
        if (this.pointBurst == null)
        {
            Debug.LogWarning("[LobbyRankEffectDirector] pointBurst 미배선 — 포인트 획득 연출을 건너뛴다.");
            return null;
        }

        int t_pieces = Mathf.Clamp((int)_delta, this.pieceMin, this.pieceMax);
        this.pointBurst.Configure(this.pieceSprite, _hud.BadgeRect, _hud.BadgeRect, t_pieces,
                                  this.angleStart, this.angleSpan, this.scatterRadius, this.gatherDuration);

        // 스프라이트가 비어 있으면 BuildBurst가 빈 시퀀스로 즉시 통지한다 — 조각 없이 배지 펀치만 남는다.
        return this.pointBurst.BuildBurst((_arrived, _total) => _hud.PlayGainImpact(_arrived, _total));
    }
}
