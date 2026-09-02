using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 로비에서 "방금 무엇을 얻었는지"를 한 번 보여주는 연출 브레인.
// 진입점은 넷이다 — 씬 로드(전투 복귀)는 Start, 씬이 유지되는 카드팩 오버레이는 PackOpenOverlay.OnClosed,
//   로비에 머문 채 지급하는 쪽(튜토리얼 보상)은 PlayNow,
//   로비 진입 뒤에 늦게 도착하는 전투 보상 응답은 BattleRewardHandoff.OnGainAdded(이 경로만 종료 통지를 삼킨다).
// 전투(BattleRewardHandoff)와 카드팩(CardPackRewardHandoff) 캐리어를 소비해
//   재화 → 각 재화 텍스트로 코인이 빨려들며 숫자가 오르고 튄다(CurrencyGainEffectPlayer에 위임)
//   카드 → 도감 탭으로 카드가 빨려들며 탭이 튄다
// 두 단계를 동시에 재생한다(획득 하나를 두 번에 걸쳐 알리지 않는다).
// 카드는 신규만 온다 — 중복분은 환급 재화가 대신하고, 그 환급은 개봉 화면이 자기 잔액 표시로 이미 받아 간다.
//
// 경계: 지급·저장은 각 씬이 이미 끝냈다. 이 클래스는 표시만 하고 재화를 건드리지 않는다.
// 배선을 비워두면 이름으로 자동 탐색한다 — 로비 프리팹 수정 없이도 동작하게(자동 탐색 실패 시 그 단계만 건너뛴다).
public class LobbyGainEffectDirector : MonoBehaviour
{
    [Header("배선 (비우면 자동 탐색)")]
    [Tooltip("카드가 빨려들 도감 탭의 시각 앵커를 제공한다.")]
    [SerializeField] LobbyTabBarView tabBar;
    [Header("삽입 세션 (비우면 자동 탐색)")]
    [SerializeField] LobbyTabController lobbyTabController;
    [Tooltip("도감 탭 인덱스. 0 Shop · 1 Pack · 2 Match · 3 Deck · 4 Collection")]
    [SerializeField] int collectionTabIndex = 4;
    [SerializeField] AlbumTabController albumTabController;

    [Header("연출 값")]
    [SerializeField] float tabPunch = 0.3f;

    [Header("팩 비행 (예고 팝업 → 팩 탭)")]
    [Tooltip("팩 탭 인덱스. 0 Shop · 1 Pack · 2 Match · 3 Deck · 4 Collection")]
    [SerializeField] int packTabIndex = 1;
    [Tooltip("날아가는 팩의 화면 크기(px). 비율은 아트가 지킨다.")]
    [SerializeField] Vector2 packFlightSize = new Vector2(240f, 300f);
    [Tooltip("출발 직후 살짝 솟았다가 탭으로 빨려든다. 0이면 곧장 간다.")]
    [SerializeField] float packScatterRadius = 120f;
    [SerializeField] float packScatterDuration = 0.24f;
    [SerializeField] float packGatherDuration = 0.42f;
    [SerializeField] float packPopDuration = 0.14f;
    [Tooltip("탭에 닿을 때의 배율 — 탭 안으로 삼켜지는 느낌.")]
    [SerializeField] float packGatherScale = 0.15f;
    [Tooltip("수렴 궤적이 직선에서 부푸는 폭(px). 0이면 직선.")]
    [SerializeField] float packArcHeight = 160f;

    // 런타임에 만든 하위 연출기(직렬화 배선이 있으면 그것을 쓴다).
    CardGainFlightEffect m_cardFlight;

    // 재생 중인 획득 연출. 팩을 연달아 열면 앞 연출이 끝나기 전에 또 불린다.
    Sequence m_master;

    // 이번 재생분만 쓰는 카드 출발점(PlayNow가 실어 준다). 보여 주던 화면이 있으면 그 카드가 서 있던
    // 자리에서 출발해야 "방금 본 그 카드가 도감으로 갔다"가 한 줄로 이어진다.
    RectTransform m_cardOrigin;
    RectTransform m_collectionTarget;

