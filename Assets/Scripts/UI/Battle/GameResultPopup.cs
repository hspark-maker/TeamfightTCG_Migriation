using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 결과 팝업의 등장 연출 진행자.
// 흐름: 암막 → 패널 팝 → 골드 줄(살아남은 카드가 한 장씩 골드로 빨려들며 계단식 가산) → 랭크 줄 → 안내문.
// 패배 팝업은 라인 등장까지만 하고 분출·롤링을 접는다 — 같은 스크립트를 승패 플래그로 갈라 쓴다.
public class GameResultPopup : MonoBehaviour
{
    [Header("배선")]
    [SerializeField] RectTransform panel;
    [SerializeField] Button mainMenuButton;       // 전체화면 터치 영역(연출 중엔 스킵, 끝난 뒤엔 메인 이동)
    [SerializeField] string mainMenuScene = "LobbyScene";
    // 암막은 ScreenDim(Canvas 직하 공유 딤)이 그린다 — SafeArea 밖이라 노치까지 덮는다.
    // 이 그룹은 전체화면 터치 영역(mainMenuButton)을 얹은 판이라 투명하게 유지하고,
    // ScreenDim이 없는 씬에서만 자기가 암막 노릇을 한다.
    [SerializeField] CanvasGroup dimGroup;
    [SerializeField] TMP_Text rewardGoldText;     // 지급된 골드 표시용(표시 전용)

    [Tooltip("생존 카드가 빨려드는 목적지(보통 골드 아이콘). 미배선이면 골드 수치 텍스트로 간다.")]
    [SerializeField] RectTransform goldIconRect;

    [Tooltip("생존 카드 수를 적는 줄(옵션). {0}에 장수가 들어간다. 카드 위가 아니라 줄 밖에 두는 표기다.")]
    [SerializeField] TMP_Text survivorLabel;
    [SerializeField] string survivorFormat = "생존 {0}장";

    [Tooltip("카드 축이 설 수 없을 때(생존 목록 미전달)만 도는 폴백 분출. 카드가 날아가면 코인은 뜨지 않는다.")]
    [SerializeField] CoinBurstEffect coinBurst;   // 코인 분출·수렴(옵션)
    [SerializeField] TMP_Text rankPointText;      // 가감된 랭크 포인트 표시용(표시 전용)
    [SerializeField] CoinBurstEffect rankBurst;   // 랭크 포인트 아이콘 분출·수렴(옵션)

    [Tooltip("랭크 줄 묶음(라벨+아이콘+수치)의 루트. 배선하면 가감이 0인 전투(튜토리얼)에서 줄째로 감춘다. 미배선이면 0이 그대로 보인다.")]
    [SerializeField] GameObject rankLine;         // 랭크 줄 전체(옵션)

    [SerializeField] CanvasGroup hintGroup;       // "터치하면 메인 화면으로" 안내

    [Header("연출")]
    [SerializeField] SurvivorGoldFlight cardFlight = new SurvivorGoldFlight();

    [Header("타이밍")]
    [SerializeField] float dimDuration = 0.2f;
    [SerializeField] float dimAlpha = 0.94f;      // 암막 짙기(ScreenDim에 넘기는 값)
    [SerializeField] float enterDuration = 0.45f;
    [SerializeField] float rewardRevealDuration = 0.3f; // 패널 등장 뒤 보상 라인이 팝하는 시간.
    [SerializeField] float goldRollDuration = 0.15f;    // 아이콘 하나가 닿을 때 수치가 굴러가는 시간(골드·랭크 공용).
    [SerializeField] float hintFadeDuration = 0.25f;

    [Tooltip("골드 줄이 패널 팝의 몇 지점에서 끼어드는가(0=동시, 1=끝난 뒤). 매 판 보는 화면이라 겹쳐서 줄인다.")]
    [SerializeField, Range(0f, 1f)] float panelOverlap = 0.6f;

    [Tooltip("랭크 줄이 골드 줄 끝보다 이만큼 일찍 시작한다.")]
    [SerializeField] float rankOverlap = 0.3f;

