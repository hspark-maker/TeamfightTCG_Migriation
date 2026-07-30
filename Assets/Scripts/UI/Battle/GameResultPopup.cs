using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 결과 팝업의 등장 연출 진행자.
// 흐름: 암막 → 패널 팝 → 골드·랭크 라인 동시 진행(코인 분출·수렴, 도착마다 수치 롤링) → 안내문.
// 패배 팝업은 라인 등장까지만 하고 분출·롤링을 접는다 — 같은 스크립트를 승패 플래그로 갈라 쓴다.
//
// 보상·랭크는 전투가 끝나는 순간 TurnRunner→RewardService/RankManager가 이미 지급·영속화했다.
// 여기서는 확정된 값을 보여주기만 한다 — 재계산도, 재지급도 없다.
// 배선이 비어 있는 단계는 조용히 건너뛴다(연출이 진행을 막지 않게).
public class GameResultPopup : MonoBehaviour
{
    [Header("배선")]
    [SerializeField] RectTransform panel;
    [SerializeField] Button mainMenuButton;       // 전체화면 터치 영역(연출 중엔 스킵, 끝난 뒤엔 메인 이동)
    [SerializeField] string mainMenuScene = "LobbyScene";
    [SerializeField] CanvasGroup dimGroup;        // 암막(옵션)
    [SerializeField] TMP_Text rewardGoldText;     // 지급된 골드 표시용(표시 전용)
    [SerializeField] CoinBurstEffect coinBurst;   // 코인 분출·수렴(옵션)
    [SerializeField] TMP_Text rankPointText;      // 가감된 랭크 포인트 표시용(표시 전용)
    [SerializeField] CoinBurstEffect rankBurst;   // 랭크 포인트 아이콘 분출·수렴(옵션)
    [SerializeField] CanvasGroup hintGroup;       // "터치하면 메인 화면으로" 안내

    [Header("타이밍")]
    [SerializeField] float dimDuration = 0.2f;
    [SerializeField] float enterDuration = 0.45f;
    [SerializeField] float titleDuration = 0.35f;
    [SerializeField] float rewardRevealDuration = 0.3f; // 패널 등장 뒤 보상 라인이 팝하는 시간.
    [SerializeField] float goldRollDuration = 0.15f;    // 아이콘 하나가 닿을 때 수치가 굴러가는 시간(골드·랭크 공용).
    [SerializeField] float hintFadeDuration = 0.25f;

    [Header("연출 값")]
    [SerializeField] float titleDrop = 120f;      // 타이틀이 이만큼 위에서 떨어진다.
    [SerializeField] float goldPunch = 0.3f;      // 아이콘이 닿을 때 수치가 튀는 세기(골드·랭크 공용).

    Sequence revealSeq;   // 진행 중 등장 연출. 재진입 시 통째로 Kill해 좀비 시퀀스 누적 방지.

    RollingCounter m_gold;
    RollingCounter m_rank;

    bool m_revealDone;    // 연출 완료 여부. 진행 중 터치는 스킵, 완료 후 터치는 메인 이동.

    void Awake()
    {
        this.panel.localScale = Vector3.zero;
        this.mainMenuButton?.onClick.AddListener(HandleTouch);

        this.m_gold = new RollingCounter(this.rewardGoldText, gameObject, this.goldRollDuration, this.goldPunch);
        this.m_rank = new RollingCounter(this.rankPointText, gameObject, this.goldRollDuration, this.goldPunch);
    }

    void OnDisable()
    {
        // 연출 중 꺼지면 트윈만 남는다 — 여기서 정리.
        KillTweens();
    }

