using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 모험 세로 경로 맵(로비 위를 덮는 전체 화면 오버레이).
//
// 좌표의 진실원은 챕터 타일 프리팹이다 — 정점 자리도 길 조각도 디자이너가 타일 안에 저작하고,
// 코드는 타일을 쌓고 저작된 자리에 정점을 놓고 길 조각을 상태에 따라 틴트할 뿐 좌표를 만들지 않는다.
// 자기 여닫음은 스스로 소유하고, 씬 전환만 모른다 — 도전을 이벤트로 올리면 LobbyRoot(LobbyMatchLauncher)가 잇는다.
public class TournamentMapOverlayView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] RectTransform content;          // 타일과 정점이 놓일 Content(레이아웃 그룹 없이 코드가 좌표를 잡는다)
    [SerializeField] TournamentNodeView nodePrefab;
    [SerializeField] Button backButton;

    [Header("챕터 띠")]
    [Tooltip("챕터 타일 아랫변(이음매 구름 자리)에 앉는 챕터 띠. 챕터 제목·진행 눈금·완주 보상 수령이 여기 모인다.\n" +
             "아랫변이라 '여기부터 이 장이다'를 말한다 — 챕터 c의 띠는 c-1과 c 사이 이음매에 선다.\n\n" +
             "★ 생성 위치는 이 프리팹이 정한다 — 루트 RectTransform의 anchoredPosition이 그대로 " +
             "'이음매로부터의 오프셋'이 된다. 프리팹을 열어 눈으로 끌어 맞추면 그 값이 모든 챕터에 함께 적용된다. " +
             "(0,0)이면 이음매 정중앙이고, 코드는 앵커만 Content 하단 중앙으로 강제한다.\n" +
             "씬에 목업으로 놓은 인스턴스를 물리면 그 목업의 씬 좌표가 곧 오프셋이 되니 주의할 것.\n\n" +
             "미배선이면 띠가 통째로 없다 — 완주 보상을 받을 자리가 사라지므로 저작 전엔 지급도 없다.")]
    [SerializeField] TournamentChapterBandView bandPrefab;

    [Tooltip("첫 타일 아래에 남기는 여백. 첫 챕터 띠(맵의 맨 아래에 선다)가 하단 안개에 가려 잘리지 않게 하는 몫이다.\n" +
             "하단 안개 두께보다 넉넉해야 한다.")]
    [SerializeField] float bottomPadding = 400f;

    [Header("길 색")]
    [Tooltip("깬 구간(정점 i를 클리어)의 길 색. 아래에서 위로 차오르는 진행 표시다.")]
    [SerializeField] Color clearedColor = new Color(1f, 0.82f, 0.35f, 1f);

    [Tooltip("아직 못 깬 구간의 길 색. 배경 그림에 이미 길이 그려져 있으므로 길을 덮지 않는 옅은 색이라야 한다 " +
             "— 흰색 반투명이 숲·설원·화산 배경 모두에서 읽힌다.")]
    [SerializeField] Color lockedColor = new Color(1f, 1f, 1f, 0.7f);

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("길 점 하나가 금색으로 물드는 시간.")]
    [SerializeField] float linkDotDuration = 0.15f;

    [Tooltip("길 점과 점 사이 간격. 구간마다 점이 6~9개로 갈리므로 총 길이도 함께 갈린다.")]
    [SerializeField] float linkDotStagger = 0.06f;

    [Header("해금 연출")]
    [Tooltip("맵에 들어와 새로 열린 것들을 훑는 총 상한(초).\n" +
             "넘치면 사이를 건너뛰고 가장 앞선(최신) 대상 하나로 곧장 넘어가 그것만 마저 보여준다 — " +
             "그 마지막 하나는 예산을 넘겨서라도 보여준다. 유저가 실제로 갈 자리가 거기이기 때문이다.\n" +
             "건너뛴 대상은 표식이 서지 않아 다음 진입에서 이어 재생된다(개수 상한과 같은 방향이다).\n" +
             "길이는 띠·정점이 저작한 제 안무가 정하므로 여기서는 총량만 조인다.\n\n" +
             "★ 0이면 첫 대상부터 건너뛰기가 걸려 항상 최신 하나만 나온다 — '무제한'이 아니라 '사슬을 끄고 " +
             "목적지만 보여준다'는 저작이다. 여러 대상을 훑게 하려면 반드시 0보다 큰 값을 줄 것.")]
    [SerializeField] float introBudget = 4f;

    [Tooltip("한 번의 진입에서 훑을 대상 수 상한(띠와 정점을 합쳐서 센다).\n" +
             "넘치면 오래된 것부터 버린다 — 유저가 실제로 갈 자리는 가장 앞선 챕터·정점이다.")]
    [SerializeField] int introMaxSteps = 3;

    [Tooltip("챕터 띠로 카메라가 미끄러지는 시간. 장이 바뀌는 이동이라 정점보다 길게 잡는다 — " +
             "여행 지도에서는 이동 그 자체가 '여기가 열렸다'는 문장이다.")]
    [SerializeField] float introTravelChapter = 0.45f;

    [Tooltip("정점으로 카메라가 미끄러지는 시간. 같은 장 안의 짧은 이동이다.")]
    [SerializeField] float introTravelNode = 0.28f;

    [Tooltip("챕터 띠 안무가 끝난 뒤의 숨. 그 장의 정점들이 계단으로 풀리는 시간이기도 하다.")]
    [SerializeField] float introGapChapter = 0.35f;

    [Tooltip("정점 안무가 끝난 뒤의 숨. 작은 사건이라 붙여 둔다.")]
    [SerializeField] float introGapNode = 0.10f;

    [Tooltip("장이 열릴 때 그 장의 정점이 하나씩 풀리는 간격(길 점 간격과 같은 축이다).\n" +
             "박을 먹는 것은 이번에 새로 열리는 정점뿐이다 — 잠긴 채 남을 정점은 장이 열리는 그 시각에 함께 내려간다.\n" +
             "챕터 숨(introGapChapter) 안에서 계단이 끝나도록 코드가 다시 조이므로, 여는 정점이 많은 챕터에서는 이보다 촘촘해진다.")]
    [SerializeField] float introNodeRelease = 0.06f;

    /// <summary>정점 도전 요청(도전 가능한 정점만 올라온다). LobbyRoot가 전투로 잇는다.</summary>
    public event Action<int> NodeSelected;

    /// <summary>맵이 화면에 떠 있는가.</summary>
    public bool IsOpen => this.gameObject.activeInHierarchy;

    // 정점 앵커가 등록돼 있어도 되는 상태 — 켜져 있고 퇴장 중이 아닐 때만.
    bool IsOnStage => this.IsOpen && !this.m_closing;

    // 타일 안에서 정점 자리·길 조각을 담고 있는 자식 이름(타일 프리팹 규약)
    const string PATH_ROOT_NAME = "PathRoot";
    const string LINK_ROOT_NAME = "LinkRoot";

    // 평탄 정점 번호와 칸이 1:1이다. 자리가 저작되지 않은 정점은 빈칸으로 남는다 — ScrollToNode가 번호로 찾는다.
    readonly List<TournamentNodeView> m_nodes = new List<TournamentNodeView>();

    // 구간별 길 조각. 진행에 따라 색을 바꾸려면 만든 뒤에도 들고 있어야 한다.
    readonly List<PathLink> m_links = new List<PathLink>();

    // 챕터 번호와 칸이 1:1이다. 타일이 없는 챕터는 빈칸으로 남는다(정점 목록과 같은 규약).
    readonly List<TournamentChapterBandView> m_bands = new List<TournamentChapterBandView>();

    // 이 화면이 만든 것만 추적한다 — Content를 통째로 비우면 저작한 것이 함께 지워진다.
    readonly List<GameObject> m_spawned = new List<GameObject>();

    // 저작 결함 보고는 같은 내용 1회만. 챕터·항목이 다르면 각각 나온다.
    readonly HashSet<string> m_reported = new HashSet<string>();

    // 정점 생성 여부. 저작 정점 수는 런타임 불변이라 최초 1회만 만들고 이후엔 Refresh로만 갱신한다.
    bool m_built;

    // 퇴장 트윈이 도는 동안 참. 그 사이에도 오브젝트는 켜져 있어 activeInHierarchy만으로는 화면에 서 있는지를 답할 수 없다.
    bool m_closing;

    // 점등 연출이 도는 동안 진행 통지의 즉시 반영을 미룬다 — 안 그러면 수령 순간 결말이 먼저 나온다.
    bool m_suspendRefresh;

    // 도장이 꽂히는 중인 정점(없으면 -1). 팝업의 [획득]이 있던 자리에 그 정점이 그대로 서 있어,
    // 여벌 탭 하나가 재도전 전투로 새면 사슬이 통째로 잘린다.
    int m_claimNode = -1;

    // 지금 점등 트윈이 물들이는 중인 구간(없으면 -1). RefreshLinks가 이 한 칸만 건너뛴다 —
    // 상태를 먼저 커밋하면 길도 같은 프레임에 금색이 돼 차오르는 연출이 통째로 사라진다.
    int m_litLink = -1;

    Sequence m_claimSeq;

    // 아직 훑지 않은 해금 대상(아래에서 위로). 재생한 것은 여기서 빠져 m_introSeen으로 옮겨 간다.
    readonly List<IntroTarget> m_introTargets = new List<IntroTarget>();

    // 이번 진입에서 실제로 재생한 대상. 끝맺음·중단이 이 목록만 표식한다 — 버린 대상은 다음 진입의 몫이다.
    readonly List<IntroTarget> m_introSeen = new List<IntroTarget>();

    // 잠긴 모습으로 미리 세워 둔 부품 전부. 재생 대상보다 넓다 — 챕터에 딸려 세운 정점들이 여기 함께 담긴다.
    // 푸는 쪽은 이 목록만 본다: 담기지 않은 부품은 손잡이가 선 채로 굳어 Refresh를 영영 받지 못한다.
    readonly List<IntroTarget> m_introStaged = new List<IntroTarget>();

    // 챕터 띠가 대상이라 개별 대상에서 빠진 정점들 — 그 장의 계단이 대신 연다.
    // 개별 대상으로도 남겨 두면 계단이 그것을 "아직 제 차례를 기다리는 대상"으로 보고 건너뛰어 아무것도 열지 않는다.
    readonly List<int> m_introChapterOpens = new List<int>();

    // 지금까지 쓴 연출 시간. 각 안무의 길이는 재생해 봐야 알 수 있어 예산을 미리 나눌 수 없다.
    float m_introSpent;

    // 대상 하나를 훑는 동안 도는 대기 시퀀스(대상마다 새로 선다). 카메라 이동 단계도 같은 자리에 담는다 —
    // 별도 필드로만 들고 있으면 마지막 대상의 이동 중에 IsIntroPending이 거짓으로 새어 그 틈으로 복귀 수령·정점 탭이 통과한다.
    Sequence m_introSeq;

    // 사슬 안에서만 도는 카메라 이동. 사슬 밖의 즉시 대입 경로(ScrollToContentY)와는 섞이지 않는다.
    Tween m_scrollTween;

    // 사슬이 카메라의 주인인 동안 참. 해제 누락이 곧 "맵이 영영 안 끌린다"라 멱등하게 다룬다.
    bool m_scrollLocked;

    // 이번 사슬에서 탭 스킵이 한 번이라도 있었는가. 예산 검사를 우회하는 데만 쓴다 —
    // m_introSpent에는 저작된 안무 길이가 통째로 더해져 있어, 없으면 스킵할수록 남은 대상이 더 많이 버려진다.
    bool m_introSkipped;

    // 지금 도는 것이 카메라 이동인가 부품 안무인가. 스킵이 "당길 안무가 아직 없다"를 구분하는 데만 쓴다.
    bool m_introTraveling;

    // 지금 훑고 있는 대상. 스킵이 Kill로 함께 지워진 콜백을 손으로 재현하려면 대상을 알아야 한다.
    IntroTarget m_introPlaying;

    // 해금 사슬이 화면을 쥐고 있는가 — 대상이 남아 있으면 대기 시퀀스가 없는 틈에도 참이다.
    bool IsIntroPending => this.m_introSeq != null || this.m_introTargets.Count > 0;

    void Awake()
    {
        if (this.backButton != null) this.backButton.onClick.AddListener(this.Close);
    }

    void OnDestroy()
    {
        if (this.backButton != null) this.backButton.onClick.RemoveListener(this.Close);
    }

    void OnEnable()
    {
        TournamentProgress.OnChanged += this.RefreshNodes;

        // 세로 맵이 화면을 다 쓰도록 하단 탭바만 걷는다(재화 HUD는 남긴다).
        LobbyShellBars.Hide(this, this.transform, EShellBars.Bottom);

        // Open()을 거치지 않고 부모가 되살린 경우에도 앵커가 선다 — 이 등록의 짝이 OnDisable의 해제다.
        this.m_closing = false;
        this.ApplyTutorialAnchor(true);
    }

    void OnDisable()
    {
        TournamentProgress.OnChanged -= this.RefreshNodes;

        this.AbortClaimSequence();
        this.ApplyTutorialAnchor(false);

        this.m_closing = false;

        LobbyShellBars.Show(this);            // 씬 이탈로 잘려도 바가 걷힌 채 굳지 않게
        this.transition.HandleDisabled(this.gameObject);
    }

    /// <summary>맵을 연다. 정점 세우기·스크롤은 활성화 뒤에 돈다 — rect가 0이면 스크롤 계산이 깨진다.</summary>
    public void Open()
    {
        // 퇴장 트윈 중에도 IsOpen은 참이다 — 여기서 물러나면 앵커가 해제된 채 맵만 되살아난다.
        if (this.IsOnStage) return;

        this.m_closing = false;

        // 억제 스위치가 켜진 채 남아 있으면 아래 RefreshNodes가 통째로 무시돼 맵이 옛 그림으로 굳는다.
        this.AbortClaimSequence();

        this.transition.SetVisible(this.gameObject, true);

        if (this.m_built) this.RefreshNodes();
        else this.Build();

        // 수령 사슬이 화면을 쥐고 있으면 수집조차 하지 않는다 — 표식을 세우지 않으니 다음 깨끗한 진입에서 그대로 재생된다.
        if (this.CanCollectUnlockIntro) this.CollectUnlockIntro();

        // 무대는 사슬이 시작되기 전에 통째로 선다 — 대상마다 제 차례에 잠그면 띠 안무가 도는 몇 초 동안
        // 정점들이 열린 모습으로 서 있다가 뒤늦게 잠겨, 유저 눈에는 역행으로 읽힌다.
        this.StageUnlockIntro();

        this.ApplyTutorialAnchor(true);   // Build 경로는 RefreshNodes를 거치지 않는다
        this.ScrollToCurrent();

        // 스크롤이 애니 없이 즉시 앉고 그 안에서 레이아웃이 확정된다 — 그 뒤라야 대상을 제자리에서 화면에 넣을 수 있다.
        this.PlayUnlockIntro();
    }

    /// <summary>맵을 닫는다. 하단바는 퇴장 트윈과 나란히 돌려준다 — OnDisable을 기다리면 늦는다.</summary>
    public void Close()
    {
        // 퇴장 트윈이 도는 동안에도 오브젝트는 켜져 있다 — 그 사이 진행 통지가 앵커를 되살리지 않게 먼저 내린다.
        this.m_closing = true;

        // OnDisable은 퇴장 트윈이 끝난 뒤에 온다 — 그 사이 억제가 살아 있으면 안 되므로 여기서 먼저 건다.
        this.AbortClaimSequence();
        this.ApplyTutorialAnchor(false);

        LobbyShellBars.Show(this);
        this.transition.SetVisible(this.gameObject, false);
    }

    /// <summary>전투에서 막 돌아온 정점의 보상을 곧바로 연다(맵이 이미 열려 있어야 한다).
    /// 방금 이긴 정점을 다시 찾아 누르게 하지 않는다 — 수령은 복귀의 결말이지 별도의 사건이 아니다.
    /// 클릭 경로(OnNodeTapped)는 그대로 남는다: 맵을 먼저 떠났거나 신고가 실패한 경우의 회수 경로다.</summary>
    public void OpenReturnReward(string _nodeId)
    {
        if (!this.IsOnStage) return;
        // 다른 정점의 수령이 아직 끝나지 않았다 — 왕복 중도 포함한다(대기 owner가 하나뿐이라 겹치면 차단막이 어긋난다).
        if (this.m_claimSeq != null || TournamentRewardFlow.IsClaiming) return;

        // 신고가 늦은 복귀에서는 맵이 이미 해금 사슬을 돌고 있다(열 때는 PendingRewardNodeId가 비어 수집 게이트를 통과했다).
        // 여기서 물러나도 수령은 유실되지 않는다 — 사슬이 끝나면 그 정점이 선물 모습으로 서고, OnNodeTapped가 같은 팝업을 연다.
        if (this.IsIntroPending) return;

        int t_index = TournamentProgress.IndexOf(_nodeId);
        if (t_index < 0 || t_index >= this.m_nodes.Count) return;
        if (!TournamentProgress.IsRewardPending(t_index)) return;

        // 도장이 그 자리에 꽂히므로(서버가 클리어를 확정하는 프레임), 그 자리가 화면 안에 있어야 결말이 보인다.
        this.ScrollToNode(t_index);

        this.OpenNodeReward(t_index);
    }

    /// <summary>해금 사슬 스킵. 맵 바닥에 떨어진 탭만 받는다 — 정점·띠 위의 탭은 그 부품이 가져간다.</summary>
    // 눌린 오브젝트를 이 루트와 견주지 않는다: 루트에는 Graphic이 없어 그 비교가 늘 어긋나고, 스킵이 영영 서지 않았다.
    // 가려낼 일은 이미 끝나 있다 — 클릭은 자기를 받아 가는 자식(정점·띠의 버튼)에서 멈추므로,
    // 여기까지 올라온 탭은 그것을 받아 갈 부품이 없던 자리, 곧 맵 바닥이다.
    //
    // 사슬이 화면 한가운데 세운 바로 그 정점·띠를 탭해도 스킵이 돌아야 한다 — 그 자리는 유저가 보고 있는 자리다.
    // 그것은 부품 쪽 계약이다: 잠긴 버튼은 interactable만 내리면 클릭을 먹고 삼켜(ExecuteEvents가 isActiveAndEnabled만 본다)
    // 여기까지 올라오지 않으므로, 각 뷰가 버튼을 enabled=false로 내려 클릭을 통과시킨다.
    // 맵은 올라온 탭을 스킵으로 받는 쪽만 책임진다.
    public void OnPointerClick(PointerEventData _e)
    {
        if (!this.IsIntroPending) return;
        if (_e == null || _e.button != PointerEventData.InputButton.Left) return;

        // 스크롤로 소비된 포인터는 탭이 아니다 — 없으면 맵을 끌어 넘긴 뒤 손 떼는 순간 연출이 지워진다.
        if (_e.dragging) return;

        // 사슬을 통째로 지우지 않는다 — 탭 한 번은 대상 하나를 결말까지 당기고 곧바로 다음으로 넘어간다.
        this.SkipCurrentIntroTarget();
    }

    // 챕터 타일을 쌓고 타일에 저작된 자리에만 정점을 세운다. 좌표를 코드가 만들지 않으므로 저작이 빠지면 그 정점은 안 나온다.
    void Build()
    {
        this.m_nodes.Clear();
        this.m_links.Clear();
        this.m_bands.Clear();
        if (this.content == null || this.nodePrefab == null) return;

        // 이 화면이 직전에 만든 것만 걷는다. Content를 통째로 비우면 저작한 것이 함께 지워진다.
        for (int t_i = 0; t_i < this.m_spawned.Count; t_i++)
            if (this.m_spawned[t_i] != null) Destroy(this.m_spawned[t_i]);
        this.m_spawned.Clear();

        // 목업으로 Content 안에 놓인 원본은 숨기기만 한다(지우면 다음 Build가 0개가 된다).
        if (this.nodePrefab.gameObject.scene.IsValid()) this.nodePrefab.gameObject.SetActive(false);

        int t_count = TournamentProgress.NodeCount;
        var t_positions = new Vector2?[t_count];
        var t_bandBottoms = new float?[TournamentProgress.ChapterCount];

        // 타일을 먼저 전부 쌓는다 — 형제 순서가 곧 깊이라 뒤에 세우는 띠·정점이 배경 위로 온다.
        // 길 조각은 타일 안에 저작돼 있어 코드가 순서를 만들 필요가 없다.
        // 첫 띠가 맵 아랫변에 걸터앉으므로 쌓기를 여백만큼 띄워 시작한다 — 없으면 하단 안개 밑에서 반쪽이 잘린다.
        float t_stack = this.BuildChapterTiles(t_positions, t_bandBottoms);
        this.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, t_stack);

        // 띠를 정점보다 먼저 세운다 — 이음매에 걸친 정점이 띠 위로 오게(정점이 진행의 주인공이다).
        this.BuildChapterBands(t_bandBottoms);

        int t_placed = 0;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            // 자리가 없는 정점은 만들지 않는다. 목록엔 빈칸을 남겨 칸 번호를 평탄 정점 번호에 붙들어 둔다.
            if (t_positions[t_i] == null)
            {
                this.m_nodes.Add(null);
                continue;
            }

            TournamentNodeView t_node = Instantiate(this.nodePrefab, this.content);
            t_node.gameObject.SetActive(true);   // 목업 원본을 숨겼을 수 있다 — 사본은 항상 보이게.
            this.Place(t_node.transform as RectTransform, t_positions[t_i].Value);
            t_node.Bind(t_i, this.OnNodeTapped);
            this.m_nodes.Add(t_node);
            this.m_spawned.Add(t_node.gameObject);
            t_placed++;
        }

        this.RefreshLinks();   // 길 색은 만들자마자 한 번 맞춰 둔다

        // 하나도 못 세웠으면(설정 미주입 등) 다음 진입에서 다시 시도한다 — 빈 맵으로 세션 내내 고착되지 않게.
        this.m_built = t_placed > 0;
    }

    // 첫 타일 아래 여백. 띠가 없으면 0이다 — 이 여백은 첫 띠 자리를 내주려고 있는 것이라
    // 띠 미배선인데도 남겨 두면 이유 없는 빈 하늘만 생긴다.
    float BottomInset => this.bandPrefab != null ? this.bottomPadding : 0f;

    // 챕터 타일을 아래에서 위로 순서대로 쌓는다(시작 높이 = BottomInset). 반환값은 쌓인 총 높이(= Content 높이).
    // _positions는 평탄 정점 번호로 색인한다 — 챕터 시작 오프셋을 누적해 넣는다.
    // _bandBottoms는 챕터 번호로 색인한다 — 그 챕터 타일의 아랫변 y(= 띠가 앉을 자리). 타일이 없으면 null로 남는다.
    float BuildChapterTiles(Vector2?[] _positions, float?[] _bandBottoms)
    {
        float t_cursor = this.BottomInset;
        int t_nodeStart = 0;
        int t_chapters = TournamentProgress.ChapterCount;

        for (int t_c = 0; t_c < t_chapters; t_c++)
        {
            if (!TournamentProgress.TryGetChapter(t_c, out TournamentChapterDef t_chapter)) continue;

            // 타일이 없으면 그 챕터 정점은 아예 만들지 않는다 — 좌표를 지어내면 배경 길과 어긋난 자리에 놓인다.
            if (t_chapter.tilePrefab == null || !(t_chapter.tilePrefab.transform is RectTransform))
                this.ErrorChapter(t_c, "쓸 수 있는 tilePrefab이 없다 — 이 챕터의 정점과 길이 화면에 나오지 않는다. TournamentConfig에 타일을 저작할 것.");
            else
            {
                _bandBottoms[t_c] = t_cursor;   // 타일을 놓기 전 커서 = 그 타일의 아랫변 = 이 챕터가 시작되는 자리
                t_cursor = this.PlaceChapterTile(t_c, t_chapter, t_cursor, t_nodeStart, _positions);
            }

            t_nodeStart += t_chapter.NodeCount;
        }

        return t_cursor;
    }

    // 타일 한 장을 커서 높이에 세우고 그 안의 정점 자리·길 조각을 걷는다. 반환값은 타일 윗변(= 다음 커서).
    float PlaceChapterTile(int _chapterIndex, TournamentChapterDef _chapter, float _cursor, int _nodeStart, Vector2?[] _positions)
    {
        GameObject t_tile = Instantiate(_chapter.tilePrefab, this.content);
        t_tile.SetActive(true);
        this.m_spawned.Add(t_tile);

        var t_rect = (RectTransform)t_tile.transform;   // 프리팹 루트 타입은 호출부에서 이미 걸렀다

        t_rect.anchorMin = new Vector2(0.5f, 0f);
        t_rect.anchorMax = new Vector2(0.5f, 0f);
        t_rect.pivot = new Vector2(0.5f, 0f);
        t_rect.anchoredPosition = new Vector2(0f, _cursor);

        // 폭 맞춤을 sizeDelta가 아니라 균등 스케일로 한다 — 안의 저작 좌표를 산술로 환산할 수 있고 길 조각도 같이 따라온다.
        // 활성화 첫 프레임엔 Content rect가 0일 수 있다 — 그때는 저작 크기 그대로(스케일 1) 둔다.
        float t_tileWidth = t_rect.rect.width;
        float t_contentWidth = this.content.rect.width;
        float t_scale = t_tileWidth > 1f && t_contentWidth > 1f ? t_contentWidth / t_tileWidth : 1f;
        t_rect.localScale = new Vector3(t_scale, t_scale, 1f);

        this.CollectTilePoints(_chapterIndex, t_rect, _cursor, t_scale, _nodeStart, _chapter.NodeCount, _positions);
        this.CollectTileLinks(_chapterIndex, t_rect, _nodeStart, _chapter.NodeCount);

        return _cursor + t_rect.rect.height * t_scale;
    }

    // 챕터마다 띠를 그 타일 아랫변에 앉힌다. 이음매 구름 한가운데 서서 여기부터 그 장이라는 것을 말하고,
    // 완주 보상을 받을 자리도 여기다 — 전투 복귀 팝업 뒤에 이어 붙이지 않는 이유는 두 수령이 한 프레임에 겹치기 때문이다.
    void BuildChapterBands(float?[] _bandBottoms)
    {
        if (this.bandPrefab == null) return;

        // 미세 위치는 프리팹이 정한다 — 루트에 저작된 anchoredPosition이 이음매로부터의 오프셋이다.
        // 사본을 만든 뒤에 읽으면 Place가 이미 덮어쓴 값이라, 원본에서 먼저 떠 둔다.
        Vector2 t_offset = this.bandPrefab.transform is RectTransform t_authored
            ? t_authored.anchoredPosition
            : Vector2.zero;

        // 목업으로 Content 안에 놓인 원본은 숨기기만 한다(지우면 다음 Build가 0개가 된다).
        if (this.bandPrefab.gameObject.scene.IsValid()) this.bandPrefab.gameObject.SetActive(false);

        // 끝 표지는 "타일이 있는 마지막 챕터"에 붙는다 — 맨 뒤 챕터가 미저작이면 그 띠 자체가 없어 표지가 영영 안 뜬다.
        int t_last = -1;
        for (int t_c = 0; t_c < _bandBottoms.Length; t_c++)
            if (_bandBottoms[t_c] != null) t_last = t_c;

        for (int t_c = 0; t_c < _bandBottoms.Length; t_c++)
        {
            // 타일이 없는 챕터엔 앉힐 자리가 없다. 목록엔 빈칸을 남겨 칸 번호를 챕터 번호에 붙들어 둔다.
            if (_bandBottoms[t_c] == null)
            {
                this.m_bands.Add(null);
                continue;
            }

            TournamentChapterBandView t_band = Instantiate(this.bandPrefab, this.content);
            t_band.gameObject.SetActive(true);   // 목업 원본을 숨겼을 수 있다 — 사본은 항상 보이게.
            this.Place(t_band.transform as RectTransform,
                new Vector2(t_offset.x, _bandBottoms[t_c].Value + t_offset.y));
            t_band.Bind(t_c, t_c == t_last);
            this.m_bands.Add(t_band);
            this.m_spawned.Add(t_band.gameObject);
        }
    }

    // 타일 안 PathRoot 자식(= 정점 자리)을 Content 좌표로 환산해 담는다. 개수가 안 맞으면 메우지 말고 보고한다.
    // 자리 좌표는 LocalOffsetInTile이 앵커·피벗과 무관하게 구한다 — 저작 앵커에 규약을 걸지 않는다.
    void CollectTilePoints(int _chapterIndex, RectTransform _tile, float _cursor, float _scale, int _nodeStart, int _nodeCount, Vector2?[] _positions)
    {
        RectTransform t_root = _tile.Find(PATH_ROOT_NAME) as RectTransform;
        if (t_root == null)
        {
            this.ErrorChapter(_chapterIndex, $"타일에 '{PATH_ROOT_NAME}'이 없다 — 이 챕터 정점 {_nodeCount}개가 화면에 나오지 않는다.");
            return;
        }

        int t_authored = t_root.childCount;
        if (t_authored != _nodeCount)
            this.ErrorChapter(_chapterIndex, $"'{PATH_ROOT_NAME}' 자리 {t_authored}개 ≠ 정점 {_nodeCount}개 — 자리가 있는 만큼만 놓는다. 자식 수를 정점 수에 맞출 것.");

        int t_take = Mathf.Min(t_authored, _nodeCount);

        for (int t_i = 0; t_i < t_authored; t_i++)
        {
            Transform t_child = t_root.GetChild(t_i);
            t_child.gameObject.SetActive(false);   // 저작용 표식이라 실행 화면에는 남기지 않는다

            if (t_i >= t_take) continue;
            if (!(t_child is RectTransform t_point)) continue;

            Vector2 t_offset = LocalOffsetInTile(t_point, _tile);
            _positions[_nodeStart + t_i] = new Vector2(t_offset.x * _scale, _cursor + t_offset.y * _scale);
        }
    }

    // 타일 안 LinkRoot 자식(= 길 조각)을 형제 순서로 걷어 담는다. transform은 절대 건드리지 않는다 — 색만 바꾼다.
    // 챕터 c의 링크 i는 평탄 구간 (_nodeStart + i)다. 챕터 경계 구간에는 링크가 없다(조각이 두 타일에 걸치지 않게).
    void CollectTileLinks(int _chapterIndex, RectTransform _tile, int _nodeStart, int _nodeCount)
    {
        Transform t_root = _tile.Find(LINK_ROOT_NAME);
        if (t_root == null)
        {
            this.WarnChapter(_chapterIndex, $"타일에 '{LINK_ROOT_NAME}'이 없다 — 길 없이 정점만 뜬다.");
            return;
        }

        int t_expected = Mathf.Max(_nodeCount - 1, 0);
        int t_authored = t_root.childCount;
        if (t_authored != t_expected)
            this.ErrorChapter(_chapterIndex, $"'{LINK_ROOT_NAME}' 조각 {t_authored}개 ≠ 구간 {t_expected}개(정점 수 - 1) — 형제 순서가 곧 구간 순서다. 개수를 맞출 것.");

        int t_take = Mathf.Min(t_authored, t_expected);
        for (int t_i = 0; t_i < t_take; t_i++)
        {
            GameObject t_link = t_root.GetChild(t_i).gameObject;

            // Graphic은 여기서 한 번만 캐시한다 — 갱신은 진행마다 도는데 탐색까지 매번 하면 낭비다.
            this.m_links.Add(new PathLink(_nodeStart + t_i, t_link, t_link.GetComponentsInChildren<Graphic>(true)));
        }
    }

    // 구간 i는 정점 i를 깼으면 클리어 색, 아니면 미클리어 색이다.
    // 숨기지 않는다 — 첫 진입은 클리어가 0개라 숨기면 길이 통째로 사라진다.
    void RefreshLinks()
    {
        for (int t_i = 0; t_i < this.m_links.Count; t_i++)
        {
            PathLink t_link = this.m_links[t_i];
            if (t_link.Graphics == null) continue;
            if (t_link.Index == this.m_litLink) continue;   // 트윈이 쥐고 있는 구간은 덮지 않는다

            Color t_color = TournamentProgress.StateOf(t_link.Index) == ETournamentNodeState.Cleared
                ? this.clearedColor
                : this.lockedColor;

            for (int t_g = 0; t_g < t_link.Graphics.Length; t_g++)
                if (t_link.Graphics[t_g] != null) t_link.Graphics[t_g].color = t_color;
        }
    }

    // 저작 결함은 조용히 메우지 않고 보고한다(같은 내용은 1회).
    void ErrorChapter(int _chapterIndex, string _message) => this.Report(_chapterIndex, _message, true);

    void WarnChapter(int _chapterIndex, string _message) => this.Report(_chapterIndex, _message, false);

    void Report(int _chapterIndex, string _message, bool _isError)
    {
        string t_line = $"[Tournament] 챕터 #{_chapterIndex}: {_message}";
        if (!this.m_reported.Add(t_line)) return;

        if (_isError) Debug.LogError(t_line);
        else Debug.LogWarning(t_line);
    }

    // 자식의 "타일 루트 피벗 원점" 기준 좌표. 타일 루트에 닿을 때까지 부모를 타고 올라가며 한 단계씩 누적한다 —
    // 중간에 몇 겹이 끼든, 앵커·피벗을 어떻게 저작하든 맞는 일반식이다.
    //
    // 월드 좌표도 localPosition도 읽지 않는다 — Canvas 밖에서 만든 프리팹은 localPosition이 전부 0이고
    // 생성 직후 프레임엔 레이아웃도 아직 안 서 있다. anchoredPosition과 부모 rect만으로 산술한다.
    static Vector2 LocalOffsetInTile(RectTransform _rect, RectTransform _tile)
    {
        Vector2 t_offset = Vector2.zero;

        for (RectTransform t_cur = _rect; t_cur != null && t_cur != _tile; t_cur = t_cur.parent as RectTransform)
        {
            if (!(t_cur.parent is RectTransform t_parent)) break;

            // 앵커가 부모 rect 안에서 가리키는 기준점(부모 피벗 원점 기준) + 그 기준점에서의 오프셋
            Vector2 t_anchorCenter = (t_cur.anchorMin + t_cur.anchorMax) * 0.5f;
            Vector2 t_refPoint = (t_anchorCenter - t_parent.pivot) * t_parent.rect.size;

            t_offset += t_refPoint + t_cur.anchoredPosition;
        }

        return t_offset;
    }

    // 앵커를 코드가 Content 하단 중앙으로 고정한다 — 프리팹 저작 앵커에 따라 좌표가 달라지지 않게.
    void Place(RectTransform _rect, Vector2 _position)
    {
        if (_rect == null) return;

        _rect.anchorMin = new Vector2(0.5f, 0f);
        _rect.anchorMax = new Vector2(0.5f, 0f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.anchoredPosition = _position;
    }

    // 진행 통지 → 전 정점 재바인딩 + 길 색 갱신. 재빌드가 아니라 Refresh라 스크롤 위치가 보존된다.
    void RefreshNodes()
    {
        // 억제가 잘못 남았다 — 결말을 쥔 주체(왕복·점등)가 둘 다 없으면 이 통지가 그대로 진실이다.
        // 팝업 콜백이 유실되는 경로(공용 팝업을 다른 흐름이 덮어쓴다)에서 맵이 옛 그림으로 굳지 않게 스스로 푼다.
        // 사슬 쪽은 대기 시퀀스가 아니라 IsIntroPending으로 묻는다 — 대상과 대상 사이의 틈에서 억제가 먼저 풀리면
        // 다음 대상이 세울 "잠긴 모습" 첫 박이 그 사이에 도착한 통지에 지워진다.
        if (this.m_suspendRefresh && this.m_claimSeq == null && !this.IsIntroPending && !TournamentRewardFlow.IsClaiming)
            this.m_suspendRefresh = false;

        // 점등 연출이 결말을 손에 쥐고 있다 — 다 돌고 나서 스스로 푼다.
        if (this.m_suspendRefresh) return;

        for (int t_i = 0; t_i < this.m_nodes.Count; t_i++)
            if (this.m_nodes[t_i] != null) this.m_nodes[t_i].Refresh();

        for (int t_i = 0; t_i < this.m_bands.Count; t_i++)
            if (this.m_bands[t_i] != null) this.m_bands[t_i].Refresh();

        this.RefreshLinks();
        this.ApplyTutorialAnchor(this.IsOnStage);   // 닫히는 중에 온 통지가 사라질 화면의 정점을 다시 등록하지 않게
    }

    // 안내 타깃은 정점 하나뿐이다 — 지목을 맵이 소유해야 여럿이 같은 키를 놓고 다투지 않는다.
    // 수령 대기 정점은 제외한다: 그 자리에서 할 일은 도전이 아니라 수령이다.
    void ApplyTutorialAnchor(bool _on)
    {
        // 해금 사슬이 도는 동안은 지목하지 않는다 — 안내 손가락이 연출 위에 겹치면 어느 쪽을 보라는 말인지 갈린다.
        if (this.IsIntroPending) _on = false;

        int t_target = -1;

        if (_on)
        {
            int t_current = TournamentProgress.CurrentNodeIndex;
            if (t_current >= 0
                && TournamentProgress.CanEnter(t_current)
                && !TournamentProgress.IsRewardPending(t_current))
                t_target = t_current;
        }

        for (int t_i = 0; t_i < this.m_nodes.Count; t_i++)
            if (this.m_nodes[t_i] != null) this.m_nodes[t_i].ApplyTutorialAnchor(t_i == t_target);
    }

    // 받을 선물이 있으면 그 정점, 없으면 지금 도전할 정점을 화면 중앙에 둔다.
    // 전부 클리어(-1)면 마지막 챕터 띠로 — 끝 표지와 마지막 완주 보상이 거기 서 있다(정점 위가 아니다).
    void ScrollToCurrent()
    {
        int t_index = TournamentProgress.FocusNodeIndex;
        if (t_index >= 0)
        {
            this.ScrollToNode(t_index);
            return;
        }

        if (this.TryScrollToLastBand()) return;

        this.ScrollToNode(this.m_nodes.Count - 1);   // 띠 미배선 폴백 — 마지막 정점이라도 보여 준다
    }

    // 타일이 있는 마지막 챕터의 띠로 옮긴다. 띠가 하나도 없으면 false(호출부가 정점으로 떨어진다).
    bool TryScrollToLastBand()
    {
        for (int t_i = this.m_bands.Count - 1; t_i >= 0; t_i--)
            if (this.ScrollToBand(t_i)) return true;

        return false;
    }

    // 챕터 띠를 뷰포트 한가운데로. 타일이 미저작인 챕터엔 띠가 없어 false다.
    bool ScrollToBand(int _chapterIndex)
    {
        if (_chapterIndex < 0 || _chapterIndex >= this.m_bands.Count) return false;
        if (this.m_bands[_chapterIndex] == null) return false;
        if (!(this.m_bands[_chapterIndex].transform is RectTransform t_rect)) return false;

        this.ScrollToContentY(t_rect.anchoredPosition.y);
        return true;
    }

    // 인덱스 비례가 아니라 정점의 실제 y로 계산한다(저작 자리가 고르지 않아 비례식이 안 맞는다).
    // 생성 직후 프레임은 레이아웃이 서 있지 않아 rect가 0이므로 강제로 갱신한 뒤에 읽는다.
    void ScrollToNode(int _index)
    {
        if (_index < 0 || _index >= this.m_nodes.Count) return;

        RectTransform t_node = this.m_nodes[_index] != null ? this.m_nodes[_index].transform as RectTransform : null;
        if (t_node == null) return;

        this.ScrollToContentY(t_node.anchoredPosition.y);
    }

    // Content 좌표 y를 뷰포트 한가운데로 가져온다(정점·띠가 같은 셈을 쓰게 하는 자리).
    // 즉시 대입 경로다 — Open()은 "스크롤이 애니 없이 즉시 앉고 그 안에서 레이아웃이 확정된다"를 전제로 사슬을 세운다.
    void ScrollToContentY(float _y)
    {
        if (!this.TryGetContentNormalizedY(_y, out float t_normalized)) return;

        this.scrollRect.verticalNormalizedPosition = t_normalized;
    }

    // Content 좌표 y를 뷰포트 한가운데로 가져오는 정규화 위치. 즉시 대입과 이동 트윈이 같은 자리를 겨냥하게 하는 셈이다.
    // 거짓이면 겨냥할 자리가 없다(미배선·Content가 화면보다 짧아 스크롤 여백이 0).
    bool TryGetContentNormalizedY(float _y, out float _normalized)
    {
        _normalized = 0f;

        if (this.scrollRect == null || this.content == null) return false;

        Canvas.ForceUpdateCanvases();

        RectTransform t_viewport = this.scrollRect.viewport != null
            ? this.scrollRect.viewport
            : this.scrollRect.transform as RectTransform;
        if (t_viewport == null) return false;

        float t_scrollable = this.content.rect.height - t_viewport.rect.height;
        if (t_scrollable <= 0f) return false;   // Content가 화면보다 짧다 = 스크롤할 여백이 없다

        float t_offset = _y - t_viewport.rect.height * 0.5f;
        _normalized = Mathf.Clamp01(t_offset / t_scrollable);
        return true;
    }

    // 정점 뷰가 이미 잠긴 버튼을 죽여 두지만, 진입 판정의 주인은 화면이다(저작·상태가 갈려도 새지 않게).
    void OnNodeTapped(int _index)
    {
        // 해금 사슬이 도는 동안의 탭은 스킵이지 도전이 아니다(스킵은 루트가 받는다).
        // 스킵 쪽과 같은 잣대를 쓴다 — 대기 시퀀스가 갈리는 틈에 정점 탭만 통과해 전투로 새지 않게.
        if (this.IsIntroPending) return;

        // 길 점등은 관문이 아니다(상태가 이미 진실이라 다음 정점은 눌러도 된다).
        // 다만 도장이 꽂히는 중인 그 정점만은 막는다.
        if (this.m_claimSeq != null && _index == this.m_claimNode) return;

        // 수령 왕복이 도는 동안은 두 번째 수령을 받지 않는다 — 대기 owner가 하나뿐이라 먼저 끝난 쪽이 남은 차단막을 걷는다.
        if (TournamentRewardFlow.IsClaiming) return;

        // 선물은 도전이 아니라 수령이다 — 전투로 잇지 않고 보상 팝업으로 간다.
        if (TournamentProgress.IsRewardPending(_index))
        {
            this.OpenNodeReward(_index);
            return;
        }

        if (!TournamentProgress.CanEnter(_index)) return;

        this.NodeSelected?.Invoke(_index);
    }

    // 수령 → 점등 → 해금. 갱신을 억제하지 않는다 — 도장은 누른 프레임에 낙관으로 꽂혀야 하고,
    // 그것을 그리는 것이 바로 진행 통지가 부르는 RefreshNodes 다.
    //
    // 대신 길 구간 하나만 미리 쥔다. 서버 확정 통지가 점등보다 먼저 도착하는데,
    // 그때 RefreshLinks 가 이 구간을 금색으로 칠해 버리면 차오르는 연출이 통째로 사라진다.
    void OpenNodeReward(int _index)
    {
        if (!TournamentProgress.TryGetNode(_index, out TournamentNodeDef t_node)) return;

        this.m_litLink = this.m_links.FindIndex(_link => _link.Index == _index) >= 0 ? _index : -1;

        // 결말 중 확정 사건(길 점등·해금)만 _onClaimed 가 잇는다. 도장은 그보다 앞서 낙관으로 끝나 있다.
        // _onClosed 는 수령 없이 닫힌 경로에서 쥐고 있던 길 구간을 놓는 안전망이다.
        if (!TournamentRewardFlow.Open(t_node.nodeId,
                _onClaimed: () => this.PlayClaimSequence(_index),
                _onClosed:  this.AbortIfIdle))
            this.AbortClaimSequence();
    }

    // 서버가 클리어를 확정한 프레임에 결말을 그리고, 길이 차오르는 것을 곧바로 잇는다.
    // 팝업의 분출·퇴장은 이 위에서 제 안무를 마저 돈다 — 그것을 기다리면 수령과 결말 사이가 통째로 빈다.
    // 해금을 점등 끝에 매달아 두면 상태를 보려고 길이 다 찰 때까지 기다려야 한다 — 길은 장식이지 관문이 아니다.
    void PlayClaimSequence(int _index)
    {
        // 맵을 떠난 뒤 콜백이 도착했다 — 다음 진입에서 진실이 그려진다.
        if (!this.IsOnStage)
        {
            this.AbortClaimSequence();
            return;
        }

        // 수령이 성사되지 않았다면 보여줄 결말이 없다(서버 거절·외부 강제 Hide 경로).
        if (!TournamentProgress.TryGetNode(_index, out TournamentNodeDef t_node)
            || !TournamentProgress.IsCleared(t_node.nodeId))
        {
            this.AbortClaimSequence();
            return;
        }

        // 길이 차기 전에 결말부터 그린다. 이 구간만 아직 흰색으로 남겨 두어야 점등이 눈에 보인다.
        int t_slot = this.m_links.FindIndex(_link => _link.Index == _index);
        this.m_litLink = t_slot >= 0 ? _index : -1;

        // 서버 낙인이 방금 섰다 — 다음 정점이 열린 모습으로 갈아 끼운다.
        // 이 정점의 도장은 낙관 시점에 이미 꽂혔으므로 여기서 다시 부르지 않는다(TournamentNodeView.Refresh).
        this.RefreshNodes();

        this.m_claimNode = _index;
        this.m_claimSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 정박 없이 곧바로 차오른다 — 도장은 이미 꽂혔고, 이 점등이 "확정됐다"를 잇는 두 번째 박이다.
        float t_at = 0f;
        t_at += t_slot >= 0 ? this.InsertLinkFill(this.m_links[t_slot], t_at)
                            : this.InsertChapterSeam(_index, t_at);

        this.m_claimSeq.InsertCallback(t_at, () => this.PunchNext(_index));
        this.m_claimSeq.OnComplete(this.EndClaimSequence);
    }

    // 점등이 끝났다 — 쥐고 있던 구간을 놓고 진실로 맞춘다(트윈이 남긴 색과 저작색이 미세하게 갈릴 수 있다).
    void EndClaimSequence()
    {
        this.m_claimSeq = null;
        this.m_claimNode = -1;

        if (this.m_litLink < 0) return;

        this.m_litLink = -1;
        this.RefreshLinks();
    }

    // 구간의 점을 저작 순서(정점 i → i+1)대로 물들인다. 다 차기까지 걸린 시간을 돌려준다.
    float InsertLinkFill(PathLink _link, float _at)
    {
        if (_link.Graphics == null) return 0f;

        float t_fill = 0f;

        for (int t_i = 0; t_i < _link.Graphics.Length; t_i++)
        {
            Graphic t_dot = _link.Graphics[t_i];
            if (t_dot == null) continue;

            float t_offset = t_i * this.linkDotStagger;
            this.m_claimSeq.Insert(_at + t_offset, t_dot.DOColor(this.clearedColor, this.linkDotDuration));
            t_fill = Mathf.Max(t_fill, t_offset + this.linkDotDuration);
        }

        return t_fill;
    }

    // 챕터 경계·마지막 정점은 길 조각이 없다(링크는 타일 안에만 저작된다).
    // 다음 정점이 화면 밖이므로 스크롤이 곧 "다음 장이 열렸다"는 문장이 된다.
    float InsertChapterSeam(int _index, float _at)
    {
        if (_index + 1 >= this.m_nodes.Count) return 0f;

        this.m_claimSeq.InsertCallback(_at + 0.15f, () => this.ScrollToNode(_index + 1));
        return 0.15f;
    }

    // 사슬의 끝 — 길이 다 찬 자리에서 다음 정점이 튄다.
    // 상태는 이미 도장이 꽂히던 프레임에 열렸고, 이 펀치는 "여기가 다음이다"를 가리키는 손짓이다.
    void PunchNext(int _index)
    {
        int t_next = _index + 1;
        if (t_next < 0 || t_next >= this.m_nodes.Count) return;

        // 챕터가 랭크로 잠겨 있으면 튀지 않는다 — 자물쇠가 튀면 "열렸다"는 거짓말이 된다.
        if (TournamentProgress.IsRankLocked(t_next)) return;

        // 자리가 미저작이라 세우지 못한 정점은 화면에 아무것도 튀지 않는다 — 표식만 세우면 그 1회 연출이 조용히 소모된다.
        TournamentNodeView t_node = this.m_nodes[t_next];
        if (t_node == null) return;

        t_node.PlayUnlockPunch();

        // 이 펀치가 곧 그 정점의 해금 연출이다 — 여기서 표식을 세우지 않으면 다음 진입에 같은 연출이 한 번 더 터진다.
        TournamentProgress.MarkNodeUnlockSeen(t_next);
    }

    // 어디서 끊겨도 진실로 스냅시킨다 — 연출은 장식일 뿐이라 중간 색에서 굳는 것만 막으면 된다.
    void AbortClaimSequence()
    {
        // 맵의 모든 이탈 경로가 이 문을 지난다 — 해금 사슬도 여기서 함께 걷힌다(반대로 부르지 않는다, 재귀가 된다).
        this.AbortUnlockIntro();

        if (this.m_claimSeq != null && this.m_claimSeq.IsActive()) this.m_claimSeq.Kill();
        this.m_claimSeq = null;
        this.m_claimNode = -1;

        // 점등이 반쯤 찬 구간을 쥔 채 끊겼다 — 놓지 않으면 그 구간만 영영 갱신에서 빠진다.
        this.m_litLink = -1;

        if (!this.m_suspendRefresh)
        {
            this.RefreshLinks();
            return;
        }

        this.m_suspendRefresh = false;
        this.RefreshNodes();
    }

    // 수령 없이 팝업이 닫힌 경로의 안전망 — 쥐고 있던 길 구간을 놓는다.
    // 사슬이 이미 시작됐거나 왕복이 아직 도는 중이면 손대지 않는다: 전자는 제 손으로 놓고,
    // 후자는 곧 도착할 _onClaimed 가 그 구간을 점등에 쓴다.
    void AbortIfIdle()
    {
        if (TournamentRewardFlow.IsClaiming || this.m_claimSeq != null || this.m_litLink < 0) return;

        this.AbortClaimSequence();
    }

    // 수령이 화면을 쥐고 있는 동안은 해금을 훑지 않는다 — 전투 복귀도 같은 Open()을 타므로
    // 이 문이 없으면 수령 연출과 해금 사슬이 한 프레임에 겹친다.
    bool CanCollectUnlockIntro
        => string.IsNullOrEmpty(TournamentProgress.PendingRewardNodeId)
           && !TournamentRewardFlow.IsClaiming
           && this.m_claimSeq == null;

    // 이번 진입에서 처음 열린 것들을 화면 아래에서 위로 늘어놓는다(챕터 띠 → 그 챕터의 새 정점들 → 다음 챕터).
    // 차분만 받아 둔다 — 표식은 실제로 재생한 뒤에야 선다.
    void CollectUnlockIntro()
    {
        this.m_introTargets.Clear();
        this.m_introSeen.Clear();
        this.m_introChapterOpens.Clear();
        this.m_introSpent = 0f;

        // 표식 없이 진행 흔적만 있던 세이브다 — 지나온 자리를 먼저 조용히 덮고, 남은 것만 아래에서 재생한다.
        TournamentProgress.BackfillSeenUnlocks();

        if (!TournamentProgress.TryTakeUnlockShowcase(out TournamentUnlockShowcase t_showcase)) return;

        int t_chapters = TournamentProgress.ChapterCount;

        for (int t_c = 0; t_c < t_chapters; t_c++)
        {
            this.GetChapterNodeRange(t_c, out int t_nodeStart, out int t_count);

            // 띠가 대상으로 섰으면 그 장의 새 정점은 계단이 맡는다 — 개별 대상으로도 남기면
            // 계단이 그 정점을 건너뛰어(IsPendingTarget) 정작 열려야 할 정점을 잠긴 채 되돌린다.
            bool t_bandTaken = t_showcase.Chapters != null && t_showcase.Chapters.Contains(t_c)
                               && this.AddIntroTarget(new IntroTarget(true, t_c));

            if (t_showcase.Nodes == null) continue;

            for (int t_i = 0; t_i < t_showcase.Nodes.Count; t_i++)
            {
                int t_node = t_showcase.Nodes[t_i];
                if (t_node < t_nodeStart || t_node >= t_nodeStart + t_count) continue;

                if (t_bandTaken) this.AddChapterOpen(t_node);
                else this.AddIntroTarget(new IntroTarget(false, t_node));
            }
        }

        // 개수 상한은 앞쪽(오래된 것)부터 버린다 — 유저가 실제로 갈 자리는 가장 앞선 챕터·정점이다.
        // 목록에는 재생할 수 있는 대상만 들어와 있으므로 이 트림이 "보여줄 수 있는 것"만 센다.
        // 무대 세우기는 수집이 끝난 뒤에 도므로 여기서 잘린 대상은 애초에 세워지지 않는다(풀어 줄 것도 없다).
        int t_max = Mathf.Max(1, this.introMaxSteps);
        while (this.m_introTargets.Count > t_max) this.m_introTargets.RemoveAt(0);
    }

    // 챕터가 차지하는 평탄 정점 구간. 타일을 쌓을 때(BuildChapterTiles)와 같은 셈이다 — 앞 챕터의 정점 수를 누적한다.
    void GetChapterNodeRange(int _chapterIndex, out int _start, out int _count)
    {
        _start = 0;
        _count = 0;

        int t_chapters = TournamentProgress.ChapterCount;

        for (int t_c = 0; t_c <= _chapterIndex && t_c < t_chapters; t_c++)
        {
            int t_nodes = TournamentProgress.TryGetChapter(t_c, out TournamentChapterDef t_chapter) ? t_chapter.NodeCount : 0;

            if (t_c == _chapterIndex)
            {
                _count = t_nodes;
                return;
            }

            _start += t_nodes;
        }
    }

    // 화면에 세울 부품이 없는 대상은 아예 담지 않는다 — 담으면 개수 상한 자리를 먹어 정상 대상을 밀어내고,
    // 사슬은 아무것도 못 보여준 채 억제·탭 차단·앵커 해제만 걸다 끝난다(그 상태가 진입마다 되풀이된다).
    // 빼도 표식은 재생한 것만 세우므로, 저작을 고친 다음 진입에서 그대로 다시 나온다.
    // 반환값은 "대상으로 섰는가"다 — 띠가 섰는지에 따라 그 장의 새 정점을 계단에 맡길지가 갈린다.
    bool AddIntroTarget(IntroTarget _target)
    {
        if (!this.HasIntroView(_target)) return false;

        this.m_introTargets.Add(_target);
        return true;
    }

    // 계단이 열 정점. 부품이 없는 자리는 담지 않는다 — 표식은 실제로 보여준 것만 세운다(AddIntroTarget과 같은 잣대).
    void AddChapterOpen(int _nodeIndex)
    {
        if (!this.HasIntroView(new IntroTarget(false, _nodeIndex))) return;
        if (this.m_introChapterOpens.Contains(_nodeIndex)) return;

        this.m_introChapterOpens.Add(_nodeIndex);
    }

    // 그 자리에 부품이 실제로 서 있는가. 자리 미저작 정점·타일 미저작 챕터는 목록에 빈칸으로 남아 있다.
    bool HasIntroView(IntroTarget _target)
    {
        if (_target.Index < 0) return false;

        if (_target.IsBand)
            return _target.Index < this.m_bands.Count && this.m_bands[_target.Index] != null;

        return _target.Index < this.m_nodes.Count && this.m_nodes[_target.Index] != null;
    }

    // 맵이 열리는 첫 프레임에 무대를 통째로 세운다 — 재생은 하지 않고 잠긴 모습만 굳힌다.
    // 챕터가 대상이면 그 장의 정점 전부를 함께 세운다: "이 장이 잠겨 있었다"는 문장은
    // 새로 열린 정점 하나만 잠긴 그림으로는 서지 않는다(신규 유저의 첫 챕터가 정확히 그 경우다).
    void StageUnlockIntro()
    {
        this.m_introStaged.Clear();
        if (this.m_introTargets.Count == 0) return;

        for (int t_i = 0; t_i < this.m_introTargets.Count; t_i++)
        {
            IntroTarget t_target = this.m_introTargets[t_i];

            if (!t_target.IsBand)
            {
                this.StageIntroNode(t_target.Index);
                continue;
            }

            if (t_target.Index < 0 || t_target.Index >= this.m_bands.Count) continue;
            if (this.m_bands[t_target.Index] == null) continue;

            this.m_bands[t_target.Index].StageChapterLocked();
            this.MarkStaged(t_target);

            this.GetChapterNodeRange(t_target.Index, out int t_start, out int t_count);
            for (int t_n = t_start; t_n < t_start + t_count; t_n++) this.StageIntroNode(t_n);
        }
    }

    // 세우기는 멱등이라 겹쳐 불려도 안전하다(재생 대상이 제 챕터에 겹치는 경우).
    void StageIntroNode(int _index)
    {
        if (_index < 0 || _index >= this.m_nodes.Count) return;
        if (this.m_nodes[_index] == null) return;

        this.m_nodes[_index].StageUnlockLocked();
        this.MarkStaged(new IntroTarget(false, _index));
    }

    // 세운 목록에 없으면 담는다. 세운 것과 푼 것의 개수가 맞아야 손잡이가 굳지 않는다.
    void MarkStaged(IntroTarget _target)
    {
        for (int t_i = 0; t_i < this.m_introStaged.Count; t_i++)
            if (this.m_introStaged[t_i].IsBand == _target.IsBand && this.m_introStaged[t_i].Index == _target.Index) return;

        this.m_introStaged.Add(_target);
    }

    // 세운 것을 전부 진실로 되돌린다(끝맺음·중단의 단일 창구). Abort*는 세우기만 한 무대도 복원까지 간다.
    void ReleaseAllStaged()
    {
        for (int t_i = 0; t_i < this.m_introStaged.Count; t_i++) this.ReleaseIntroView(this.m_introStaged[t_i]);

        this.m_introStaged.Clear();
    }

    // 챕터 띠 안무가 끝난 시각에 그 장의 정점들을 제 진실로 돌려보낸다 — 이것이 "이 장이 열렸다"는 문장이다.
    // 아직 제 차례를 기다리는 재생 대상은 두고 간다: 그 정점은 PlayUnlockReveal이 무대를 이어받아야 한다.
    void ReleaseChapterNodes(int _chapterIndex)
    {
        this.GetChapterNodeRange(_chapterIndex, out int t_start, out int t_count);

        for (int t_i = this.m_introStaged.Count - 1; t_i >= 0; t_i--)
        {
            IntroTarget t_target = this.m_introStaged[t_i];

            if (t_target.IsBand) continue;
            if (t_target.Index < t_start || t_target.Index >= t_start + t_count) continue;
            if (this.IsPendingTarget(t_target.Index)) continue;

            this.m_introStaged.RemoveAt(t_i);
            this.ReleaseIntroView(t_target);
        }
    }

    // 계단의 한 칸. 이번에 새로 열린 정점이면 스냅이 아니라 해금 안무로 연다 —
    // 띠가 대상인 장에서는 그 정점이 개별 대상에서 빠져 있어(CollectUnlockIntro) 열어 줄 자리가 여기뿐이다.
    // 그 밖의 정점은 지금처럼 진실로 돌려보낸다(잠긴 채 남을 정점이라 그림이 바뀌지 않는다).
    void ReleaseChapterNode(int _nodeIndex)
    {
        if (this.IsPendingTarget(_nodeIndex)) return;

        for (int t_i = this.m_introStaged.Count - 1; t_i >= 0; t_i--)
        {
            IntroTarget t_target = this.m_introStaged[t_i];

            if (t_target.IsBand || t_target.Index != _nodeIndex) continue;

            // 연 정점은 무대 목록에 그대로 둔다 — 이탈이 도는 안무를 걷는 문(ReleaseAllStaged)이 그 하나뿐이다.
            if (this.TryOpenChapterNode(_nodeIndex)) return;

            this.m_introStaged.RemoveAt(t_i);
            this.ReleaseIntroView(t_target);
            return;
        }
    }

    // 계단이 여는 한 칸. 열었으면 참 — 표식도 여기서 세운다(PlayIntroBody를 지나지 않는 유일한 재생 경로다).
    bool TryOpenChapterNode(int _nodeIndex)
    {
        if (!this.m_introChapterOpens.Remove(_nodeIndex)) return false;

        var t_target = new IntroTarget(false, _nodeIndex);

        // 계단이 도는 사이 재빌드·파괴로 부품이 사라졌다 — 표식 없이 스냅 경로로 돌려보낸다.
        if (!this.HasIntroView(t_target)) return false;

        // 잠긴 모습을 다시 보여주는 첫 박은 건너뛴다 — 띠 안무가 이미 그 문장을 말했고,
        // 여기서 또 멈추면 이 정점만 제 칸에서 뒤처져 계단 밖으로 튀어나온다.
        this.m_nodes[_nodeIndex].PlayUnlockReveal(true);
        this.MarkIntroSeen(t_target);
        return true;
    }

    // 계단이 미처 열지 못한 정점의 표식. 스킵도 "봤다"는 의사 표시라 재생과 같은 자리에 선다 —
    // 빠뜨리면 그 정점만 다음 진입마다 해금 연출을 되풀이한다.
    void MarkChapterOpensSeen(int _chapterIndex)
    {
        this.GetChapterNodeRange(_chapterIndex, out int t_start, out int t_count);

        for (int t_i = this.m_introChapterOpens.Count - 1; t_i >= 0; t_i--)
        {
            int t_node = this.m_introChapterOpens[t_i];
            if (t_node < t_start || t_node >= t_start + t_count) continue;

            this.m_introChapterOpens.RemoveAt(t_i);
            this.MarkIntroSeen(new IntroTarget(false, t_node));
        }
    }

    // 표식은 실제로 보여준 것만 센다. 계단과 스킵이 같은 정점을 두 번 담을 수 있어 여기서 한 번으로 조인다.
    void MarkIntroSeen(IntroTarget _target)
    {
        for (int t_i = 0; t_i < this.m_introSeen.Count; t_i++)
            if (this.m_introSeen[t_i].IsBand == _target.IsBand && this.m_introSeen[t_i].Index == _target.Index) return;

        this.m_introSeen.Add(_target);
    }

    // 아직 재생을 기다리는 정점인가(이미 재생한 것·버린 것은 거짓이다).
    bool IsPendingTarget(int _nodeIndex)
    {
        for (int t_i = 0; t_i < this.m_introTargets.Count; t_i++)
            if (!this.m_introTargets[t_i].IsBand && this.m_introTargets[t_i].Index == _nodeIndex) return true;

        return false;
    }

    void ReleaseIntroView(IntroTarget _target)
    {
        if (_target.IsBand)
        {
            if (_target.Index >= 0 && _target.Index < this.m_bands.Count) this.m_bands[_target.Index]?.AbortUnlock();
            return;
        }

        if (_target.Index >= 0 && _target.Index < this.m_nodes.Count) this.m_nodes[_target.Index]?.AbortUnlockReveal();
    }

    // 예산 초과로 목록 앞쪽을 버린다. 버리는 대상은 이미 잠긴 채 세워져 있으므로 목록에서 지우는 것만으로는 모자라다 —
    // 풀지 않으면 재생될 일도 없는 부품이 잠긴 모습으로 굳는다.
    void DropIntroTargets(int _count)
    {
        for (int t_i = 0; t_i < _count && this.m_introTargets.Count > 0; t_i++)
        {
            IntroTarget t_target = this.m_introTargets[0];
            this.m_introTargets.RemoveAt(0);

            this.ReleaseIntroView(t_target);

            for (int t_s = this.m_introStaged.Count - 1; t_s >= 0; t_s--)
                if (this.m_introStaged[t_s].IsBand == t_target.IsBand && this.m_introStaged[t_s].Index == t_target.Index)
                    this.m_introStaged.RemoveAt(t_s);

            // 띠를 버리면 그 띠에 딸려 세운 정점들도 풀어 줄 주체가 사라진다.
            if (t_target.IsBand) this.ReleaseChapterNodes(t_target.Index);
        }
    }

    // 사슬의 시작. 억제를 걸어 두어야 해금이 세우는 "잠긴 모습" 첫 박이 진행 통지에 지워지지 않는다.
    void PlayUnlockIntro()
    {
        if (this.m_introTargets.Count == 0) return;

        // 사슬이 서지 못한 경로는 애초에 잠그지 않는다. 표식 초기화를 빠뜨리면 다음 진입부터 예산이 영영 무력화된다.
        this.m_introSkipped = false;
        this.LockScrollForIntro(true);

        this.m_suspendRefresh = true;
        this.StepUnlockIntro();
    }

    // 사슬의 1단계 — 다음 대상을 골라 카메라만 그 자리로 미끄러뜨린다.
    // 길이는 재생해 봐야 알 수 있으므로 시간표를 한 번에 짜지 않는다 —
    // 한 대상이 끝나는 시각에 다음 대상을 예약해 하나씩 이어 붙인다.
    void StepUnlockIntro()
    {
        while (this.m_introTargets.Count > 0)
        {
            // 예산을 다 썼으면 사이를 버리고 가장 앞선(최신) 대상 하나로 건너뛴다 — 개수 상한과 같은 방향이다.
            // 재생은 그대로 아래에서 위로 간다: 자르는 쪽만 최신을 남기고, 남은 것은 목록 순서대로 훑는다.
            // 버린 대상은 표식이 서지 않으니 다음 진입에서 이어 재생된다.
            //
            // 탭으로 당겨 온 사슬에는 이 문을 걸지 않는다: m_introSpent에는 저작된 안무 길이가 통째로 더해져 있어,
            // 그대로 두면 스킵할수록 남은 대상이 더 많이 버려지는 역설이 된다(대상 수는 introMaxSteps가 이미 묶는다).
            if (!this.m_introSkipped && this.m_introSpent >= Mathf.Max(0f, this.introBudget))
                this.DropIntroTargets(this.m_introTargets.Count - 1);

            IntroTarget t_target = this.m_introTargets[0];
            this.m_introTargets.RemoveAt(0);

            // 화면에 아무것도 나오지 않을 대상은 이동조차 하지 않는다 — 숨도 쉬지 않고 곧바로 다음을 당겨 쓴다.
            if (!this.HasIntroView(t_target)) continue;

            this.m_introPlaying = t_target;
            this.m_introTraveling = true;

            float t_travel = this.TravelToTarget(t_target);

            // 이동도 m_introSeq에 담는다 — 이 자리를 비우면 마지막 대상의 이동 중에 IsIntroPending이 새어
            // 그 틈으로 복귀 수령·정점 탭이 통과한다.
            Sequence t_move = DOTween.Sequence().SetLink(this.gameObject);
            t_move.AppendInterval(Mathf.Max(0.01f, t_travel));
            t_move.OnComplete(() =>
            {
                this.m_introSeq = null;
                this.PlayIntroBody();
            });

            this.m_introSeq = t_move;
            return;
        }

        this.EndUnlockIntro();
    }

    // 사슬의 2단계 — 카메라가 도착한 자리에서 부품 안무를 세우고, 그 길이만큼 다음 차례를 미룬다.
    // _chain이 거짓이면 대기 시퀀스를 세우지 않는다: 스킵이 곧바로 결말까지 당길 참이라 기다릴 것이 없다.
    // 반환값은 "안무가 실제로 섰는가"다.
    bool PlayIntroBody(bool _chain = true)
    {
        this.m_introTraveling = false;

        IntroTarget t_playing = this.m_introPlaying;

        if (!this.TryPlayIntroTarget(t_playing, out float t_length))
        {
            // 이동하는 사이 재빌드·파괴로 부품이 사라졌다 — 표식 없이 다음 대상으로 넘어간다.
            if (_chain) this.StepUnlockIntro();
            return false;
        }

        this.MarkIntroSeen(t_playing);
        this.m_introSpent += t_length;

        if (!_chain) return true;

        // 큰 사건과 작은 사건의 숨을 가른다 — 같은 길이로 두면 둘의 무게가 구분되지 않는다.
        float t_gap = Mathf.Max(0f, t_playing.IsBand ? this.introGapChapter : this.introGapNode);

        // 계단이 여는 정점은 제 해금 안무를 돌린다 — 대기가 그 끝까지 품지 못하면
        // 뒤이은 EndUnlockIntro·ReleaseAllStaged가 그 안무를 시작하자마자 걷어 간다.
        float t_wait = Mathf.Max(0.01f, t_length + t_gap);
        float t_step = 0f;

        if (t_playing.IsBand)
        {
            t_step = this.ChapterStepInterval(t_playing.Index, t_gap);
            t_wait = Mathf.Max(t_wait, this.ChapterStepsEnd(t_playing.Index, t_length, t_step));
        }

        Sequence t_seq = DOTween.Sequence().SetLink(this.gameObject);
        t_seq.AppendInterval(t_wait);   // Append는 현재 길이 뒤에 붙는다 — 계단 콜백보다 먼저 세워야 대기가 밀리지 않는다

        // 띠 안무가 끝나는 시각이 곧 그 장이 열리는 시각이다 — 딸려 세운 정점들을 여기서부터 계단으로 풀어 준다.
        // 다음 대상으로 넘어가는 숨까지 기다리면 열림이 한 박 늦게 도착한다.
        if (t_playing.IsBand) this.InsertChapterNodeSteps(t_seq, t_playing.Index, t_length, t_step);

        t_seq.OnComplete(() =>
        {
            this.m_introSeq = null;
            this.StepUnlockIntro();
        });

        this.m_introSeq = t_seq;
        return true;
    }

    // 그 장의 정점을 인덱스 순서대로 하나씩 푼다 — "장이 열리면서 길이 뻗어 나간다"는 문장이다.
    // 별도 시퀀스를 만들지 않고 대기 시퀀스에 얹는다: 그러면 중단·이탈의 Kill이 계단까지 함께 걷어
    // 수명 관리를 세 곳(Abort·End·OnDisable)에 새로 배선하지 않아도 된다.
    void InsertChapterNodeSteps(Sequence _seq, int _chapterIndex, float _at, float _step)
    {
        this.GetChapterNodeRange(_chapterIndex, out int t_start, out int t_count);
        if (t_count <= 0) return;

        // 잠긴 채 남을 정점도 제 칸을 밟는다 — 길을 따라 위에서 아래로 훑는 리듬이 곧 이 연출의 문장이고,
        // 여는 정점은 그 리듬의 한 칸으로 들어와야 "맨 앞에서 시작"으로 읽힌다. 박을 빼면 혼자 튄다.
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            int t_node = t_start + t_i;
            _seq.InsertCallback(_at + t_i * _step, () => this.ReleaseChapterNode(t_node));
        }
    }

    // 계단의 박자. 잣대는 그 장의 전체 정점 수다 — 여는 정점만 박을 먹게 하면
    // 새로 열리는 정점이 하나뿐인 장(온보딩의 첫 챕터가 정확히 그렇다)에서 간격이 0이 되어
    // 계단이 통째로 사라지고, 그 정점 혼자 남들보다 늦게 열리는 것으로 읽힌다.
    float ChapterStepInterval(int _chapterIndex, float _gap)
    {
        this.GetChapterNodeRange(_chapterIndex, out int _, out int t_count);
        if (t_count <= 1) return 0f;

        return Mathf.Min(Mathf.Max(0f, this.introNodeRelease), Mathf.Max(0f, _gap) / (t_count - 1));
    }

    // 계단이 다 끝나는 시각 — 마지막 칸이 여는 정점의 해금 안무까지 포함한다. 대기 길이가 이것을 품어야 한다.
    float ChapterStepsEnd(int _chapterIndex, float _at, float _step)
    {
        this.GetChapterNodeRange(_chapterIndex, out int t_start, out int t_count);

        float t_end = _at;

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            int t_node = t_start + t_i;
            if (!this.m_introChapterOpens.Contains(t_node)) continue;

            TournamentNodeView t_view = t_node < this.m_nodes.Count ? this.m_nodes[t_node] : null;
            if (t_view != null) t_end = Mathf.Max(t_end, _at + t_i * _step + t_view.UnlockRevealDuration(true));
        }

        return t_end;
    }

    // 대상 자리로 카메라를 미끄러뜨린다. 반환값은 실제 이동 시간(0이면 이동이 없었다 — 곧바로 안무로 넘어간다).
    // 사슬 안에서만 쓰는 경로다: 사슬 밖은 즉시 대입(ScrollToContentY)이 그대로 주인이다.
    float TravelToTarget(IntroTarget _target)
    {
        this.KillScrollTween();

        if (!this.TryGetIntroTargetY(_target, out float t_y)) return 0f;
        if (!this.TryGetContentNormalizedY(t_y, out float t_normalized)) return 0f;

        float t_duration = Mathf.Max(0f, _target.IsBand ? this.introTravelChapter : this.introTravelNode);

        // 이미 그 자리다 — 0프레임 이동에 시간을 얹으면 사슬이 이유 없이 늘어진다.
        if (t_duration <= 0f || Mathf.Abs(this.scrollRect.verticalNormalizedPosition - t_normalized) < 0.0005f)
        {
            this.scrollRect.verticalNormalizedPosition = t_normalized;
            return 0f;
        }

        this.m_scrollTween = this.scrollRect
            .DOVerticalNormalizedPos(t_normalized, t_duration)
            .SetEase(Ease.InOutSine)
            .SetLink(this.gameObject);

        return t_duration;
    }

    // 대상 부품이 앉아 있는 Content 좌표 y. 자리 미저작·재빌드로 부품이 없으면 거짓이다.
    bool TryGetIntroTargetY(IntroTarget _target, out float _y)
    {
        _y = 0f;

        if (this.scrollRect == null || !this.HasIntroView(_target)) return false;

        RectTransform t_rect = _target.IsBand
            ? this.m_bands[_target.Index].transform as RectTransform
            : this.m_nodes[_target.Index].transform as RectTransform;
        if (t_rect == null) return false;

        _y = t_rect.anchoredPosition.y;
        return true;
    }

    // 이동을 걷고 대상 자리에 즉시 앉힌다 — 이동 중에 탭했더라도 당겨 낸 결말이 화면 안에서 보여야 한다.
    void SnapToIntroTarget(IntroTarget _target)
    {
        if (!this.TryGetIntroTargetY(_target, out float t_y)) return;

        this.ScrollToContentY(t_y);
    }

    void KillScrollTween()
    {
        Tween t_tween = this.m_scrollTween;
        this.m_scrollTween = null;

        if (t_tween != null && t_tween.IsActive()) t_tween.Kill();
    }

    // 사슬이 도는 동안 카메라의 주인을 하나로 만든다.
    // vertical=false가 아니라 enabled 축이다 — ScrollRect가 켜져 있으면 드래그를 계속 소비해
    // PointerEventData.dragging이 서고, OnPointerClick의 드래그 게이트에 걸려 탭 스킵이 통과하지 못한다.
    void LockScrollForIntro(bool _on)
    {
        if (this.m_scrollLocked == _on) return;

        this.m_scrollLocked = _on;

        if (this.scrollRect == null) return;

        if (_on) this.scrollRect.StopMovement();   // 잠기기 전에 굴러가던 관성은 여기서 끊는다
        this.scrollRect.enabled = !_on;
    }

    // 탭 한 번 = 대상 하나를 결말까지. 당길 것이 있었으면 참.
    // Kill은 대기 시퀀스에 매달린 두 콜백(다음 대상 예약·계단 해제)을 함께 지우므로 여기서 손으로 재현한다 —
    // 빠지면 그 장의 정점들이 잠긴 모습으로 굳는다.
    bool SkipCurrentIntroTarget()
    {
        if (!this.IsIntroPending) return false;

        this.m_introSkipped = true;

        // 대상과 대상 사이의 틈이다 — 당길 안무가 없으니 다음 대상의 이동을 곧바로 시작한다.
        if (this.m_introSeq == null)
        {
            this.StepUnlockIntro();
            return true;
        }

        this.KillScrollTween();
        this.SnapToIntroTarget(this.m_introPlaying);

        // 대기 시퀀스를 먼저 걷는다 — 이동 시퀀스를 살려 둔 채 안무를 세우면 그 OnComplete가 뒤늦게 또 세운다.
        Sequence t_seq = this.m_introSeq;
        this.m_introSeq = null;
        if (t_seq.IsActive()) t_seq.Kill();

        // 이동만 돌고 있었다면 안무가 아직 서지도 않았다 — 여기서 세워야 당길 결말이 생긴다.
        if (this.m_introTraveling) this.PlayIntroBody(false);

        IntroTarget t_playing = this.m_introPlaying;

        if (t_playing.IsBand)
        {
            if (t_playing.Index >= 0 && t_playing.Index < this.m_bands.Count)
                this.m_bands[t_playing.Index]?.RequestSkipUnlock();

            // 스킵 경로는 계단을 쓰지 않는다 — 남은 정점을 즉시 전량 해제한다(이미 풀린 정점에 다시 불려도 멱등이다).
            // 계단이 열었어야 할 정점은 표식만 세우고 넘긴다: 스킵도 "봤다"는 의사 표시라 여기서 빠뜨리면
            // 그 정점만 다음 진입에서 해금 연출을 되풀이한다.
            this.MarkChapterOpensSeen(t_playing.Index);
            this.ReleaseChapterNodes(t_playing.Index);
        }
        else if (t_playing.Index >= 0 && t_playing.Index < this.m_nodes.Count)
            this.m_nodes[t_playing.Index]?.RequestSkipReveal();

        if (this.m_introTargets.Count > 0) this.StepUnlockIntro();
        else this.EndUnlockIntro();

        return true;
    }

    // 도착한 자리에서 그 부품의 안무를 시작한다. 카메라는 이 시점에 이미 대상 앞에 서 있다.
    // 부품이 제 시퀀스를 스스로 돌리므로 맵은 길이만 받아 다음 차례의 시작 시각을 잡는다.
    // 반환값은 "그 자리에 부품이 있었는가"다 — 길이와 따로 답해야 표식이 빈 자리까지 소모하지 않는다.
    // 다만 길이 0을 잣대로 삼지 않는다: 부품이 제자리에 있으면서 0을 돌려주는 길(비활성·미바인딩)이 있어,
    // 그것까지 되풀이하면 진입마다 같은 자리에서 예산만 태우고 영영 걸린다.
    bool TryPlayIntroTarget(IntroTarget _target, out float _length)
    {
        _length = 0f;

        if (!this.HasIntroView(_target)) return false;

        if (_target.IsBand)
        {
            _length = this.m_bands[_target.Index].PlayChapterUnlock();
            return true;
        }

        _length = this.m_nodes[_target.Index].PlayUnlockReveal();
        return true;
    }

    // 사슬의 끝 — 억제를 풀어 진실을 그리고, 실제로 보여준 것만 한 번에 표식한다.
    void EndUnlockIntro()
    {
        // 카메라를 유저에게 돌려주는 것이 사슬의 마지막 의무다 — 빠지면 맵이 영영 끌리지 않는다.
        this.LockScrollForIntro(false);
        this.KillScrollTween();
        this.m_introTraveling = false;

        this.m_introSeq = null;
        this.m_introTargets.Clear();

        // 재생까지 가지 못하고 세우기만 남은 것이 있다 — 풀지 않으면 그 부품만 잠긴 모습으로 굳는다.
        this.ReleaseAllStaged();

        this.m_suspendRefresh = false;
        this.RefreshNodes();
        this.ApplyTutorialAnchor(this.IsOnStage);   // 사슬이 끝난 뒤에야 안내가 선다

        this.CommitUnlockSeen();
    }

    // 어디서 끊겨도 진실로 스냅시킨다 — 스킵도 "봤다"는 의사 표시라 표식은 그대로 남긴다.
    void AbortUnlockIntro()
    {
        // 조기 반환보다 위여야 한다 — 맵의 모든 이탈 경로(OnDisable·Close·Open)가 이 문을 지나므로,
        // 여기서 놓치면 잠금이 남은 채 굳어 맵이 영영 끌리지 않는다.
        this.LockScrollForIntro(false);
        this.KillScrollTween();
        this.m_introTraveling = false;

        // 세우기만 하고 사슬이 서지 못한 경로가 있다 — 그때도 무대는 걷어야 한다.
        if (!this.IsIntroPending && this.m_introSeen.Count == 0 && this.m_introStaged.Count == 0) return;

        Sequence t_seq = this.m_introSeq;
        this.m_introSeq = null;
        if (t_seq != null && t_seq.IsActive()) t_seq.Kill();

        this.m_introTargets.Clear();

        // 반쯤 걷힌 베일·부푼 자물쇠가 그대로 굳지 않게 세운 부품마다 제 안무를 끝으로 당긴다.
        // 재생한 것도 세운 목록에 그대로 남아 있어 이 한 줄이 둘을 함께 걷는다.
        this.ReleaseAllStaged();

        this.m_suspendRefresh = false;
        this.RefreshNodes();
        this.ApplyTutorialAnchor(this.IsOnStage);

        this.CommitUnlockSeen();
    }

    // 보여준 대상만 배치 커밋한다(저장은 한 번만 튄다). 버린 대상은 담지 않는다 — 다음 진입에서 재생돼야 한다.
    void CommitUnlockSeen()
    {
        if (this.m_introSeen.Count == 0) return;

        var t_chapters = new List<int>();
        var t_nodes = new List<int>();

        for (int t_i = 0; t_i < this.m_introSeen.Count; t_i++)
        {
            IntroTarget t_target = this.m_introSeen[t_i];
            if (t_target.IsBand) t_chapters.Add(t_target.Index);
            else t_nodes.Add(t_target.Index);
        }

        this.m_introSeen.Clear();

        TournamentProgress.MarkUnlockSeen(new TournamentUnlockShowcase(t_chapters, t_nodes));
    }

    // 해금 사슬이 훑을 대상 하나. 띠와 정점이 한 줄에 섞여 서므로 병렬 리스트로 흩지 않는다(PathLink와 같은 관용구).
    readonly struct IntroTarget
    {
        public readonly bool IsBand;   // 거짓이면 정점
        public readonly int Index;     // 띠면 챕터 번호, 정점이면 평탄 정점 번호

        public IntroTarget(bool _isBand, int _index)
        {
            IsBand = _isBand;
            Index = _index;
        }
    }

    // 구간 하나의 길 조각. 평탄 인덱스·루트·틴트 대상이 늘 붙어 다녀야 해서 병렬 리스트로 흩지 않는다.
    readonly struct PathLink
    {
        public readonly int Index;            // 정점 Index → Index + 1 사이 구간(평탄 번호)
        public readonly GameObject Root;      // 저작 단위. 위치·회전·크기는 코드가 건드리지 않는다
        public readonly Graphic[] Graphics;   // 색을 입힐 대상(조각 아래 전부). 생성 때 한 번만 캐시한다

        public PathLink(int _index, GameObject _root, Graphic[] _graphics)
        {
            Index = _index;
            Root = _root;
            Graphics = _graphics;
        }
    }
}