    // 도착 강조를 받을 자리. 목적지와 갈라 두는 이유는 탭 버튼에 자물쇠 배지 같은 런타임 자식이 붙어
    // 자식 순서로 짚으면 그쪽이 대신 튀기 때문이다(잠금이 풀린 뒤에는 꺼진 배지가 남아 아무것도 안 튄다).
    RectTransform m_collectionPunch;

    // 이번 재생분 식별자. 앞 연출을 강제 마무리(Complete)하면 그 시퀀스의 완료 콜백도 함께 터지는데,
    // 그것을 이번 재생의 종료로 오인하면 기다리던 안내가 카드가 날기도 전에 다음으로 넘어간다.
    int m_runId;

    // 마지막으로 종료를 알린 재생분. m_runId와 다르면 아직 끝나지 않은 재생이 있다.
    // m_master는 코루틴이 한 프레임 양보한 뒤에야 생겨서 "재생 중인가"의 기준이 못 된다.
    int m_finishedRunId;

    static LobbyGainEffectDirector s_instance;

    /// <summary>획득 연출이 끝났다. <b>실을 것이 없어 그냥 지나간 경우도 포함</b>해서 알린다 —
    /// 이 연출이 끝나기를 기다리는 안내(튜토리얼 CardGrant)가 신호를 놓치면 그 자리에서 영영 멈춘다.</summary>
    public static event System.Action OnAnyFinished;

    /// <summary>이 씬에서 획득 연출을 재생할 수 있는가. 꺼져 있으면 코루틴이 돌지 못해
    /// 통지가 영영 오지 않으므로, 있기만 한 것으로는 부족하다.</summary>
    public static bool Exists => s_instance != null && s_instance.isActiveAndEnabled;

    /// <summary>씬을 다시 열지 않고 지금 실린 캐리어를 재생한다. 로비에 머문 채 지급하는 쪽이 쓴다
    /// (Start·오버레이 닫힘은 이미 지나갔으므로 그 둘로는 닿지 않는다).
    /// _cardOrigin을 주면 카드가 그 자리에서 출발한다(보상 화면이 카드를 세워 두던 자리).
    /// <b>false면 아무것도 재생되지 않았고 종료 통지도 오지 않는다</b> — 캐리어 정리는 호출자 몫이다.</summary>
    public static bool PlayNow(RectTransform _cardOrigin = null)
    {
        // 판정 기준을 Exists와 같이 둔다 — 꺼져 있는 오브젝트에서는 코루틴이 돌지 못해
        // 통지도 캐리어 소비도 없이 조용히 사라진다.
        if (!Exists) return false;

        s_instance.m_cardOrigin = _cardOrigin;
        s_instance.Play();
        return true;
    }

    /// <summary>재생할 수 없어 그냥 지나갔음을 알린다. 종료를 기다리는 쪽이 영영 멈추지 않게 하는 탈출로다
    /// (OnAnyFinished가 "보여줄 것이 없어 지나간 경우도 알린다"는 규약의 연장선).</summary>
    public static void NotifySkipped()
    {
        OnAnyFinished?.Invoke();
    }

    /// <summary>팩 하나가 (보통 예고 팝업이 세워 두던 자리에서) 팩 탭으로 빨려든다.
    /// 카드 획득 비행과 같은 코어(UiGainBurst)를 쓰고 끝나면 같은 신호(OnAnyFinished)를 낸다 —
    /// 기다리는 쪽이 "카드였나 팩이었나"를 가려 듣지 않아도 되게.
    /// 소유·재화를 건드리지 않는다(예고일 뿐이다). <b>false면 아무것도 재생되지 않았고 종료 통지도 오지 않는다</b>.</summary>
    public static bool PlayPackFlight(Sprite _art, RectTransform _origin)
    {
        if (!Exists) return false;

        return s_instance.PlayPack(_art, _origin);
    }

    /// <summary>연출이 끝나기를 기다리지 않고 지금 놓아준다 — 흡수와 다음 안내를 나란히 세우는 저작이 쓴다.
    /// 비행은 그대로 계속 돌고, 뒤늦게 오는 진짜 종료 신호는 그때의 스텝이 듣지 않아 삼켜진다.</summary>
    public static void NotifyDetached()
    {
        OnAnyFinished?.Invoke();
    }

