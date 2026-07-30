using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 UI 레이아웃 점검 도구. Tools > Lobby > 레이아웃 점검.
///
/// 로비 UI를 레이아웃 그룹 기반으로 개편한 뒤, 눈으로 못 잡는 세 종류의 결함을
/// 씬/프리팹에 실제로 배치된 상태에서 수치로 확인한다.
///
///   1) 화면 밖으로 나가거나 바닥에서 뜨는 최상위 3분할 (TopBar/Content/BottomBar)
///   2) 자기 자식을 감싸지 못하는 "가짜 컨테이너" — rect가 컨텐츠와 무관한 노드
///   3) 폭·높이가 0으로 붕괴한 노드
///
/// 열려 있는 씬(또는 프리팹 편집 모드)의 LobbyCanvas를 그대로 읽는다. 선택된
/// GameObject가 있으면 그 하위만 본다.
/// </summary>
public static class LobbyLayoutAudit
{
    const float k_Epsilon = 0.5f;

    [MenuItem("Tools/Lobby/레이아웃 점검")]
    static void Audit()
    {
        RectTransform t_root = FindRoot();
        if (t_root == null)
        {
            Debug.LogWarning("[LobbyLayoutAudit] LobbyCanvas를 못 찾았다. LobbyScene을 열거나 LobbyCanvas 프리팹을 편집 모드로 열고 실행할 것.");
            return;
        }

        // 직렬화된 값이 아니라 실제 해석된 rect를 봐야 하므로 강제 리빌드.
        Canvas.ForceUpdateCanvases();
        foreach (LayoutGroup t_group in t_root.GetComponentsInChildren<LayoutGroup>(true))
            LayoutRebuilder.ForceRebuildLayoutImmediate(t_group.transform as RectTransform);
        Canvas.ForceUpdateCanvases();

        var t_sb = new StringBuilder();
        t_sb.AppendLine($"[LobbyLayoutAudit] 기준: {t_root.name}  rect={Fmt(t_root.rect)}");

        ReportSplit(t_root, t_sb);
        int t_fake = ReportFakeContainers(t_root, t_sb);
        int t_collapsed = ReportCollapsed(t_root, t_sb);

        t_sb.AppendLine($"\n요약: 가짜 컨테이너 {t_fake}건, 붕괴 노드 {t_collapsed}건");
        Debug.Log(t_sb.ToString());
    }

    static RectTransform FindRoot()
    {
        if (Selection.activeGameObject != null)
        {
            var t_sel = Selection.activeGameObject.transform as RectTransform;
            if (t_sel != null) return t_sel;
        }
        foreach (Canvas t_canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (t_canvas.name.Contains("LobbyCanvas")) return t_canvas.transform as RectTransform;
        }
        return null;
    }

    /// 최상위 3분할이 캔버스를 정확히 채우는지. 남거나 넘치면 그게 곧 결함이다.
    static void ReportSplit(RectTransform _root, StringBuilder _sb)
    {
        RectTransform t_lobby = _root.name == "LobbyRoot" ? _root : FindChild(_root, "LobbyRoot");
        if (t_lobby == null) { _sb.AppendLine("\n[3분할] LobbyRoot 없음 — 건너뜀"); return; }

        _sb.AppendLine($"\n[3분할] LobbyRoot height={t_lobby.rect.height:F1}");
        float t_sum = 0f;
        foreach (string t_name in new[] { "TopBar", "Content", "BottomBar" })
        {
            RectTransform t_child = FindChild(t_lobby, t_name);
            if (t_child == null) { _sb.AppendLine($"   {t_name}: 없음"); continue; }
            var t_le = t_child.GetComponent<LayoutElement>();
            string t_src = t_le == null
                ? "LayoutElement 없음(sizeDelta 의존)"
                : $"pref={t_le.preferredHeight} flex={t_le.flexibleHeight}";
            t_sum += t_child.rect.height;
            _sb.AppendLine($"   {t_name}: height={t_child.rect.height:F1}  [{t_src}]");
        }
        float t_slack = t_lobby.rect.height - t_sum;
        if (Mathf.Abs(t_slack) > k_Epsilon)
            _sb.AppendLine($"   >> 합계 {t_sum:F1} vs 부모 {t_lobby.rect.height:F1} — {(t_slack > 0 ? "빈 공간" : "넘침")} {Mathf.Abs(t_slack):F1}px");
        else
            _sb.AppendLine($"   >> 합계 일치 (오차 {t_slack:F2}px)");

        RectTransform t_bottom = FindChild(t_lobby, "BottomBar");
        if (t_bottom != null)
        {
            // LobbyRoot 로컬 좌표에서 BottomBar 아래 모서리가 0이어야 바닥에 붙는다.
            Vector3[] t_corners = new Vector3[4];
            t_bottom.GetWorldCorners(t_corners);
            Vector3[] t_rootCorners = new Vector3[4];
            t_lobby.GetWorldCorners(t_rootCorners);
            float t_gap = t_corners[0].y - t_rootCorners[0].y;
            _sb.AppendLine(Mathf.Abs(t_gap) <= k_Epsilon
                ? "   >> BottomBar 바닥 밀착 OK"
                : $"   >> BottomBar 바닥에서 {t_gap:F1}px {(t_gap > 0 ? "떠 있음" : "잘려 있음")}");
        }
    }

