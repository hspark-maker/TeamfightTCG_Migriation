using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 결과 팝업의 등장 연출 진행자.
// 흐름: 암막 → 패널 팝 → 골드 줄(살아남은 카드가 한 장씩 골드로 빨려들며 가산) → 랭크 줄 → 안내문.
// 골드와 랭크는 수치 연출을 한 벌(RollingCounter)로 공유한다 — 값을 실어 나르는 것만 카드와 별로 갈릴 뿐,
// 팝하는 시점도 계단으로 오르는 리듬도 두 줄이 같다.
// 패배 팝업은 라인 등장까지만 하고 분출·롤링을 접는다 — 같은 스크립트를 승패 플래그로 갈라 쓴다.
public class GameResultPopup : MonoBehaviour
{
    [Header("배선")]
    [Tooltip("정지 타이틀. banner를 배선하지 않은 화면(DefeatUI)만 이쪽으로 돈다 — enterDuration 동안 "
           + "OutBack으로 팝한다. banner가 있으면 여기엔 손대지 않으니 비워 둬라.")]
    [SerializeField] RectTransform panel;

    [Tooltip("결과 배너(승리/패배 타이틀). 배선하면 panel 대신 이 배너의 Animator 등장이 화면을 이끌고, "
           + "보상 줄이 그 도중에 끼어든다. 길이는 enterDuration이 아니라 배너의 showDuration을 따른다.")]
    [SerializeField] VictoryBannerView banner;
    [SerializeField] Button mainMenuButton;       // 전체화면 터치 영역(연출 중엔 스킵, 끝난 뒤엔 메인 이동)
    [SerializeField] string mainMenuScene = "LobbyScene";
    // 암막은 ScreenDim(Canvas 직하 공유 딤)이 그린다 — SafeArea 밖이라 노치까지 덮는다.
    // 이 그룹은 전체화면 터치 영역(mainMenuButton)을 얹은 판이라 투명하게 유지하고,
    // ScreenDim이 없는 씬에서만 자기가 암막 노릇을 한다.
    [SerializeField] CanvasGroup dimGroup;
    [SerializeField] TMP_Text rewardGoldText;     // 지급된 골드 표시용(표시 전용)

    [Tooltip("생존 카드가 빨려드는 목적지(보통 골드 아이콘). 미배선이면 골드 수치 텍스트로 간다.")]
    [SerializeField] RectTransform goldIconRect;

    [Tooltip("생존 카드 수를 적는 줄(옵션). {0}=생존 장수, {1}=데리고 나간 전체 장수(생존+전사). "
           + "카드 위가 아니라 줄 밖에 두는 표기다 — 카드 아트 위에는 어떤 글자도 얹지 않는다.")]
    [SerializeField] TMP_Text survivorLabel;
    [SerializeField] string survivorFormat = "{1}장 중 {0}장 생존";

    [Tooltip("카드 축이 설 수 없을 때(생존 목록 미전달)만 도는 폴백 분출. 카드가 날아가면 코인은 뜨지 않는다.")]
    [SerializeField] CoinBurstEffect coinBurst;   // 코인 분출·수렴(옵션)
    [SerializeField] TMP_Text rankPointText;      // 가감된 랭크 포인트 표시용(표시 전용)
    [SerializeField] CoinBurstEffect rankBurst;   // 랭크 포인트 아이콘 분출·수렴(옵션)

    [Tooltip("랭크 줄 묶음(라벨+아이콘+수치)의 루트. 배선하면 가감이 0인 전투(튜토리얼)에서 줄째로 감춘다. 미배선이면 0이 그대로 보인다.")]
    [SerializeField] GameObject rankLine;         // 랭크 줄 전체(옵션)

    [Tooltip("골드 줄 묶음(라벨+아이콘+수치)의 루트. 배선하면 전투 보상이 없는 전투(모험)에서 줄째로 감춘다. 미배선이면 0이 그대로 보인다.\n" +
             "일반 전투는 패배도 loseGold가 있어 이 줄이 사라지지 않는다.")]
    [SerializeField] GameObject goldLine;         // 골드 줄 전체(옵션)

    [SerializeField] CanvasGroup hintGroup;       // "터치하면 메인 화면으로" 안내

    [Header("연출")]
    [SerializeField] SurvivorGoldFlight cardFlight = new SurvivorGoldFlight();

    [Header("타이밍")]
    [SerializeField] float dimDuration = 0.2f;
    [SerializeField] float dimAlpha = 0.94f;      // 암막 짙기(ScreenDim에 넘기는 값)
    [SerializeField] float enterDuration = 0.45f;
    [SerializeField] float rewardRevealDuration = 0.3f; // 패널 등장 뒤 보상 라인이 팝하는 시간.
    [Tooltip("아이콘 하나가 닿을 때마다 그 몫만큼 수치가 굴러가는 시간(골드·랭크 공용). 생존 카드도 별도 "
           + "한 장씩 닿으므로 두 줄 다 계단으로 오른다 — 카드가 날아가는 시간(SurvivorGoldFlight의 "
           + "flyDuration)보다 많이 짧으면 숫자가 카드보다 먼저 도착해 인과가 끊긴다.")]
    [SerializeField] float goldRollDuration = 0.15f;
    [SerializeField] float hintFadeDuration = 0.25f;

