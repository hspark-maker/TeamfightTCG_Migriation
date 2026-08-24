using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>결과판이 보여줄 한 벌. 강화 판정 결과(<see cref="EnhanceResult"/>)에는 오른 폭도 이전 레벨도 없어
/// 호출부가 시도 **전에** 잡아둔 값과 합쳐야 완성된다 — 그 합친 한 벌을 흩어진 인자 대신 한 값으로 묶는다.</summary>
public readonly struct EnhanceResultLine
{
    public readonly EEnhanceOutcome Outcome;
    public readonly int    FromHp;
    public readonly int    ToHp;
    public readonly int    FromLevel;
    public readonly int    ToLevel;
    public readonly bool   CanRetry;
    public readonly string RetryNotice;   // 못 누르는 이유(최고 레벨·잔액 부족). 누를 수 있으면 빈 문자열.

    /// <summary>"한 번 더"에 들 비용. **이미 표기까지 끝난 문자열**로 받는다 — 숫자 포맷을 여기서 또 정하면
    /// 하단 바의 강화 비용과 같은 값이 화면마다 다른 모양으로 뜬다(포맷의 주인은 호출부 하나).
    /// 더 올릴 단계가 없으면 호출부가 "없음" 표기를 넣는다.</summary>
    public readonly string RetryCostText;

    /// <summary>"한 번 더"의 비용 재화 아이콘. 스프라이트째 받는 이유는 <see cref="RetryCostText"/>와 같다 —
    /// 재화에서 그림을 고르는 규칙이 화면마다 따로 있으면 하단 바와 결과판이 같은 비용을 다른 아이콘으로 띄운다.
    /// null이면 프리팹에 저작된 그림을 그대로 둔다(미배선 허용).</summary>
    public readonly Sprite RetryCostIcon;

    /// <summary>이번 강화로 **새로 열린 것**(키워드·시너지·진화). 없으면 null/빈 문자열이고 그 행은 아예 뜨지 않는다.
    /// 무엇이 열렸는지 판정하는 것은 성장 규칙을 아는 호출부 몫이고, 여기는 완성된 문장만 받는다
    /// (<see cref="RetryNotice"/>·<see cref="RetryCostText"/>와 같은 규약).</summary>
    public readonly string UnlockText;

    /// <summary>제목 문구를 갈아끼운다(빈값이면 패널의 저작 문구). 진화처럼 같은 판을 다른 이름으로 쓰는 경우의 축 —
    /// 무엇이라 부를지는 성장 규칙을 아는 호출부 몫이고, 여기는 완성된 문장만 받는다(<see cref="UnlockText"/>와 같은 규약).</summary>
    public readonly string TitleText;

    public EnhanceResultLine(EEnhanceOutcome _outcome, int _fromHp, int _toHp, int _fromLevel, int _toLevel,
                             bool _canRetry, string _retryNotice, string _retryCostText = null,
                             Sprite _retryCostIcon = null, string _unlockText = null, string _titleText = null)
    {
        this.UnlockText = _unlockText;
        this.TitleText  = _titleText;
        this.Outcome       = _outcome;
        this.FromHp        = _fromHp;
        this.ToHp          = _toHp;
        this.FromLevel     = _fromLevel;
        this.ToLevel       = _toLevel;
        this.CanRetry      = _canRetry;
        this.RetryNotice   = _retryNotice;
        this.RetryCostText = _retryCostText;
        this.RetryCostIcon = _retryCostIcon;
    }
}

// 강화 한 번의 결과판(CardDetailOverlay 안의 한 상태).
// 카드가 빛에 완전히 덮인 순간 태어나므로, 빛이 걷힐 때는 이미 거기 있다 — 페이드로 뒤늦게 붙으면 담금질의 인과가 끊긴다.
// 그리고 탭할 때까지 걷히지 않는다. 자동 복귀를 없앤 것이 이 화면의 목적이다(결과를 읽을 시간).
// 예외는 호출부가 켜 주는 구간뿐이다(Show의 _autoReturn) — 이어 누를 것이 없는 자리에서 탭을 요구하면
// 손이 갈 곳 없이 멈춘다.
//
// 판정도 값 반영도 하지 않는다. 무엇이 바뀌었는지는 호출부가 전부 계산해 넘긴다(CardEnhanceRitualView와 같은 결).
//
// ⚠ 카드를 가리지 않는다 — 무대(CardSlot)에 선 카드가 결과의 주인공이므로 배경은 투명하고,
//   글자는 걷힌 DetailPanel·BottomBar가 비운 자리에 얹힌다.
public class EnhanceResultPanelView : MonoBehaviour
{
    // 오른 것이 없을 때의 자리표시. 행을 지우지 않는 이유는 상세 패널과 같다 — 결과마다 높이가 흔들린다.
    const string NoGain = "—";

