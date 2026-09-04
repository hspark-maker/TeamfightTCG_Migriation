using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 설정 판(LobbySetting)의 레벨 게이지를 계정 레벨 축에 <b>배선하는</b> 일회성 도구.
///
/// 저작 상태의 게이지는 세 층이 같은 스프라이트를 서로 다른 Pixels Per Unit Multiplier로 9-slice 해서
/// 테두리 두께 차이를 만든 구조다. Image Type을 Filled로 바꾸면 9-slice가 무시돼 그 림이 통째로 무너지므로,
/// 채움 사각의 <b>폭</b>을 굴리는 방식으로 간다(BarProgressGauge). 그래서 바 루트는 왼쪽 피벗으로 옮기고
/// 두 자식 층은 스트레치 앵커로 부모 폭을 따라오게 바꾼다.
///
/// 자식이 없어 아무것도 오려내지 않는 Mask 두 개도 함께 걷는다 — 복제로 딸려온 죽은 컴포넌트다.
///
/// 멱등하다. 배선이 틀어지면 메뉴에서 한 번 더 돌리면 된다.</summary>
static class AccountLevelBaker
{
    const string PanelPath = "Assets/Assets/Prefabs/UI/PooledUI/LobbySetting.prefab";

    const string GaugeRootName = "Level_Gauge";
    const string FillRootName  = "Level_Gauge_Bar";
    const string RimName       = "Level_Gauge_Bar (2)";
    const string BodyName      = "Level_Gauge_Bar (1)";
    const string ExpTextName   = "EXP";
    const string LevelRootName = "Level";

    // 저작된 층 간 여백. 부모 폭을 따라가게 바꾸면서 원래 크기 차이를 sizeDelta 음수로 옮긴다.
    static readonly Vector2 RimInset  = new Vector2(-3.3925f, -4.1769f);
    static readonly Vector2 BodyInset = new Vector2(-3.3925f, -6.2628f);

    [MenuItem("Tools/UI/로비 설정판 계정 레벨 배선")]
    static void Bake()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(PanelPath);
        if (t_root == null) { Debug.LogError($"[AccountLevelBaker] 프리팹을 못 열었다: {PanelPath}"); return; }

        try
        {
            RectTransform t_gaugeRoot = FindRect(t_root, GaugeRootName);
            RectTransform t_fill      = FindRect(t_root, FillRootName);
            RectTransform t_rim       = FindRect(t_root, RimName);
            RectTransform t_body      = FindRect(t_root, BodyName);
            if (t_gaugeRoot == null || t_fill == null || t_rim == null || t_body == null)
            {
                Debug.LogError($"[AccountLevelBaker] 게이지 노드를 못 찾았다 — {GaugeRootName}/{FillRootName}/{RimName}/{BodyName}");
                return;
            }

            // 채움 사각은 왼쪽에서 자란다. 앵커·피벗의 x만 0으로 옮기므로 anchoredPosition은 그대로다.
            t_fill.anchorMin = new Vector2(0f, t_fill.anchorMin.y);
            t_fill.anchorMax = new Vector2(0f, t_fill.anchorMax.y);
            t_fill.pivot     = new Vector2(0f, t_fill.pivot.y);

            StretchToParent(t_rim,  RimInset);
            StretchToParent(t_body, BodyInset);

            RemoveDeadMask(t_rim);
            RemoveDeadMask(t_body);

            BarProgressGauge t_gauge = t_fill.GetComponent<BarProgressGauge>();
            if (t_gauge == null) t_gauge = t_fill.gameObject.AddComponent<BarProgressGauge>();
            Wire(t_gauge, "fillRect", t_fill);

            AccountLevelView t_view = t_gaugeRoot.GetComponent<AccountLevelView>();
            if (t_view == null) t_view = t_gaugeRoot.gameObject.AddComponent<AccountLevelView>();

            TMP_Text t_levelText = FindLevelText(t_root);
            TMP_Text t_expText   = FindRect(t_root, ExpTextName)?.GetComponent<TMP_Text>();
            if (t_levelText == null || t_expText == null)
            {
                Debug.LogError($"[AccountLevelBaker] 텍스트를 못 찾았다 — {LevelRootName} 하위 TMP / {ExpTextName}");
                return;
            }

            Wire(t_view, "levelText", t_levelText);
            Wire(t_view, "expText",   t_expText);
            Wire(t_view, "gauge",     t_gauge);

            PrefabUtility.SaveAsPrefabAsset(t_root, PanelPath);
            Debug.Log("[AccountLevelBaker] 완료 — 게이지 폭 구동 전환 + 뷰 배선");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    // 레벨 수치는 Level 노드 하위의 TMP다(노드 자신은 밑판 Image라 텍스트가 없다).
    static TMP_Text FindLevelText(GameObject _root)
    {
        RectTransform t_level = FindRect(_root, LevelRootName);
        return t_level != null ? t_level.GetComponentInChildren<TMP_Text>(true) : null;
    }

    static RectTransform FindRect(GameObject _root, string _name)
    {
        var t_all = _root.GetComponentsInChildren<RectTransform>(true);
        for (int t_i = 0; t_i < t_all.Length; t_i++)
            if (t_all[t_i].name == _name) return t_all[t_i];

        return null;
    }

    // 부모 폭을 따라가게 편다. 원래 크기 차이는 sizeDelta 음수(안쪽 여백)로 옮긴다.
    static void StretchToParent(RectTransform _rect, Vector2 _inset)
    {
        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.one;
        _rect.sizeDelta = _inset;
    }

    // 자식이 없는 Mask는 아무것도 오려내지 않으면서 스텐실 패스만 쓴다.
    static void RemoveDeadMask(RectTransform _rect)
    {
        Mask t_mask = _rect.GetComponent<Mask>();
        if (t_mask == null || _rect.childCount > 0) return;

        Object.DestroyImmediate(t_mask, true);
    }

    static void Wire(Object _target, string _field, Object _value)
    {
        var t_so = new SerializedObject(_target);
        SerializedProperty t_prop = t_so.FindProperty(_field);
        if (t_prop == null) { Debug.LogError($"[AccountLevelBaker] {_target.GetType().Name}.{_field} 를 못 찾았다"); return; }

        t_prop.objectReferenceValue = _value;
        t_so.ApplyModifiedPropertiesWithoutUndo();
    }
}
