using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// 로비에서 "방금 무엇을 얻었는지"를 한 번 보여주는 연출 브레인.
// 진입점은 둘이다 — 씬 로드(전투 복귀)는 Start, 씬이 유지되는 카드팩 오버레이는 PackOpenOverlay.OnClosed.
// 전투(BattleRewardHandoff)와 카드팩(CardPackRewardHandoff) 캐리어를 소비해
//   재화 → 각 재화 텍스트로 코인이 빨려들며 숫자가 오르고 튄다(CurrencyGainEffectPlayer에 위임 — 도감 수확과 같은 손맛)
//   카드 → 도감 탭으로 카드가 빨려들며 탭이 튄다
// 두 단계를 동시에 재생한다(획득 하나를 두 번에 걸쳐 알리지 않는다).
// 카드는 신규만 온다 — 중복분은 환급 재화가 대신하고, 그 환급은 개봉 화면이 자기 잔액 표시로 이미 받아 간다.
//
// 경계: 지급·저장은 각 씬이 이미 끝냈다. 이 클래스는 표시만 하고 재화를 건드리지 않는다.
// 배선을 비워두면 이름으로 자동 탐색한다 — 로비 프리팹 수정 없이도 동작하게(자동 탐색 실패 시 그 단계만 건너뛴다).
public class LobbyGainEffectDirector : MonoBehaviour
{
    [Header("배선 (비우면 자동 탐색)")]
    [Tooltip("카드가 빨려들 도감 탭 버튼. 비우면 collectionTabName으로 찾는다.")]
    [SerializeField] RectTransform collectionTabTarget;
    [Tooltip("도감 탭 버튼 오브젝트 이름(자동 탐색용).")]
    [SerializeField] string collectionTabName = "Button_Collection";
    [Tooltip("도감 탭이 선택돼 원 버튼이 꺼져 있을 때 대신 쓸 오브젝트 이름.")]
    [SerializeField] string tabFocusName = "Button_Focus";

    [Header("삽입 세션 (비우면 자동 탐색)")]
    [SerializeField] LobbyTabController lobbyTabController;
    [Tooltip("도감 탭 인덱스. 0 Shop · 1 Pack · 2 Match · 3 Deck · 4 Collection")]
    [SerializeField] int collectionTabIndex = 4;
    [SerializeField] AlbumTabController albumTabController;

    [Header("연출 값")]
    [SerializeField] float tabPunch = 0.3f;

    // 런타임에 만든 하위 연출기(직렬화 배선이 있으면 그것을 쓴다).
    CardGainFlightEffect m_cardFlight;

    // 재생 중인 획득 연출. 팩을 연달아 열면 앞 연출이 끝나기 전에 또 불린다.
    Sequence m_master;

    void OnEnable()
    {
        PackOpenOverlay.OnClosed += OnPackOpenClosed;
    }

    void OnDisable()
    {
        // static 이벤트에 죽은 씬 오브젝트가 남으면 다음 씬에서 오발화한다.
        PackOpenOverlay.OnClosed -= OnPackOpenClosed;

        // 카드 비행 도중 씬이 바뀌면 m_master가 Kill되고 OnComplete는 영영 안 온다 —
        // 세션에 아직 인계 못 한 위장은 여기서 되돌린다(안 그러면 그 카드가 도감에서 영영 빈 칸이다).
        if (!AlbumInsertSession.IsRunning && AlbumInsertQueue.HasPending) CancelInsertSession();
    }

    void Start()
    {
        StartCoroutine(PlayWhenReady());
    }

    // 오버레이 개봉은 로비를 재로드하지 않는다 — 닫힘 신호가 Start를 대신하는 두 번째 진입점이다.
    void OnPackOpenClosed()
    {
        StartCoroutine(PlayWhenReady());
    }

    // 레이아웃 그룹이 x좌표를 정하고 LobbyTabController.Start가 탭을 고르기 전에는 목적지 좌표가 확정되지 않는다.
    // 한 프레임 양보 + 캔버스 강제 갱신 후에 위치를 읽는다(RankRewardPanel과 같은 이유).
    IEnumerator PlayWhenReady()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        // 재화가 둘 이상 들어와도 한 번의 획득이다 — 한 버킷에 합쳐 보여준다(종류가 갈리면 그 안에서 나뉜다).
        // 카드팩 중복 환급은 대개 여기 오지 않는다: 개봉 화면이 자기 잔액 표시로 이미 받아 갔고,
        //   그럴 때 캐리어에는 카드만 실려 온다(PackAcquireController.PendingRefund).
        //   그 화면에 받을 자리가 없었을 때만 여기로 넘어와 전투 보상과 함께 나간다.
        var t_gains = new CurrencyGainBucket();
        BattleRewardHandoff.TryConsume(t_gains);

        IReadOnlyList<CardData> t_cards = null;
        if (CardPackRewardHandoff.TryConsume(t_gains, out var t_packCards)) t_cards = t_packCards;

