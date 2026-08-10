using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// 삽입 연출의 상태 머신 브레인(Panel_AlbumInsert 루트 부착).
// 큐에 실려 온 신규 카드를 한 장씩 "떠 있는 카드 → 밀어 넣기 → 슬롯 안착"으로 소비한다.
//
// 소유는 이미 확정돼 있다 — 이 세션이 다루는 것은 오직 AlbumInsertMask(그림 위장)뿐이라
// 도중에 앱이 꺼져도 카드는 그냥 꽂힌 상태가 된다(설계상 허용).
//
// ⚠ 위장이 켜진 채 세션이 죽으면 카드가 영영 빈 칸으로 보인다 — OnDisable/OnDestroy의 해제를 지우지 말 것.
public class AlbumInsertSession : MonoBehaviour
{
    public static bool IsRunning { get; private set; }

    /// <summary>세션이 큐를 인계받아 처리를 끝냈다(성공·중단 무관). 안내가 다음 스텝을 이어 걸 신호다.</summary>
    public static event Action OnAnyFinished;

    /// <summary>안내가 이 세션을 몰고 있다 — 탭 이탈을 삼키고, 끝나면 페이지 오버레이까지 걷는다.</summary>
    public static bool TutorialMode;

    [Header("바깥 연결")]
    [SerializeField] AlbumTabController   albumTabController;
    [SerializeField] AlbumPageOverlayView pageOverlay;
    [SerializeField] LobbyTabController   lobbyTabController;

    [Header("패널 내부")]
    [SerializeField] AlbumSleeveView          sleeve;
    [SerializeField] AlbumInsertCardDragger   dragger;
    [SerializeField] AlbumInsertHintView      hint;
    [SerializeField] CardVisualView           cardVisual;
    [SerializeField] CanvasGroup              group;

    [Header("타이밍")]
    [SerializeField] float  pageTurnDuration = 0.25f;
    [SerializeField] float  seatDuration     = 0.28f;
    [Tooltip("손을 뗐을 때 카드가 살짝 되밀리는 시간.")]
    [SerializeField] float  reboundDuration  = 0.16f;
    [Tooltip("손을 뗄 때 되밀리는 양(진행도). 0이면 민 자리에 딱 멈춰 기계적으로 보인다.")]
    [Range(0f, 0.3f)] [SerializeField] float reboundAmount = 0.05f;
    [Tooltip("손을 댄 뒤 이만큼 아무 입력이 없으면 손가락 안내를 되살린다.")]
    [SerializeField] float  hintIdleDelay    = 3f;
    [Header("건너뛰기 자동 진행")]
    [Tooltip("건너뛴 뒤 카드 한 장이 저절로 다 밀려 들어가는 데 걸리는 시간.")]
    [SerializeField] float  autoSeatDuration = 0.5f;
    [Tooltip("자동 진행에서 카드가 뜬 뒤 밀리기 시작할 때까지의 텀. 0이면 뜨자마자 빨려 들어가 장수가 안 읽힌다.")]
    [SerializeField] float  autoStepGap      = 0.15f;
    [Header("안착 마무리 펄스")]
    [Tooltip("안착 직후 칸이 잠깐 줄어들었다 돌아오는 배율(1보다 작게).")]
    [SerializeField] float  settlePulseScale    = 0.9f;
    [Tooltip("줄었다 돌아오기까지의 전체 시간.")]
    [SerializeField] float  settlePulseDuration = 0.24f;
    [SerializeField] string guideMessage     = "슬리브에 넣기 위해 스와이프하세요";

    readonly List<AlbumInsertStep> m_steps = new List<AlbumInsertStep>();

    AlbumTheme        m_openTheme;   // 지금 오버레이가 열고 있는 테마(오버레이는 페이지만 노출한다)
    RectTransform     m_slotRect;    // 이번 스텝 칸의 rect — 안착 마무리 펀치 대상
    Coroutine     m_routine;
    bool          m_notifyOnFinish;// Begin을 시도한 세션만 완료를 알린다 — 배선 누락으로 시작조차 못 해도 한 번은 알려야 안내가 안 멎는다
    bool          m_autoPlay;      // 건너뛰기 이후 — 스텝은 그대로 두고 드래그 대기만 자동 트윈으로 대체한다
    bool          m_seatRequested;
    bool          m_releaseRequested;
    bool          m_fingerPaused;
    float         m_idleTime;

