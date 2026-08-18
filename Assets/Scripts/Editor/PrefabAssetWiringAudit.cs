using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **인스턴스를 물려야 할 자리에 프리팹 에셋이 물린 배선**을 찾아 고친다.
///
/// 왜 위험한가: 에셋을 물면 코드가 화면에 없는 원본을 조작한다. 화면에는 아무 일도 안 일어나고,
/// 에디터에서는 그 조작이 프리팹 파일에 그대로 기록된다(자식 파괴는 "Destroying assets is not permitted"로 터진다).
/// 실제로 LobbyMatchLauncher.overlayHost가 이 상태라 매치 덱 화면이 열리지 않았다.
///
/// 판정 기준: 참조 대상이 **영속(에셋)** 이고, 그 대상이 나온 프리팹이 **이 프리팹 안에 인스턴스로도 들어 있으면**
/// 저작자는 그 인스턴스를 의도한 것이다. keywordIconPrefab처럼 Instantiate용으로 일부러 에셋을 무는 자리는
/// 그 프리팹이 안에 인스턴스로 들어 있지 않으므로 걸리지 않는다.
/// </summary>
static class PrefabAssetWiringAudit
{
    static readonly string[] k_Targets =
    {
        "Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab",
        "Assets/Assets/Prefabs/UI/LobbyUI/LobbyOverlayHost.prefab",
    };

    [MenuItem("Tools/Lobby/Asset-Wired References - Report")]
    static void Report() => Run(false);

    [MenuItem("Tools/Lobby/Asset-Wired References - Fix")]
    static void Fix() => Run(true);

    static void Run(bool _fix)
    {
        int t_found = 0, t_fixed = 0;

        // 디스크 기준으로 다시 읽는다. 플레이 중에 이 프리팹들이 조작됐으면(에셋 배선 버그가 정확히 그렇다)
        // 메모리에 더티 상태가 남아 있고, 그대로 저장하면 런타임 값(카드 이름·아트 등)이 프리팹에 굳는다.
        foreach (string t_reload in k_Targets)
            AssetDatabase.ImportAsset(t_reload, ImportAssetOptions.ForceUpdate);

        foreach (string t_path in k_Targets)
        {
            GameObject t_root = PrefabUtility.LoadPrefabContents(t_path);
            if (t_root == null) continue;

            bool t_dirty = false;

            try
            {
                // 이 프리팹 안에 인스턴스로 들어와 있는 원본 프리팹 목록. 판정의 기준이다.
                Dictionary<Object, GameObject> t_instanced = CollectInstancedSources(t_root);

                foreach (Component t_component in t_root.GetComponentsInChildren<Component>(true))
                {
                    if (t_component == null) continue;

                    var t_so = new SerializedObject(t_component);
                    SerializedProperty t_prop = t_so.GetIterator();
                    bool t_changed = false;

                    while (t_prop.NextVisible(true))
                    {
                        if (t_prop.propertyType != SerializedPropertyType.ObjectReference) continue;

                        Object t_value = t_prop.objectReferenceValue;
                        if (t_value == null || !EditorUtility.IsPersistent(t_value)) continue;

                        Object t_sourceRoot = PrefabAssetRootOf(t_value);
                        if (t_sourceRoot == null ||
                            !t_instanced.TryGetValue(t_sourceRoot, out GameObject t_instanceRoot)) continue;

                        Object t_replacement = MatchInInstance(t_value, t_instanceRoot);
                        t_found++;

                        Debug.LogError(
                            $"[PrefabAssetWiringAudit] {t_path}\n" +
                            $"  {Path(t_component.transform)} · {t_component.GetType().Name}.{t_prop.propertyPath}\n" +
                            $"  에셋({t_value.name})을 물고 있다 — 같은 프리팹의 인스턴스를 물어야 한다." +
                            (t_replacement != null ? "" : " (대응 인스턴스를 못 찾음 — 손으로 배선할 것)"));

                        if (_fix && t_replacement != null)
                        {
                            t_prop.objectReferenceValue = t_replacement;
                            t_changed = true;
                            t_fixed++;
                        }
                    }

                    if (t_changed)
                    {
                        t_so.ApplyModifiedPropertiesWithoutUndo();
                        t_dirty = true;
                    }
                }

                if (_fix && t_dirty) PrefabUtility.SaveAsPrefabAsset(t_root, t_path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(t_root);
            }
        }

        Debug.Log($"[PrefabAssetWiringAudit] {(_fix ? "FIX" : "REPORT")} — 발견 {t_found}, 수정 {t_fixed}");
    }

    /// <summary>이 프리팹 안에 인스턴스로 들어와 있는 원본 프리팹 → 그 인스턴스 루트.</summary>
    static Dictionary<Object, GameObject> CollectInstancedSources(GameObject _root)
    {
        var t_map = new Dictionary<Object, GameObject>();

        foreach (Transform t_child in _root.GetComponentsInChildren<Transform>(true))
        {
            GameObject t_go = t_child.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(t_go)) continue;

            GameObject t_source = PrefabUtility.GetCorrespondingObjectFromSource(t_go);
            if (t_source != null && !t_map.ContainsKey(t_source)) t_map[t_source] = t_go;
        }

        return t_map;
    }

    static GameObject PrefabAssetRootOf(Object _value)
    {
        GameObject t_go = _value as GameObject ?? (_value as Component)?.gameObject;
        return t_go != null ? t_go.transform.root.gameObject : null;
    }

    /// <summary>에셋 쪽 참조와 같은 자리의 인스턴스 쪽 오브젝트/컴포넌트를 찾는다.</summary>
    static Object MatchInInstance(Object _assetValue, GameObject _instanceRoot)
    {
        if (_assetValue is GameObject t_assetGo)
            return FindByPath(t_assetGo.transform, _instanceRoot)?.gameObject;

        if (_assetValue is Component t_assetComponent)
        {
            Transform t_target = FindByPath(t_assetComponent.transform, _instanceRoot);
            return t_target != null ? t_target.GetComponent(t_assetComponent.GetType()) : null;
        }

        return null;
    }

    /// <summary>에셋 루트 기준 상대 경로를 인스턴스 쪽에서 그대로 되짚는다.</summary>
    static Transform FindByPath(Transform _assetTransform, GameObject _instanceRoot)
    {
        if (_assetTransform == _assetTransform.root) return _instanceRoot.transform;

        string t_relative = Path(_assetTransform);
        int t_cut = t_relative.IndexOf('/');
        if (t_cut < 0) return _instanceRoot.transform;

        return _instanceRoot.transform.Find(t_relative.Substring(t_cut + 1));
    }

    static string Path(Transform _transform)
    {
        string t_path = _transform.name;
        for (Transform t = _transform.parent; t != null; t = t.parent) t_path = $"{t.name}/{t_path}";
        return t_path;
    }
}
