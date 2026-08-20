using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 로비 PlayBtn이 여는 매칭 연출 오버레이의 셸(MatchmakingRoot에 부착). MatchDeckShell의 앞자리 쌍둥이다.
//
// 상대를 실제로 구하는 것은 IMatchmaker이고 이 화면은 그 대기를 연출로 채운다 —
// 페이크(로컬 AI)든 실제(Photon)든 이 화면은 달라지지 않는다. 대기 시간의 주인이 매치메이커이기 때문이다.
//
// 무엇을 어떻게 움직일지는 전부 MatchmakingFx·MatchHandoffFx가 쥔다. 셸은 "언제"만 정한다.
public class MatchmakingShell : MonoBehaviour
{
    [SerializeField] MatchProfileView myProfile;
    [SerializeField] MatchProfileView opponentProfile;
    [SerializeField] TMP_Text         titleText;
    [SerializeField] Button           cancelButton;
    [SerializeField] GameObject       versusRoot;

    [Header("문구")]
    [Tooltip("탐색 중 문구. 점(...)은 코드가 붙였다 지운다 — 여기에 직접 넣으면 점이 겹친다.")]
    [SerializeField] string searchingTitle = "상대를 찾는 중";

    [Tooltip("상대가 확정된 순간의 문구.")]
    [SerializeField] string foundTitle = "상대를 찾았다!";

    [Header("연출 박자")]
    [Tooltip("상대가 꽂히는 안무가 끝난 뒤 충돌까지의 조임 구간(초). 이 구간은 '빈 정지'가 아니라 " +
             "압력이 차오르는 시간이다(MatchmakingFx의 조임 축들이 여기서 돈다) — 이름과 랭크를 읽는 시간이기도 하다.\n" +
             "0.4 아래로 내리면 못 읽고, 쌓인 것이 없어 충돌도 같이 약해진다.")]
    [Min(0f)] [SerializeField] float chargeHold = 0.62f;

    [Tooltip("충돌·정착이 끝난 뒤 갈라짐까지의 여운(초). 마지막 한 박은 완전 정지여야 다음 사건이 새 사건으로 읽힌다.")]
    [Min(0f)] [SerializeField] float afterglowHold = 0.34f;

    [Tooltip("대치할 때 두 프로필이 서로에게 다가가는 거리(px). 0이면 이동 없이 VS만 뜬다.")]
    [SerializeField] float versusApproach = 60f;

    [Header("연출")]
    [SerializeField] MatchmakingFx fx = new MatchmakingFx();

    [Tooltip("로비에서 이 화면이 덮어 오는 진입. 갈라짐(handoffFx)의 앞자리 짝이다 — " +
             "배너가 나중에 밀려날 그 방향에서 되돌아 들어온다.")]
    [SerializeField] MatchmakingEntryFx entryFx = new MatchmakingEntryFx();

    [Tooltip("배경 두 판(BG/Top·BG/Bottom). 이 화면의 등장은 두 판이 대각으로 맞물리는 것이고 " +
             "퇴장은 그 대각이 갈라지는 것이다 — 판이 곧 이 화면의 문이다.")]
    [SerializeField] MatchmakingBgFx bgFx = new MatchmakingBgFx();

    [Tooltip("덱 화면으로 넘어가는 전환. 커튼으로 덮지 않고 두 화면을 잇는다 — 자세한 규약은 MatchHandoffFx 참고.")]
    [SerializeField] MatchHandoffFx handoffFx = new MatchHandoffFx();

    const float DOT_INTERVAL = 0.35f;
    const int   DOT_MAX      = 3;

    // 유저 취소용. 씬 파괴 토큰과 링크해 두어 어느 쪽이 끊겨도 매치메이커까지 전달된다.
    CancellationTokenSource m_cts;
    CancellationTokenSource m_dotsCts;

    bool m_running;
    bool m_wired;