    [Header("무대")]
    [Tooltip("패널 전체의 알파·입력. 미배선이면 런타임에 붙인다.")]
    [SerializeField] CanvasGroup group;
    [Tooltip("패널 전면을 덮는 투명 버튼. 어디를 탭해도 닫히게 한다 — 닫는 법을 찾게 만들면 연타가 끊긴다.")]
    [SerializeField] Button tapCatcher;

    [Header("타이틀")]
    [SerializeField] TMP_Text titleText;
    [SerializeField] Color    successColor   = new Color(0.45f, 1f, 0.55f, 1f);
    [SerializeField] Color    failColor      = new Color(1f, 0.45f, 0.4f, 1f);
    [SerializeField] string   successMessage = "강화 성공!";
    [SerializeField] string   failMessage    = "강화 실패";

    [Header("결과 행 (선택 — 미배선이면 그 행을 건너뛴다)")]
    [Tooltip("차례로 떠오를 행들. 배열 순서가 곧 등장 순서다.\n" +
             "⚠ LayoutGroup에 구동되지 않는 노드여야 한다 — 매 프레임 좌표가 되돌려지면 떠오르지 않는다.")]
    [SerializeField] CanvasGroup[] rows;
    [Tooltip("오른 체력 \"체력 71 → 73\". 뒤 숫자가 굴러 오른다.")]
    [SerializeField] TMP_Text effectValueText;
    [Tooltip("오른 성급 \"1성 → 2성\".")]
    [SerializeField] TMP_Text gradeValueText;

    [Header("한 번 더 (선택)")]
    [Tooltip("결과판에서 곧바로 다음 강화로 잇는다 — 연타가 이 시스템의 본체라 여기서 손이 끊기면 안 된다.")]
    [SerializeField] Button   retryButton;
    [Tooltip("못 누르는 이유(최고 레벨·잔액 부족). 상세 패널의 상시 문구와 같은 문장을 호출부가 넘긴다.")]
    [SerializeField] TMP_Text retryNoticeText;
    [Tooltip("다음 강화에 들 비용. 하단 바의 강화 버튼과 같은 값·같은 표기를 호출부가 넘긴다(미배선이면 조용히 건너뛴다).")]
    [SerializeField] TMP_Text retryCostText;
    [Tooltip("비용 옆 재화 아이콘. 재화는 레벨마다 갈릴 수 있어 그림도 호출부가 스텝에서 골라 넘긴다(미배선이면 조용히 건너뛴다).")]
    [SerializeField] Image    retryCostIcon;

    [Header("해금 알림 (선택)")]
    [Tooltip("이번 강화로 열린 것(키워드·시너지·진화). 열린 게 없는 강화가 대부분이라 행째로 껐다 켠다.")]
    [SerializeField] GameObject unlockRow;
    [SerializeField] TMP_Text   unlockValueText;

    [Header("박자")]
    [SerializeField] float fadeInDuration  = 0.12f;
    [SerializeField] float fadeOutDuration = 0.15f;
    [Tooltip("행 하나가 떠오르는 시간.")]
    [SerializeField] float rowRiseDuration = 0.18f;
    [Tooltip("행과 행 사이 간격. 한꺼번에 뜨면 나열이 되고, 벌리면 도장이 찍히듯 쌓인다.")]
    [SerializeField] float rowStagger      = 0.07f;
    [Tooltip("행이 아래에서 떠오르는 높이(px).")]
    [SerializeField] float rowRise         = 18f;
    [Tooltip("체력이 굴러 오르는 시간. 한 칸짜리라도 숫자가 바뀌는 순간이 눈에 걸려야 한다.")]
    [SerializeField] float rollDuration    = 0.35f;
    [Tooltip("행이 다 뜬 뒤 스스로 걷히기까지 머무는 시간. 자동 복귀를 켠 판(Show의 _autoReturn)에만 쓰인다.")]
    [SerializeField] float autoReturnHold  = 1f;

