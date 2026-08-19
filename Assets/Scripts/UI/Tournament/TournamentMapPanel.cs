using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 보상 토너먼트 세로 경로 맵(로비 Content를 채우는 탭 패널).
//
// 정점 수·좌표를 전부 코드가 만든다 — 프리팹에 정점을 손으로 박으면 SO 저작이 늘 때 화면이 조용히 어긋난다.
// 씬 전환·탭 이동은 모른다. 도전·뒤로가기를 이벤트로 올리고 LobbyRoot(LobbyMatchLauncher)가 처리한다.
public class TournamentMapPanel : LobbyTabPanel
{
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] RectTransform content;          // 정점이 놓일 Content(레이아웃 그룹 없이 코드가 좌표를 잡는다)
    [SerializeField] TournamentNodeView nodePrefab;
    [SerializeField] Button backButton;

    [Tooltip("정점 사이 세로 간격(px). 정점 수가 늘면 Content 높이가 이 값에서 파생된다.")]
    [SerializeField] float nodeSpacing = 320f;

    [Tooltip("경로가 좌우로 흔들리는 진폭(px). 정점은 중앙에서 ±이 값만큼 번갈아 놓인다. 0이면 일직선.")]
    [SerializeField] float pathAmplitude = 180f;

    [Tooltip("첫 정점 아래 · 마지막 정점 위에 남길 여백(px).")]
    [SerializeField] float edgePadding = 260f;

    [Header("경로 저작(선택)")]
    [Tooltip("경로 포인트의 부모. 자식이 놓인 자리가 곧 정점 자리이고, 형제 순서가 정점 순서다(첫 자식 = 1번 정점).\n" +
             "포인트가 정점 수보다 적으면 남는 정점은 마지막 포인트에서 아래 간격·진폭 공식으로 이어 붙인다.\n" +
             "포인트에 표식 그림을 달아 두면 저작 중에만 보이고 실행할 때 꺼진다.\n" +
             "비우면 전부 공식 배치다.")]
    [SerializeField] RectTransform pathRoot;

    [Header("경로 장식(선택)")]
    [Tooltip("정점 사이를 잇는 선 프리팹. 비우면 선 없이 정점만 놓는다.\n" +
             "가로로 누운 그림을 저작할 것 — 두 정점 사이 거리만큼 폭(width)이 늘고 각도가 돌아간다.")]
    [SerializeField] RectTransform connectorPrefab;

    /// <summary>정점 도전 요청(도전 가능한 정점만 올라온다). LobbyRoot가 전투로 잇는다.</summary>
    public event Action<int> NodeSelected;

    /// <summary>맵을 닫고 매치 탭으로 돌아가는 요청. 탭 이동의 주체는 LobbyRoot다.</summary>
    public event Action BackRequested;

    readonly List<TournamentNodeView> m_nodes = new List<TournamentNodeView>();

    // 저작 포인트를 Content 좌표로 읽어 둔 것. 정점 수보다 적어도 되고, 비어 있으면 전부 공식 배치다.
    readonly List<Vector2> m_points = new List<Vector2>();

    // 이 화면이 만든 것만 추적한다 — Content를 통째로 비우면 저작한 포인트·배경 장식이 함께 지워진다.
    readonly List<GameObject> m_spawned = new List<GameObject>();

    // 정점 생성 여부. 저작 정점 수는 런타임 불변이라 최초 1회만 만들고 이후엔 Refresh로만 갱신한다.
    bool m_built;

    void Awake()
    {
        if (this.backButton != null) this.backButton.onClick.AddListener(this.OnBackClicked);
    }

    void OnDestroy()
    {
        if (this.backButton != null) this.backButton.onClick.RemoveListener(this.OnBackClicked);
    }

    void OnEnable()
    {
        TournamentProgress.OnChanged += this.RefreshNodes;
    }

    void OnDisable()
    {
        TournamentProgress.OnChanged -= this.RefreshNodes;
    }

    public override void OnEnter()
    {
        if (this.m_built) this.RefreshNodes();
        else this.Build();

        this.ScrollToCurrent();
    }

    // 정점을 TournamentProgress.NodeCount만큼 세운다(정점 수는 SO에서 파생 — 상수 하드코딩 금지).
    // 위치는 저작 포인트가 있으면 그 자리, 없으면 공식이다.
    void Build()
    {
        this.m_nodes.Clear();
        if (this.content == null || this.nodePrefab == null) return;

        // 이 화면이 직전에 만든 것만 걷는다. Content를 통째로 비우면 저작한 포인트·배경 장식이 함께 지워진다.
        for (int t_i = 0; t_i < this.m_spawned.Count; t_i++)
            if (this.m_spawned[t_i] != null) Destroy(this.m_spawned[t_i]);
        this.m_spawned.Clear();

        // 목업으로 Content 안에 놓인 원본은 숨기기만 한다(지우면 다음 Build가 0개가 된다).
        if (this.nodePrefab.gameObject.scene.IsValid()) this.nodePrefab.gameObject.SetActive(false);

        this.CollectAuthoredPoints();

        int t_count = TournamentProgress.NodeCount;
        var t_positions = new Vector2[t_count];
        float t_top = 0f;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            t_positions[t_i] = this.PositionOf(t_i);
            if (t_positions[t_i].y > t_top) t_top = t_positions[t_i].y;
        }

        // 높이는 가장 높은 정점에서 파생한다 — 저작 포인트가 공식 간격을 따르지 않아도 맞는다.
        this.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, t_top + this.edgePadding);

        // 연결선을 먼저 세운다 — 나중에 붙는 정점이 자연히 그 위에 온다(형제 순서 = 그리는 순서).
        this.BuildConnectors(t_positions);

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            TournamentNodeView t_node = Instantiate(this.nodePrefab, this.content);
            t_node.gameObject.SetActive(true);   // 목업 원본을 숨겼을 수 있다 — 사본은 항상 보이게.
            this.Place(t_node.transform as RectTransform, t_positions[t_i]);
            t_node.Bind(t_i, this.OnNodeTapped);
            this.m_nodes.Add(t_node);
            this.m_spawned.Add(t_node.gameObject);
        }

        // 정점이 하나도 안 나왔으면(설정 미주입 등) 다음 진입에서 다시 시도한다 — 빈 맵으로 세션 내내 고착되지 않게.
        this.m_built = t_count > 0;
    }

    // 저작 포인트를 Content 좌표(가로=중앙 기준, 세로=바닥 기준)로 읽고 표식은 끈다.
    // Content pivot이 바닥(0.5, 0)이라는 전제다 — 높이가 변해도 바닥이 안 움직여 좌표가 흔들리지 않는다.
    void CollectAuthoredPoints()
    {
        this.m_points.Clear();
        if (this.pathRoot == null) return;

        for (int t_i = 0; t_i < this.pathRoot.childCount; t_i++)
        {
            Transform t_point = this.pathRoot.GetChild(t_i);
            this.m_points.Add(this.content.InverseTransformPoint(t_point.position));
            t_point.gameObject.SetActive(false);   // 저작용 표식이라 실행 화면에는 남기지 않는다
        }
    }

    // 정점 사이 연결선.
    void BuildConnectors(Vector2[] _positions)
    {
        if (this.connectorPrefab == null) return;

        for (int t_i = 0; t_i < _positions.Length - 1; t_i++)
        {
            Vector2 t_from = _positions[t_i];
            Vector2 t_to = _positions[t_i + 1];
            Vector2 t_delta = t_to - t_from;

            RectTransform t_line = Instantiate(this.connectorPrefab, this.content);
            t_line.gameObject.SetActive(true);
            this.Place(t_line, (t_from + t_to) * 0.5f);
            t_line.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, t_delta.magnitude);
            t_line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(t_delta.y, t_delta.x) * Mathf.Rad2Deg);
            this.m_spawned.Add(t_line.gameObject);
        }
    }

    // 정점 i의 좌표. 저작 포인트가 있으면 그 자리를 그대로 쓰고, 모자라면 마지막 포인트에서 공식으로 이어 붙인다.
    // y는 Content 바닥 기준이라 인덱스 0이 맨 아래다 — 경로가 위로 "올라가는" 것으로 읽히게(레퍼런스가 아래에서 위로 오른다).
    Vector2 PositionOf(int _index)
    {
        if (_index < this.m_points.Count) return this.m_points[_index];

        float t_x = (_index & 1) == 0 ? -this.pathAmplitude : this.pathAmplitude;

        if (this.m_points.Count == 0)
            return new Vector2(t_x, this.edgePadding + this.nodeSpacing * _index);

        Vector2 t_last = this.m_points[this.m_points.Count - 1];
        return new Vector2(t_x, t_last.y + this.nodeSpacing * (_index - this.m_points.Count + 1));
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

    // 진행 통지 → 전 정점 재바인딩. 재빌드가 아니라 Refresh라 스크롤 위치가 보존된다.
    void RefreshNodes()
    {
        for (int t_i = 0; t_i < this.m_nodes.Count; t_i++)
            if (this.m_nodes[t_i] != null) this.m_nodes[t_i].Refresh();
    }

    // 지금 도전할 정점을 화면 중앙에 둔다. 전부 클리어(-1)면 마지막 정점.
    void ScrollToCurrent()
    {
        int t_index = TournamentProgress.CurrentNodeIndex;
        if (t_index < 0) t_index = this.m_nodes.Count - 1;

        this.ScrollToNode(t_index);
    }

    // 인덱스 비례가 아니라 정점의 실제 y로 계산한다(지그재그·여백 때문에 비례식이 안 맞는다).
    // 생성 직후 프레임은 레이아웃이 서 있지 않아 rect가 0이므로 강제로 갱신한 뒤에 읽는다.
    void ScrollToNode(int _index)
    {
        if (this.scrollRect == null || this.content == null) return;
        if (_index < 0 || _index >= this.m_nodes.Count) return;

        RectTransform t_node = this.m_nodes[_index] != null ? this.m_nodes[_index].transform as RectTransform : null;
        if (t_node == null) return;

        Canvas.ForceUpdateCanvases();

        RectTransform t_viewport = this.scrollRect.viewport != null
            ? this.scrollRect.viewport
            : this.scrollRect.transform as RectTransform;
        if (t_viewport == null) return;

        float t_scrollable = this.content.rect.height - t_viewport.rect.height;
        if (t_scrollable <= 0f) return;   // Content가 화면보다 짧다 = 스크롤할 여백이 없다

        float t_offset = t_node.anchoredPosition.y - t_viewport.rect.height * 0.5f;
        this.scrollRect.verticalNormalizedPosition = Mathf.Clamp01(t_offset / t_scrollable);
    }

    // 정점 뷰가 이미 잠긴 버튼을 죽여 두지만, 진입 판정의 주인은 화면이다(저작·상태가 갈려도 새지 않게).
    void OnNodeTapped(int _index)
    {
        if (TournamentProgress.StateOf(_index) != ETournamentNodeState.Playable) return;

        this.NodeSelected?.Invoke(_index);
    }

    void OnBackClicked() => this.BackRequested?.Invoke();
}