    /// rect가 자기 자식들을 감싸지 못하는 컨테이너. 자식이 부모 밖으로 크게 벗어나면 보고.
    static int ReportFakeContainers(RectTransform _root, StringBuilder _sb)
    {
        _sb.AppendLine("\n[가짜 컨테이너] rect가 자식을 못 감싸는 노드 (여유 100px 초과)");
        int t_count = 0;
        foreach (RectTransform t_node in _root.GetComponentsInChildren<RectTransform>(true))
        {
            if (t_node.childCount == 0) continue;
            // 스크롤 뷰포트/마스크는 의도적으로 컨텐츠를 잘라내므로 제외한다.
            if (t_node.GetComponent<RectMask2D>() != null || t_node.GetComponent<Mask>() != null) continue;
            if (t_node.GetComponent<ScrollRect>() != null) continue;

            Rect t_self = t_node.rect;
            float t_worst = 0f;
            string t_who = null;
            for (int i = 0; i < t_node.childCount; i++)
            {
                var t_child = t_node.GetChild(i) as RectTransform;
                if (t_child == null || !t_child.gameObject.activeInHierarchy) continue;
                Rect t_local = ToParentSpace(t_child, t_node);
                float t_over = Mathf.Max(
                    Mathf.Max(t_self.xMin - t_local.xMin, t_local.xMax - t_self.xMax),
                    Mathf.Max(t_self.yMin - t_local.yMin, t_local.yMax - t_self.yMax));
                if (t_over > t_worst) { t_worst = t_over; t_who = t_child.name; }
            }
            if (t_worst > 100f)
            {
                t_count++;
                _sb.AppendLine($"   {Path(t_node, _root)}  rect={Fmt(t_self)}  최대 이탈 {t_worst:F0}px ({t_who})");
            }
        }
        if (t_count == 0) _sb.AppendLine("   없음");
        return t_count;
    }

    static int ReportCollapsed(RectTransform _root, StringBuilder _sb)
    {
        _sb.AppendLine("\n[붕괴 노드] 활성 상태인데 폭 또는 높이가 0");
        int t_count = 0;
        foreach (RectTransform t_node in _root.GetComponentsInChildren<RectTransform>(true))
        {
            if (!t_node.gameObject.activeInHierarchy) continue;
            if (t_node.rect.width > k_Epsilon && t_node.rect.height > k_Epsilon) continue;
            // ContentSizeFitter가 붙은 빈 컨텐츠는 자식이 없으면 0이 정상이다.
            if (t_node.childCount == 0 && t_node.GetComponent<ContentSizeFitter>() != null) continue;
            t_count++;
            _sb.AppendLine($"   {Path(t_node, _root)}  rect={Fmt(t_node.rect)}");
        }
        if (t_count == 0) _sb.AppendLine("   없음");
        return t_count;
    }

    // ---- helpers ----------------------------------------------------------

    static RectTransform FindChild(RectTransform _parent, string _name)
    {
        for (int i = 0; i < _parent.childCount; i++)
        {
            if (_parent.GetChild(i).name == _name) return _parent.GetChild(i) as RectTransform;
        }
        return null;
    }

    /// 자식 rect를 부모 로컬 좌표로 옮긴다(회전/스케일 없다고 가정 — UI 기본).
    static Rect ToParentSpace(RectTransform _child, RectTransform _parent)
    {
        var t_corners = new Vector3[4];
        _child.GetWorldCorners(t_corners);
        Vector3 t_min = _parent.InverseTransformPoint(t_corners[0]);
        Vector3 t_max = _parent.InverseTransformPoint(t_corners[2]);
        return new Rect(t_min.x, t_min.y, t_max.x - t_min.x, t_max.y - t_min.y);
    }

    static string Path(Transform _node, Transform _root)
    {
        var t_parts = new List<string>();
        Transform t_cur = _node;
        while (t_cur != null && t_cur != _root) { t_parts.Add(t_cur.name); t_cur = t_cur.parent; }
        t_parts.Reverse();
        return string.Join("/", t_parts);
    }

    static string Fmt(Rect _r) => $"({_r.x:F0},{_r.y:F0}) {_r.width:F0}x{_r.height:F0}";
}
