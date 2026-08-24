using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 하단 탭바가 안 눌릴 때 <b>무엇이 클릭을 먹는지</b> 한 번에 찍는다.
///
/// 추측으로 좁히지 않기 위한 도구다 — "보이는데 안 눌린다"는 원인 후보가 넷이고
/// (덮개 / CanvasGroup.blocksRaycasts / Button.interactable / 리스너 미배선) 겉보기로는 구분이 안 된다.
///
/// <b>플레이 모드에서 로비를 띄운 채</b> 돌릴 것. 에디트 모드에서는 EventSystem 레이캐스트가 뜻이 없다.
/// </summary>
static class BottomBarClickDiagnostic
{
    [MenuItem("Tools/Lobby/진단 - 하단바 클릭 막는 것 찾기")]
    static void Run()
    {
        var sb = new StringBuilder("=== 하단바 클릭 진단 ===\n");

        if (!Application.isPlaying)
            sb.AppendLine("⚠ 플레이 모드가 아니다 — 레이캐스트 결과는 뜻이 없다. 로비를 띄운 채 다시 돌릴 것.\n");

        var t_bar = Object.FindFirstObjectByType<LobbyTabBarView>(FindObjectsInactive.Include);
        if (t_bar == null)
        {
            Debug.LogError("[진단] 씬에 LobbyTabBarView가 없다 — 로비 씬인지 확인할 것.");
            return;
        }

        ReportBar(sb, t_bar);
        ReportCanvasGroups(sb, t_bar.transform);
        ReportShellBars(sb);
        ReportRaycast(sb, t_bar);
        ReportOverlays(sb);

        Debug.Log(sb.ToString());
    }

    static void ReportBar(StringBuilder _sb, LobbyTabBarView _bar)
    {
        _sb.AppendLine($"[탭바] '{_bar.name}' activeInHierarchy={_bar.gameObject.activeInHierarchy}");

        // 탭은 자식 계층의 TabButtonView 순서로 정해진다(인스펙터 배선 없음) — 진단도 같은 경로로 읽는다.
        TabButtonView[] t_views = _bar.GetComponentsInChildren<TabButtonView>(true);
        _sb.AppendLine($"  수집된 탭 {t_views.Length}개 (계층 순서 = 탭 인덱스)");

        for (int t_i = 0; t_i < t_views.Length; t_i++)
        {
            // 프로퍼티(TabButtonView.Button)를 쓰지 않는다 — 그쪽은 없으면 AddComponent라 진단이 씬을 바꾼다.
            var t_button = t_views[t_i].GetComponent<Button>();
            if (t_button == null)
            {
                _sb.AppendLine($"  [{t_i}] {t_views[t_i].name} : Button 없음"
                             + (Application.isPlaying ? " ← 런타임 확보에 실패한 것이다" : " (플레이 시 TabButtonView가 확보한다)"));
                continue;
            }

            // 리스너 수: 프리팹 onClick(persistent) + 코드가 건 것(runtime)을 함께 본다.
            int t_persistent = t_button.onClick.GetPersistentEventCount();

            _sb.AppendLine($"  [{t_i}] {t_button.name} interactable={t_button.interactable} "
                         + $"active={t_button.gameObject.activeInHierarchy} "
                         + $"raycastTarget={(t_button.targetGraphic != null ? t_button.targetGraphic.raycastTarget.ToString() : "targetGraphic 없음")} "
                         + $"persistentListeners={t_persistent}");
        }
    }

    static void ReportCanvasGroups(StringBuilder _sb, Transform _node)
    {
        _sb.AppendLine("\n[조상 CanvasGroup] blocksRaycasts=False가 하나라도 있으면 그 아래는 전부 안 눌린다");

        bool t_any = false;
        for (Transform t_t = _node; t_t != null; t_t = t_t.parent)
        {
            var t_cg = t_t.GetComponent<CanvasGroup>();
            if (t_cg == null) continue;

            t_any = true;
            _sb.AppendLine($"  {Path(t_t)} : alpha={t_cg.alpha} interactable={t_cg.interactable} "
                         + $"blocksRaycasts={t_cg.blocksRaycasts} ignoreParentGroups={t_cg.ignoreParentGroups}"
                         + (t_cg.blocksRaycasts ? "" : "   ← 범인 후보"));
        }

        if (!t_any) _sb.AppendLine("  (없음)");
    }

