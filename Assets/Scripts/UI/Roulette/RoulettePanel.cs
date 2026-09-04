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
// 여기서 저작 표를 되읽지 않으므로 2단계에서 서버 표와 클라 저작이 어긋나도 연출이 실지급과 갈리지 않는다.
public class RoulettePanel : PooledUIBase
{
    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] RouletteWheelView wheel;

    [Tooltip("판의 칸 8개. 순서가 곧 칸 번호다 — 0번이 12시이고 시계방향으로 1, 2, 3...입니다.")]
    [SerializeField] RouletteSlotView[] slots;

    [SerializeField] RouletteBulbRing bulbRing;

    [Header("버튼")]
    [SerializeField] Button spinButton;
    [SerializeField] Button closeButton;

    [Tooltip("패널 밖(딤)을 눌러 닫는 판. 알파 0 Image의 Button에 배선합니다 — 미배선이면 밖을 눌러도 닫히지 않습니다.")]
    [SerializeField] Button dimButton;

    [Tooltip("회전 중 회전 버튼에 씌울 알파입니다.")]
    [Range(0f, 1f)] [SerializeField] float disabledAlpha = 0.5f;

    [SerializeField] CanvasGroup spinGroup;

    [Header("문구")]
    [Tooltip("판 이름. 비워 두면 저작 문구를 그대로 둡니다.")]
    [SerializeField] TMP_Text titleText;

    [Tooltip("1회 회전 비용 문구입니다. 보유량이 아니라 비용을 적습니다 — " +
             "1단계는 티켓 0장으로 무제한 회전이라 보유량을 띄우면 영구 0이 노출됩니다.")]
    [SerializeField] TMP_Text priceText;

    [Tooltip("비용 문구 형식입니다. {0}=재화 이름, {1}=수량.")]
    [SerializeField] string priceFormat = "{0} {1}장";

    [Tooltip("잔액이 움직이지 않는다는 표식입니다. RouletteManager.IsLocalSource일 때만 켜집니다 — " +
             "서버 소스가 꽂히면 스스로 꺼지므로 2단계에 걷어낼 코드가 없습니다.")]
    [SerializeField] GameObject localModeBadge;

    [Header("연출")]
    [Tooltip("panel에는 프레임(Lucky_Spin)을 배선한다 — root를 물리면 전체화면 딤까지 함께 커진다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("공용 ScreenDim(Full)에 요청할 암막 짙기.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    [Tooltip("결과가 즉시 와도 판이 이만큼은 돈다(밀리초). 손맛의 바닥이라 왕복이 이보다 길면 그냥 통과합니다.")]
    [SerializeField] int minSpinMs = 2500;

    [Tooltip("획득 코인이 출발할 자리. 비워 두면 당첨된 칸에서 출발합니다.")]
    [SerializeField] RectTransform gainOrigin;

    [Tooltip("이 화면 위에서 코인을 그릴 전용 재생기입니다(그 재생기의 shared는 반드시 꺼 둘 것). " +
             "비워 두면 공용 재생기를 씁니다 — 로비 캔버스에서 돌아 이 판 뒤로 코인이 숨을 수 있습니다.")]
    [SerializeField] CurrencyGainEffectPlayer gainPlayer;

    // 회전 한 판이 떠 있는 동안 참. 같은 프레임 더블탭은 interactable=false로 막지 못한다.
    bool m_spinning;

    // 수명은 회전 1회에 매단다 — 패널 수명에 매달면 닫았다 다시 연 뒤 회전이 돌지 않는다.
    CancellationTokenSource m_spinCts;

    public override void Initialization(UIData _data) { }

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    /// <summary>씬 버튼 UnityEvent가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.</summary>
    public void Open()
    {
        this.SetVisible(true);

        // 회전 도중 닫힌 판은 칸과 어긋난 각도로 남아 있다 — 여는 순간 저작 자리로 되돌린다.
        if (this.wheel != null) this.wheel.SnapToAuthored();

        this.BuildSlots();
        this.RefreshChrome();

        if (this.bulbRing != null) this.bulbRing.PlayIdle();
    }

    public void Close()
    {
        this.CancelSpin();
        this.SetVisible(false);
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
    }

    void OnDisable()
    {
        this.CancelSpin();

        if (this.wheel != null) this.wheel.Stop();
        if (this.bulbRing != null) this.bulbRing.Stop();

        // 안전망 — Close를 거치지 않고 꺼지면 공용 딤이 남는다.
        ScreenDim.Hide(this);

        // 오버레이 자체가 꺼지는 경로(씬 정리 등)에서만 온다 — 열고 닫기로는 불리지 않는다.
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
        this.ApplySpinInteractable(false);

        var t_cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        this.m_spinCts = t_cts;
        CancellationToken t_token = t_cts.Token;

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

            if (t_outcome.IsJackpot && this.bulbRing != null)
            {
                await this.bulbRing.PlayJackpotAsync(t_token);
                if (this == null) return;
            }

            this.PlayGainEffect(t_outcome);
        }
        finally
        {
            // 어느 갈래로 끝나든 되돌린다 — 안 풀면 이 화면이 통째로 굳는다.
            if (this.m_spinCts == t_cts) this.m_spinCts = null;
            t_cts.Dispose();

            this.m_spinning = false;

            if (this != null)
            {
                this.ApplySpinInteractable(true);

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
            if (this.slots[t_i] == null) continue;

            bool t_has = t_i < t_count;
            this.slots[t_i].gameObject.SetActive(t_has);
            if (!t_has) continue;

            RouletteSlotDef t_def = t_defs[t_i];
            this.slots[t_i].Bind(t_def.currency, t_def.amount, t_def.isJackpot);
        }

        if (t_count != this.slots.Length)
            Debug.LogWarning($"[RoulettePanel] 저작 칸 {this.slots.Length}개와 설정 칸 {t_count}개가 다르다 — 판 그림과 상품이 어긋난다.", this);
    }

    void RefreshChrome()
    {
        if (this.titleText != null)
        {
            string t_name = RouletteManager.DisplayName;
            if (!string.IsNullOrEmpty(t_name)) this.titleText.text = t_name;
        }

        if (this.priceText != null)
            this.priceText.text = string.Format(this.priceFormat,
                                                CurrencyLook.NameOf(RouletteManager.PriceType),
                                                RouletteManager.Price.ToString("N0"));

        if (this.localModeBadge != null) this.localModeBadge.SetActive(RouletteManager.IsLocalSource);

        this.ApplySpinInteractable(!this.m_spinning);
    }

    void ApplySpinInteractable(bool _interactable)
    {
        bool t_on = _interactable && RouletteManager.IsAvailable;

        if (this.spinButton != null) this.spinButton.interactable = t_on;
        if (this.spinGroup != null) this.spinGroup.alpha = t_on ? 1f : this.disabledAlpha;
    }

    void PlayWinPunch(int _slotIndex)
    {
        if (this.slots == null || _slotIndex < 0 || _slotIndex >= this.slots.Length) return;

        if (this.slots[_slotIndex] != null) this.slots[_slotIndex].PlayWinPunch();
    }

    // 1단계는 잔액이 움직이지 않는다 — 롤업이 (잔액 − 획득량) → 잔액으로 세므로 그림은 정상이고,
    // 끝값이 시작값과 같다는 사실은 로컬 배지가 덮는다.
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
            NetworkFailurePopup.Show("룰렛 회전이 끝나지 못했습니다.");
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
            case ERouletteSpinResult.InsufficientTicket: return "룰렛 티켓이 부족합니다.";
            case ERouletteSpinResult.Rejected:           return "회전이 거절되었습니다.\n잠시 후 다시 시도해 주세요.";
            case ERouletteSpinResult.RouletteNotFound:   return "룰렛을 찾을 수 없습니다.\n잠시 후 다시 시도해 주세요.";
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

        // 암막은 공용 ScreenDim(Full)이 그린다 — 딤 노드는 알파 0으로 남아 뒤쪽 입력만 삼킨다.
        if (_visible) ScreenDim.Show(this, this.dimAlpha, true, this.transition.OpenDuration);
        else ScreenDim.Hide(this);

        // 풀 계약(PooledUIBase.isShow). 열고 닫는 길이 여기 하나뿐이라 상태도 여기서만 쓴다.
        this.isShow = _visible;

        this.transition.SetVisible(this.ResolveTarget(), _visible);

        if (!_visible && this.bulbRing != null) this.bulbRing.Stop();
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
