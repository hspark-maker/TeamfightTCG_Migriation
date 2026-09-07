using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>로비 셸에서 걷을 수 있는 바. 화면마다 걷는 범위가 달라 요청에 실어 보낸다.</summary>
[System.Flags]
public enum EShellBars
{
    None   = 0,
    Top    = 1 << 0,
    Bottom = 1 << 1,
    All    = Top | Bottom,
}

/// <summary>로비 셸의 상단바·하단탭바를 잠시 걷는 공용 창구.
///
/// 요청은 owner 키로 쌓이고 걷히는 범위는 **요청들의 합집합**이다 — 하나라도 남아 있으면
/// 그 바는 걷힌 채다. 겹쳐 뜨는 화면이 서로의 복원을 잡아먹어 "바가 사라진 채 굳는" 상태를 막는다.
///
/// SetActive로 끄지 않는다. 끄면 그 프레임에 레이아웃이 튀므로 알파로 지우고 입력만 막는다.
///
/// 상단바를 풀 오버레이 위로 올리는 <see cref="LiftTop"/>도 여기 있다 — 바를 찾는 길이 이미 여기 있어서다.
/// 다만 걷기(알파)와 올리기(정렬)는 축이 달라 요청 스택을 따로 쥔다.</summary>
public static class LobbyShellBars
{
    const float FADE_SECONDS = 0.18f;

    sealed class Request
    {
        public object owner;
        public EShellBars bars;
    }

    static readonly List<Request> s_requests = new List<Request>();
    static CanvasGroup s_top;
    static CanvasGroup s_bottom;
    // 지금 실제로 걷혀 있는 범위. 같은 상태를 다시 걸어 트윈이 재시작되지 않게 한다.
    static EShellBars s_applied;

    sealed class LiftRequest
    {
        public object owner;
        // 예약된 되돌리기(DropTopAfter)가 그 사이 다시 열린 화면의 승격을 거두지 않게 하는 표.
        public int ticket;
    }

    // 상단바 승격을 붙들고 있는 주인들. 걷기 요청(s_requests)과 축이 달라 서로 간섭하지 않는다.
    static readonly List<LiftRequest> s_liftOwners = new List<LiftRequest>();
    static int s_nextLiftTicket;
    static Canvas s_liftedTop;

    // 승격 동안 레이캐스트를 막아 둔 상단바. 막기 전의 값은 저장하지 않는다 — 되돌릴 때 걷기 요청에서 다시 센다.
    static CanvasGroup s_liftedTopGroup;

    /// <summary>_context는 로비 계층 안의 아무 노드나 준다 — 여기서 LobbyRoot를 거슬러 찾는다.
    /// 셸 밖(탭을 단독 배치한 테스트 씬)이면 조용히 아무 일도 하지 않는다.</summary>
    public static void Hide(object _owner, Transform _context, EShellBars _bars = EShellBars.All)
    {
        if (_owner == null || _bars == EShellBars.None) return;

        Prune();
        RemoveOwner(_owner);   // 같은 주인이 범위를 바꿔 다시 요청할 수 있다
        s_requests.Add(new Request { owner = _owner, bars = _bars });

        Bind(_context);
        Apply();
    }

    /// <summary>죽은 요청을 걷고 지금 상태를 다시 적용한다. 로비에 들어설 때 한 번 부른다 —
    /// 요청을 흘린 화면이 있어도 로비를 다시 밟으면 바가 되살아난다(안 그러면 재시작 말고는 길이 없다).</summary>
    public static void Refresh()
    {
        Prune();
        Apply();

        // 승격 축도 같이 회수한다 — 이쪽에만 회수가 없으면 되돌리기를 흘린 화면 하나에 상단바가
        // 310 층에 입력이 죽은 채로 남고 로비를 다시 밟아도 풀리지 않는다.
        ReclaimLifts();
        ApplyLift(null);
    }

    public static void Show(object _owner)
    {
        if (_owner == null) return;

        RemoveOwner(_owner);
        Prune();
        Apply();
    }