    /// <summary>큐에 쌓인 신규 카드로 세션을 시작한다. 큐가 비었거나 꽂을 칸이 없으면 조용히 끝난다.</summary>
    public void Begin()
    {
        if (IsRunning) return;

        m_notifyOnFinish = true;

        // ⚠ 순서가 중요하다: 이 컴포넌트는 Panel_PageOverlay 자식이라 오버레이가 닫혀 있으면 GameObject가 비활성이고,
        //   비활성 상태에서 StartCoroutine을 부르면 예외다.
        //   ① 큐 소비·플랜 빌드(순수 계산, 활성과 무관) → ② OpenThemePage로 부모를 켠다
        //   → ③ 자기 SetActive(true) → ④ StartCoroutine.
        if (!AlbumInsertQueue.TryConsume(out var t_cards))
        {
            Finish();
            return;
        }

        m_steps.Clear();

        // 호출자가 위장을 걸어 두는 계약이지만, 그 사이 누군가 Clear했을 수 있다(오버레이 OnDisable 안전망 등).
        // 방어적으로 다시 건다 — 이미 걸려 있으면 무연산이다.
        AlbumInsertMask.HideAll(t_cards);

        this.TakeSteps(t_cards);

        if (m_steps.Count == 0 || !this.HasWiring())
        {
            Finish();
            return;
        }

        IsRunning  = true;
        m_autoPlay = false;

        var t_first = m_steps[0];
        m_openTheme = t_first.Theme;

        // 탭 컨트롤러가 없어도 오버레이는 반드시 연다 — 안 열면 이 GameObject가 비활성이라 코루틴을 못 돌린다.
        if (albumTabController != null) albumTabController.OpenThemePage(t_first.Theme, t_first.PageIndex);
        else pageOverlay.Open(t_first.Theme, t_first.PageIndex);

        gameObject.SetActive(true);
        transform.SetAsLastSibling();   // 프리팹 저작 순서 방어(중첩 Canvas·overrideSorting은 쓰지 않는다)

        // 부모(오버레이)가 끝내 안 켜졌으면 StartCoroutine이 예외다 — 위장을 되돌리고 조용히 물러난다.
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogError("[AlbumInsertSession] 페이지 오버레이가 켜지지 않아 연출을 건너뛴다 — 카드는 그대로 꽂힌다.", this);
            Finish();
            return;
        }

        // 다른 탭으로 나가려 하면 자동 진행이 아니라 즉시 끝낸다 — 안 보이는 화면에서 연출을 계속 돌릴 이유가 없다.
        // 안내 중에는 이탈 자체를 삼킨다 — 선택된 탭은 버튼이 꺼지고 Focus가 대신하므로(LobbyTabController.Select),
        // 유저가 먼저 그 탭으로 가 버리면 뒤이어 그 버튼을 가리키는 안내가 영영 뜨지 못한다. 탈출로는 건너뛰기다.
        if (lobbyTabController != null)
            lobbyTabController.SetLeaveGuard(_p => { if (TutorialMode) return; AbortAll(); _p(); });
        if (pageOverlay != null) pageOverlay.SetInteractionLocked(true);

        if (group != null) group.blocksRaycasts = true;
        this.HideCard();

