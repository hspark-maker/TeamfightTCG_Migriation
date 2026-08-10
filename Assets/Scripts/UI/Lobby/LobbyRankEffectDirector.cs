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

    // 커버 아래에서 미리 세워 둔 승급 연출(재생 대기 중). 조립하는 순간 표시가 과거로 되돌아간다.
    Sequence m_tierUp;

    void Start()
    {
        this.StartCoroutine(this.PlayWhenReady());
    }

    void OnDisable()
    {
        // 재생에 닿지 못한 채 꺼지면 정지한 시퀀스가 RankHud.Render의 연출 가드를 영영 막아 표시가 과거에 고착된다.
        if (this.m_tierUp == null) return;

        this.m_tierUp.Kill();
        this.m_tierUp = null;
    }

    IEnumerator PlayWhenReady()
    {
        // 탭 선택(LobbyTabController.Start)과 레이아웃이 끝나야 배지 좌표가 확정된다(LobbyGainEffectDirector와 같은 이유).
        yield return null;
        Canvas.ForceUpdateCanvases();

        // 연출할 자리가 없어도 소비한다 — 남기면 다음 전투 결과에 옛 소식이 병합돼 두 배로 계산된다.
        if (!RankResultHandoff.TryConsume(out var t_result)) yield break;
        if (!RankHud.TryGet(out var t_hud)) yield break;

        // 승급은 커버 아래에서 세워 둔다 — 조립 시점에 핍·배지가 전투 직전으로 되돌아가야
        // 커버가 걷히는 순간 유저가 처음 보는 화면이 "오르기 전"이 된다.
        if (t_result.IsTierUp)
        {
            this.m_tierUp = t_hud.BuildTierUp(t_result.PrevTierIndex);
            this.m_tierUp.Pause();
        }

        yield return new WaitWhile(() => LoadingCoverView.IsCovering);

        if (this.startDelay > 0f) yield return new WaitForSeconds(this.startDelay);

        yield return this.PlayPointChange(t_hud, t_result);

        // 포인트가 다 찬 뒤에 별이 켜진다 — 순서가 뒤집히면 "왜 올랐는지"가 사라진다.
        var t_seq = this.m_tierUp;
        this.m_tierUp = null;
        if (t_seq != null) t_seq.Play();
    }

    // 증감 반응 1개를 재생하고 끝날 때까지 기다린다. 완료가 아니라 Kill을 기다린다 — 도중에 끊겨도 승급으로 넘어간다.
    IEnumerator PlayPointChange(RankHud _hud, RankApplyResult _result)
    {
        // 승급이 함께 왔으면 손실은 알리지 않는다 — 여러 판이 합쳐진 결과라 "올랐다"가 지배적인 소식이다.
        if (_result.Delta == 0 || (_result.Delta < 0 && _result.IsTierUp)) yield break;

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
        return this.pointBurst.BuildBurst((_arrived, _total) => _hud.PlayGainImpact(_arrived >= _total));
    }
}