    // 대치 연출이 카드를 밀었다 되돌릴 기준 위치. 연출 도중 다시 열려도 어긋난 자리를 홈으로 삼지 않게 Awake에서만 잡는다.
    Vector2 m_myHome;
    Vector2 m_opponentHome;

    // 갈라짐에 함께 실려 나가는 것들과 그 기준 위치(제목·취소 버튼). 배너와 같은 이유로 Awake에서만 잡는다 —
    // 이미 밀려난 값을 홈으로 삼으면 매칭을 열 때마다 제목이 화면 밖으로 조금씩 걸어 나간다.
    RectTransform[] m_riders;
    Vector2[]       m_riderHomes;

    // 지금 화면에 떠 있는 안무. 화면이 내려갈 때 함께 걷지 않으면 파괴된 대상 위에서 계속 돈다.
    Sequence m_stage;

    void Awake()
    {
        if (myProfile       != null) m_myHome       = myProfile.Rect.anchoredPosition;
        if (opponentProfile != null) m_opponentHome = opponentProfile.Rect.anchoredPosition;

        CaptureRiderHomes();

        fx.Capture();

        EnsureWired();
    }

    void OnDestroy()
    {
        // 셸만 먼저 파괴되는 경우(호스트는 살아 있다) 매치메이커가 계속 돌다 파괴된 화면을 그린다.
        m_cts?.Cancel();
        StopDots();
        KillStage();
        fx.StopScan();

        // 고여 있던 빛은 시퀀스가 아니라 fx가 소유한다 — 무대만 걷으면 자가설치 노드가 남는다.
        fx.ClearCharge();
    }

    // 취소 버튼은 여기서 건다 — 프리팹 onClick으로 배선하면 셸이 모르는 취소 경로가 생겨 토큰이 살아남는다.
    void EnsureWired()
    {
        if (m_wired) return;
        m_wired = true;

        if (cancelButton == null)
        {
            Debug.LogError("[MatchmakingShell] cancelButton 미배선 — 매칭 중 물러날 방법이 없다.");

            return;
        }

        cancelButton.onClick.RemoveAllListeners();
        cancelButton.onClick.AddListener(Cancel);
    }

    // 매칭 게이트. 호스트(LobbyMatchLauncher)가 덱 화면을 열기 "전에" 이걸 await 한다.
    // null = 유저가 물러났거나 매칭이 실패했다(또는 씬이 내려갔다) → 호스트가 복귀를 처리한다.
    public async UniTask<MatchOpponent?> RunMatchAsync(IMatchmaker _matchmaker, CancellationToken _ct)
    {
        if (_matchmaker == null)
        {
            Debug.LogError("[MatchmakingShell] 매치메이커가 없다 — 매칭을 건너뛴다.");

            return null;
        }

        // 이미 진행 중인데 다시 부르면 두 await가 같은 화면을 두고 경쟁한다.
        if (m_running)
        {
            Debug.LogWarning("[MatchmakingShell] 매칭이 이미 진행 중이다 — 중복 진입을 무시한다.");

            return null;
        }

        m_running = true;
        m_cts     = CancellationTokenSource.CreateLinkedTokenSource(_ct);

        MatchOpponent? t_result = null;
        try
        {
            t_result = await RunStagesAsync(_matchmaker, _ct);

            return t_result;
        }
        finally
        {
            StopDots();

            // 내리는 것은 물러날 때뿐이다. 상대가 확정되면 이 화면의 부품들이 그대로 덱 화면으로 옮겨 앉으므로
            // (PlayHandoffAsync) 여기서 내리면 옮길 것이 사라진다 — 실제로 내리는 것도 그 전환의 마지막 프레임이다.
            // 매치메이커가 계약을 어기고 던져도 t_result는 null이라 화면이 남지 않는다.
            if (t_result == null) Close();

            m_running = false;
            m_cts.Dispose();
            m_cts = null;
        }
    }