    [Tooltip("골드 줄이 타이틀 등장(배너 showDuration 또는 enterDuration)의 몇 지점에서 끼어드는가(0=동시, 1=끝난 뒤). 매 판 보는 화면이라 겹쳐서 줄인다.")]
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
        // 타이틀은 배너든 패널이든 하나는 있어야 한다. 둘 다 비면 제목 없이 보상 줄만 뜨는데,
        // 매 판 보는 화면이라 조용히 넘어가면 아무도 배선이 끊긴 줄 모른다.
        if (this.banner == null && this.panel == null)
            Debug.LogError($"[{name}] 결과창에 타이틀이 없다 — banner 또는 panel 중 하나는 배선해야 한다.", this);

        // 배너가 화면을 이끄는 배선에서는 패널 스케일에 손대지 않는다 — Animator 포즈와 싸운다.
        if (this.banner == null && this.panel != null)
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
    /// _survivorCards는 승리 보상을 만든 생존 카드로, 왼쪽부터 한 장씩 차례로 골드로 빨려든다.
    /// null과 빈 리스트는 다른 뜻이다 — null은 "생존 수를 모른다"(코인 분출로 폴백),
    /// 빈 리스트는 "0장"(카드도 코인도 없이 하한 보상만). 합치면 카드 없이 코인이 터져 인과가 거꾸로 학습된다.
    /// _fallenCards는 이번 판에 잃은 카드로, 같은 줄 오른쪽에 흑백으로 서기만 한다 —
    /// 보상에도 골드 가산의 분모에도 관여하지 않는다. 오직 "몇 장 중"을 보여주는 몫이다.
    /// </summary>
    public void Show(CurrencyGain _reward, long _rankDelta = 0, bool _won = true,
                     IReadOnlyList<int> _survivorCards = null,
                     IReadOnlyList<int> _fallenCards = null)
    {
        gameObject.SetActive(true);

        KillTweens();

        this.m_revealDone = false;

        long t_gold = _reward.HasAmount ? _reward.Amount : 0;

        // 패배 보상은 카드와 무관한 고정액이라 카드 축을 세우지 않는다(줄이 없으면 전사자도 세울 자리가 없다).
        IReadOnlyList<int> t_cards  = _won ? _survivorCards : null;
        IReadOnlyList<int> t_fallen = _won ? _fallenCards   : null;

        // 카드가 0장이면 값을 실어 나를 것이 없다 — 0에서 출발시키면 어디서 왔는지 모를 숫자가 혼자 오른다.
        bool t_goldWillRoll = _won && t_gold != 0 && (t_cards == null || t_cards.Count > 0);

        ResetVisual(t_gold, _rankDelta, _won, t_goldWillRoll, t_cards, t_fallen);

        // 결과 연출은 통째로 unscaled로 돈다. 배너 Animator가 unscaled로 못박혀 있는 데다,
        // 부전승 경로(TurnRunner의 _withBeat:false)는 결정타 강조가 눌러둔 timeScale을
        // 되돌리지 않고 이 팝업으로 들어온다 — scaled로 두면 배너만 정상 속도로 서고 보상 줄이 기어온다.
        this.revealSeq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);

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

        // 타이틀 등장. 배너가 배선돼 있으면 그 Animator 재생이 이 자리를 대신한다 —
        // 트윈이 아니라 자기가 도므로 시퀀스는 그 길이만큼 비워 두고 뒷줄을 그 위에 겹친다.
        float t_enter = this.enterDuration;
        if (this.banner != null)
        {
            t_enter = this.banner.ShowDuration;
            this.revealSeq.InsertCallback(t_cursor, this.banner.Show);
        }
        else if (this.panel != null)
        {
            this.revealSeq.Insert(t_cursor, this.panel.DOScale(1f, this.enterDuration).SetEase(Ease.OutBack));
        }

        float t_end = t_cursor + t_enter;

        // 골드 줄은 타이틀이 다 서기 전에 끼어든다 — 순차로 두면 그만큼 결과 화면이 길어진다.
        float t_goldAt = t_cursor + t_enter * this.panelOverlap;
        t_end = Mathf.Max(t_end, InsertLine(BuildGoldLine(_won, t_cards, t_fallen, out bool t_cardsFlew), t_goldAt));

        // 카드가 날아갈 때만 랭크를 뒤로 미룬다(꼬리는 물린다). 그 외에는 예전처럼 같은 시점에 겹친다.
        float t_rankAt = t_cardsFlew ? Mathf.Max(t_goldAt, t_end - this.rankOverlap) : t_goldAt;
        t_end = Mathf.Max(t_end, InsertLine(BuildCounterLine(this.m_rank, this.rankBurst, _won), t_rankAt));

        // 배너는 콜백 한 번으로 켜질 뿐 시퀀스에 길이를 남기지 않는다.
        // 끝점을 못 박아 두지 않으면 뒷줄이 전부 미배선인 화면에서 duration 0으로 즉시 완료되고,
        // 배너가 도는 중에 m_revealDone이 서서 첫 터치가 스킵이 아니라 씬 이동이 된다.
        this.revealSeq.InsertCallback(t_end, () => { });

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