    Sequence m_seq;
    Action   m_onClose;
    Action   m_onRetry;

    // 자동 복귀 예약. 걷힌 뒤에 뒤늦게 오면 **다음 판**을 닫아버리므로 판이 바뀌는 길목마다 죽인다.
    Tween m_autoReturn;
    bool  m_autoReturnOn;

    // 행들의 authoring 좌표. 트윈 중간값을 기준으로 잡으면 열 때마다 자리가 밀린다 → 1회만 캡처한다.
    Vector2[] m_rowBase;

    // 열려 있는가 / 닫히는 중인가. 닫히는 동안의 두 번째 탭이 복귀를 두 번 시작하면 무대가 두 번 되돌아간다.
    bool m_open;
    bool m_closing;

    // 결과 행이 다 떠오른 시점. 그 전의 탭은 "빨리 보여달라"는 뜻이라 닫지 않고 연출만 끝까지 당긴다.
    Action m_onRowsDone;
    bool   m_rowsDone;

    /// <summary>결과를 무대에 올리고 탭을 기다린다.
    /// _onClose는 탭으로 걷힌 시점 — 호출부가 여기서 무대 복귀를 시작한다.
    /// _onRetry는 "한 번 더"로 걷힌 시점 — 호출부가 복귀를 마친 뒤 다음 강화로 잇는다.
    /// 둘 중 정확히 하나만, 정확히 한 번 온다(중단 경로에선 어느 것도 오지 않는다 — <see cref="HideImmediate"/> 참고).
    ///
    /// _autoReturn이면 읽을 것이 다 나온 뒤 <see cref="autoReturnHold"/>만큼 머물다 스스로 걷는다(탭과 같은 길).
    /// **어느 결과가 그 대상인지는 성장 규칙을 아는 호출부 몫**이고 여기는 켬/끔만 받는다
    /// (<see cref="EnhanceResultLine.UnlockText"/>와 같은 규약) — 머무는 박자만 이쪽 저작값이다.</summary>
    public void Show(EnhanceResultLine _line, Action _onClose, Action _onRetry,
                     Action _onRowsDone = null, bool _autoReturn = false)
    {
        this.m_onRowsDone = _onRowsDone;
        this.m_rowsDone   = false;
        // 닫을 수단이 하나도 없으면 띄우는 순간이 곧 소프트락이다 — 무대만 돌려보내고 뜨지 않는다.
        if (this.tapCatcher == null && this.retryButton == null)
        {
            Debug.LogError("[EnhanceResultPanelView] 탭 받이·'한 번 더'가 둘 다 미배선 — 결과판을 띄우면 닫을 수단이 없다.");
            _onClose?.Invoke();
            return;
        }

        gameObject.SetActive(true);
        EnsureBase();
        KillSeq();
        KillAutoReturn();   // 앞 판의 예약이 살아 있으면 이제 막 뜬 이 판을 닫는다

        this.m_onClose      = _onClose;
        this.m_onRetry      = _onRetry;
        this.m_open         = true;
        this.m_closing      = false;
        this.m_autoReturnOn = _autoReturn;

        bool t_success = _line.Outcome == EEnhanceOutcome.Success;

        if (this.titleText != null)
        {
            this.titleText.color = t_success ? this.successColor : this.failColor;
            this.titleText.text  = !string.IsNullOrEmpty(_line.TitleText) ? _line.TitleText
                                 : t_success                             ? this.successMessage
                                                                         : this.failMessage;
        }

        if (this.gradeValueText != null)
            this.gradeValueText.text = t_success ? GrowthStar.TransitionLabel(_line.FromLevel, _line.ToLevel)
                                                 : $"{GrowthStar.Label(_line.FromLevel)} 유지";

        if (this.retryButton != null) this.retryButton.interactable = _line.CanRetry;
        if (this.retryNoticeText != null) this.retryNoticeText.text = _line.RetryNotice;
        if (this.retryCostText   != null) this.retryCostText.text   = _line.RetryCostText;

        // 스프라이트가 없으면 프리팹 그림을 그대로 둔다 — 빈 칸으로 갈아치우면 아이콘이 사라진다.
        if (this.retryCostIcon != null && _line.RetryCostIcon != null)
            this.retryCostIcon.sprite = _line.RetryCostIcon;

        // 열린 게 없으면 행 자체를 접는다 — 빈 줄이 남으면 "뭔가 열렸나?" 하고 한 번 더 읽게 된다.
        bool t_hasUnlock = !string.IsNullOrEmpty(_line.UnlockText);
        if (this.unlockRow != null) this.unlockRow.SetActive(t_hasUnlock);
        if (this.unlockValueText != null && t_hasUnlock) this.unlockValueText.text = _line.UnlockText;

        this.group.alpha          = 0f;
        this.group.blocksRaycasts = true;

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);
        t_seq.Insert(0f, this.group.DOFade(1f, Mathf.Max(0.01f, this.fadeInDuration)));