    /// <summary>
    /// 덱 화면으로 넘어가는 전환. 상대가 확정된 뒤 호스트가 덱 화면을 세워 두고 이걸 await 한다.
    /// 끝나면 이 화면은 내려가고 모든 축이 저작 상태로 돌아간다 — 전환은 도달하는 과정만 바꾼다.
    /// </summary>
    public async UniTask PlayHandoffAsync(MatchHandoffTargets _targets, CancellationToken _ct)
    {
        KillStage();

        var t_root = (RectTransform)transform;

        m_stage = handoffFx.Build(myProfile, opponentProfile, VersusRect, fx.Dim.Target,
                                  t_root, Riders, fx.RaySprite, in _targets);
        m_stage.SetLink(gameObject);

        // 배경 두 판이 갈라지며 덱 화면이 드러난다. 이 축이 없으면 판(alpha 0.94)이 덱을 가린 채
        // 등장 안무가 진행되다가, 화면을 내리는 프레임에 이미 절반쯤 진행된 덱이 튀어나온다.
        m_stage.Insert(0f, bgFx.BuildPart(t_root));

        // 배너가 다 나가고 배경 판까지 다 열린 프레임에 내려간다. 한 프레임이라도 일찍 내리면
        // 아직 화면에 남아 있던 판이 통째로 사라져 전환 한복판이 끊긴다 — 걷어내는 도중에 끄는 것이 곧 하드컷이다.
        // 뒤의 덱 등장까지 켜 두지 않는 이유는 알파 0짜리 딤이 그동안 터치를 먹기 때문이다.
        m_stage.InsertCallback(Mathf.Max(handoffFx.CloseAt, bgFx.PartDuration), Close);

        await m_stage.ToUniTask(cancellationToken: _ct).SuppressCancellationThrow();

        // 씬이 내려가는 중이다 — 파괴될 오브젝트를 건드리지 않는다.
        if (_ct.IsCancellationRequested) return;

        Close();   // 안무가 중간에 잘렸을 수 있다(SetActive는 멱등).
    }

    // 탐색중 → 발견 → 대치 → 진입. 어느 단계에서 끊겨도 null로 빠져나온다.
    async UniTask<MatchOpponent?> RunStagesAsync(IMatchmaker _matchmaker, CancellationToken _ct)
    {
        OpenSearching();

        MatchOpponent? t_opponent = await _matchmaker.FindOpponentAsync(m_cts.Token);

        // 씬이 내려가는 중이다 — 파괴될 오브젝트를 건드리지 않는다.
        if (_ct.IsCancellationRequested) return null;

        // 유저 취소이거나 상대를 못 구했다. 어디로 돌아갈지는 호스트가 정한다(셸은 씬을 모른다).
        //
        // 덱이 빈 상대는 여기서 거르지 않는다 — 실제 매칭은 상대 덱이 배틀 씬(SyncInitialDecks)에서
        // 도착해 이 시점엔 프로필만 온다. 덱 폴백은 호스트(ConfirmOpponent)가 전담한다.
        if (t_opponent == null) return null;

        // 꽂힘(①)과 조임(②)은 한 시퀀스로 붙어 돈다 — 사이에 셸이 끼면 그 프레임에 안무가 한 번 끊긴다.
        ShowFound(t_opponent.Value);

        // 이 뒤의 대기는 유저 취소(m_cts)가 아니라 씬 파괴(_ct)만 본다 — 발견 이후는 취소를 받지 않기 때문이다.
        if (await WaitAsync(fx.FoundDuration + chargeHold, _ct)) return null;

        // 충돌(③)이 무대를 갈아탄다. 조임이 끌어다 놓은 자리에서 그대로 이어받으므로 되돌리지 않는다.
        PlayVersus();

        // 정착 + 여운(④). 여운이 안무보다 짧으면 VS의 호흡이 잘린 채 갈라짐이 시작된다.
        if (await WaitAsync(fx.VersusDuration + Mathf.Max(fx.AfterglowDuration, afterglowHold), _ct)) return null;

        return t_opponent;
    }