    /// <summary>상단바를 풀 오버레이 위(<see cref="UiSortingOrder.LobbyBarsLifted"/>)로 올린다.
    /// 풀에서 여는 화면(<see cref="UiSortingOrder.PooledOverlay"/>)이 로비 캔버스를 통째로 덮어
    /// 재화 표시가 사라지는 동안만 쓴다 — 화면을 닫을 때 <see cref="DropTop"/>으로 반드시 되돌린다.
    ///
    /// <b>보이게만 하고 누르게는 하지 않는다.</b> 승격은 그림과 함께 입력도 끌어올린다(GraphicRaycaster가 붙고,
    /// overrideSorting이 조상 CanvasGroup의 레이캐스트 필터를 끊는다) — 그대로 두면 위에 뜬 화면이 막으려던 입력이
    /// 상단바를 통해 뚫린다. 그래서 승격 동안 상단바의 <see cref="CanvasGroup.blocksRaycasts"/>만 내리고
    /// 되돌릴 때 원래 값으로 복원한다(interactable은 건드리지 않는다 — 그 값은 버튼을 비활성 룩으로 다시 그린다).
    ///
    /// _context는 로비 계층 안팎 아무 노드나 준다(<see cref="Hide"/>와 같은 경로로 LobbyRoot를 찾는다).</summary>
    public static void LiftTop(object _owner, Transform _context)
    {
        if (_owner == null) return;

        PruneLifts();
        RemoveLiftOwner(_owner);
        s_liftOwners.Add(new LiftRequest { owner = _owner, ticket = ++s_nextLiftTicket });

        ApplyLift(_context);
    }

    /// <summary>승격 요청을 거둔다. 마지막 주인이 빠질 때만 상단바가 로비 캔버스 정렬로 돌아간다.</summary>
    public static void DropTop(object _owner)
    {
        if (_owner == null) return;

        RemoveLiftOwner(_owner);
        PruneLifts();

        ApplyLift(null);
    }

    /// <summary>_seconds 뒤에 <see cref="DropTop"/>한다. 퇴장 연출이 끝난 뒤에 상단바를 내리려는 화면용이다 —
    /// 닫는 순간 바로 내리면 판이 아직 보이는 동안 상단바가 그 뒤로 사라진다.
    ///
    /// 기다리는 동안 같은 주인이 다시 <see cref="LiftTop"/>하면 이 예약은 무효가 된다(재개봉이 이긴다).</summary>
    public static void DropTopAfter(object _owner, float _seconds)
    {
        if (_owner == null) return;

        int t_ticket = TicketOf(_owner);
        if (t_ticket == 0) return;   // 승격 중이 아니면 예약할 것도 없다

        if (_seconds <= 0f)
        {
            DropTop(_owner);

            return;
        }

        DropTopAfterAsync(_owner, _seconds, t_ticket).Forget();
    }

    static async UniTaskVoid DropTopAfterAsync(object _owner, float _seconds, int _ticket)
    {
        // 짝이 되는 퇴장 트윈(PopupTransition)이 SetUpdate 없이 스케일 시간으로 도므로 같은 축으로 센다.
        // 트윈을 무시간 축으로 옮기는 쪽이 아니라 여기를 맞춘 이유: PopupTransition은 여러 화면이 공유해
        // 그쪽을 바꾸면 이 예약과 무관한 화면들의 연출 속도까지 함께 바뀐다.
        await UniTask.Delay(Mathf.CeilToInt(_seconds * 1000f));

        if (TicketOf(_owner) != _ticket) return;

        DropTop(_owner);
    }

    static int TicketOf(object _owner)
    {
        for (int t_i = 0; t_i < s_liftOwners.Count; t_i++)
            if (ReferenceEquals(s_liftOwners[t_i].owner, _owner)) return s_liftOwners[t_i].ticket;

        return 0;
    }

    static void ApplyLift(Transform _context)
    {
        if (s_liftOwners.Count == 0)
        {
            UiSortingOrder.DropNested(s_liftedTop);
            s_liftedTop = null;
            ReleaseTopInput();

            return;
        }

        if (s_liftedTop != null) return;

        Transform t_root = FindLobbyRoot(_context);
        if (t_root == null) return;   // 로비가 없는 씬이면 조용히 아무 일도 하지 않는다

        Transform t_top = FindBar(t_root, "TopBar");
        if (t_top == null) return;

        s_liftedTop = UiSortingOrder.LiftNested(t_top.gameObject, UiSortingOrder.LobbyBarsLifted);
        MuteTopInput(t_top);
    }