    [Header("연출 값")]
    [SerializeField] float goldPunch = 0.3f;      // 아이콘이 닿을 때 수치가 튀는 세기(골드·랭크 공용).
    [SerializeField] float goldIconPunch = 0.18f; // 카드가 닿을 때 골드 아이콘이 튀는 세기.

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
        // 공유 딤은 소유자별 스택이라 내 요청을 반드시 걷어야 한다(안 걷으면 다음 판까지 화면이 어둡게 남는다).
        ScreenDim.Hide(this);
    }

    /// <summary>
    /// 결과 팝업 노출. 두 값 모두 이미 지급·영속화된 값을 그대로 표시만 한다(_rankDelta는 패배 시 음수).
    /// _won=false면 분출·롤링을 통째로 접고 확정값만 띄운다 — 축하 연출은 승리의 몫이다.
    /// _survivorCards는 승리 보상을 만든 생존 카드로, 한 장씩 골드로 빨려들며 계단을 만든다.
    /// null과 빈 리스트는 다른 뜻이다 — null은 "생존 수를 모른다"(코인 분출로 폴백),
    /// 빈 리스트는 "0장"(카드도 코인도 없이 하한 보상만). 합치면 카드 없이 코인이 터져 인과가 거꾸로 학습된다.
    /// </summary>
    public void Show(CurrencyGain _reward, long _rankDelta = 0, bool _won = true,
                     IReadOnlyList<CardData> _survivorCards = null)
    {
        gameObject.SetActive(true);

        KillTweens();

        this.m_revealDone = false;

        long t_gold = _reward.HasAmount ? _reward.Amount : 0;

        // 패배 보상은 카드와 무관한 고정액이라 카드 축을 세우지 않는다.
        IReadOnlyList<CardData> t_cards = _won ? _survivorCards : null;

        // 카드가 0장이면 굴릴 계단이 없다 — 0에서 출발시키면 어디서 왔는지 모를 숫자가 혼자 오른다.
        bool t_goldWillRoll = _won && t_gold != 0 && (t_cards == null || t_cards.Count > 0);

        ResetVisual(t_gold, _rankDelta, _won, t_goldWillRoll, t_cards);

        this.revealSeq = DOTween.Sequence().SetLink(gameObject);

        float t_cursor = 0f;

        // 암막 먼저, 그다음 패널 팝. ScreenDim은 트윈이 아니라 자기가 페이드하므로 그 시간만큼 시퀀스를 비운다.
        if (ScreenDim.IsAvailable)
        {
            ScreenDim.Show(this, this.dimAlpha, true, this.dimDuration);
            t_cursor += this.dimDuration;
        }
        else if (this.dimGroup != null)
        {
            this.revealSeq.Insert(0f, this.dimGroup.DOFade(1f, this.dimDuration));
            t_cursor += this.dimDuration;
        }

        this.revealSeq.Insert(t_cursor, this.panel.DOScale(1f, this.enterDuration).SetEase(Ease.OutBack));

        float t_end = t_cursor + this.enterDuration;

        // 골드 줄은 패널이 다 커지기 전에 끼어든다 — 순차로 두면 그만큼 결과 화면이 길어진다.
        float t_goldAt = t_cursor + this.enterDuration * this.panelOverlap;
        t_end = Mathf.Max(t_end, InsertLine(BuildGoldLine(_won, t_cards, out bool t_cardsFlew), t_goldAt));

        // 카드가 날아갈 때만 랭크를 뒤로 미룬다(꼬리는 물린다). 그 외에는 예전처럼 같은 시점에 겹친다.
        float t_rankAt = t_cardsFlew ? Mathf.Max(t_goldAt, t_end - this.rankOverlap) : t_goldAt;
        t_end = Mathf.Max(t_end, InsertLine(BuildCounterLine(this.m_rank, this.rankBurst, _won), t_rankAt));

        if (this.hintGroup != null)
            this.revealSeq.Insert(t_end, this.hintGroup.DOFade(1f, this.hintFadeDuration));

        // 스킵(Complete)으로 와도 여기를 지난다 — 수치는 항상 확정값으로 안착한다.
        this.revealSeq.OnComplete(() =>
        {
            this.m_gold.Finish();
            this.m_rank.Finish();
            this.m_revealDone = true;
        });
    }

    // 골드 줄. 수치가 팝하는 동안 생존 카드가 나란히 서고, 한 장씩 골드로 빨려들며 그만큼 계단이 오른다.
    // 카드 축이 설 수 없으면(_cards == null) 예전 코인 분출로 폴백한다 — 계단 없이 값만 툭 뜨는 것보다 낫다.
    // _cardsFlew는 호출자가 랭크 줄을 뒤로 미룰지 판단하는 데 쓴다.
    Sequence BuildGoldLine(bool _animate, IReadOnlyList<CardData> _cards, out bool _cardsFlew)
    {
        _cardsFlew = false;

        Tween t_reveal = this.m_gold.BuildReveal(this.rewardRevealDuration);
        if (t_reveal == null) return null;

        Sequence t_line = DOTween.Sequence();
        t_line.Insert(0f, t_reveal);

        if (!_animate || this.m_gold.Total <= 0) return t_line;
        if (_cards != null && _cards.Count == 0) return t_line;   // 카드 0장: 하한 보상이 이미 박혀 있다.

        RectTransform t_target = this.goldIconRect != null
                               ? this.goldIconRect
                               : (RectTransform)this.rewardGoldText.transform;

        Sequence t_flight = _cards == null ? null
                          : this.cardFlight.Build(_cards, (RectTransform)transform, t_target,
                                                  this.m_gold.HandleArrived,
                                                  () => UiPunch.Play(t_target, this.goldIconPunch, this.goldRollDuration));
        if (t_flight != null)
        {
            t_line.Insert(0f, t_flight);
            _cardsFlew = true;
            return t_line;
        }

        if (this.coinBurst != null)
        {
            t_line.Append(this.coinBurst.BuildBurst(this.m_gold.HandleArrived));
            return t_line;
        }

        t_line.AppendCallback(() => this.m_gold.HandleArrived(1, 1));
        t_line.AppendInterval(this.goldRollDuration);
        return t_line;
    }

    // 랭크 줄(수치 팝 → 아이콘 분출·수렴). 텍스트 미배선이면 null(라인 자체가 없음).
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

    // 라인 하나를 지정 시각에 얹고 그 끝 시각을 돌려준다(라인이 없으면 시작 시각 그대로).
    float InsertLine(Sequence _line, float _at)
    {
        if (_line == null) return _at;

        this.revealSeq.Insert(_at, _line);
        return _at + _line.Duration();
    }

    // 연출 시작 상태로 되돌린다(재진입 대비).
    void ResetVisual(long _gold, long _rankDelta, bool _animate, bool _goldWillRoll,
                     IReadOnlyList<CardData> _survivorCards)
    {
        this.panel.localScale = Vector3.zero;

        if (this.dimGroup != null) this.dimGroup.alpha = 0f;
        if (this.hintGroup != null) this.hintGroup.alpha = 0f;

        this.cardFlight.Reset();

        // 랭크를 정산하지 않는 전투(튜토리얼)에서는 "0"이 아니라 줄 자체가 없어야 한다.
        if (this.rankLine != null) this.rankLine.SetActive(_rankDelta != 0);

        // 생존 수를 모르는 경로(폴백)에서는 없는 값을 지어내지 않고 줄째로 감춘다.
        if (this.survivorLabel != null)
        {
            this.survivorLabel.gameObject.SetActive(_survivorCards != null);
            if (_survivorCards != null)
                this.survivorLabel.text = string.Format(this.survivorFormat, _survivorCards.Count);
        }

        // 라벨('골드'·'랭크 포인트')과 아이콘은 프리팹의 정적 요소, 여기선 가감 수치만 채운다.
        // 굴릴 값이 있으면 0에서 출발, 없으면 곧장 확정값을 보여준다.
        this.m_gold.Reset(_gold, _goldWillRoll);
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
        this.cardFlight?.Reset();   // 시퀀스가 끊기면 마지막 정리 콜백이 오지 않는다.
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