    void OpenSearching()
    {
        gameObject.SetActive(true);
        EnsureWired();

        // 직전 전환이 배너를 화면 밖으로 밀어내고 화면을 줄여 놓은 채 끝났다 — 저작 상태로 되돌린 뒤에 연다.
        KillStage();
        fx.Reset(myProfile, opponentProfile, (RectTransform)transform, VersusRect);
        handoffFx.Reset((RectTransform)transform, VersusRect);

        // 지난 전환이 배경 두 판을 화면 밖으로 밀어 놓고 끝났다 — 되돌리지 않으면 다음 매칭이 배경 없이 열린다.
        bgFx.Reset();

        RestoreHome(myProfile,       m_myHome);
        RestoreHome(opponentProfile, m_opponentHome);
        RestoreRiders();

        if (myProfile       != null) myProfile.Render(MatchProfile.OfLocalPlayer());
        if (opponentProfile != null) opponentProfile.ShowSearching();
        if (versusRoot      != null) versusRoot.SetActive(false);

        // 지난 매칭이 발견 순간에 내려 둔 버튼이다(DismissCancel) — 자리·알파는 RestoreRiders가 이미 되돌렸다.
        if (cancelButton != null) cancelButton.gameObject.SetActive(true);

        SetCancelInteractable(true);
        StartDots();

        // 진입 안무. 어둠이 로비 위로 차오르고 두 배너가 바깥에서 꽂힌다 — 이게 없으면 로비가 한 프레임에
        // 사라져 매칭이 "다음 화면"이 되고, 갈라짐(handoffFx)이 세운 축과도 끊긴다.
        //
        // 여는 순서가 곧 전제다: 바로 위에서 fx.Reset·RestoreHome이 저작 상태로 되돌린 뒤라야
        // 안무가 지금 자리를 홈으로, 지금 딤 알파를 목표로 삼을 수 있다.
        var t_root = (RectTransform)transform;

        var t_enter = entryFx.Build(myProfile, opponentProfile, VersusRect, fx.Dim.Target,
                                    t_root, Riders, bgFx.EnterNormal);

        // 배경 두 판이 맞물려 로비를 덮는 것이 곧 이 화면의 등장이다 — 배너는 그 뒤에 들어온다(entryFx.bannerAt).
        t_enter.Insert(0f, bgFx.BuildClose(t_root));

        // 스캔·호흡은 배너가 앉은 뒤에 켠다 — 아직 날아드는 틀 안에서 띠가 돌면 두 움직임이 겹쳐 어느 쪽도 읽히지 않는다.
        // 안무가 그 전에 잘리는 길은 발견(ShowFound)과 씬 파괴뿐이고, 둘 다 켜면 안 되는 자리라 보정하지 않는다.
        //
        // 호흡을 함께 거는 이유: 대기는 이 화면에서 가장 긴 구간인데(2~3.5초) 축이 스캔 하나뿐이라,
        // 띠가 서너 번 지나고 나면 그 뒤로는 정지 화면과 구분되지 않는다.
        t_enter.InsertCallback(entryFx.ScanAt, StartWaitingAxes);

        PlayStage(t_enter);
    }

    // 기다리는 동안 도는 상주 축들(스캔 띠 · 두 틀의 호흡). 끝을 모르므로 무한 반복이고,
    // 걷는 것은 발견(ShowFound)과 화면이 내려갈 때(OnDestroy·fx.Reset)뿐이다.
    void StartWaitingAxes()
    {
        if (opponentProfile != null) fx.StartScan(opponentProfile.SearchingRect);

        fx.StartIdle(myProfile       != null ? myProfile.FoundRect       : null,
                     opponentProfile != null ? opponentProfile.SearchingRect : null);
    }

