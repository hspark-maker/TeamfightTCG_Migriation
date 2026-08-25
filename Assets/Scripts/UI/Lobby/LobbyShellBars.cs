using System.Collections.Generic;
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
/// SetActive로 끄지 않는다. 끄면 그 프레임에 레이아웃이 튀므로 알파로 지우고 입력만 막는다.</summary>
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
    }

    public static void Show(object _owner)
    {
        if (_owner == null) return;

        RemoveOwner(_owner);
        Prune();
        Apply();
    }

    static void Bind(Transform _context)
    {
        if (s_top != null || s_bottom != null) return;   // 이미 물려 있다

        // 새로 물리는 바는 펼쳐진 상태에서 시작한다 — 씬이 바뀌었으면 이전 씬의 적용 상태는 뜻이 없다.
        s_applied = EShellBars.None;
        if (_context == null) return;

        Transform t_root = FindLobbyRoot(_context);
        if (t_root == null) return;

        s_top    = EnsureGroup(t_root.Find("TopBar"));
        s_bottom = EnsureGroup(t_root.Find("BottomBar"));
    }

    /// <summary>요청자가 늘 LobbyRoot 안에 있지는 않다 — 탭 콘텐츠는 자손이지만
    /// SafeArea 직속 오버레이는 형제이고, <b>풀드 UI(카드 상세)는 아예 다른 캔버스</b>다
    /// (UIPoolManager 캔버스는 DontDestroyOnLoad라 로비 캔버스 계층 밖이다).
    /// 위로 훑고 → 자기 캔버스에서 내려찾고 → 그래도 없으면 로드된 씬 전체를 훑는다.</summary>
    static Transform FindLobbyRoot(Transform _context)
    {
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

    static void Apply()
    {
        // 합집합이 아니라 **가장 마지막 요청**이 이긴다 — 위에 뜬 화면이 아래 화면보다 적게 걷을 수 있어야
        // "페이지 오버레이는 둘 다 걷고, 그 위 상세 화면은 상단바를 되돌린다"가 성립한다.
        EShellBars t_hidden = s_requests.Count > 0 ? s_requests[s_requests.Count - 1].bars : EShellBars.None;

        ApplyTo(s_top,    (t_hidden & EShellBars.Top)    != 0, (s_applied & EShellBars.Top)    != 0);
        ApplyTo(s_bottom, (t_hidden & EShellBars.Bottom) != 0, (s_applied & EShellBars.Bottom) != 0);

        s_applied = t_hidden;

        // 다 돌려준 뒤에는 참조를 놓는다 — 씬이 바뀌어도 파괴된 바를 붙들고 있지 않게
        if (t_hidden != EShellBars.None) return;
        s_top    = null;
        s_bottom = null;
    }

    static void ApplyTo(CanvasGroup _group, bool _hide, bool _wasHidden)
    {
        if (_group == null) return;   // 씬 전환으로 이미 파괴됐다

        // 되돌리는 방향은 캐시를 믿지 않는다. s_applied는 static이라 씬이 다시 로드돼도 살아남고,
        // 트윈이 중간에 잘리면 알파만 되돌아오고 blocksRaycasts는 false로 남는다 —
        // 그러면 바가 **보이는데 안 눌리는** 상태로 굳고, 캐시가 "이미 펼침"이라 믿어 아무도 고치지 않는다.
        // 펼치기는 멱등하므로 매번 확인해도 비용이 없다(값이 이미 맞으면 트윈도 걸지 않는다).
        if (!_hide)
        {
            bool t_alreadyOpen = _group.blocksRaycasts && _group.interactable && _group.alpha >= 0.999f;
            if (t_alreadyOpen) return;
        }
        else if (_hide == _wasHidden) return;

        // 걷힌 바 위로 손가락이 지나가도 버튼이 눌리면 안 된다.
        _group.blocksRaycasts = !_hide;
        _group.interactable   = !_hide;

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