    /// <summary>
    /// 결과 팝업 노출. 두 값 모두 이미 지급·영속화된 값을 그대로 표시만 한다(_rankDelta는 패배 시 음수).
    /// _won=false면 분출·롤링을 통째로 접고 확정값만 띄운다 — 축하 연출은 승리의 몫이다.
    /// </summary>
    public void Show(long _rewardGold, long _rankDelta = 0, bool _won = true)
    {
        gameObject.SetActive(true);

        KillTweens();

        this.m_revealDone = false;

        ResetVisual(_rewardGold > 0 ? _rewardGold : 0, _rankDelta, _won);

        this.revealSeq = DOTween.Sequence().SetLink(gameObject);

        if (this.dimGroup != null)
            this.revealSeq.Append(this.dimGroup.DOFade(1f, this.dimDuration));

        this.revealSeq.Append(this.panel.DOScale(1f, this.enterDuration).SetEase(Ease.OutBack));

        bool t_lineStarted = false;

        // 골드·랭크는 같은 시점에 함께 굴러간다 — 첫 라인만 Append하고 나머지는 Join으로 겹친다.
        JoinCounter(BuildCounterLine(this.m_gold, this.coinBurst, _won), ref t_lineStarted);
        JoinCounter(BuildCounterLine(this.m_rank, this.rankBurst, _won), ref t_lineStarted);

        if (this.hintGroup != null)
            this.revealSeq.Append(this.hintGroup.DOFade(1f, this.hintFadeDuration));

        // 스킵(Complete)으로 와도 여기를 지난다 — 수치는 항상 확정값으로 안착한다.
        this.revealSeq.OnComplete(() =>
        {
            this.m_gold.Finish();
            this.m_rank.Finish();
            this.m_revealDone = true;
        });
    }

    // 라인 하나(수치 팝 → 아이콘 분출·수렴)를 독립 시퀀스로 만든다. 텍스트 미배선이면 null(라인 자체가 없음).
    // _animate=false면 라인이 등장만 하고 수치는 확정값에 박힌 채로 있는다(패배 팝업).
    Sequence BuildCounterLine(RollingCounter _counter, CoinBurstEffect _burst, bool _animate)
    {
        Tween t_reveal = _counter.BuildReveal(this.rewardRevealDuration);
        if (t_reveal == null) return null;

        Sequence t_line = DOTween.Sequence();
        t_line.Append(t_reveal);

        if (!_animate || _counter.Total == 0) return t_line;   // 가감이 없으면 굴릴 것도 없다.

        // 아이콘이 튀어 수치로 빨려들고, 닿을 때마다 그만큼 숫자가 굴러 오른다.
        if (_burst != null && _counter.Total > 0)
        {
            t_line.Append(_burst.BuildBurst(_counter.HandleArrived));
            return t_line;
        }

        // 분출이 미배선이거나 값이 음수면 아이콘 없이 수치만 한 번에 굴린다.
        t_line.AppendCallback(() => _counter.HandleArrived(1, 1));
        t_line.AppendInterval(this.goldRollDuration);
        return t_line;
    }

    // 첫 라인은 패널 등장 뒤에 붙이고(Append), 이후 라인은 같은 시점에 겹친다(Join).
    void JoinCounter(Sequence _line, ref bool _started)
    {
        if (_line == null) return;

        if (_started) this.revealSeq.Join(_line);
        else          this.revealSeq.Append(_line);

        _started = true;
    }

    // 연출 시작 상태로 되돌린다(재진입 대비).
    void ResetVisual(long _gold, long _rankDelta, bool _animate)
    {
        this.panel.localScale = Vector3.zero;

        if (this.dimGroup != null) this.dimGroup.alpha = 0f;
        if (this.hintGroup != null) this.hintGroup.alpha = 0f;

        // 라벨('골드'·'랭크 포인트')과 아이콘은 프리팹의 정적 요소, 여기선 가감 수치만 채운다.
        // 굴릴 값이 있으면 0에서 출발, 없으면 곧장 확정값을 보여준다.
        this.m_gold.Reset(_gold, _animate && _gold != 0);
        this.m_rank.Reset(_rankDelta, _animate && _rankDelta != 0);
    }

    // 전체화면 터치. 연출 중이면 스킵, 끝난 뒤면 메인 화면으로.
    void HandleTouch()
    {
        if (!this.m_revealDone)
        {
            if (this.revealSeq != null && this.revealSeq.IsActive()) this.revealSeq.Complete(true);
            else this.m_revealDone = true;   // 시퀀스가 이미 사라진 예외 상황 — 다음 터치가 먹히게.
            return;
        }

        BattleCleanup.LoadScene(this.mainMenuScene);
    }