    // 여기서부터 취소를 받지 않는다 — 이미 뽑은 상대를 버리고 다시 누르면 다른 상대가 나와,
    // 유저가 상대를 고르는 장치로 오해할 수 있다.
    void ShowFound(in MatchOpponent _opponent)
    {
        StopDots();
        fx.StopScan();
        fx.StopIdle();
        DismissCancel();

        if (titleText != null) titleText.text = foundTitle;

        if (opponentProfile == null) return;

        opponentProfile.Render(_opponent.Profile);

        var t_root = (RectTransform)transform;
        var t_seq  = fx.BuildFound(opponentProfile, t_root);

        // 조임은 꽂힘이 끝나는 자리에 이어 붙인다. 별도 무대로 돌리면 그 사이 한 프레임이 완전 정지가 되어,
        // 채우려던 바로 그 공백이 앞으로 옮겨 갈 뿐이다.
        t_seq.Insert(fx.FoundDuration,
                     fx.BuildCharge(myProfile != null ? myProfile.Rect : null, m_myHome,
                                    opponentProfile.Rect, m_opponentHome,
                                    VersusStep, t_root, VersusAnchored, chargeHold));

        PlayStage(t_seq);
    }

    void PlayVersus()
    {
        var t_vs = VersusRect;
        if (versusRoot != null) versusRoot.SetActive(true);

        // 짓기 전에 먼저 걷는다. PlayStage도 걷지만 그건 인자를 다 만든 뒤라,
        // 안무를 짓는 동안 조임이 아직 살아 있어 같은 카드를 두 시퀀스가 붙들고 있는 순간이 생긴다.
        KillStage();

        PlayStage(fx.BuildVersus(myProfile != null ? myProfile.Rect : null,       m_myHome,
                                 opponentProfile != null ? opponentProfile.Rect : null, m_opponentHome,
                                 VersusStep, t_vs, (RectTransform)transform));
    }

    // 미는 방향은 두 카드의 실제 배치에서 구한다 — 어느 쪽이 위인지 프리팹을 몰라도 된다.
    // 조임과 충돌이 같은 걸음을 써야 끌린 방향 그대로 부딪힌다.
    Vector2 VersusStep
    {
        get
        {
            Vector2 t_gap = m_opponentHome - m_myHome;

            return (t_gap.sqrMagnitude > 0.01f ? t_gap.normalized : Vector2.right) * versusApproach;
        }
    }

    // VS가 뜰 자리. 조임의 빛이 여기에 고인다 — VS가 아직 꺼져 있어도 좌표는 읽을 수 있다.
    Vector2 VersusAnchored => VersusRect != null ? VersusRect.anchoredPosition : Vector2.zero;

    // 배너에 실리지 않은 것들. 갈라짐이 이들도 함께 실어 내보낸다 —
    // 아니면 전환 한복판에서 제목과 취소 버튼이 한 프레임에 증발한다.
    RectTransform[] Riders => m_riders ??= new[]
    {
        titleText    != null ? (RectTransform)titleText.transform    : null,
        cancelButton != null ? (RectTransform)cancelButton.transform : null,
    };