        m_routine = StartCoroutine(this.RunSteps());
    }

    void OnEnable()
    {
        if (dragger != null)
        {
            dragger.OnProgress += this.HandleProgress;
            dragger.OnSeat     += this.HandleSeat;
            dragger.OnRelease  += this.HandleRelease;
            dragger.OnGrab     += this.HandleGrab;
        }

        if (hint != null) hint.OnSkip += this.SkipToAuto;
    }

    void OnDisable()
    {
        if (dragger != null)
        {
            dragger.OnProgress -= this.HandleProgress;
            dragger.OnSeat     -= this.HandleSeat;
            dragger.OnRelease  -= this.HandleRelease;
            dragger.OnGrab     -= this.HandleGrab;
            dragger.Interactable = false;
        }

        if (hint != null) hint.OnSkip -= this.SkipToAuto;

        this.KillTweens();
        m_routine = null;

        this.ReleaseGuards();
    }

    void OnDestroy()
    {
        this.ReleaseGuards();
    }

    // 세션이 죽는 모든 경로의 마지막 방어선. 정리 순서를 두 벌로 두지 않으려고 Finish에 위임한다.
    void ReleaseGuards()
    {
        if (!IsRunning) return;

        this.Finish();
    }

    IEnumerator RunSteps()
    {
        // 스텝을 다 쓰면 큐를 다시 본다 — 연출 도중 또 팩을 열면 그 카드가 큐에 쌓인다.
        // 여기서 이어받지 않으면 큐에 영구 잔류하고, 다음 세션이 위장 없는 카드를 스텝으로 만들어
        // 이미 꽂힌 슬롯 위에 카드가 뜬다.
        // finally — 중간에 예외가 나도 위장·잠금·가드가 살아남으면 카드가 영영 빈 칸이 된다.
        // (AbortAll의 StopCoroutine 경로는 finally가 돌지 않으므로 그쪽에서 직접 Finish를 부른다)
        try
        {
            while (m_steps.Count > 0)
            {
                for (int t_i = 0; t_i < m_steps.Count; t_i++)
                {
                    var t_step = m_steps[t_i];

                    yield return this.PageTurn(t_step);

                    bool t_spawned = false;
                    yield return this.Spawn(t_step, _ok => t_spawned = _ok);
                    if (!t_spawned) continue;   // 슬롯을 못 얻은 카드는 Spawn이 이미 위장을 풀었다

                    // 건너뛴 뒤에는 손을 기다리지 않는다 — 뜬 카드를 잠깐 보여 주고 그대로 안착 트윈에 넘긴다.
                    if (m_autoPlay) yield return new WaitForSecondsRealtime(this.autoStepGap);
                    else            yield return this.AwaitDrag();

                    yield return this.Seat(t_step);
                }

                m_steps.Clear();

                if (!AlbumInsertQueue.TryConsume(out var t_more)) break;

                AlbumInsertMask.HideAll(t_more);
                this.TakeSteps(t_more);
            }
        }
        finally
        {
            this.Finish();
        }
    }

    // 카드 목록을 스텝으로 바꿔 담는다. 꽂을 칸이 없는 카드는 여기서 위장을 풀지 않으면 영영 빈 칸으로 남는다.
    void TakeSteps(IReadOnlyList<CardData> _cards)
    {
        var t_steps = AlbumInsertPlan.Build(_cards, out var t_unplaced);

        for (int t_i = 0; t_i < t_unplaced.Count; t_i++) AlbumInsertMask.Reveal(t_unplaced[t_i]);

        m_steps.AddRange(t_steps);
    }

    // 같은 페이지면 통과. 페이지가 바뀌는 동안은 패널을 지운다 — 옛 자리에 카드가 떠 있으면 자리 이동이 튄다.
    IEnumerator PageTurn(AlbumInsertStep _step)
    {
        this.SetGroupAlpha(0f);
        this.HideCard();

        if (pageOverlay == null) yield break;
        if (m_openTheme == _step.Theme && pageOverlay.PageIndex == _step.PageIndex) yield break;

        m_openTheme = _step.Theme;
        pageOverlay.Open(_step.Theme, _step.PageIndex);

        yield return new WaitForSecondsRealtime(this.pageTurnDuration);
    }

    // ⚠ GridRatioFitter가 cellSize를 런타임에 정한다 — 한 프레임 넘기고 ForceUpdateCanvases 한 뒤에만
    //   슬롯 rect가 진짜 값이다. 그 전에 읽으면 카드가 엉뚱한 자리에 뜬다.
    IEnumerator Spawn(AlbumInsertStep _step, Action<bool> _result)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        if (pageOverlay == null || !pageOverlay.TryGetSlot(_step.SlotIndex, out var t_slot))
        {
            Debug.LogWarning($"[AlbumInsertSession] 슬롯 {_step.SlotIndex}을 얻지 못했다 — 이 카드는 연출 없이 꽂는다.", this);
            AlbumInsertMask.Reveal(_step.Card);
            _result?.Invoke(false);
            yield break;
        }

        m_slotRect = t_slot.transform as RectTransform;

        // 카드를 번호 위·비닐 아래(InsertDock)로 들여보낸다 — 밀어 넣을수록 번호를 덮고 비닐 뒤로 잠긴다.
        sleeve.AlignTo(m_slotRect, t_slot.InsertDock);
        sleeve.SetProgress(0f);
        dragger.TravelPixels = sleeve.CardHeight;
        dragger.ResetProgress();   // 카드가 실제로 시작 자리에 놓인 지금이 리셋 시점이다

        cardVisual.Bind(_step.Card, true);
        cardVisual.gameObject.SetActive(true);
        UiPunch.Play(cardVisual.transform);

        // 자동 진행 중엔 안내가 거짓말이 된다 — 밀라고 해 놓고 저절로 들어가면 입력이 씹힌 것으로 읽힌다.
        if (hint != null)
        {
            if (m_autoPlay) hint.Hide();
            else            hint.Show(this.guideMessage, sleeve.CardHolder);
        }

        m_seatRequested    = false;
        m_releaseRequested = false;
        m_fingerPaused     = false;
        m_idleTime         = 0f;

        this.SetGroupAlpha(1f);
        dragger.Interactable = !m_autoPlay;

        _result?.Invoke(true);
    }

    // 임계를 넘길 때까지 스와이프를 반복한다 — 한 번에 다 들어가지 않는 것이 이 연출의 전제다.
    // 손을 떼면 살짝 되밀릴 뿐 **민 만큼은 남는다**. 유휴가 길어지면 손가락 안내를 되살린다.
    IEnumerator AwaitDrag()
    {
        // 미는 도중 건너뛰기를 누르면 민 자리에서 이어 받는다 — 되돌렸다 다시 밀면 진행이 되감기는 것으로 보인다.
        while (!m_seatRequested && !m_autoPlay)
        {
            if (m_releaseRequested)
            {
                m_releaseRequested = false;
                yield return this.Rebound();
                continue;
            }

            m_idleTime += Time.unscaledDeltaTime;
            if (m_fingerPaused && m_idleTime >= this.hintIdleDelay)
            {
                m_fingerPaused = false;
                if (hint != null) hint.ResumeFinger();
            }

            yield return null;
        }

        m_seatRequested = false;
    }

    IEnumerator Seat(AlbumInsertStep _step)
    {
        dragger.Interactable = false;
        if (hint != null) hint.PauseFinger();

        // 자동 진행은 HandleSeat를 안 거친다 — 잔떨림을 여기서 끄지 않으면 안착한 카드가 계속 떤다.
        if (m_autoPlay && sleeve != null) sleeve.SetPushing(false);

        // 자동 진행은 진행도 0부터 밀어야 하므로, 거의 다 민 뒤의 마무리와 같은 시간·감속이면 카드가 빨려 들어간다.
        float t_duration = m_autoPlay ? this.autoSeatDuration : this.seatDuration;
        Ease  t_ease     = m_autoPlay ? Ease.InOutCubic       : Ease.OutCubic;

        // ⚠ DOKill은 타깃 단위다 — 안착 트윈은 CardHolder, 카드 비주얼 펀치는 CardVisualView로 노드를 나눈다.
        var t_holder = sleeve.CardHolder;
        if (t_holder != null)
        {
            t_holder.DOKill();
            yield return this.TweenProgress(t_holder, 1f, t_duration, t_ease).WaitForCompletion();
        }

        // 트윈은 목표만 끝까지 민다 — 그림은 slipGlide만큼 늦게 따라온다. 다 따라붙기 전에 바꿔치기하면
        // 덜 들어간 카드가 꽂힌 카드로 둔갑한다. (상한은 방어 — 끝내 못 따라붙어도 연출만 건너뛴다)
        for (float t_wait = 0f; !sleeve.Settled && t_wait < 0.6f; t_wait += Time.unscaledDeltaTime)
            yield return null;

        // 다 밀어 넣은 카드는 비닐(씰 앞면) 뒤에 잠겨 있고 번호도 그 카드에 덮여 있다.
        // 위장을 풀면 같은 칸이 같은 카드를 같은 자리·같은 크기로 그리므로, 같은 프레임에 드래그 카드를
        // 걷어도 교체가 보이지 않는다(이음매 0프레임). 비닐은 걷지 않는다 — 꽂힌 칸도 같은 톤으로 덮여 있다.
        AlbumInsertMask.Reveal(_step.Card);
        this.HideCard();

        this.SettlePulse(m_slotRect);
    }

    // 안착 마무리 강조 — 부풀리는 펀치 대신 "원래크기 → 축소 → 원래크기"로 꾹 눌러 담는 손맛을 낸다.
    // ⚠ 칸(m_slotRect)은 세션이 잠시 빌려 쓰는 남의 오브젝트다. 배율이 누적되면 도감에 그대로 남으므로
    //   시작 전 DOComplete로 기준 배율에 되돌리고, Yoyo 2루프로 반드시 원래 크기에서 끝낸다.
    void SettlePulse(RectTransform _rect)
    {
        if (_rect == null) return;

        _rect.DOComplete();
        _rect.DOScale(_rect.localScale * this.settlePulseScale, this.settlePulseDuration * 0.5f)
             .SetEase(Ease.InOutSine)
             .SetLoops(2, LoopType.Yoyo)
             .SetLink(_rect.gameObject);
    }

    // 카드가 스스로 움직이는 유일한 통로. 진행도를 몰면 위치·기울기·좌우 어긋남이 함께 따라온다 —
    // ⚠ y만 트윈하면(예전 DOAnchorPosY) 각도가 그 자리에 얼어붙는다.
    // SetTarget(holder)가 필수다: HandleGrab의 CardHolder.DOKill()이 이 트윈도 걷어야
    // 되감기 중 다시 잡았을 때 트윈과 손가락이 카드를 동시에 끌지 않는다.
    Tween TweenProgress(RectTransform _holder, float _to, float _duration, Ease _ease)
    {
        return DOTween.To(() => sleeve.Progress, _v => sleeve.SetProgress(_v), _to, _duration)
                      .SetEase(_ease)
                      .SetTarget(_holder)
                      .SetLink(gameObject);
    }

    // 손을 뗀 순간 — 처음으로 되돌리지 않는다. 밀어 넣다 만 카드가 살짝 뱉어지는 만큼만 물러난다.
    // ⚠ 여기서 0으로 되돌리면 "여러 번 나눠 꽂는다"는 이 연출의 전제가 통째로 무너진다(예전 동작).
    IEnumerator Rebound()
    {
        var t_holder = sleeve.CardHolder;
        if (t_holder == null) yield break;

        float t_to = Mathf.Max(0f, sleeve.Progress - this.reboundAmount);

        t_holder.DOKill();
        yield return this.TweenProgress(t_holder, t_to, this.reboundDuration, Ease.OutQuad).WaitForCompletion();

        // 되밀린 만큼 드래그 누적도 깎아 둔다 — 안 맞추면 다음 스와이프 첫 프레임에 카드가 그만큼 순간이동한다.
        if (dragger != null) dragger.SyncProgress(t_to);
    }

    // 건너뛰기 — 남은 스텝을 버리지 않는다. 손만 떼고, 나머지가 한 장씩 저절로 꽂히는 것을 끝까지 보여준다.
    // (연출을 통째로 날리면 "무엇을 몇 장 받았는지"가 화면에서 사라진다 — 그 정보가 이 연출의 값이다.)
    void SkipToAuto()
    {
        if (!IsRunning || m_autoPlay) return;

        m_autoPlay = true;

        if (dragger != null) dragger.Interactable = false;
        if (hint != null) hint.Hide();

        // 손이 닿은 채 눌렀으면 OnEndDrag가 무시돼 잔떨림이 켜진 채 남는다 — 아무도 안 미는 카드가 떨면 안 된다.
        if (sleeve != null) sleeve.SetPushing(false);

        // 되밀림 트윈이 돌고 있으면 그 대기부터 끊어야 자동 진행이 이 자리에서 바로 이어진다.
        if (sleeve != null && sleeve.CardHolder != null) sleeve.CardHolder.DOKill();
    }

    // 남은 스텝을 전부 버리고 끝낸다. AlbumInsertMask.Clear()가 나머지를 한 번에 드러낸다.
    void AbortAll()
    {
        if (!IsRunning) return;

        if (m_routine != null)
        {
            StopCoroutine(m_routine);
            m_routine = null;
        }

        this.KillTweens();
        this.Finish();
    }

    void Finish()
    {
        // 펄스가 도는 중에 끝나면 남의 칸이 줄어든 채로 도감에 남는다 — 완료(=원래 크기 복귀)시키고 놓는다.
        if (m_slotRect != null) m_slotRect.DOComplete();

        m_steps.Clear();
        m_openTheme = null;
        m_slotRect  = null;
        m_routine   = null;
        m_autoPlay  = false;

        if (dragger != null) dragger.Interactable = false;
        if (hint != null) hint.Hide();
        this.HideCard();

        // 카드 홀더를 남의 칸 안에 두고 끝내면 다음 세션이 그 칸에서 시작한다 — 홈(패널)으로 되돌린다.
        if (sleeve != null) sleeve.Release();

        // 위장 해제가 먼저다 — 잠금을 먼저 풀면 그 프레임에 빈 칸이 눌릴 수 있다.
        AlbumInsertMask.Clear();
        if (pageOverlay != null) pageOverlay.SetInteractionLocked(false);
        if (lobbyTabController != null) lobbyTabController.ClearLeaveGuard();

        IsRunning = false;

        // 전면 Blocker가 남아 있으면 세션이 끝난 뒤에도 도감 입력을 계속 먹는다.
        if (group != null) group.blocksRaycasts = false;
        if (gameObject.activeSelf) gameObject.SetActive(false);

        // 안내가 몰던 세션이면 오버레이까지 걷는다 — 다음 안내가 하단 탭바를 쓰는데 오버레이 딤이 덮는다.
        // ⚠ IsRunning을 내린 뒤여야 한다. 앞에 두면 오버레이 비활성이 OnDisable → ReleaseGuards → Finish 재진입을 부른다.
        if (TutorialMode && pageOverlay != null) pageOverlay.Close();

        // 화면이 정리된 뒤에 알린다 — 듣는 쪽이 곧바로 다음 안내를 그 화면 위에 건다.
        if (!m_notifyOnFinish) return;
        m_notifyOnFinish = false;
        OnAnyFinished?.Invoke();
    }

    void HandleProgress(float _p)
    {
        m_idleTime = 0f;
        if (sleeve != null) sleeve.SetProgress(_p);
    }

    // 손이 떨어지는 두 경로 모두에서 잔떨림을 끈다 — 아무도 안 미는 카드가 계속 떨면 유령이 민 것처럼 보인다.
    void HandleSeat()
    {
        m_seatRequested = true;
        if (sleeve != null) sleeve.SetPushing(false);
    }

    void HandleRelease()
    {
        m_releaseRequested = true;
        if (sleeve != null) sleeve.SetPushing(false);
    }

    // 스와이프 시작 — 손가락은 걷고, 되밀림 트윈이 돌고 있으면 손에 소유권을 넘긴다.
    // 여기서 각도를 새로 뽑는 것이 "매번 다르게 걸린다"의 전부다 — 봉투가 이미 좁아져 있으므로
    // 깊이 들어간 카드일수록 아무리 뽑아도 덜 흔들린다(수렴은 공짜다).
    void HandleGrab()
    {
        m_idleTime = 0f;

        if (sleeve != null && sleeve.CardHolder != null) sleeve.CardHolder.DOKill();
        if (sleeve != null)
        {
            sleeve.NudgeTilt();
            sleeve.SetPushing(true);
        }

        if (m_fingerPaused) return;
        m_fingerPaused = true;
        if (hint != null) hint.PauseFinger();
    }

    void HideCard()
    {
        if (cardVisual == null) return;

        cardVisual.transform.DOKill();
        cardVisual.transform.localScale = Vector3.one;
        cardVisual.gameObject.SetActive(false);
    }

    void KillTweens()
    {
        if (sleeve != null && sleeve.CardHolder != null) sleeve.CardHolder.DOKill();
        if (cardVisual != null) cardVisual.transform.DOKill();
    }

    void SetGroupAlpha(float _a)
    {
        if (group != null) group.alpha = _a;
    }

    // 배선 누락은 "아무 일도 안 일어남"으로만 나타나 원인 추적이 어렵다 → 조용히 끝내지 않는다.
    bool HasWiring()
    {
        if (sleeve != null && dragger != null && cardVisual != null && pageOverlay != null) return true;

        Debug.LogError($"[AlbumInsertSession] 배선 누락 — sleeve={sleeve}, dragger={dragger}, cardVisual={cardVisual}, pageOverlay={pageOverlay}. 연출 없이 카드를 꽂는다.", this);
        return false;
    }
}
