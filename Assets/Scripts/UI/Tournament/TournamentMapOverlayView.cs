using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 보상 토너먼트 세로 경로 맵(로비 위를 덮는 전체 화면 오버레이).
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

    /// <summary>정점 도전 요청(도전 가능한 정점만 올라온다). LobbyRoot가 전투로 잇는다.</summary>
    public event Action<int> NodeSelected;

    /// <summary>맵이 화면에 떠 있는가.</summary>
    public bool IsOpen => this.gameObject.activeInHierarchy;

    // 타일 안에서 정점 자리·길 조각을 담고 있는 자식 이름(타일 프리팹 규약)
    const string PATH_ROOT_NAME = "PathRoot";
    const string LINK_ROOT_NAME = "LinkRoot";
    const string SEAM_FOG_NAME  = "SeamFog";

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
    }

    void OnDisable()
    {
        TournamentProgress.OnChanged -= this.RefreshNodes;

        LobbyShellBars.Show(this);            // 씬 이탈로 잘려도 바가 걷힌 채 굳지 않게
        this.transition.HandleDisabled(this.gameObject);
    }

    /// <summary>맵을 연다. 정점 세우기·스크롤은 활성화 뒤에 돈다 — rect가 0이면 스크롤 계산이 깨진다.</summary>
    public void Open()
    {
        if (this.IsOpen) return;

        this.transition.SetVisible(this.gameObject, true);

        if (this.m_built) this.RefreshNodes();
        else this.Build();

        this.ScrollToCurrent();
    }

    /// <summary>맵을 닫는다. 하단바는 퇴장 트윈과 나란히 돌려준다 — OnDisable을 기다리면 늦는다.</summary>
    public void Close()
    {
        LobbyShellBars.Show(this);
        this.transition.SetVisible(this.gameObject, false);
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

        // 여백을 준 만큼 첫 타일 아랫변이 하늘에 드러난다 — 이음매와 같은 구름으로 덮어 맵을 닫는다.
        this.BuildBottomCap(this.BottomInset);

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

    // 맵의 아랫변(첫 타일 아랫변)을 이음매와 같은 구름으로 닫는다. 그림은 타일이 저작한 SeamFog에서 빌린다 —
    // 여기서 따로 스프라이트를 물리면 타일별로 안개가 갈릴 때 이 한 장만 옛 그림으로 남는다.
    // 첫 타일에는 SeamFog가 없으므로(맵의 아랫변은 이음매가 아니다) 챕터가 하나뿐이면 조용히 건너뛴다.
    void BuildBottomCap(float _bottom)
    {
        if (_bottom <= 0f) return;   // 여백이 없으면 아랫변이 하단 안개 밑에 그대로 잠긴다(예전과 같다)

        Image t_source = null;
        Vector3 t_tileScale = Vector3.one;

        for (int t_i = 0; t_i < this.m_spawned.Count && t_source == null; t_i++)
        {
            if (this.m_spawned[t_i] == null) continue;

            Transform t_fog = this.m_spawned[t_i].transform.Find(SEAM_FOG_NAME);
            if (t_fog == null) continue;

            t_source = t_fog.GetComponent<Image>();

            // 원본은 폭 맞춤으로 스케일된 타일 '안'에 있고 이 한 장은 Content 직속이다 —
            // 타일 스케일을 함께 가져와야 끝 구름만 다른 크기로 뜨지 않는다.
            t_tileScale = this.m_spawned[t_i].transform.localScale;
        }

        if (t_source == null || t_source.sprite == null) return;

        var t_cap = new GameObject("BottomCapFog", typeof(RectTransform), typeof(Image));
        var t_rect = (RectTransform)t_cap.transform;
        t_rect.SetParent(this.content, false);

        var t_image = t_cap.GetComponent<Image>();
        t_image.sprite = t_source.sprite;
        t_image.color = t_source.color;
        t_image.raycastTarget = false;   // 구름 뒤 정점을 눌러야 한다

        this.Place(t_rect, new Vector2(0f, _bottom));
        t_rect.sizeDelta = ((RectTransform)t_source.transform).sizeDelta;
        t_rect.localScale = t_tileScale;

        this.m_spawned.Add(t_cap);
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
        for (int t_i = 0; t_i < this.m_nodes.Count; t_i++)
            if (this.m_nodes[t_i] != null) this.m_nodes[t_i].Refresh();

        for (int t_i = 0; t_i < this.m_bands.Count; t_i++)
            if (this.m_bands[t_i] != null) this.m_bands[t_i].Refresh();

        this.RefreshLinks();
    }

    // 지금 도전할 정점을 화면 중앙에 둔다.
    // 전부 클리어(-1)면 마지막 챕터 띠로 — 끝 표지와 마지막 완주 보상이 거기 서 있다(정점 위가 아니다).
    void ScrollToCurrent()
    {
        int t_index = TournamentProgress.CurrentNodeIndex;
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
        if (!TournamentProgress.CanEnter(_index)) return;

        this.NodeSelected?.Invoke(_index);
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