        if (this.titleText != null)
            t_seq.InsertCallback(0f, () => UiPunch.Play(this.titleText.transform));

        BuildRows(t_seq, t_success, _line);

        // 읽을 것이 다 나온 자리. 탭으로 당겨도 여기를 지나므로(Complete는 콜백을 그대로 실행) 신호가 유실되지 않는다.
        t_seq.OnComplete(MarkRowsDone);

        this.m_seq = t_seq;
        t_seq.Play();   // 재생 책임을 코드에 남긴다(PopupTransition과 같은 결).
    }

    /// <summary>결과를 읽는 중인가. 이 동안에는 하단 바의 강화 버튼이 "한 번 더"로 동작한다 —
    /// 같은 자리의 같은 버튼이 계속 그 일을 하게 두는 편이, 결과판이 자기 버튼을 하나 더 띄우는 것보다 읽기 쉽다.</summary>
    public bool IsOpen => this.m_open && !this.m_closing;

    /// <summary>"한 번 더"를 밖(하단 바 버튼)에서 눌렀을 때. 결과판 자신의 버튼과 **같은 길**로 흘려보낸다 —
    /// 닫힘 애니·콜백 1회 보장이 그 경로에만 있어서, 여기서 따로 처리하면 두 규약이 갈린다.</summary>
    public void RequestRetry()
    {
        if (!IsOpen) return;
        OnRetryPressed();
    }

    /// <summary>탭 대신 밖에서 걷는다(튜토리얼 자동 복귀). 탭과 **같은 길**이라 복귀 콜백도 그대로 1회 흐른다 —
    /// 갈라 두면 "자동으로 닫혔을 때만 무대가 안 돌아오는" 경로가 생긴다(<see cref="RequestRetry"/>와 같은 규약).
    /// 아직 행이 쌓이는 중이면 남은 구간을 최종 상태로 당기고 걷는다 — 자동 복귀는 읽을 것이 다 나온 뒤에
    /// 부르는 것이 계약이지만, 그 사이 끼어들어도 중간 상태로 잘려 보이지 않게 한다.</summary>
    public void RequestClose()
    {
        if (!IsOpen) return;

        if (!this.m_rowsDone && this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);

        BeginClose(this.m_onClose);
    }

    /// <summary>결과판을 잘라내고 감춘다(카드 전환·닫힘 경로). 콜백은 흘리지 않는다 —
    /// 이 경로에선 호출부가 무대까지 함께 잘라내므로 복귀를 한 번 더 시킬 이유가 없다.</summary>
    public void HideImmediate()
    {
        KillSeq();
        KillAutoReturn();

        this.m_open         = false;
        this.m_closing      = false;
        this.m_onClose      = null;
        this.m_onRetry      = null;
        this.m_autoReturnOn = false;

        if (this.group != null)
        {
            this.group.alpha          = 0f;
            this.group.blocksRaycasts = true;
        }

        gameObject.SetActive(false);
    }

    // 버튼은 여기서 배선한다 — 패널은 열 때마다 꺼졌다 켜지므로 Awake 한 번으로는 부족하고,
    // Remove 후 Add라 중복 등록도 남지 않는다(CardDetailOverlayView와 같은 관용구).
    void OnEnable()
    {
        if (this.tapCatcher != null)
        {
            this.tapCatcher.onClick.RemoveListener(OnTapped);
            this.tapCatcher.onClick.AddListener(OnTapped);
        }

        if (this.retryButton != null)
        {
            this.retryButton.onClick.RemoveListener(OnRetryPressed);
            this.retryButton.onClick.AddListener(OnRetryPressed);
        }
    }

    void OnDisable()
    {
        if (this.tapCatcher  != null) this.tapCatcher.onClick.RemoveListener(OnTapped);
        if (this.retryButton != null) this.retryButton.onClick.RemoveListener(OnRetryPressed);

        KillSeq();
        KillAutoReturn();

        // 걷히는 도중에 꺼졌다면 OnComplete가 안 돌아 복귀 신호가 삼켜진다. 상태만은 반드시 풀어둔다 —
        // 다음에 켜졌을 때 "닫히는 중"으로 남아 있으면 탭이 통째로 먹힌다.
        // (무대 복귀는 이 경로에서 호출부가 CancelImmediate로 직접 한다.)
        this.m_open         = false;
        this.m_closing      = false;
        this.m_onClose      = null;
        this.m_onRetry      = null;
        this.m_autoReturnOn = false;
    }

    // 성공이면 행이 아래에서 차례로 떠오르고 오른 체력이 굴러 오른다.
    // 실패는 같은 행을 쓰되 박자를 뺀다 — 성공의 리듬이 있어야 실패의 정적이 아프다.
    void BuildRows(Sequence _seq, bool _success, EnhanceResultLine _line)
    {
        if (this.effectValueText != null)
            this.effectValueText.text = _success && _line.ToHp > _line.FromHp
                                      ? EffectLine(_line.FromHp, _line.FromHp)
                                      : _success ? EffectLine(_line.FromHp, _line.ToHp)
                                                 : NoGain;

        if (this.rows != null)
            for (int t_i = 0; t_i < this.rows.Length; t_i++)
            {
                CanvasGroup t_row = this.rows[t_i];
                if (t_row == null) continue;

                var t_rect = t_row.transform as RectTransform;

                if (!_success)
                {
                    // 실패는 패널과 함께 그냥 거기 있다.
                    t_row.alpha = 1f;
                    if (t_rect != null) t_rect.anchoredPosition = this.m_rowBase[t_i];
                    continue;
                }

                float t_at = this.fadeInDuration + this.rowStagger * t_i;

                t_row.alpha = 0f;
                if (t_rect != null) t_rect.anchoredPosition = this.m_rowBase[t_i] - new Vector2(0f, this.rowRise);

                _seq.Insert(t_at, t_row.DOFade(1f, this.rowRiseDuration));
                if (t_rect != null)
                    _seq.Insert(t_at, t_rect.DOAnchorPos(this.m_rowBase[t_i], this.rowRiseDuration).SetEase(Ease.OutCubic));
            }

        if (!_success || this.effectValueText == null || _line.ToHp <= _line.FromHp) return;

        float t_rollAt  = this.fadeInDuration + this.rowRiseDuration;
        float t_rollDur = Mathf.Max(0.05f, this.rollDuration);

        // 굴리기는 그 행이 다 떠오른 뒤에 — 떠오르는 중에 숫자까지 움직이면 둘 다 안 읽힌다.
        // 카드 위 숫자는 여기 맞춰 굴리지 않는다: 그쪽은 섬광이 물러날 때 이미 새 값으로 드러났다(FlashGrowth).
        _seq.Insert(t_rollAt, BuildRoll(_line.FromHp, _line.ToHp, t_rollDur));
    }

    Tween BuildRoll(int _from, int _to, float _duration)
    {
        float t_shown = _from;

        return DOTween.To(() => t_shown,
                          _v => { t_shown = _v; this.effectValueText.text = EffectLine(_from, Mathf.RoundToInt(_v)); },
                          (float)_to, _duration)
                      .SetEase(Ease.OutQuad)
                      // 굴리다 끊기면 중간값이 남는다 — 마지막 한 번을 못 박는다.
                      .OnKill(() => { if (this.effectValueText != null) this.effectValueText.text = EffectLine(_from, _to); })
                      .OnComplete(() => UiPunch.Play(this.effectValueText.transform));
    }

    // 표시 문장의 진실원. 굴리는 중이든 못 박는 중이든 여기 하나를 지나야 두 문장이 갈리지 않는다.
    static string EffectLine(int _from, int _shown) => $"체력 {_from} → {_shown}";

    void OnTapped()
    {
        // 아직 행이 쌓이는 중이면 탭은 "건너뛰기"다 — 여기서 닫아버리면 결과를 읽기도 전에 사라진다.
        // 남은 구간을 최종 상태로 당기면 OnComplete가 곧 읽을 것이 다 나왔다고 알린다.
        if (!this.m_rowsDone)
        {
            if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);
            else MarkRowsDone();   // 시퀀스가 이미 죽었으면 신호만 흘린다
            return;
        }

        BeginClose(this.m_onClose);
    }

    // 한 번만 흘린다 — 탭으로 당긴 뒤 OnComplete가 또 오거나, 닫힘이 먼저 와도 두 번 알리지 않는다.
    void MarkRowsDone()
    {
        if (this.m_rowsDone) return;

        this.m_rowsDone = true;
        this.m_onRowsDone?.Invoke();

        // 자동 복귀는 여기서부터 잰다 — 읽을 것이 다 나온 뒤가 계약이다.
        // 이미 걷히는 중이면(BeginClose가 지나며 부른 경우) 예약할 것이 없다.
        if (!this.m_autoReturnOn || this.m_closing) return;

        this.m_autoReturn = DOVirtual.DelayedCall(Mathf.Max(0f, this.autoReturnHold), RequestClose)
                                     .SetLink(gameObject);
    }

    void OnRetryPressed()
    {
        // "한 번 더"가 없으면 그냥 닫기와 같다 — 눌린 것이 삼켜져 패널이 굳는 것보다 낫다.
        BeginClose(this.m_onRetry ?? this.m_onClose);
    }

    void BeginClose(Action _after)
    {
        if (!this.m_open || this.m_closing) return;

        // 다 나오기 전에 닫히는 경로("한 번 더" 연타)에서도 신호는 흘린다 — 안 그러면 하단 바가 숨은 채 굳는다.
        MarkRowsDone();

        this.m_closing = true;
        KillSeq();
        KillAutoReturn();   // 탭이 먼저 걷었다면 예약은 할 일이 없다

        // 걷히는 동안 입력을 죽인다 — 두 번째 탭이 복귀를 두 번 시작하면 무대가 두 번 되돌아간다.
        this.group.blocksRaycasts = false;

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);
        t_seq.Append(this.group.DOFade(0f, Mathf.Max(0.01f, this.fadeOutDuration)));

        // OnKill이 아니라 OnComplete다 — 중단 경로(HideImmediate)에서 잘릴 때 무대 복귀가 딸려 나가면 안 된다.
        // 그 경로의 마무리는 호출부가 ritual.CancelImmediate로 직접 한다.
        t_seq.OnComplete(() =>
        {
            this.m_seq = null;
            HideImmediate();
            _after?.Invoke();
        });

        this.m_seq = t_seq;
        t_seq.Play();
    }

    void EnsureBase()
    {
        if (this.group == null)
        {
            this.group = GetComponent<CanvasGroup>();
            if (this.group == null) this.group = gameObject.AddComponent<CanvasGroup>();
        }

        if (this.m_rowBase != null || this.rows == null) return;

        this.m_rowBase = new Vector2[this.rows.Length];
        for (int t_i = 0; t_i < this.rows.Length; t_i++)
        {
            var t_rect = this.rows[t_i] != null ? this.rows[t_i].transform as RectTransform : null;
            this.m_rowBase[t_i] = t_rect != null ? t_rect.anchoredPosition : Vector2.zero;
        }
    }

    void KillSeq()
    {
        this.m_seq?.Kill();
        this.m_seq = null;
    }

    void KillAutoReturn()
    {
        this.m_autoReturn?.Kill();
        this.m_autoReturn = null;
    }
}