    // 골드 줄. 수치가 팝하는 동안 이번 판에 데리고 나간 카드가 나란히 서고(전사는 흑백으로 오른쪽),
    // 살아남은 카드가 한 장씩 골드로 빨려들며 닿을 때마다 그만큼 수치가 오른다 — 랭크 줄과 같은 리듬이다.
    // 카드 축이 설 수 없으면(_cards == null) 예전 코인 분출로 폴백한다 — 값만 툭 뜨는 것보다 낫다.
    // _cardsFlew는 호출자가 랭크 줄을 뒤로 미룰지 판단하는 데 쓴다.
    Sequence BuildGoldLine(bool _animate, IReadOnlyList<int> _cards, IReadOnlyList<int> _fallen,
                           out bool _cardsFlew)
    {
        _cardsFlew = false;

        Tween t_reveal = this.m_gold.BuildReveal(this.rewardRevealDuration);
        if (t_reveal == null) return null;

        Sequence t_line = DOTween.Sequence();

        if (!_animate || this.m_gold.Total <= 0 || (_cards != null && _cards.Count == 0))
        {
            // 카드 0장은 하한 보상이 이미 박혀 있어 굴릴 것이 없다 — 수치가 팝하고 끝난다.
            t_line.Insert(0f, t_reveal);
            return t_line;
        }

        RectTransform t_target = this.goldIconRect != null
                               ? this.goldIconRect
                               : (RectTransform)this.rewardGoldText.transform;

        Sequence t_flight = _cards == null ? null
                          : this.cardFlight.Build(_cards, _fallen, (RectTransform)transform, t_target,
                                                  this.m_gold.HandleArrived,
                                                  () => UiPunch.Play(t_target, this.goldIconPunch, this.goldRollDuration));
        if (t_flight != null)
        {
            t_line.Insert(0f, t_flight);

            // 수치는 줄이 서는 순간 함께 팝한다(랭크 줄과 같은 시점이다) — 카드가 한 장씩 닿을 때마다
            // 계단으로 오르므로, "+0"이 먼저 떠 있어도 흡수가 원인이라는 것이 그 계단에서 읽힌다.
            t_line.Insert(0f, t_reveal);
            _cardsFlew = true;
            return t_line;
        }

        t_line.Insert(0f, t_reveal);

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
                     IReadOnlyList<int> _survivorCards, IReadOnlyList<int> _fallenCards)
    {
        if (this.banner != null) this.banner.HideImmediate();
        else if (this.panel != null) this.panel.localScale = Vector3.zero;

        if (this.dimGroup != null) this.dimGroup.alpha = 0f;
        if (this.hintGroup != null) this.hintGroup.alpha = 0f;

        this.cardFlight.Reset();

        // 랭크를 정산하지 않는 전투(튜토리얼)에서는 "0"이 아니라 줄 자체가 없어야 한다.
        if (this.rankLine != null) this.rankLine.SetActive(_rankDelta != 0);

        // 전투 보상이 없는 전투(모험)도 같은 규칙이다 — 상은 맵에서 따로 받는다.
        if (this.goldLine != null) this.goldLine.SetActive(_gold != 0);

        // 생존 수를 모르는 경로(폴백)에서는 없는 값을 지어내지 않고 줄째로 감춘다.
        if (this.survivorLabel != null)
        {
            this.survivorLabel.gameObject.SetActive(_survivorCards != null);
            if (_survivorCards != null)
            {
                // 전사 목록이 없는 경로(구 호출자·미리보기)에서는 전체 = 생존이라 "N장 중 N장"이 된다.
                // 없는 손실을 지어내는 것보다 낫다 — 분모는 실제로 받은 목록에서만 나온다.
                int t_total = _survivorCards.Count + (_fallenCards != null ? _fallenCards.Count : 0);
                this.survivorLabel.text = string.Format(this.survivorFormat, _survivorCards.Count, t_total);
            }
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

            // 배너는 트윈이 아니라 Animator라 Complete로 끝나지 않는다.
            // Complete가 콜백을 먼저 흘려 Show를 켜므로, 그 뒤에 최종 포즈로 못 박는다.
            if (this.banner != null) this.banner.ShowImmediate();
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
            // 시퀀스와 같은 시계(unscaled)로 굴린다 — 도착과 숫자가 어긋나면 인과가 끊긴다.
            m_rollTween = DOVirtual.Float(0f, 1f, m_rollDuration,
                                          _t => Render(t_start + (long)((t_goal - t_start) * _t)))
                                   .SetUpdate(true)
                                   .SetLink(m_link)
                                   .OnComplete(() => Render(t_goal));

            // 이전 펀치는 완료시켜 죽인다(Kill(true)) — 스케일이 중간값에 눌린 채 남지 않게.
            m_punchTween?.Kill(true);
            m_punchTween = m_text.transform
                                 .DOPunchScale(Vector3.one * m_punch, m_rollDuration, 1, 0.6f)
                                 .SetUpdate(true)
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