    void Awake()
    {
        s_instance = this;
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    void OnEnable()
    {
        PackOpenOverlay.OnClosed += OnPackOpenClosed;
        BattleRewardHandoff.OnGainAdded += OnBattleRewardArrived;
    }

    void OnDisable()
    {
        // static 이벤트에 죽은 씬 오브젝트가 남으면 다음 씬에서 오발화한다.
        PackOpenOverlay.OnClosed -= OnPackOpenClosed;
        BattleRewardHandoff.OnGainAdded -= OnBattleRewardArrived;

        // 카드 비행 도중 씬이 바뀌면 m_master가 Kill되고 OnComplete는 영영 안 온다 —
        // 세션에 아직 인계 못 한 위장은 여기서 되돌린다(안 그러면 그 카드가 도감에서 영영 빈 칸이다).
        if (!AlbumInsertSession.IsRunning && AlbumInsertQueue.HasPending) CancelInsertSession();
    }

    void Start()
    {
        Play();
    }

    // 오버레이 개봉은 로비를 재로드하지 않는다 — 닫힘 신호가 Start를 대신하는 두 번째 진입점이다.
    void OnPackOpenClosed()
    {
        Play();
    }

    // 전투 보상 응답은 로비 Start 뒤에 도착할 수 있다 — 그때는 캐리어만 실리고 아무도 재생하지 않아 이 자리가 필요하다.
    void OnBattleRewardArrived()
    {
        // 돌고 있는 재생이 있으면 비켜선다 — 재생에 들어가면 PlayWhenReady가 m_master를 Complete해
        // 진행 중인 카드 비행·삽입을 중간에 잘라 버린다. 캐리어는 그대로 남아 다음 진입점(Start)이 집는다.
        //
        // 기준이 m_master가 아니라 run 식별자인 이유: 코루틴은 yield return null로 시작해 m_master가
        // 그 다음 프레임에야 생긴다. m_master로 재면 로비 Start와 같은 프레임에 도착한 응답이 가드를 뚫고
        // 두 번째 재생을 띄우는데, 그러면 앞 재생은 m_runId가 밀려 삼켜지고 이번 재생은 _silent라 삼켜져
        // OnAnyFinished가 한 번도 안 나간다 — 그 신호를 기다리던 튜토리얼 CardGain 스텝이 그 자리에 선다.
        if (m_runId != m_finishedRunId) return;
        if (AlbumInsertSession.IsRunning || AlbumInsertQueue.HasPending) return;

        // 팩 캐리어가 차 있으면 비켜선다 — 재생은 두 캐리어를 한꺼번에 소비하므로, 무음 재생이 그 카드까지
        // 날려 버리고 종료 통지를 삼킨다. 카드 획득 통지를 기다리는 쪽이 그 자리에 선다.
        if (CardPackRewardHandoff.HasPending) return;

        // 안내 중에도 비켜선다 — 늦게 오는 보상이 안내가 짠 순서에 끼어든다.
        if (OutgameTutorialRunner.IsRunning || TriggeredTutorialRunner.IsRunning) return;

        // 이 경로의 종료는 아무도 기다리지 않는다 — OnAnyFinished를 내면 그 신호를 기다리던 다른 스텝
        // (튜토리얼 CardGain · 모험 선물 등장)이 자기 차례로 오인해 조기 통과한다.
        Play(true);
    }

    // 식별자는 코루틴 안이 아니라 여기서 발급한다 — 재개까지 한 프레임이 비어,
    // 그 사이에 앞 재생분이 자연 종료하면 그 통지가 이번 재생의 종료로 오인된다.
    void Play(bool _silent = false)
    {
        StartCoroutine(PlayWhenReady(++m_runId, _silent));
    }

    // 레이아웃 그룹이 x좌표를 정하고 LobbyTabController.Start가 탭을 고르기 전에는 목적지 좌표가 확정되지 않는다.
    // 한 프레임 양보 + 캔버스 강제 갱신 후에 위치를 읽는다(RankRewardPanel과 같은 이유).
    IEnumerator PlayWhenReady(int _run, bool _silent)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        // 출발점은 이번 재생분의 것이다 — 여기서 비워야 다음 진입(씬 로드·팩 닫힘)이 옛 자리를 물려받지 않는다.
        var t_origin = m_cardOrigin;
        m_cardOrigin = null;

        // 재화가 둘 이상 들어와도 한 번의 획득이다 — 한 버킷에 합쳐 보여준다(종류가 갈리면 그 안에서 나뉜다).
        // 카드팩 중복 환급은 대개 여기 오지 않는다: 개봉 화면이 자기 잔액 표시로 이미 받아 갔고,
        //   그럴 때 캐리어에는 카드만 실려 온다(PackAcquireController.PendingRefund).
        //   그 화면에 받을 자리가 없었을 때만 여기로 넘어와 전투 보상과 함께 나간다.
        var t_gains = new CurrencyGainBucket();
        BattleRewardHandoff.TryConsume(t_gains);

        IReadOnlyList<int> t_cards = null;
        if (CardPackRewardHandoff.TryConsume(t_gains, out var t_packCards)) t_cards = t_packCards;

        int t_cardCount = t_cards != null ? t_cards.Count : 0;
        if (t_gains.IsEmpty && t_cardCount <= 0)
        {
            NotifyFinished(_run, _silent);
            yield break;
        }

        // 직전 연출을 먼저 마무리한다. 큐에 이번 카드를 넣기 전이어야 한다 —
        // 뒤로 미루면 옛 시퀀스의 종료 콜백(StartInsertSession)이 아직 날지도 않은 이번 카드까지 세션에 끌고 간다.
        if (m_master != null && m_master.IsActive()) m_master.Complete(true);

        // 도감 탭이 켜지는 순간 이미 빈 칸이어야 한다 — 위장은 탭이 열리기 전에 걸어둔다.
        if (t_cardCount > 0)
        {
            AlbumInsertQueue.Enqueue(t_cards);
            AlbumInsertMask.HideAll(t_cards);
        }

        // 연출 레이어는 캔버스 좌표계 위여야 한다(anchoredPosition으로 날린다).
        if (transform is not RectTransform)
        {
            Debug.LogWarning("[LobbyGainEffectDirector] RectTransform이 아닌 오브젝트에 붙어 있어 연출을 건너뛴다.");
            if (t_cardCount > 0) CancelInsertSession();
            NotifyFinished(_run, _silent);
            yield break;
        }

        // 하단 탭 바·상단 바보다 위에 그려져야 카드가 가려지지 않는다.
        transform.SetAsLastSibling();

        m_master = DOTween.Sequence().SetLink(gameObject);

        bool t_gainStaged = !t_gains.IsEmpty && TryStageGains(m_master, t_gains);
        bool t_cardStaged = t_cardCount > 0 && TryStageCards(m_master, t_cards, t_origin);

        // 카드 연출이 안 붙었으면 착지 콜백도 없다 — 위장을 여기서 되돌리지 않으면 카드가 영영 빈 칸이다.
        // 재화만 온 경우까지 Clear하면 돌고 있는 세션의 위장을 벗긴다 — 이번에 건 위장이 있을 때만 되돌린다.
        // OnComplete는 하나뿐이라 삽입 세션 시작과 완료 통지를 한 콜백에 담는다.
        if (t_cardStaged) m_master.OnComplete(() => { StartInsertSession(); NotifyFinished(_run, _silent); });
        else if (t_cardCount > 0) CancelInsertSession();

        // 붙일 단계가 없으면(배선 탐색 실패) 빈 시퀀스를 남기지 않는다.
        if (!t_gainStaged && !t_cardStaged)
        {
            m_master.Kill();
            m_master = null;
            NotifyFinished(_run, _silent);
            yield break;
        }

        // 재화만 붙은 경우도 알려야 한다 — 카드 콜백이 없으니 여기서 시퀀스 끝에 매단다.
        if (!t_cardStaged) m_master.OnComplete(() => NotifyFinished(_run, _silent));
    }