    // interactable은 입력만 막는 값이 아니다 — 하위 Selectable을 disabledColor로 다시 그린다(Button만 회색이 되어
    // "일부만 흐려진 상단바"가 된다). 그림을 바꾸지 않고 입력만 막는 값은 blocksRaycasts 하나뿐이다.
    static void MuteTopInput(Transform _top)
    {
        CanvasGroup t_group = EnsureGroup(_top);
        if (t_group == null) return;

        s_liftedTopGroup       = t_group;
        t_group.blocksRaycasts = false;
    }

    // 복원값은 저장해 두지 않고 그 시점의 걷기 요청에서 다시 센다. 저장하면 두 갈래로 어긋난다:
    // 이미 걷힌(=false) 동안 승격이 걸리면 저장값이 false로 박혀 걷기가 풀린 뒤에도 안 눌리고,
    // 캐시(s_applied)로 보정하면 이전 로비 세션의 값이 남아 보이는데 안 눌리는 바가 굳는다(되살릴 경로가 없다).
    static void ReleaseTopInput()
    {
        CanvasGroup t_group = s_liftedTopGroup;
        s_liftedTopGroup = null;

        if (t_group == null) return;   // 씬 전환으로 이미 파괴됐다

        Prune();
        t_group.blocksRaycasts = (HiddenNow() & EShellBars.Top) == 0;
    }

    static void RemoveLiftOwner(object _owner)
    {
        for (int t_i = s_liftOwners.Count - 1; t_i >= 0; t_i--)
            if (ReferenceEquals(s_liftOwners[t_i].owner, _owner)) s_liftOwners.RemoveAt(t_i);
    }

    // 로비를 다시 밟을 때만 도는 회수. root만 토글하는 화면은 닫혀도 자기 오브젝트가 켜진 채라
    // PruneLifts에 걸리지 않으므로, 여기서 풀의 표시 상태로 한 번 더 걷는다.
    // 평소 경로(LiftTop·DropTop)에 넣지 않는 이유: 퇴장 연출을 기다리는 동안 isShow는 이미 false다.
    static void ReclaimLifts()
    {
        PruneLifts();

        for (int t_i = s_liftOwners.Count - 1; t_i >= 0; t_i--)
            if (s_liftOwners[t_i].owner is PooledUIBase t_pooled && !t_pooled.isShow) s_liftOwners.RemoveAt(t_i);
    }

    // Prune과 같은 이유다 — 풀드 UI는 파괴되지 않고 꺼지기만 하므로, 꺼진 화면이 승격을 붙들면
    // 상단바가 다른 화면에서도 풀 오버레이 위에 남는다.
    static void PruneLifts()
    {
        for (int t_i = s_liftOwners.Count - 1; t_i >= 0; t_i--)
        {
            object t_raw = s_liftOwners[t_i].owner;

            if (t_raw is Object t_owner && t_owner == null) { s_liftOwners.RemoveAt(t_i); continue; }
            if (t_raw is Behaviour t_behaviour && !t_behaviour.isActiveAndEnabled) s_liftOwners.RemoveAt(t_i);
        }
    }

    static void Bind(Transform _context)
    {
        if (s_top != null || s_bottom != null) return;   // 이미 물려 있다

        // 새로 물리는 바는 펼쳐진 상태에서 시작한다 — 씬이 바뀌었으면 이전 씬의 적용 상태는 뜻이 없다.
        s_applied = EShellBars.None;
        if (_context == null) return;

        Transform t_root = FindLobbyRoot(_context);
        if (t_root == null) return;

        s_top    = EnsureGroup(FindBar(t_root, "TopBar"));
        s_bottom = EnsureGroup(FindBar(t_root, "BottomBar"));
    }

    // 저작본이 사본 이름(TopBar (1))을 달고 있어, 정확한 일치로만 찾으면 그 바의 제어가 통째로 불발한다.
    static Transform FindBar(Transform _root, string _name)
    {
        Transform t_exact = _root.Find(_name);
        if (t_exact != null) return t_exact;

        for (int t_i = 0; t_i < _root.childCount; t_i++)
        {
            Transform t_child = _root.GetChild(t_i);
            if (IsCopyName(t_child.name, _name)) return t_child;
        }

        return null;
    }

