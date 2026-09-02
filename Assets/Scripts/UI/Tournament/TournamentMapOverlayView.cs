using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 모험 세로 경로 맵(로비 위를 덮는 전체 화면 오버레이).
//
// 좌표의 진실원은 챕터 타일 프리팹이다 — 정점 자리도 길 조각도 디자이너가 타일 안에 저작하고,
// 코드는 타일을 쌓고 저작된 자리에 정점을 놓고 길 조각을 상태에 따라 틴트할 뿐 좌표를 만들지 않는다.
// 자기 여닫음은 스스로 소유하고, 씬 전환만 모른다 — 도전을 이벤트로 올리면 LobbyRoot(LobbyMatchLauncher)가 잇는다.
public class TournamentMapOverlayView : MonoBehaviour
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

    // 수령 왕복이 도는 중(서버 응답을 아직 못 받았다). 팝업이 응답보다 먼저 닫혀도 억제를 풀지 않게 막는다 —
    // 풀면 아직 미수령인 정점이 한 박 드러났다가 곧바로 도장에 덮인다.
    bool m_awaitingClaim;

    // 도장이 꽂히는 중인 정점(없으면 -1). 팝업의 [획득]이 있던 자리에 그 정점이 그대로 서 있어,
    // 여벌 탭 하나가 재도전 전투로 새면 사슬이 통째로 잘린다.
    int m_claimNode = -1;

    // 지금 점등 트윈이 물들이는 중인 구간(없으면 -1). RefreshLinks가 이 한 칸만 건너뛴다 —
    // 상태를 먼저 커밋하면 길도 같은 프레임에 금색이 돼 차오르는 연출이 통째로 사라진다.
    int m_litLink = -1;

    Sequence m_claimSeq;

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

        this.ApplyTutorialAnchor(true);   // Build 경로는 RefreshNodes를 거치지 않는다
        this.ScrollToCurrent();
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
        if (this.m_claimSeq != null) return;             // 다른 정점의 수령 연출이 아직 돌고 있다

        int t_index = TournamentProgress.IndexOf(_nodeId);
        if (t_index < 0 || t_index >= this.m_nodes.Count) return;
        if (!TournamentProgress.IsRewardPending(t_index)) return;

        // 도장이 그 자리에 꽂히므로(서버가 클리어를 확정하는 프레임), 그 자리가 화면 안에 있어야 결말이 보인다.
        this.ScrollToNode(t_index);

        this.OpenNodeReward(t_index);
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
        {
            if (this.m_bands[t_i] == null) continue;
            if (!(this.m_bands[t_i].transform is RectTransform t_rect)) continue;

            this.ScrollToContentY(t_rect.anchoredPosition.y);
            return true;
        }

        return false;
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
    void ScrollToContentY(float _y)
    {
        if (this.scrollRect == null || this.content == null) return;

        Canvas.ForceUpdateCanvases();

        RectTransform t_viewport = this.scrollRect.viewport != null
            ? this.scrollRect.viewport
            : this.scrollRect.transform as RectTransform;
        if (t_viewport == null) return;

        float t_scrollable = this.content.rect.height - t_viewport.rect.height;
        if (t_scrollable <= 0f) return;   // Content가 화면보다 짧다 = 스크롤할 여백이 없다

        float t_offset = _y - t_viewport.rect.height * 0.5f;
        this.scrollRect.verticalNormalizedPosition = Mathf.Clamp01(t_offset / t_scrollable);
    }

    // 정점 뷰가 이미 잠긴 버튼을 죽여 두지만, 진입 판정의 주인은 화면이다(저작·상태가 갈려도 새지 않게).
    void OnNodeTapped(int _index)
    {
        // 길 점등은 관문이 아니다(상태가 이미 진실이라 다음 정점은 눌러도 된다).
        // 다만 도장이 꽂히는 중인 그 정점만은 막는다.
        if (this.m_claimSeq != null && _index == this.m_claimNode) return;

        // 선물은 도전이 아니라 수령이다 — 전투로 잇지 않고 보상 팝업으로 간다.
        if (TournamentProgress.IsRewardPending(_index))
        {
            this.OpenNodeReward(_index);
            return;
        }

        if (!TournamentProgress.CanEnter(_index)) return;

        this.NodeSelected?.Invoke(_index);
    }

    // 수령 → 점등 → 해금. 억제를 팝업보다 먼저 걸어야, 서버 채택이 보내는 통지가 결말을 앞질러 그리지 않는다.
    void OpenNodeReward(int _index)
    {
        if (!TournamentProgress.TryGetNode(_index, out TournamentNodeDef t_node)) return;

        this.m_suspendRefresh = true;
        this.m_awaitingClaim  = true;

        // 결말은 팝업이 걷힐 때가 아니라 서버가 클리어를 확정한 프레임에 시작한다(_onClaimed).
        // _onClosed는 수령 없이 닫힌 경로의 안전망일 뿐이라, 이미 시작한 사슬을 끊지 않는 AbortIfIdle이 받는다.
        // false는 수령을 시작조차 못 한 경우뿐이라 걸어 둔 억제만 푼다.
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
        this.m_awaitingClaim = false;

        // 맵을 떠난 뒤 콜백이 도착했다 — 다음 진입에서 진실이 그려진다.
        if (!this.IsOpen)
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

        this.RevealClear(_index);

        this.m_claimNode = _index;
        this.m_claimSeq = DOTween.Sequence().SetLink(this.gameObject);

        // 도장과 첫 길 점이 같은 프레임에서 출발한다 — 상태가 바뀌는 것과 길이 차는 것이 한 사건이어야 한다.
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

    // 억제를 풀어 진실을 그리고, 그 프레임에 도장이 꽂힌다(서버가 클리어를 확정한 바로 그 프레임이다).
    void RevealClear(int _index)
    {
        this.m_suspendRefresh = false;
        this.RefreshNodes();

        if (_index >= 0 && _index < this.m_nodes.Count) this.m_nodes[_index]?.PlayClearStamp();
    }

    // 사슬의 끝 — 길이 다 찬 자리에서 다음 정점이 튄다.
    // 상태는 이미 도장이 꽂히던 프레임에 열렸고, 이 펀치는 "여기가 다음이다"를 가리키는 손짓이다.
    void PunchNext(int _index)
    {
        int t_next = _index + 1;
        if (t_next < 0 || t_next >= this.m_nodes.Count) return;

        // 챕터가 랭크로 잠겨 있으면 튀지 않는다 — 자물쇠가 튀면 "열렸다"는 거짓말이 된다.
        if (TournamentProgress.IsRankLocked(t_next)) return;

        this.m_nodes[t_next]?.PlayUnlockPunch();
    }

    // 어디서 끊겨도 진실로 스냅시킨다 — 연출은 장식일 뿐이라 중간 색에서 굳는 것만 막으면 된다.
    void AbortClaimSequence()
    {
        if (this.m_claimSeq != null && this.m_claimSeq.IsActive()) this.m_claimSeq.Kill();
        this.m_claimSeq = null;
        this.m_claimNode = -1;
        this.m_awaitingClaim = false;

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

    // 수령 없이 팝업이 닫힌 경로의 안전망. 결말은 이제 팝업이 아니라 서버 응답이 데려오므로,
    // 사슬이 이미 시작됐거나 왕복이 아직 도는 중이면 손대지 않는다 —
    // 여기서 무조건 걷으면 아직 미수령인 정점이 한 박 드러났다가 뒤늦은 도장에 덮인다.
    void AbortIfIdle()
    {
        if (this.m_awaitingClaim || this.m_claimSeq != null || !this.m_suspendRefresh) return;

        this.AbortClaimSequence();
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