    // 최신 재생분만 알린다 — 뒤늦게 터지는 옛 시퀀스의 콜백은 삼킨다.
    // _silent인 재생은 아무도 기다리지 않는 경로다(늦게 도착한 전투 보상) — 그 종료를 남의 차례로 오인시키지 않는다.
    void NotifyFinished(int _run, bool _silent)
    {
        if (_run != m_runId) return;

        // 삼키는 것은 통지뿐이다 — 재생이 끝났다는 사실 자체는 _silent와 무관하게 기록해야
        // 네 번째 진입점이 "아직 도는 재생이 있다"로 영영 막히지 않는다.
        m_finishedRunId = _run;

        if (_silent) return;

        OnAnyFinished?.Invoke();
    }

    // 도감 탭 착지에 이어붙는 삽입 세션. 큐·위장은 이미 걸려 있고, 연출을 세우는 일은 도감 탭이 진다 —
    // 여기서 정하는 것은 "도감 탭을 대신 켜 줄 것인가" 하나뿐이다.
    void StartInsertSession()
    {
        if (!AlbumInsertQueue.HasPending) return;

        // 연속 개봉 — 이미 돌고 있는 세션이 남은 큐까지 가져간다(위장 해제도 그 세션이 한다).
        if (AlbumInsertSession.IsRunning) return;

        var t_album = ResolveAlbumTab();
        if (t_album == null)
        {
            Debug.LogWarning("[LobbyGainEffectDirector] AlbumTabController를 찾지 못해 삽입 연출을 건너뛴다 — 카드는 그대로 꽂힌다.");
            CancelInsertSession();
            return;
        }

        // 안내 중에는 유저가 직접 도감 탭을 누르는 것 자체가 스텝이다 — 여기서 켜면 그 스텝을 대신 해 버린다.
        // 도감에 이미 들어와 있었다면 이 호출이 곧바로 세우고, 아니면 탭이 켜지는 순간 스스로 선다.
        // 전체 해금(첫 랭크 승급) 뒤는 예외다 — 그 뒤 획득은 안내가 짠 순서가 아니므로 일반 경로대로 탭을 대신 켠다.
        if (OutgameTutorialRunner.IsRunning && !OutgameFeatureLock.AllUnlocked)
        {
            t_album.TryBeginInsert();
            return;
        }

        // _fireTrigger는 반드시 false — true면 도감 탭 첫 진입 튜토리얼이 발화해 딤이 삽입 세션을 덮는다.
        if (this.lobbyTabController != null) this.lobbyTabController.Select(t_album, false);

        // 탭을 못 켰으면 세션이 설 자리가 없다 — 위장을 남기면 그 카드가 도감에서 영영 빈 칸이다.
        if (!t_album.isActiveAndEnabled)
        {
            Debug.LogWarning("[LobbyGainEffectDirector] 도감 탭을 켜지 못해 삽입 연출을 건너뛴다 — 카드는 그대로 꽂힌다.");
            CancelInsertSession();
            return;
        }

        t_album.TryBeginInsert();
    }

