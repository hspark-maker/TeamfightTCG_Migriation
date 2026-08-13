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
    [Tooltip("상대 프로필이 꽂힌 뒤 대치로 넘어가기까지의 뜸(초). 이름과 랭크를 읽을 시간이다 — 0.4 아래로 내리면 못 읽는다.")]
    [SerializeField] float foundHold = 0.7f;

    [Tooltip("두 프로필이 부딪히고 VS가 뜬 뒤 덱 화면으로 넘어가기까지의 뜸(초).")]
    [SerializeField] float versusHold = 0.8f;

    [Tooltip("대치할 때 두 프로필이 서로에게 다가가는 거리(px). 0이면 이동 없이 VS만 뜬다.")]
    [SerializeField] float versusApproach = 60f;

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

    void Awake()
    {
        if (myProfile       != null) m_myHome       = myProfile.Rect.anchoredPosition;
        if (opponentProfile != null) m_opponentHome = opponentProfile.Rect.anchoredPosition;

        EnsureWired();
    }

    void OnDestroy()
    {
        // 셸만 먼저 파괴되는 경우(호스트는 살아 있다) 매치메이커가 계속 돌다 파괴된 화면을 그린다.
        m_cts?.Cancel();
        StopDots();
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

        try
        {
            return await RunStagesAsync(_matchmaker, _ct);
        }
        finally
        {
            StopDots();

            // 성공 경로도 여기로 온다(SetActive는 멱등) — 매치메이커가 계약을 어기고 던져도 화면이 남지 않는다.
            Close();

            m_running = false;
            m_cts.Dispose();
            m_cts = null;
        }
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

        ShowFound(t_opponent.Value);

        // 이 뒤의 대기는 유저 취소(m_cts)가 아니라 씬 파괴(_ct)만 본다 — 발견 이후는 취소를 받지 않기 때문이다.
        if (await WaitAsync(foundHold, _ct)) return null;

        PlayVersus();
        if (await WaitAsync(versusHold, _ct)) return null;

        return t_opponent;
    }

    void OpenSearching()
    {
        gameObject.SetActive(true);
        EnsureWired();

        RestoreHome(myProfile,       m_myHome);
        RestoreHome(opponentProfile, m_opponentHome);

        if (myProfile       != null) myProfile.Render(MatchProfile.OfLocalPlayer());
        if (opponentProfile != null) opponentProfile.ShowSearching();
        if (versusRoot      != null) versusRoot.SetActive(false);

        SetCancelInteractable(true);
        StartDots();
    }

    // 여기서부터 취소를 받지 않는다 — 이미 뽑은 상대를 버리고 다시 누르면 다른 상대가 나와,
    // 유저가 상대를 고르는 장치로 오해할 수 있다.
    void ShowFound(in MatchOpponent _opponent)
    {
        StopDots();
        SetCancelInteractable(false);

        if (titleText != null) titleText.text = foundTitle;

        if (opponentProfile == null) return;

        opponentProfile.Render(_opponent.Profile);
        UiPunch.Play(opponentProfile.transform);
    }

    void PlayVersus()
    {
        if (versusRoot != null)
        {
            versusRoot.SetActive(true);
            UiPunch.Play(versusRoot.transform, 0.6f, 0.35f);
        }

        if (versusApproach <= 0f || myProfile == null || opponentProfile == null) return;

        // 미는 방향은 두 카드의 실제 배치에서 구한다 — 어느 쪽이 왼쪽인지 프리팹을 몰라도 된다.
        Vector2 t_gap  = m_opponentHome - m_myHome;
        Vector2 t_step = (t_gap.sqrMagnitude > 0.01f ? t_gap.normalized : Vector2.right) * versusApproach;

        Approach(myProfile.Rect,       m_myHome,        t_step);
        Approach(opponentProfile.Rect, m_opponentHome, -t_step);
    }

    // 서로를 향해 부딪혔다가 제자리로 튕겨 나온다.
    static void Approach(RectTransform _rect, Vector2 _home, Vector2 _step)
    {
        _rect.DOKill();
        _rect.anchoredPosition = _home;

        // SetTarget이 없으면 시퀀스에 중첩된 트윈이 DOKill(대상 필터)에서 빠져나가 재진입 때 위치를 계속 민다.
        DOTween.Sequence().SetTarget(_rect).SetLink(_rect.gameObject)
               .Append(_rect.DOAnchorPos(_home + _step, 0.16f).SetEase(Ease.InQuad))
               .Append(_rect.DOAnchorPos(_home,         0.32f).SetEase(Ease.OutBack));
    }

    static void RestoreHome(MatchProfileView _view, Vector2 _home)
    {
        if (_view == null) return;

        _view.Rect.DOKill();
        _view.Rect.anchoredPosition = _home;
    }

    // 취소 버튼. 실제로 어디로 돌아갈지는 호스트가 정한다(셸은 씬을 모른다).
    public void Cancel()
    {
        m_cts?.Cancel();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    void SetCancelInteractable(bool _on)
    {
        if (cancelButton != null) cancelButton.interactable = _on;
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
