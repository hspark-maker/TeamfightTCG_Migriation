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
//
// 진입점은 둘이다. RunMatchAsync는 랭크전(상대를 기다린다)이고, PlayVersusAsync는 모험 정점 대치
// (상대가 이미 정해져 있다)다. 대치는 상대를 구하지 않으므로 탐색 문구·점(...)·스캔 띠·취소 버튼·
// 발견 슬램이 전부 없고, 배경 두 판에 두 프로필이 실려 떨어져 이음매에서 한 번 부딪히는 것이 전부다 —
// 찾지 않은 상대를 찾은 척하면 그 자리에서 매칭 문법이 거짓이 된다.
//
// 부품은 한 벌뿐이고 두 모드가 갈리는 것은 튜닝 묶음 셋(ClashTuning·EntranceTuning·SeamTuning)이다.
// 진입할 때마다 그 판의 값을 통째로 갈아끼워, 한 인스턴스가 두 모드를 번갈아 타도 항상 결정적이다.
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

    [Tooltip("랭크전에서 두 프로필이 서로에게 다가가는 거리(px). 0이면 이동 없이 VS만 뜬다.")]
    [SerializeField] float versusApproach = 60f;

    [Header("대치 인트로(모험) — 랭크전과 갈리는 값만")]
    [Tooltip("대치 문구. 이 진입점은 상대를 찾지 않으므로 탐색 문구가 없다 — 열리는 순간부터 이 한 줄이다.")]
    [SerializeField] string versusTitle = "도전";

    [Tooltip("대치에서 두 프로필이 서로에게 다가가는 거리(px). 랭크전보다 크게 잡는다 — 그쪽은 물러났다 치는 " +
             "예비동작이 있지만 여기는 낙하가 그 몫을 대신하고 곧바로 관성으로 박힌다.")]
    [SerializeField] float versusIntroApproach = 90f;

    // ⚠ 아래 세 묶음(격돌·등장·이음매)의 이니셜라이저 숫자는 진실원이 아니다 — 런타임은 프리팹 저작값이 이긴다.
    //   지우면 프리팹에 저작되지 않은 새 인스턴스에서 연출이 0으로 죽으므로 씨앗으로 남겨 둔다.
    const string TUNING_SOURCE_NOTE =
        "\n\n[진실원] 런타임에 도는 값은 프리팹(MatchmakingRoot)에 저작된 이 인스펙터 값이다. " +
        "코드 이니셜라이저의 숫자는 프리팹에 저작되지 않은 새 인스턴스가 0으로 죽지 않게 두는 씨앗일 뿐이라 " +
        "여기 값과 다를 수 있다 — 튜닝은 이 인스펙터에서 한다.";

    [Tooltip("대치의 격돌 세기. 예비동작 거리·시간을 0에 가깝게 두는 것이 이 모드의 핵심이다 — " +
             "낙하가 이미 예비동작이라 물러났다 치면 사건이 두 번이 된다." + TUNING_SOURCE_NOTE)]
    [SerializeField] MatchmakingFx.ClashTuning versusIntroClash = new MatchmakingFx.ClashTuning
    {
        chargeShake = 3.6f, chargeGlowFrom = 0.3f, releaseKick = 0.12f,
        windUpDistance = 0f, windUpDuration = 0.01f, windUpHold = 0f,
    };

    [Tooltip("대치의 등장 박자. 프로필은 판에 실려 떨어지므로 banner* 는 사실상 꺼 둔다 — " +
             "여기 남는 것은 화면 배율과 제목뿐이다." + TUNING_SOURCE_NOTE)]
    [SerializeField] MatchmakingEntryFx.EntranceTuning versusIntroEntrance = new MatchmakingEntryFx.EntranceTuning
    {
        rootDuration = 0.2f, bannerAt = 0f, bannerDuration = 0.01f, bannerDistance = 0f,
        riderAt = 0f, riderDuration = 0.2f,
    };

    [Tooltip("대치의 이음매 굵기. 이 모드는 두 판이 맞물리는 것 자체가 사건이라 랭크전보다 굵다." + TUNING_SOURCE_NOTE)]
    [SerializeField] MatchmakingBgFx.SeamTuning versusIntroSeam = new MatchmakingBgFx.SeamTuning
    {
        seamThickness = 10f,
    };

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

    // 대치가 태울 라이더(제목만). 취소가 없는 모드라 랭크전 배열과 갈린다 — 홈 좌표는 위 배열이 소유한다.
    RectTransform[] m_versusRiders;

    // 조임의 떨림이 밀어 놓을 화면 자리. 떨리는 중에 다시 잡으면 어긋난 자리가 홈이 되므로 Awake에서만 잡는다.
    Vector2 m_rootHome;

    // 판이 맞물리는 시각 = 프로필이 부딪히는 시각. 대치의 모든 박자가 이 한 값을 기준으로 붙는다.
    float m_landAt;

    // 랭크전 튜닝의 진실원. 프리팹 저작값을 Awake에서 한 번 잡아 두고 랭크전에 진입할 때마다 되돌린다.
    MatchmakingFx.ClashTuning         m_rankedClash;
    MatchmakingEntryFx.EntranceTuning m_rankedEntrance;
    MatchmakingBgFx.SeamTuning        m_rankedSeam;

    // 이번 판이 대치인가. 라이더 구성과 대치 걸음이 여기서 갈린다.
    bool  m_versusMode;
    float m_approach;

    // 지금 화면에 떠 있는 안무. 화면이 내려갈 때 함께 걷지 않으면 파괴된 대상 위에서 계속 돈다.
    Sequence m_stage;

    void Awake()
    {
        // 층의 주인은 표다 — 이 호출이 매번 UiSortingOrder.Matchmaking을 다시 찍으므로 프리팹 저작값은 읽기용 사본이고,
        // 사본이 표와 갈리면 OnValidate가 잡는다. 승격 문은 하나여야 한다는 규약대로 여기서 걸고,
        // Canvas·GraphicRaycaster가 빠진 채로 서는 경우까지 이 한 줄이 메운다(있으면 그대로 재사용된다).
        UiSortingOrder.LiftNested(gameObject, UiSortingOrder.Matchmaking);

        // 홈 좌표를 잡기 전에 와야 한다 — 아래에서 굳는 기준이 전부 이 rect 위에서 읽힌다.
        FitToParent();

        if (myProfile       != null) m_myHome       = myProfile.Rect.anchoredPosition;
        if (opponentProfile != null) m_opponentHome = opponentProfile.Rect.anchoredPosition;

        m_rootHome = ((RectTransform)transform).anchoredPosition;

        CaptureRiderHomes();

        fx.Capture();

        // 지금 프리팹에 저작된 값이 곧 랭크전 튜닝이다 — 대치가 갈아끼운 뒤 되돌아올 자리다.
        m_rankedClash    = fx.CaptureClash();
        m_rankedEntrance = entryFx.CaptureEntrance();
        m_rankedSeam     = bgFx.CaptureSeam();

        m_approach = versusApproach;

        EnsureWired();
    }

    // 저작 사본이 표와 갈리면 프리팹만 고친 사람은 자기 수정이 먹은 줄 안다 — 고치는 그 자리에서 알린다.
    // 실행 시점으로 미루면 늦고, 로비 밑에 중첩으로 선 캔버스는 저작값을 되돌려 주지도 않는다
    // (overrideSorting이 꺼져 있으면 getter가 부모 값을 준다 · UiSortingOrder.DropNested).
    void OnValidate()
    {
#if UNITY_EDITOR
        var t_canvas = GetComponent<Canvas>();
        if (t_canvas == null || !t_canvas.isRootCanvas) return;

        if (t_canvas.sortingOrder != UiSortingOrder.Matchmaking)
            Debug.LogWarning(
                $"[MatchmakingShell] 저작된 층이 표와 다릅니다(저작 {t_canvas.sortingOrder} ≠ 표 {UiSortingOrder.Matchmaking}) — "
              + "런타임은 표를 따르므로 프리팹만 고쳐서는 바뀌지 않습니다. UiSortingOrder.Matchmaking을 고칠 것.", this);
#endif
    }

    /// <summary>부모 아래에서 화면을 꽉 채우게 되돌린다. 부모를 얻은 직후 한 번만 부르면 된다 —
    /// 전투로 실려 갈 때 부모에서 떨어지지만 그때는 루트 Canvas가 rect를 소유하므로 되돌릴 것이 없다.
    ///
    /// <para>⚠ 프리팹에 저작된 루트 RectTransform 값을 믿을 수 없다. 이 프리팹은 루트에 Canvas를 갖는데,
    /// Unity는 <b>루트</b> Canvas의 RectTransform을 Overlay 규약(pivot 0,0 · 앵커 0,0 · 배율 0)으로 매 프레임 덮어쓰고
    /// 프리팹 저장도 그 상태를 직렬화한다(SceneCurtain.prefab도 같은 값으로 저작돼 있다).
    /// 루트로 설 때는 Canvas가 다시 덮어 주므로 무해하지만, 로비 아래 <b>중첩</b>으로 설 때는 아무도 덮어 주지 않아
    /// 화면이 크기 0으로 좌하단 구석에 앉는다.</para></summary>
    void FitToParent()
    {
        if (transform.parent == null) return;

        var t_rect = (RectTransform)transform;

        t_rect.localScale = Vector3.one;
        t_rect.anchorMin  = Vector2.zero;
        t_rect.anchorMax  = Vector2.one;
        t_rect.pivot      = new Vector2(0.5f, 0.5f);
        t_rect.offsetMin  = Vector2.zero;
        t_rect.offsetMax  = Vector2.zero;
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

        ApplyRankedTuning();

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
                                  t_root, ActiveRiders, fx.RaySprite, in _targets);
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

    /// <summary>
    /// 대치 게이트(모험 정점). 호스트가 덱 화면을 세우기 "전에" 이걸 await 한다. 취소 버튼이 없으므로
    /// 끝은 하나뿐이다 — 끝까지 돌거나 씬이 내려간다. 물러나는 자리는 다음 화면(덱)의 취소 버튼이다.
    /// </summary>
    public async UniTask PlayVersusAsync(MatchOpponent _opponent, CancellationToken _ct)
    {
        // 이미 진행 중인데 다시 부르면 두 await가 같은 화면을 두고 경쟁한다.
        if (m_running)
        {
            Debug.LogWarning("[MatchmakingShell] 대치가 이미 진행 중이다 — 중복 진입을 무시한다.");

            return;
        }

        m_running = true;

        ApplyVersusTuning();

        // 끝까지 돈 경우에만 화면을 남긴다 — 그때만 갈라짐(PlayHandoffAsync)이 이어받아 내려 준다.
        bool t_handedOn = false;
        try
        {
            OpenVersus(_opponent);

            // 낙하 → 충돌 → 여운이 한 무대에서 이어 돈다. 무대를 갈아타지 않으므로 기다림도 한 번뿐이다.
            if (await WaitAsync(m_landAt + fx.VersusDuration
                              + Mathf.Max(fx.AfterglowDuration, afterglowHold), _ct))
                return;

            t_handedOn = true;
        }
        finally
        {
            m_running = false;

            // 취소·예외로 빠지면 넘겨받을 화면이 없다. 화면만 끄면 안무는 계속 돌아 프리팹 밖 전역 섬광이
            // 로비 위에 터지고, 고여 있던 빛은 시퀀스가 아니라 fx가 소유해 무대만 걷으면 노드가 남는다.
            if (!t_handedOn)
            {
                KillStage();
                fx.ClearCharge();
                Close();
            }
        }
    }

    // 랭크전 한 벌로 되돌린다. 한 인스턴스가 두 모드를 번갈아 타므로 진입할 때마다 통째로 갈아끼운다 —
    // 지난 판의 튜닝이 남으면 같은 버튼이 매번 다른 연출을 낸다.
    void ApplyRankedTuning()
    {
        m_versusMode = false;
        m_approach   = versusApproach;

        fx.ApplyClash(in m_rankedClash);
        entryFx.ApplyEntrance(in m_rankedEntrance);
        bgFx.ApplySeam(in m_rankedSeam);

        if (cancelButton != null) cancelButton.gameObject.SetActive(true);
    }

    // 대치 한 벌로 갈아끼운다. 취소 버튼은 여기서 내린다 — 이 모드엔 물러나는 개념이 없다.
    void ApplyVersusTuning()
    {
        m_versusMode = true;
        m_approach   = versusIntroApproach;

        fx.ApplyClash(in versusIntroClash);
        entryFx.ApplyEntrance(in versusIntroEntrance);
        bgFx.ApplySeam(in versusIntroSeam);

        if (cancelButton != null) cancelButton.gameObject.SetActive(false);
    }

    // 대치의 진입 = 낙하 = 충돌. 셋이 한 무대에서 이어 돈다.
    //
    // ⚠ 짓는 순서가 계약이다. 부품들이 "짓는 순간" 대상을 DOKill 하거나 자가설치 노드를 세우기 때문에,
    //   순서를 바꾸면 방금 지은 축이 그 자리에서 지워지거나 아직 없는 것을 참조하게 된다.
    //     ① entryFx — StageZoom이 화면을 DOKill 한다
    //     ② 조임     — 여기서 VS 자리의 빛(ChargeGlow)이 실제로 세워진다
    //     ③ 충돌     — 짓는 시점에 ②의 빛을 읽어 폭발을 예약하고, 두 프로필을 DOKill 한다
    //     ④ 낙하     — ③의 DOKill 뒤에 지어야 살아남는다
    void OpenVersus(in MatchOpponent _opponent)
    {
        gameObject.SetActive(true);

        // 직전 전환이 프로필을 화면 밖으로 밀어내고 화면을 줄여 놓은 채 끝났다 — 저작 상태로 되돌린 뒤에 연다.
        KillStage();

        // 배경을 먼저 되돌린다 — 지난 전환이 판을 화면 밖으로 밀어 놓았고, 지난 확정이 밝기 축의 기준 색을
        // 덱 색으로 옮겨 놓았다. 기준을 저작값으로 돌린 뒤라야 이어지는 fx.Reset이 옳은 색으로 칠한다.
        bgFx.Reset(fx.Dim);

        fx.Reset(myProfile, opponentProfile, (RectTransform)transform, VersusRect);
        handoffFx.Reset((RectTransform)transform, VersusRect);

        RestoreHome(myProfile,       m_myHome);
        RestoreHome(opponentProfile, m_opponentHome);
        RestoreRiders();
        RestoreRootHome();

        if (titleText  != null) titleText.text = versusTitle;
        if (versusRoot != null) versusRoot.SetActive(false);

        if (myProfile       != null) myProfile.Render(MatchProfile.OfLocalPlayer());
        if (opponentProfile != null) opponentProfile.Render(_opponent.Profile);

        var t_root = (RectTransform)transform;

        // 낙하 거리는 판이 푼다. 실린 것이 판과 같은 거리를 써야 이음매 위에 얹혀 있는 것으로 읽힌다.
        bgFx.SolveTravel(t_root, out float t_up, out float t_down);

        // 판이 맞물리는 시각이 곧 부딪히는 시각이다. 낙하와 충돌이 서로 다른 식을 쓰면 프로필이 착지 자세로
        // 멈췄다가 뒤늦게 돌진한다 — 아래 축들은 전부 이 한 값만 읽는다.
        m_landAt = Mathf.Max(entryFx.Duration, bgFx.CloseDuration);

        // 프로필을 entryFx에 태우지 않는다 — 바깥에서 따로 꽂히는 축과 판에 실려 떨어지는 축은 배타적이다.
        // 남는 것은 화면 배율과 제목뿐이다.
        Sequence t_enter = entryFx.Build(null, null, VersusRect,
                                         fx.Dim.Target, t_root, ActiveRiders, bgFx.EnterNormal);

        // 판 두 장이 맞물려 로비를 덮는다. 이게 이 화면의 등장이다.
        t_enter.Insert(0f, bgFx.BuildClose(t_root));

        // 낙하 위에 압력이 함께 차오른다. 프로필 인자를 비우는 것은 서로에게 끌리는 축(드리프트)만 빼기 위해서다.
        // ⚠ 통째로 빼면 안 된다 — 여기서 세워지는 빛이 없으면 아래 충돌의 빛 폭발이 함께 사라진다.
        t_enter.Insert(0f, fx.BuildCharge(null, default, null, default,
                                          VersusStep, t_root, VersusAnchored, m_landAt));

        // 관성 그대로 박힌다. 예비동작 거리를 0으로 저작해 두면 낙하 자체가 그 몫을 한다.
        Sequence t_clash = fx.BuildVersus(myProfile != null ? myProfile.Rect : null, m_myHome,
                                          opponentProfile != null ? opponentProfile.Rect : null, m_opponentHome,
                                          VersusStep, VersusRect, t_root);

        // 충돌이 프로필을 DOKill 한 뒤에 얹는다 — 순서를 뒤집으면 낙하가 그 자리에서 지워져
        // 두 프로필이 t=0부터 제자리에 붙은 채 판만 떨어진다.
        StageRide(t_enter, t_up, t_down);

        // 떨림은 진폭이 t²라 착지 프레임에 가장 크다. 무대를 갈아타지 않아 조임의 자체 복구(OnKill)가
        // 여기서 안 돌므로, 부딪힌 자리에서 화면을 직접 제자리로 돌려놓는다.
        t_enter.InsertCallback(m_landAt, RestoreRootHome);

        // 색이 덱 색으로 옮겨 앉는 것은 부딪힘의 결과다 — 착지 뒤에 시작해야 원인과 결과가 갈린다.
        t_enter.Insert(m_landAt, bgFx.BuildConfirm(fx.Dim));

        // VS는 부딪히는 그 프레임에 켜진다. 미리 켜면 슬램의 출발 배율(1 + vsOvershoot)이 먼저 보여
        // 튀어나온 것이 아니라 줄어든 것이 된다.
        t_enter.InsertCallback(m_landAt + fx.HitAt, ShowVersus);

        t_enter.Insert(m_landAt, t_clash);

        PlayStage(t_enter);
    }

    // 두 프로필이 판에 실려 함께 떨어진다.
    //
    // 부모를 판으로 갈아끼우지 않는다 — 판은 기울어 있어서 자식으로 옮기면 프로필이 함께 기운다.
    // 게다가 두 프로필이 서로 다른 판에 들어가 좌표계가 갈리므로 대치 걸음(VersusStep)도
    // 갈라짐의 밀어내기도 전부 무의미해진다. 판은 배율까지 1이 아니라 프로필이 함께 줄어들기도 한다.
    // 실려 있다는 말은 부모가 같다는 뜻이 아니라 거리·시간·이징이 같다는 뜻이다.
    void StageRide(Sequence _seq, float _up, float _down)
    {
        // 방향은 순수 수직이다 — 판도 (0, ±travel)로만 움직인다. 이음매 법선을 태우면 프로필이
        // 판 위에서 가로로 미끄러져 실려 있음이 깨진다(법선은 바깥에서 들어오는 제목 몫으로 남는다).
        StageRideOne(_seq, opponentProfile, m_opponentHome,  _up);
        StageRideOne(_seq, myProfile,       m_myHome,       -_down);
    }

    void StageRideOne(Sequence _seq, MatchProfileView _view, Vector2 _home, float _offsetY)
    {
        if (_view == null || Mathf.Approximately(_offsetY, 0f)) return;

        RectTransform t_rect = _view.Rect;

        t_rect.anchoredPosition = _home + new Vector2(0f, _offsetY);

        // 길이는 착지 시각 하나만 읽는다 — 판보다 등장이 길면 프로필이 먼저 착지해 굳었다가 뒤늦게 돌진한다.
        // 이징은 판을 그대로 따른다(같은 가속으로 내려와야 실려 있는 것으로 읽힌다).
        _seq.Insert(0f, t_rect.DOAnchorPos(_home, m_landAt).SetEase(bgFx.CloseEase));
    }

    void ShowVersus()
    {
        if (versusRoot != null) versusRoot.SetActive(true);
    }

    // 떨림이 밀어 놓은 화면을 제자리로. 조임이 자체 복구(OnKill)를 갖고 있지만 대치는 무대를 갈아타지 않아
    // 그것이 안무가 다 끝난 뒤에야 돈다 — 부딪힌 자리에서 셸이 직접 되돌린다.
    void RestoreRootHome()
    {
        ((RectTransform)transform).anchoredPosition = m_rootHome;
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
        // 배경을 먼저 되돌린다 — 지난 전환이 판을 화면 밖으로 밀어 놓았고, 지난 확정이 밝기 축의 기준 색을
        // 덱 색으로 옮겨 놓았다. 기준을 저작값으로 돌린 뒤라야 이어지는 fx.Reset이 옳은 색으로 칠한다
        // (순서를 뒤집으면 fx.Reset이 덱 색을 칠하고 다음 매칭이 그 색으로 열린다).
        bgFx.Reset(fx.Dim);

        fx.Reset(myProfile, opponentProfile, (RectTransform)transform, VersusRect);
        handoffFx.Reset((RectTransform)transform, VersusRect);

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
                                    t_root, ActiveRiders, bgFx.EnterNormal);

        // 배경 두 판이 맞물려 로비를 덮는 것이 곧 이 화면의 등장이다 — 배너는 그 뒤에 들어온다(entryFx.bannerAt).
        t_enter.Insert(0f, bgFx.BuildClose(t_root));

        // 스캔·호흡은 배너가 앉은 뒤에 켠다 — 아직 날아드는 틀 안에서 띠가 돌면 두 움직임이 겹쳐 어느 쪽도 읽히지 않는다.
        // 안무가 그 전에 잘리는 길은 발견(ShowFound)과 씬 파괴뿐이고, 둘 다 켜면 안 되는 자리라 보정하지 않는다.
        //
        // 호흡을 함께 거는 이유: 대기는 이 화면에서 가장 긴 구간인데(2~3.5초) 축이 스캔 하나뿐이라,
        // 띠가 서너 번 지나고 나면 그 뒤로는 정지 화면과 구분되지 않는다.
        t_enter.InsertCallback(entryFx.ScanAt, StartWaitingAxes);

        // 배너가 앉고 탐색 표현이 도는 프레임에 맞춘다 — 여는 순간에 울리면 로비 탭 소리와 겹친다.
        t_enter.InsertCallback(entryFx.ScanAt, () => SoundManager.Instance?.PlayCue(EOutgameSound.MatchSearch));

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

        // 카드가 꽂히는 그 프레임부터 배경 두 판이 덱 화면의 섹션 색으로 옮겨 앉는다 —
        // 상대가 정해졌다는 사실이 배너 한 장이 아니라 화면 전체의 색으로 드러나고,
        // 그 색이 이미 다음 화면의 색이라 나중에 판이 갈라질 때 두 화면이 색으로 이어진다.
        // 조임보다 먼저 끝나야 한다(confirmDuration 툴팁) — 안 끝나면 대치가 무대를 갈아탈 때 잘린다.
        t_seq.Insert(fx.SlamAt, bgFx.BuildConfirm(fx.Dim));
        t_seq.InsertCallback(fx.SlamAt, () => SoundManager.Instance?.PlayCue(EOutgameSound.MatchFound));

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
        ShowVersus();

        // 짓기 전에 먼저 걷는다. PlayStage도 걷지만 그건 인자를 다 만든 뒤라,
        // 안무를 짓는 동안 조임이 아직 살아 있어 같은 카드를 두 시퀀스가 붙들고 있는 순간이 생긴다.
        KillStage();

        SoundManager.Instance?.PlayCue(EOutgameSound.MatchVersus);

        PlayStage(fx.BuildVersus(myProfile != null ? myProfile.Rect : null,       m_myHome,
                                 opponentProfile != null ? opponentProfile.Rect : null, m_opponentHome,
                                 VersusStep, t_vs, (RectTransform)transform));
    }

    // 미는 방향은 두 카드의 실제 배치에서 구한다 — 어느 쪽이 위인지 프리팹을 몰라도 된다.
    // 조임과 충돌이 같은 걸음을 써야 끌린 방향 그대로 부딪힌다.
    // 걸음 크기는 이번 판의 모드가 정한다(랭크전 versusApproach · 대치 versusIntroApproach).
    Vector2 VersusStep
    {
        get
        {
            Vector2 t_gap = m_opponentHome - m_myHome;

            return (t_gap.sqrMagnitude > 0.01f ? t_gap.normalized : Vector2.right) * m_approach;
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

    // 대치가 태우는 것은 제목 하나뿐이다 — 이 모드엔 취소가 없어 랭크전보다 한 칸 짧다.
    RectTransform[] VersusRiders => m_versusRiders ??= new[]
    {
        titleText != null ? (RectTransform)titleText.transform : null,
    };

    // 이번 판이 태울 것들. 홈 좌표(m_riderHomes)는 랭크전 배열 기준이라 되돌리는 쪽은 항상 Riders를 본다.
    RectTransform[] ActiveRiders => m_versusMode ? VersusRiders : Riders;

    // 취소 버튼. 실제로 어디로 돌아갈지는 호스트가 정한다(셸은 씬을 모른다).
    public void Cancel()
    {
        m_cts?.Cancel();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    /// <summary>이 화면을 그대로 다음 씬으로 데려갈 수 있는가. 배경 두 판이 맞물려 화면을 덮고 있어야 한다 —
    /// 판이 이미 갈라진 뒤(덱 화면으로 넘어간 모험 경로)라면 데려가 봐야 덮을 것이 없다.
    ///
    /// <para>진입 경로가 아니라 <b>화면의 상태</b>로 답한다. 밖에서 "매칭 갈래를 탔는가"로 추론하면
    /// 진입 경로가 하나 늘 때마다 판정이 갈라진다.</para>
    ///
    /// <para>activeSelf가 아니라 activeInHierarchy인 이유: 이 화면은 로비 캔버스의 자식이라
    /// 부모가 꺼지면 아무것도 안 보이는데도 activeSelf는 참을 답한다.</para></summary>
    public bool CanCarryToScene => gameObject.activeInHierarchy && bgFx.IsClosed;

    /// <summary>씬을 넘어가기 직전 정리. <b>도는 안무만</b> 걷고 판·프로필·배너는 그 자리에 그대로 둔다 —
    /// 지금 화면이 그대로 실려 가는 것이 이 전환의 전부라, 무엇 하나라도 되돌리면 그 자리가 하드컷이 된다.</summary>
    public void PrepareForCarry()
    {
        StopDots();
        KillStage();
        fx.StopScan();

        // 고여 있던 빛은 시퀀스가 아니라 fx가 소유한다 — 무대만 걷으면 자가설치 노드가 남는다(OnDestroy와 같은 이유).
        fx.ClearCharge();

        // 씬이 갈리는 동안 취소가 눌리면 돌아갈 로비가 이미 없다.
        SetCancelInteractable(false);
    }

    /// <summary>이 화면이 물러나며 뒤에 있는 것을 드러낸다(재생까지 하고 그 시퀀스를 돌려준다).
    ///
    /// <para>배경 두 판이 갈라지고, 그 결에 <b>두 프로필과 제목도 각자 자기 쪽 판을 따라</b> 위아래로 실려 나간다 —
    /// 덱 화면으로 넘어갈 때(<see cref="PlayHandoffAsync"/>)와 같은 문법이다. 판만 열면 부품이 허공에 남았다가
    /// 화면이 파괴되는 프레임에 통째로 증발한다.</para></summary>
    public Sequence PlayCarryPart()
    {
        var t_root = (RectTransform)transform;

        Sequence t_seq = handoffFx.BuildCarry(myProfile, opponentProfile, VersusRect, fx.Dim.Target,
                                              t_root, ActiveRiders, fx.RaySprite);

        t_seq.Insert(0f, bgFx.BuildPart(t_root));

        // 화면 전환을 덮는 물건이라 timeScale을 신뢰하지 않는다 — 배속이 걸리면 판이 영영 안 걷힌다.
        return t_seq.SetLink(gameObject).SetUpdate(true).Play();
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