    // 세션이 시작되지 못한 모든 경로의 정리. 위장이 남으면 카드가 영영 빈 칸이다.
    static void CancelInsertSession()
    {
        AlbumInsertQueue.Clear();
        AlbumInsertMask.Clear();
    }

    // 도감 탭은 평소 꺼져 있다 — 비활성 포함으로 찾는다.
    AlbumTabController ResolveAlbumTab()
    {
        return this.albumTabController;
    }

    // 재화는 공용 재생기가 조립한다(수치 고정 해제 안전망까지 그 시퀀스에 붙어 온다).
    // 수치 자리에서 튀어 제자리로 돌아오는 모드라 출발점을 주지 않는다.
    bool TryStageGains(Sequence _master, CurrencyGainBucket _gains)
    {
        if (!CurrencyGainEffectPlayer.TryGet(this, out var t_player)) return false;

        var t_seq = t_player.BuildGain(null, _gains);
        if (t_seq == null) return false;

        // 카드 단계와 같은 0초에 꽂아 동시에 돌린다.
        _master.Insert(0f, t_seq);
        return true;
    }

    bool TryStageCards(Sequence _master, IReadOnlyList<int> _cards, RectTransform _origin)
    {
        m_collectionTarget = tabBar != null ? tabBar.GetVisualAnchor(collectionTabIndex) : null;
        m_collectionPunch  = tabBar != null ? tabBar.GetPunchAnchor(collectionTabIndex) : null;
        if (m_collectionTarget == null)
        {
            Debug.LogWarning("[LobbyGainEffectDirector] 도감 탭 앵커가 연결되지 않아 카드 연출을 건너뛴다.");
            return false;
        }

        // 출발점이 없으면 목적지에서 분출한다(씬 로드·팩 닫힘 경로 — 보여 주던 카드가 없다).
        var t_flight = EnsureCardFlight();
        t_flight.Configure(_origin != null ? _origin : m_collectionTarget, m_collectionTarget);

        _master.Insert(0f, t_flight.BuildFlight(_cards, (_arrived, _total) => OnCardArrived()));
        return true;
    }