    // 취소 버튼. 실제로 어디로 돌아갈지는 호스트가 정한다(셸은 씬을 모른다).
    public void Cancel()
    {
        m_cts?.Cancel();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    RectTransform VersusRect => versusRoot != null ? (RectTransform)versusRoot.transform : null;

    // 한 번에 도는 안무는 하나뿐이다 — 발견이 아직 도는 중에 대치가 겹치면 같은 카드를 두 트윈이 민다.
    void PlayStage(Sequence _seq)
    {
        KillStage();

        if (_seq == null) return;

        m_stage = _seq.SetLink(gameObject);
        m_stage.Play();
    }

    void KillStage()
    {
        m_stage?.Kill();
        m_stage = null;
    }

    void SetCancelInteractable(bool _on)
    {
        if (cancelButton != null) cancelButton.interactable = _on;
    }

    // 취소 버튼을 자리에서 물러나게 한 뒤 내린다. interactable만 끄면 반투명한 버튼이 그 자리에 남아
    // "눌러도 되나?"를 남긴다 — 여기서부터 물러날 수 없다는 사실은 흐려짐이 아니라 자리를 뜨는 것으로 말해야 한다.
    //
    // 내려도 갈라짐(MatchHandoffFx)의 rider 목록에는 그대로 남는다 — 비활성 오브젝트 위의 트윈은 그려지지 않아
    // 무해하고, 배열에서 빼면 홈 좌표(m_riderHomes)의 인덱스가 어긋난다.
    void DismissCancel()
    {
        SetCancelInteractable(false);

        if (cancelButton == null) return;

        var t_seq = fx.BuildCancelDismiss((RectTransform)cancelButton.transform);

        t_seq.SetLink(gameObject);

        // 발견 안무(m_stage)와 나란히 돈다 — 같은 사건의 두 축이라 한쪽이 다른 쪽을 기다리면 박자가 어긋난다.
        t_seq.OnComplete(() => { if (cancelButton != null) cancelButton.gameObject.SetActive(false); });
        t_seq.Play();
    }

    // 제목·취소 버튼의 저작 자리를 한 번만 잡는다. Riders 프로퍼티가 배열을 세우고 여기가 그 자세를 기록한다.
    void CaptureRiderHomes()
    {
        var t_riders = Riders;

        m_riderHomes = new Vector2[t_riders.Length];

        for (int t_i = 0; t_i < t_riders.Length; t_i++)
            if (t_riders[t_i] != null) m_riderHomes[t_i] = t_riders[t_i].anchoredPosition;
    }

    // 갈라짐이 밀어낸 제목·취소 버튼을 제자리로. 되돌리지 않으면 다음 매칭이 제목 없는 화면으로 열린다.
    void RestoreRiders()
    {
        if (m_riderHomes == null) return;

        var t_riders = Riders;

        for (int t_i = 0; t_i < t_riders.Length && t_i < m_riderHomes.Length; t_i++)
        {
            var t_rider = t_riders[t_i];
            if (t_rider == null) continue;

            t_rider.DOKill();
            t_rider.anchoredPosition = m_riderHomes[t_i];

            var t_group = t_rider.GetComponent<CanvasGroup>();
            if (t_group == null) continue;   // 전환을 한 번도 안 탔으면 아직 붙지 않았다

            t_group.DOKill();
            t_group.alpha = 1f;
        }
    }

    static void RestoreHome(MatchProfileView _view, Vector2 _home)
    {
        if (_view == null) return;

        _view.Rect.DOKill();
        _view.Rect.anchoredPosition = _home;
        _view.Rect.localScale       = Vector3.one;

        // 전환이 카드를 통째로 흐려 놓고 끝난다 — 되돌리지 않으면 다음 매칭이 투명한 카드로 열린다.
        _view.Group.DOKill();
        _view.Group.alpha = 1f;
    }

    // 취소되면 true. 예외 대신 값으로 받아 호출부가 한 줄로 갈린다.
    static async UniTask<bool> WaitAsync(float _seconds, CancellationToken _ct)
    {
        if (_seconds <= 0f) return _ct.IsCancellationRequested;

        return await UniTask.Delay(TimeSpan.FromSeconds(_seconds), cancellationToken: _ct)
                            .SuppressCancellationThrow();
    }

    void StartDots()
    {
        StopDots();

        m_dotsCts = new CancellationTokenSource();
        AnimateDotsAsync(m_dotsCts.Token).Forget();
    }

    void StopDots()
    {
        if (m_dotsCts == null) return;

        m_dotsCts.Cancel();
        m_dotsCts.Dispose();
        m_dotsCts = null;
    }

    async UniTaskVoid AnimateDotsAsync(CancellationToken _ct)
    {
        int t_count = 0;

        while (!_ct.IsCancellationRequested)
        {
            if (titleText != null) titleText.text = searchingTitle + new string('.', t_count);

            t_count = (t_count + 1) % (DOT_MAX + 1);

            bool t_canceled = await UniTask.Delay(TimeSpan.FromSeconds(DOT_INTERVAL), cancellationToken: _ct)
                                           .SuppressCancellationThrow();

            if (t_canceled) return;
        }
    }
}