    void KillTweens()
    {
        this.revealSeq?.Kill();
        this.revealSeq = null;
        this.m_gold?.Kill();
        this.m_rank?.Kill();
    }

    // 수치 텍스트 한 줄 + 그 롤링·펀치 상태 한 벌.
    // 골드와 랭크가 같은 연출을 쓰므로, 값 종류가 늘어도 필드·메서드를 복제하지 않고 이 단위를 하나 더 만든다.
    class RollingCounter
    {
        readonly TMP_Text   m_text;
        readonly GameObject m_link;   // 트윈 수명을 팝업 오브젝트에 묶는다.
        readonly float      m_rollDuration;
        readonly float      m_punch;

        long  m_total;   // 이번 표시의 확정값. 아이콘이 다 닿으면 이 값에 정확히 안착한다.
        long  m_shown;   // 현재 텍스트에 찍힌 값(롤링 시작점).
        Tween m_rollTween;
        Tween m_punchTween;

        public RollingCounter(TMP_Text _text, GameObject _link, float _rollDuration, float _punch)
        {
            m_text         = _text;
            m_link         = _link;
            m_rollDuration = _rollDuration;
            m_punch        = _punch;
        }

        public long Total => m_total;

        /// <summary>표시 시작 상태로 되돌린다. _willRoll이면 0에서 출발(아이콘이 값을 실어 나른다).</summary>
        public void Reset(long _total, bool _willRoll)
        {
            m_total = _total;
            if (m_text == null) return;

            m_text.transform.localScale = Vector3.zero;
            Render(_willRoll ? 0 : _total);
        }

        /// <summary>수치가 팝하며 등장하는 트윈. 텍스트가 없으면 null(호출자가 라인 전체를 건너뛴다).</summary>
        public Tween BuildReveal(float _duration)
        {
            if (m_text == null) return null;
            return m_text.transform.DOScale(1f, _duration).SetEase(Ease.OutBack);
        }

        /// <summary>아이콘 하나가 수치에 닿았다 — 그 몫만큼 숫자를 굴리고 살짝 튀긴다.</summary>
        public void HandleArrived(int _arrived, int _total)
        {
            if (m_text == null) return;

            // 마지막 하나는 나눗셈 오차 없이 확정값 그대로 — 표시액이 지급액과 어긋나지 않게.
            long t_goal = _arrived >= _total
                ? m_total
                : (long)(m_total * (double)_arrived / _total);

            long t_start = m_shown;

            m_rollTween?.Kill();
            m_rollTween = DOVirtual.Float(0f, 1f, m_rollDuration,
                                          _t => Render(t_start + (long)((t_goal - t_start) * _t)))
                                   .SetLink(m_link)
                                   .OnComplete(() => Render(t_goal));

            // 이전 펀치는 완료시켜 죽인다(Kill(true)) — 스케일이 중간값에 눌린 채 남지 않게.
            m_punchTween?.Kill(true);
            m_punchTween = m_text.transform
                                 .DOPunchScale(Vector3.one * m_punch, m_rollDuration, 1, 0.6f)
                                 .SetLink(m_link);
        }

        /// <summary>롤링을 끊고 확정값에 안착시킨다(정상 종료·스킵 공용).</summary>
        public void Finish()
        {
            m_rollTween?.Kill();
            m_rollTween = null;
            m_punchTween?.Kill(true);
            m_punchTween = null;
            Render(m_total);
        }

        public void Kill()
        {
            m_rollTween?.Kill();
            m_rollTween = null;
            m_punchTween?.Kill();
            m_punchTween = null;
        }

        void Render(long _value)
        {
            m_shown = _value;
            if (m_text == null) return;

            // 획득은 부호를 붙이고, 감소는 N0가 이미 '-'를 찍는다. 0은 부호 없이.
            m_text.text = _value > 0 ? $"+{_value:N0}" : _value < 0 ? $"{_value:N0}" : "0";
        }
    }
}