    void OnCardArrived()
    {
        UiPunch.Play(m_collectionPunch, this.tabPunch);
    }

    // 팩 한 장짜리 비행. 카드 쪽과 달리 캐리어도 삽입 세션도 없다 — 만들고, 날리고, 지운다.
    bool PlayPack(Sprite _art, RectTransform _origin)
    {
        if (_art == null) return false;
        if (transform is not RectTransform t_layer) return false;

        var t_target = this.tabBar != null ? this.tabBar.GetVisualAnchor(this.packTabIndex) : null;
        var t_punch  = this.tabBar != null ? this.tabBar.GetPunchAnchor(this.packTabIndex) : null;
        if (t_target == null)
        {
            Debug.LogWarning("[LobbyGainEffectDirector] 팩 탭 앵커가 연결되지 않아 팩 비행을 건너뛴다.");
            return false;
        }

        // 하단 탭 바보다 위에 그려져야 팩이 가려지지 않는다(카드 비행과 같은 이유).
        transform.SetAsLastSibling();

        int t_run = ++m_runId;

        var t_settings = new UiGainBurst.Settings(1, this.packScatterRadius, this.packScatterDuration,
                                                  this.packGatherDuration, 0f, this.packPopDuration,
                                                  _angleStart: 90f, _angleSpan: 0f,
                                                  _gatherScale: this.packGatherScale, _spinDegrees: 0f,
                                                  _restScale: 1f, _arcHeight: this.packArcHeight);

        GameObject t_flyer = null;

        var t_seq = UiGainBurst.Build(t_layer,
            UiGainBurst.ToLayerLocal(t_layer, _origin != null ? _origin : t_target),
            UiGainBurst.ToLayerLocal(t_layer, t_target),
            t_settings,
            _spawn: _i =>
            {
                t_flyer = CreatePackFlyer(_art);
                return (RectTransform)t_flyer.transform;
            },
            _despawn: _rt => { if (_rt != null) _rt.gameObject.SetActive(false); },
            _onArrived: (_arrived, _total) => UiPunch.Play(t_punch, this.tabPunch));

        t_seq.SetLink(gameObject);
        t_seq.OnComplete(() =>
        {
            if (t_flyer != null) Destroy(t_flyer);
            NotifyFinished(t_run, false);
        });
        t_seq.Play();

        return true;
    }

    // 날아갈 팩 한 장. 개봉 화면의 팩 노드는 그 화면의 것이라 빌려올 수 없어 아트만 새로 세운다.
    GameObject CreatePackFlyer(Sprite _art)
    {
        var t_go = new GameObject("PackFlyer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        var t_image = t_go.GetComponent<Image>();
        t_image.sprite = _art;
        t_image.preserveAspect = true;
        t_image.raycastTarget = false;

        ((RectTransform)t_go.transform).sizeDelta = this.packFlightSize;
        return t_go;
    }

    CardGainFlightEffect EnsureCardFlight()
    {
        if (m_cardFlight == null) m_cardFlight = GetComponent<CardGainFlightEffect>();
        if (m_cardFlight == null) m_cardFlight = gameObject.AddComponent<CardGainFlightEffect>();
        return m_cardFlight;
    }
}