    /// <summary>LobbyShellBars가 바를 걷어둔 채 남아 있는지. 내부 static이라 리플렉션으로 읽는다.</summary>
    static void ReportShellBars(StringBuilder _sb)
    {
        _sb.AppendLine("\n[LobbyShellBars] 걷기 요청이 남아 있으면 바가 걷힌 채로 굳는다");

        System.Type t_type = typeof(LobbyShellBars);
        const BindingFlags FLAGS = BindingFlags.NonPublic | BindingFlags.Static;

        var t_requests = t_type.GetField("s_requests", FLAGS)?.GetValue(null) as System.Collections.IList;
        var t_applied  = t_type.GetField("s_applied",  FLAGS)?.GetValue(null);
        var t_bottom   = t_type.GetField("s_bottom",   FLAGS)?.GetValue(null) as CanvasGroup;
        var t_top      = t_type.GetField("s_top",      FLAGS)?.GetValue(null) as CanvasGroup;

        _sb.AppendLine($"  s_applied={t_applied}  s_top={(t_top == null ? "null" : t_top.name)}  "
                     + $"s_bottom={(t_bottom == null ? "null" : t_bottom.name)}");
        _sb.AppendLine($"  남은 요청 {(t_requests == null ? -1 : t_requests.Count)}건");

        if (t_requests == null) return;

        System.Type t_req = t_type.GetNestedType("Request", BindingFlags.NonPublic);
        FieldInfo t_owner = t_req?.GetField("owner", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        FieldInfo t_bars  = t_req?.GetField("bars",  BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        for (int t_i = 0; t_i < t_requests.Count; t_i++)
        {
            object t_o = t_owner?.GetValue(t_requests[t_i]);
            _sb.AppendLine($"    - owner={(t_o is Object t_u ? (t_u == null ? "(파괴됨)" : t_u.name) : t_o)} "
                         + $"bars={t_bars?.GetValue(t_requests[t_i])}   ← 이게 남아 있으면 Show를 못 받은 것");
        }
    }

    /// <summary>탭바 각 버튼 중심에 실제로 레이캐스트를 쏴서 <b>맨 위에 무엇이 걸리는지</b> 본다.
    /// 1등이 버튼 자신이 아니면 그게 덮개다.</summary>
    static void ReportRaycast(StringBuilder _sb, LobbyTabBarView _bar)
    {
        _sb.AppendLine("\n[레이캐스트] 버튼 중심에 무엇이 걸리는가 (1등이 버튼이 아니면 그게 덮개다)");

        if (EventSystem.current == null)
        {
            _sb.AppendLine("  EventSystem이 없다 — 어떤 UI도 클릭되지 않는다. ← 이거면 원인 확정");
            return;
        }

        for (int t_i = 0; t_i < _bar.Count; t_i++)
        {
            RectTransform t_anchor = _bar.GetButtonAnchor(t_i);
            if (t_anchor == null) { _sb.AppendLine($"  [{t_i}] 앵커 없음"); continue; }

            Vector2 t_screen = RectTransformUtility.WorldToScreenPoint(null, t_anchor.position);
            var t_data = new PointerEventData(EventSystem.current) { position = t_screen };
            var t_hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(t_data, t_hits);

            if (t_hits.Count == 0)
            {
                _sb.AppendLine($"  [{t_i}] {t_anchor.name} @ {t_screen} : 아무것도 안 걸림 "
                             + "← 버튼 그래픽의 raycastTarget이 꺼졌거나 CanvasGroup이 막았다");
                continue;
            }

            _sb.AppendLine($"  [{t_i}] {t_anchor.name} @ {t_screen} : 1등 = {Path(t_hits[0].gameObject.transform)} "
                         + $"(canvas order {t_hits[0].sortingOrder})");
            for (int t_h = 1; t_h < t_hits.Count && t_h < 4; t_h++)
                _sb.AppendLine($"        {t_h + 1}등 = {Path(t_hits[t_h].gameObject.transform)}");
        }
    }

    /// <summary>지금 화면에 선 캔버스들을 정렬 순서대로. 로비(0)보다 위에 전면 캔버스가 떠 있으면 그게 덮개다.</summary>
    static void ReportOverlays(StringBuilder _sb)
    {
        _sb.AppendLine("\n[활성 캔버스] 정렬 순서 큰 것이 위. 로비는 0이다");

        var t_casters = Object.FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Exclude,
                                                                  FindObjectsSortMode.None);
        var t_list = new List<GraphicRaycaster>(t_casters);
        t_list.Sort((a, b) =>
        {
            var t_ca = a.GetComponent<Canvas>();
            var t_cb = b.GetComponent<Canvas>();
            int t_oa = t_ca != null ? t_ca.sortingOrder : 0;
            int t_ob = t_cb != null ? t_cb.sortingOrder : 0;

            return t_ob.CompareTo(t_oa);
        });

        foreach (GraphicRaycaster t_caster in t_list)
        {
            var t_canvas = t_caster.GetComponent<Canvas>();
            _sb.AppendLine($"  order={(t_canvas != null ? t_canvas.sortingOrder : 0)} "
                         + $"override={(t_canvas != null && t_canvas.overrideSorting)} : {Path(t_caster.transform)}");
        }
    }

    static string Path(Transform _node)
    {
        var t_sb = new StringBuilder(_node.name);
        for (Transform t_p = _node.parent; t_p != null; t_p = t_p.parent)
            t_sb.Insert(0, t_p.name + "/");

        return t_sb.ToString();
    }
}
