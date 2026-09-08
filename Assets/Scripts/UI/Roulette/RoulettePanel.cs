using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 룰렛 화면(RouletteOverlay에 부착). 칸을 채우고 회전 한 판의 흐름을 중계한다.
// 풀(UIPoolManager)이 수명을 쥔다 — 규약은 RankRewardPanel·KeywordGrowthPanel과 같다.
//
// 멈출 칸도 상품도 이 화면이 정하지 않는다. 결과값이 칸 번호와 상품을 함께 운반하고,
// 여기서 저작 표를 되읽지 않으므로 서버 블롭과 클라 저작이 어긋나도 연출이 실지급과 갈리지 않는다.
public class RoulettePanel : PooledUIBase
{
    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] RouletteWheelView wheel;

    [Tooltip("판의 칸 8개. 순서가 곧 칸 번호다 — 0번이 12시이고 시계방향으로 1, 2, 3...입니다.")]
    [SerializeField] RouletteSlotView[] slots;

    [SerializeField] RouletteBulbRing bulbRing;

    [Tooltip("보유 티켓 수를 그릴 자리입니다. 비워 두면 아무것도 그리지 않습니다.")]
    [SerializeField] TMP_Text ticketText;

    [Tooltip("개발용 로컬 추첨으로 도는 중임을 알리는 표식입니다. 서버 판정이면 자동으로 꺼집니다.")]
    [SerializeField] GameObject localModeBadge;

    [Header("버튼")]
    [SerializeField] Button spinButton;
    [SerializeField] Button closeButton;

    [Tooltip("패널 밖(딤)을 눌러 닫는 판. 알파 0 Image의 Button에 배선합니다 — 미배선이면 밖을 눌러도 닫히지 않습니다.")]
    [SerializeField] Button dimButton;

    [Tooltip("회전 중 회전 버튼에 씌울 알파입니다.")]
    [Range(0f, 1f)] [SerializeField] float disabledAlpha = 0.5f;

    [SerializeField] CanvasGroup spinGroup;

    [Header("연출")]
    [Tooltip("panel에는 딤을 뺀 내용물 컨테이너(Board)를 배선한다 — root를 물리면 전체화면 딤까지 함께 커지고, " +
             "프레임 하나만 물리면 판과 버튼이 제자리에 남아 팝업이 한 덩어리로 열리지 않는다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("공용 ScreenDim(Full)에 요청할 암막 짙기입니다. 다른 로비 오버레이(랭크 보상 0.72 · 키워드 강화 0.75)와 결을 맞춘 값입니다.\n\n" +
             "Panel은 알파 0으로 남아 밖을 눌러 닫는 판정만 맡습니다 — 이 값을 올려도 그 판이 짙어지는 것이 아니라 공용 딤이 짙어집니다.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.75f;

    [Tooltip("결과가 즉시 와도 판이 이만큼은 돈다(밀리초). 손맛의 바닥이라 왕복이 이보다 길면 그냥 통과합니다.")]
    [SerializeField] int minSpinMs = 2500;

    [Tooltip("회전 중 닫기를 막아 두는 최대 시간(밀리초)입니다.\n\n" +
             "회전이 도는 동안 닫으면 비용만 빠지고 결과를 볼 자리가 사라지므로 닫기와 딤을 잠급니다. " +
             "다만 서버 왕복이 재시도까지 겹치면 몇십 초가 될 수 있어, 이 시간이 지나면 회전 중이라도 닫기를 되살립니다 " +
             "— 결과를 놓치는 것보다 안내 없는 화면에 갇히는 쪽이 나쁩니다.\n\n" +
             "Min Spin Ms 보다 넉넉히 길게 두세요. 그보다 짧으면 정상 회전에서도 도중에 잠금이 풀립니다.")]
    [SerializeField] int closeLockMaxMs = 8000;

    [Tooltip("획득 코인이 출발할 자리. 비워 두면 당첨된 칸에서 출발합니다.")]
    [SerializeField] RectTransform gainOrigin;

    [Tooltip("이 화면 위에서 코인을 그릴 전용 재생기입니다(그 재생기의 shared는 반드시 꺼 둘 것). " +
             "비워 두면 공용 재생기를 씁니다 — 로비 캔버스에서 돌아 이 판 뒤로 코인이 숨을 수 있습니다.")]
    [SerializeField] CurrencyGainEffectPlayer gainPlayer;

    // 회전 한 판이 떠 있는 동안 참. 같은 프레임 더블탭은 interactable=false로 막지 못한다.
    bool m_spinning;

    // 닫기를 막고 있는 동안 참. 회전 중이라도 상한을 넘기면 먼저 풀리므로 m_spinning과 따로 둔다.
    bool m_closeLocked;

    // 수명은 회전 1회에 매단다 — 패널 수명에 매달면 닫았다 다시 연 뒤 회전이 돌지 않는다.
    CancellationTokenSource m_spinCts;

    // 풀 컨테이너에서 떨어져 나오려고 확보한 Canvas(LiftToOverlayLayer 참조)
    Canvas m_sortingCanvas;

    public override void Initialization(UIData _data) { }

    protected override void Awake()
    {
        base.Awake();
        this.LiftToOverlayLayer();
    }

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    /// <summary>씬 버튼 UnityEvent가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.</summary>
    public void Open()
    {
        this.SetVisible(true);

        // 이 판은 상단바를 덮는다 — 회전 비용과 보상이 곧 재화라 잔액이 보이는 채로 돌아야 한다.
        LobbyShellBars.LiftTop(this, this.transform);

        this.BuildSlots();
        this.RefreshTicketText();
        this.ApplySpinInteractable();

        if (this.localModeBadge != null) this.localModeBadge.SetActive(!RouletteManager.IsServerBacked);
        if (this.bulbRing != null) this.bulbRing.PlayIdle();
    }

    public void Close()
    {
        this.CancelSpin();

        this.SetVisible(false);

        // 판이 아직 페이드로 남아 있는 동안 상단바가 그 뒤로 사라지지 않게 퇴장이 끝난 뒤에 내린다.
        LobbyShellBars.DropTopAfter(this, this.transition.CloseDuration);
    }

    void OnEnable()
    {
        // 재활성마다 중복 등록 방지.
        if (this.spinButton != null)
        {
            this.spinButton.onClick.RemoveAllListeners();
            this.spinButton.onClick.AddListener(this.OnSpinPressed);
        }
        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(this.Close);
        }
        if (this.dimButton != null)
        {
            this.dimButton.onClick.RemoveAllListeners();
            this.dimButton.onClick.AddListener(this.Close);
        }

        CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
    }

    void OnDisable()
    {
        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;

        // 안전망 — 씬 전환·풀 회수처럼 Close를 거치지 않는 길이 있다. 되돌리기의 정규 자리는 Close다.
        LobbyShellBars.DropTop(this);

        // 같은 이유로 공용 딤도 여기서 걷는다. 켜진 채 남으면 로비 입력이 통째로 죽는다.
        ScreenDim.Hide(this);

        this.CancelSpin();

        if (this.wheel != null) this.wheel.Stop();
        if (this.bulbRing != null) this.bulbRing.Stop();

        // root가 미배선이면 페이드 대상이 이 오브젝트 자신이라 닫을 때마다 여기로 온다.
        // 잘린 퇴장을 마무리하는 것이 이 호출의 일이므로 그 경로로 와도 무해하다.
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    void OnSpinPressed()
    {
        // interactable=false만으로는 같은 프레임의 두 번째 클릭이 통과한다.
        if (this.m_spinning) return;

        ERouletteSpinResult t_precheck = RouletteManager.Precheck();
        if (t_precheck != ERouletteSpinResult.Success)
        {
            this.ShowFailure(t_precheck);
            return;
        }

        this.RunSpinAsync().Forget();
    }

    // 취소는 정상 종료다 — 바깥 경계에서 한 번만 삼킨다.
    async UniTaskVoid RunSpinAsync()
    {
        await this.SpinAsync().SuppressCancellationThrow();
    }

    async UniTask SpinAsync()
    {
        this.m_spinning = true;
        this.m_closeLocked = true;
        this.ApplySpinInteractable();

        var t_cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        this.m_spinCts = t_cts;
        CancellationToken t_token = t_cts.Token;

        this.ReleaseCloseLockAfterAsync(t_token).Forget();

        try
        {
            // 이미 출발한 두 대기를 순서대로 await 하면 합류점이 자동으로 max(둘)가 된다 — 분기가 한 줄도 없다.
            // UniTask는 struct라 같은 것을 두 번 await 하면 터진다. 각각 로컬에 담아 한 번씩만 기다린다.
            UniTask<RouletteSpinOutcome> t_request = RouletteManager.SpinAsync(t_token);
            UniTask t_floor = UniTask.Delay(this.minSpinMs, DelayType.UnscaledDeltaTime, cancellationToken: t_token);

            if (this.wheel != null) this.wheel.BeginSpin();

            RouletteSpinOutcome t_outcome = await t_request;
            await t_floor;

            if (this == null) return;

            if (!t_outcome.Success)
            {
                // 급정지는 결함으로 읽힌다 — 판이 제자리로 감속 복귀한 뒤에 안내를 띄운다.
                if (this.wheel != null) await this.wheel.ReturnHomeAsync(t_token);
                if (this == null) return;

                this.ShowFailure(t_outcome.Result);
                return;
            }

            if (this.wheel != null) await this.wheel.SettleAtAsync(t_outcome.SlotIndex, t_token);
            if (this == null) return;

            this.PlayWinPunch(t_outcome.SlotIndex);

            this.PlayGainEffect(t_outcome);
        }
        finally
        {
            // 어느 갈래로 끝나든 되돌린다 — 안 풀면 이 화면이 통째로 굳는다.
            if (this.m_spinCts == t_cts) this.m_spinCts = null;

            // 곁가지로 띄운 잠금 해제 타이머를 여기서 걷는다. 안 걷으면 살아남은 타이머가
            // 다음 회전의 잠금을 제 시간보다 일찍 푼다(이 시점엔 모든 대기가 끝나 취소가 무해하다).
            t_cts.Cancel();
            t_cts.Dispose();

            this.m_spinning = false;
            this.m_closeLocked = false;

            if (this != null)
            {
                this.ApplySpinInteractable();

                // 닫는 중이면 마퀴를 다시 켜지 않는다 — Close가 방금 걷은 무한 시퀀스를 되살리는 자리다.
                if (this.bulbRing != null && this.isShow) this.bulbRing.PlayIdle();
            }
        }
    }

    // 판에 미리 깔아 두는 상품 목록. 여는 시점의 저작값이라 매니저에서 당겨도 되지만,
    // 회전 결과를 그릴 때는 절대 여기를 되읽지 않는다 — 결과값이 상품까지 운반한다.
    void BuildSlots()
    {
        if (this.slots == null) return;

        IReadOnlyList<RouletteSlotDef> t_defs = RouletteManager.Slots;
        int t_count = t_defs != null ? t_defs.Count : 0;

        for (int t_i = 0; t_i < this.slots.Length; t_i++)
        {
            if (this.slots[t_i] == null || t_i >= t_count) continue;

            RouletteSlotDef t_def = t_defs[t_i];
            this.slots[t_i].Bind(t_def.currency, t_def.amount);
        }

        if (t_count != this.slots.Length)
            Debug.LogWarning($"[RoulettePanel] 저작 칸 {this.slots.Length}개와 설정 칸 {t_count}개가 다르다 — 판 그림과 상품이 어긋난다.", this);
    }

    // 낙관 홀드·응답 채택·디버그 지급이 전부 이 통지를 때리므로 회전 뒤에 따로 갱신하지 않는다.
    // 비용 재화를 매니저에서 묻는다 — 티켓으로 못박으면 다이아로 도는 판에서 버튼이 잔액을 따라오지 않는다.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (_type != RouletteManager.PriceType) return;

        this.RefreshTicketText();

        // 잔액이 회전 가부를 가르므로 버튼도 같은 통지로 따라온다.
        this.ApplySpinInteractable();
    }

    void RefreshTicketText()
    {
        if (this.ticketText == null) return;

        this.ticketText.text = $"티켓 {CurrencyManager.GetBalance(ECurrencyType.RouletteTicket):N0}";
    }

    // 회전 버튼과 닫기 경로의 상태를 한 자리에서 맞춘다. 근거는 둘 다 필드라 인자를 받지 않는다.
    // 가부는 Precheck가 판정한다 — IsAvailable만 보면 티켓이 없어도 버튼이 눌려, 누른 뒤 팝업으로 거절당한다.
    void ApplySpinInteractable()
    {
        bool t_on = !this.m_spinning && RouletteManager.Precheck() == ERouletteSpinResult.Success;

        if (this.spinButton != null) this.spinButton.interactable = t_on;
        if (this.spinGroup != null) this.spinGroup.alpha = t_on ? 1f : this.disabledAlpha;

        this.ApplyCloseLocked(this.m_closeLocked);
    }

    // interactable로는 못 막는다(눌린 클릭을 그대로 먹는다). enabled를 내려야 반응 자체가 없다.
    void ApplyCloseLocked(bool _locked)
    {
        if (this.closeButton != null) this.closeButton.enabled = !_locked;
        if (this.dimButton != null) this.dimButton.enabled = !_locked;
    }

    // 왕복이 길어지면 회전 중에도 닫기를 되살린다. 재시도까지 겹치면 몇십 초가 되는데,
    // 결과 연출을 놓치는 것보다 안내 없는 화면에 갇히는 쪽이 나쁘다.
    async UniTaskVoid ReleaseCloseLockAfterAsync(CancellationToken _ct)
    {
        bool t_canceled = await UniTask.Delay(this.closeLockMaxMs, DelayType.UnscaledDeltaTime, cancellationToken: _ct)
                                       .SuppressCancellationThrow();

        if (t_canceled || this == null) return;

        this.m_closeLocked = false;
        this.ApplyCloseLocked(false);
    }

    void PlayWinPunch(int _slotIndex)
    {
        if (this.slots == null || _slotIndex < 0 || _slotIndex >= this.slots.Length) return;

        if (this.slots[_slotIndex] != null) this.slots[_slotIndex].PlayWinPunch();
    }

    // 잔액은 서버 응답 채택이 이미 갈아끼웠다 — 롤업이 (잔액 − 획득량) → 잔액으로 세므로 끝값이 곧 실제 지급 뒤 잔액이다.
    void PlayGainEffect(RouletteSpinOutcome _outcome)
    {
        CurrencyGainEffectPlayer t_player = this.gainPlayer;
        if (t_player == null && !CurrencyGainEffectPlayer.TryGet(this, out t_player)) return;

        t_player.Play(this.ResolveGainOrigin(_outcome.SlotIndex), new CurrencyGain(_outcome.Currency, _outcome.Amount), null);
    }

    RectTransform ResolveGainOrigin(int _slotIndex)
    {
        if (this.gainOrigin != null) return this.gainOrigin;

        if (this.slots != null && _slotIndex >= 0 && _slotIndex < this.slots.Length && this.slots[_slotIndex] != null)
            return this.slots[_slotIndex].transform as RectTransform;

        return null;
    }

    // 유저가 스스로 닫은 회전은 안내하지 않는다 — 취소는 실패가 아니다.
    void ShowFailure(ERouletteSpinResult _result)
    {
        if (_result == ERouletteSpinResult.Success || _result == ERouletteSpinResult.Canceled) return;

        if (_result == ERouletteSpinResult.NetworkFailed)
        {
            NetworkFailurePopup.Show("회전 결과를 확인하지 못했습니다.");
            return;
        }

        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = MessageOf(_result),
            yesText   = "확인",
            noText    = "닫기",
        });
    }

    static string MessageOf(ERouletteSpinResult _result)
    {
        switch (_result)
        {
            case ERouletteSpinResult.InsufficientTicket: return "룰렛 티켓이 부족합니다.\n티켓 획득처는 준비 중입니다.";
            case ERouletteSpinResult.RewardUnreadable:   return "보상은 지급되었습니다.\n결과를 그리지 못했으니 잔액을 확인해 주세요.";
            case ERouletteSpinResult.Rejected:           return "회전이 거절되었습니다.\n잠시 후 다시 시도해 주세요.";
            case ERouletteSpinResult.RouletteNotFound:   return "룰렛을 준비하지 못했습니다.\n잠시 후 다시 시도해 주세요.";
            default:                                     return "지금은 룰렛을 돌릴 수 없습니다.";
        }
    }

    void CancelSpin()
    {
        // 판은 급정지시키지 않는다 — 트윈은 대상이 꺼질 때 SetLink가 걷는다.
        this.m_spinCts?.Cancel();
    }

    void SetVisible(bool _visible)
    {
        // 저작본은 루트가 꺼진 채로 들어온다 — 여기서 켜 주지 않으면 하위 root만 토글돼 화면에 아무것도 뜨지 않는다.
        if (_visible && !this.gameObject.activeSelf) this.gameObject.SetActive(true);

        // 암막은 공용 ScreenDim(Full)이 그린다 — Panel은 알파 0으로 남아 뒤쪽 입력만 삼킨다.
        if (_visible) ScreenDim.Show(this, this.dimAlpha, true, this.transition.OpenDuration);
        else ScreenDim.Hide(this);

        // 풀 계약(PooledUIBase.isShow). 열고 닫는 길이 여기 하나뿐이라 상태도 여기서만 쓴다.
        this.isShow = _visible;

        this.transition.SetVisible(this.ResolveTarget(), _visible);

        if (!_visible && this.bulbRing != null) this.bulbRing.Stop();
    }

    // 풀 컨테이너(UiSortingOrder.Pool)에서 떨어져 나와 로비 오버레이 층에 내려앉는다(절차는 UiSortingOrder가 쥔다).
    void LiftToOverlayLayer()
        => this.m_sortingCanvas = UiSortingOrder.LiftNested(gameObject, UiSortingOrder.PooledOverlay);

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