    // 유니티가 붙이는 사본 접미사("이름 (숫자)")만 같은 바로 친다 — 접두 일치까지 받으면
    // TopBarShadow 같은 형제가 생기는 순간 엉뚱한 노드를 집는다.
    static bool IsCopyName(string _childName, string _name)
    {
        if (_childName.Length < _name.Length + 4) return false;
        if (!_childName.StartsWith(_name, System.StringComparison.Ordinal)) return false;
        if (_childName[_name.Length] != ' ' || _childName[_name.Length + 1] != '(') return false;
        if (_childName[_childName.Length - 1] != ')') return false;

        for (int t_i = _name.Length + 2; t_i < _childName.Length - 1; t_i++)
            if (!char.IsDigit(_childName[t_i])) return false;

        return true;
    }

    /// <summary>요청자가 늘 LobbyRoot 안에 있지는 않다 — 탭 콘텐츠는 자손이지만
    /// SafeArea 직속 오버레이는 형제이고, <b>풀드 UI(카드 상세)는 아예 다른 캔버스</b>다
    /// (UIPoolManager 캔버스는 DontDestroyOnLoad라 로비 캔버스 계층 밖이다).
    /// 위로 훑고 → 자기 캔버스에서 내려찾고 → 그래도 없으면 로드된 씬 전체를 훑는다.</summary>
    static Transform FindLobbyRoot(Transform _context)
    {
        if (_context == null) return FindInLoadedScenes();   // 되돌리기 경로는 문맥 없이 부를 수 있다

        for (Transform t_node = _context; t_node != null; t_node = t_node.parent)
            if (t_node.name == "LobbyRoot") return t_node;

        Transform t_canvas = _context.root;
        Transform t_fast   = t_canvas.Find("SafeArea/LobbyRoot");
        if (t_fast != null) return t_fast;

        Transform t_local = FindDescendant(t_canvas, "LobbyRoot");
        if (t_local != null) return t_local;

        return FindInLoadedScenes();
    }

    /// <summary>로드된 씬들의 루트를 훑어 LobbyRoot를 찾는다. Bind 한 번당 최대 1회만 도는 경로다
    /// (물린 뒤에는 Bind가 즉시 반환한다) — GetRootGameObjects의 할당을 매 프레임 치르지 않는다.
    /// 전투 씬처럼 로비가 없는 곳에서는 null이고, 그때는 조용히 아무 일도 하지 않는다.</summary>
    static Transform FindInLoadedScenes()
    {
        for (int t_i = 0; t_i < SceneManager.sceneCount; t_i++)
        {
            Scene t_scene = SceneManager.GetSceneAt(t_i);
            if (!t_scene.isLoaded) continue;

            GameObject[] t_roots = t_scene.GetRootGameObjects();
            for (int t_r = 0; t_r < t_roots.Length; t_r++)
            {
                Transform t_found = FindDescendant(t_roots[t_r].transform, "LobbyRoot");
                if (t_found != null) return t_found;
            }
        }

        return null;
    }

    static Transform FindDescendant(Transform _node, string _name)
    {
        if (_node.name == _name) return _node;

        for (int t_i = 0; t_i < _node.childCount; t_i++)
        {
            Transform t_found = FindDescendant(_node.GetChild(t_i), _name);
            if (t_found != null) return t_found;
        }

        return null;
    }

    static CanvasGroup EnsureGroup(Transform _bar)
    {
        if (_bar == null) return null;

        CanvasGroup t_group = _bar.GetComponent<CanvasGroup>();

        return t_group != null ? t_group : _bar.gameObject.AddComponent<CanvasGroup>();
    }

    // 합집합이 아니라 **가장 마지막 요청**이 이긴다 — 위에 뜬 화면이 아래 화면보다 적게 걷을 수 있어야
    // "페이지 오버레이는 둘 다 걷고, 그 위 상세 화면은 상단바를 되돌린다"가 성립한다.
    static EShellBars HiddenNow()
        => s_requests.Count > 0 ? s_requests[s_requests.Count - 1].bars : EShellBars.None;