        int t_cardCount = t_cards != null ? t_cards.Count : 0;
        if (t_gains.IsEmpty && t_cardCount <= 0) yield break;

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
            yield break;
        }

        // 하단 탭 바·상단 바보다 위에 그려져야 카드가 가려지지 않는다.
        transform.SetAsLastSibling();

        // 조립 전에 직전 연출을 즉시 마무리한다 — 코인·카드 잔해와 수치 고정이 겹치면 서로를 밟는다.
        // 새 고정(BeginGainRollUp)보다 먼저여야 옛 시퀀스의 해제가 새 값을 풀어버리지 않는다(CurrencyGainEffectPlayer.Play와 같은 이유).
        if (m_master != null && m_master.IsActive()) m_master.Complete(true);

        m_master = DOTween.Sequence().SetLink(gameObject);

        bool t_gainStaged = !t_gains.IsEmpty && TryStageGains(m_master, t_gains);
        bool t_cardStaged = t_cardCount > 0 && TryStageCards(m_master, t_cards);

        // 카드 연출이 안 붙었으면 착지 콜백도 없다 — 위장을 여기서 되돌리지 않으면 카드가 영영 빈 칸이다.
        // 재화만 온 경우까지 Clear하면 돌고 있는 세션의 위장을 벗긴다 — 이번에 건 위장이 있을 때만 되돌린다.
        if (t_cardStaged) m_master.OnComplete(StartInsertSession);
        else if (t_cardCount > 0) CancelInsertSession();

        // 붙일 단계가 없으면(배선 탐색 실패) 빈 시퀀스를 남기지 않는다.
        if (!t_gainStaged && !t_cardStaged)
        {
            m_master.Kill();
            m_master = null;
        }
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
        if (OutgameTutorialRunner.IsRunning)
        {
            t_album.TryBeginInsert();
            return;
        }

        // _fireTrigger는 반드시 false — true면 도감 탭 첫 진입 튜토리얼이 발화해 딤이 삽입 세션을 덮는다.
        if (this.lobbyTabController != null) this.lobbyTabController.Select(this.collectionTabIndex, false);

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
        if (this.albumTabController == null)
            this.albumTabController = FindFirstObjectByType<AlbumTabController>(FindObjectsInactive.Include);

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

    bool TryStageCards(Sequence _master, IReadOnlyList<CardData> _cards)
    {
        if (this.collectionTabTarget == null) this.collectionTabTarget = FindTabTarget();
        if (this.collectionTabTarget == null)
        {
            Debug.LogWarning($"[LobbyGainEffectDirector] 도감 탭('{this.collectionTabName}')을 찾지 못해 카드 연출을 건너뛴다.");
            return false;
        }

        var t_flight = EnsureCardFlight();
        t_flight.Configure(this.collectionTabTarget, this.collectionTabTarget);

        _master.Insert(0f, t_flight.BuildFlight(_cards, (_arrived, _total) => OnCardArrived()));
        return true;
    }

    void OnCardArrived()
    {
        UiPunch.Play(PunchTargetOf(this.collectionTabTarget), this.tabPunch);
    }

    CardGainFlightEffect EnsureCardFlight()
    {
        if (m_cardFlight == null) m_cardFlight = GetComponent<CardGainFlightEffect>();
        if (m_cardFlight == null) m_cardFlight = gameObject.AddComponent<CardGainFlightEffect>();
        return m_cardFlight;
    }

    // 탭 버튼은 레이아웃 그룹이 배치하므로 버튼 자체를 튀기면 형제 배치가 흔들려 보인다 — 아이콘 자식이 있으면 그쪽을 튀긴다.
    static Transform PunchTargetOf(RectTransform _tab)
    {
        if (_tab == null) return null;
        return _tab.childCount > 0 ? _tab.GetChild(0) : _tab;
    }

    // 도감 탭 RectTransform 탐색. 선택된 탭은 버튼이 꺼지고 Focus가 그 자리를 대신하므로 그때는 Focus를 쓴다.
    RectTransform FindTabTarget()
    {
        var t_root = GetComponentInParent<Canvas>();
        if (t_root == null) return null;

        var t_tab = FindByName(t_root.transform, this.collectionTabName);
        if (t_tab != null && t_tab.gameObject.activeInHierarchy) return t_tab;

        var t_focus = FindByName(t_root.transform, this.tabFocusName);
        return t_focus != null && t_focus.gameObject.activeInHierarchy ? t_focus : t_tab;
    }

    static RectTransform FindByName(Transform _root, string _name)
    {
        if (string.IsNullOrEmpty(_name)) return null;

        var t_all = _root.GetComponentsInChildren<RectTransform>(true);
        for (int t_i = 0; t_i < t_all.Length; t_i++)
            if (t_all[t_i].name == _name) return t_all[t_i];

        return null;
    }
}