    static void Apply()
    {
        EShellBars t_hidden = HiddenNow();

        ApplyTo(s_top,    (t_hidden & EShellBars.Top)    != 0, (s_applied & EShellBars.Top)    != 0, s_liftedTopGroup != null);
        ApplyTo(s_bottom, (t_hidden & EShellBars.Bottom) != 0, (s_applied & EShellBars.Bottom) != 0);

        s_applied = t_hidden;

        // 다 돌려준 뒤에는 참조를 놓는다 — 씬이 바뀌어도 파괴된 바를 붙들고 있지 않게
        if (t_hidden != EShellBars.None) return;
        s_top    = null;
        s_bottom = null;
    }

    static void ApplyTo(CanvasGroup _group, bool _hide, bool _wasHidden, bool _muteInput = false)
    {
        if (_group == null) return;   // 씬 전환으로 이미 파괴됐다

        // 레이캐스트 축과 표시 축은 다르다. 승격 중인 바는 펼쳐도 레이캐스트를 되돌려 받지 않지만(LiftTop 참조),
        // interactable은 하위 Selectable을 비활성 룩으로 다시 그리므로 승격이 건드리지 않는다 —
        // 걷힘(_hide)일 때만 내린다(그때는 알파가 0이라 그 룩이 보이지 않는다).
        bool t_raycast      = !_hide && !_muteInput;
        bool t_interactable = !_hide;

        // 되돌리는 방향은 캐시를 믿지 않는다. s_applied는 static이라 씬이 다시 로드돼도 살아남고,
        // 트윈이 중간에 잘리면 알파만 되돌아오고 blocksRaycasts는 false로 남는다 —
        // 그러면 바가 **보이는데 안 눌리는** 상태로 굳고, 캐시가 "이미 펼침"이라 믿어 아무도 고치지 않는다.
        // 펼치기는 멱등하므로 매번 확인해도 비용이 없다(값이 이미 맞으면 트윈도 걸지 않는다).
        if (!_hide)
        {
            bool t_alreadyOpen = _group.blocksRaycasts == t_raycast
                                 && _group.interactable == t_interactable
                                 && _group.alpha >= 0.999f;
            if (t_alreadyOpen) return;
        }
        else if (_hide == _wasHidden) return;

        // 걷힌 바 위로 손가락이 지나가도 버튼이 눌리면 안 된다.
        _group.blocksRaycasts = t_raycast;
        _group.interactable   = t_interactable;

        float t_target = _hide ? 0f : 1f;

        _group.DOKill();

        // 트윈 주인은 요청자가 아니라 바다 — 요청자에 SetLink를 걸면 그 오브젝트가 꺼지는 순간
        // 트윈이 같이 죽어 바가 걷힌 채 굳는다.
        if (!_group.gameObject.activeInHierarchy)
        {
            _group.alpha = t_target;

            return;
        }

        _group.DOFade(t_target, FADE_SECONDS)
              .SetUpdate(true)   // 결과창 등에서 timeScale이 눌려도 UI 전환은 같은 속도로 돈다
              .SetLink(_group.gameObject);
    }

    static void RemoveOwner(object _owner)
    {
        for (int t_i = s_requests.Count - 1; t_i >= 0; t_i--)
            if (ReferenceEquals(s_requests[t_i].owner, _owner)) s_requests.RemoveAt(t_i);
    }

    // 요청자가 Show 없이 사라지면 바가 영영 걷힌 채 남는다 — 호출마다 걷어낸다.
    //
    // 파괴만으로는 부족하다. 풀드 UI는 파괴되지 않고 비활성으로만 남으므로(UIPoolManager가 재사용한다)
    // 파괴 검사에 걸리지 않는다 — 꺼진 화면이 바를 걷은 채 붙들고 있으면 아무도 그 요청을 회수하지 못한다.
    // 지금 화면에 없는 주인은 바를 걷을 자격도 없다.
    static void Prune()
    {
        for (int t_i = s_requests.Count - 1; t_i >= 0; t_i--)
        {
            object t_raw = s_requests[t_i].owner;

            if (t_raw is Object t_owner && t_owner == null) { s_requests.RemoveAt(t_i); continue; }
            if (t_raw is Behaviour t_behaviour && !t_behaviour.isActiveAndEnabled) s_requests.RemoveAt(t_i);
        }
    }
}
