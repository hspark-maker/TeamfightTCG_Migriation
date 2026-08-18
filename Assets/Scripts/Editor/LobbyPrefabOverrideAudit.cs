using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class LobbyPrefabOverrideAudit
{
    const string k_LobbyCanvasPath =
        "Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab";
    const string k_VendorRoot = "Assets/Layer Lab/";
    const int k_WarningThreshold = 30;
    const int k_ErrorThreshold = 60;

    static readonly string[] s_SkinProperties =
    {
        "m_Sprite",
        "m_Color",
        "m_Text"
    };

    [MenuItem("Tools/Lobby/Audit Prefab Overrides")]
    public static void Audit()
    {
        GameObject t_canvas =
            PrefabUtility.LoadPrefabContents(k_LobbyCanvasPath);
        if (t_canvas == null)
        {
            Debug.LogError($"[LobbyPrefabOverrideAudit] Missing {k_LobbyCanvasPath}");
            return;
        }

        int t_errors = 0;
        int t_warnings = 0;
        List<GameObject> t_nestedRoots = FindNestedRoots(t_canvas);

        foreach (GameObject t_root in t_nestedRoots)
        {
            PropertyModification[] t_modifications =
                PrefabUtility.GetPropertyModifications(t_root) ??
                Array.Empty<PropertyModification>();
            string t_sourcePath = SourcePath(t_root);

            if (t_modifications.Length > k_ErrorThreshold)
            {
                t_errors++;
                Debug.LogError(
                    $"[LobbyPrefabOverrideAudit] {t_root.name}: " +
                    $"{t_modifications.Length} overrides (> {k_ErrorThreshold})",
                    t_root);
            }
            else if (t_modifications.Length > k_WarningThreshold)
            {
                t_warnings++;
                Debug.LogWarning(
                    $"[LobbyPrefabOverrideAudit] {t_root.name}: " +
                    $"{t_modifications.Length} overrides (> {k_WarningThreshold})",
                    t_root);
            }

            foreach (PropertyModification t_modification in t_modifications)
            {
                if (!IsSkinProperty(t_modification.propertyPath))
                    continue;

                t_errors++;
                Debug.LogError(
                    $"[LobbyPrefabOverrideAudit] {t_root.name}: " +
                    $"skin override {t_modification.propertyPath}",
                    t_root);
            }

            if (t_sourcePath.StartsWith(
                    k_VendorRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                t_errors++;
                Debug.LogError(
                    $"[LobbyPrefabOverrideAudit] {t_root.name}: " +
                    $"direct vendor instance {t_sourcePath}",
                    t_root);
            }
        }

        int t_missing = CountMissingComponents(t_canvas);
        if (t_missing > 0)
        {
            t_errors += t_missing;
            Debug.LogError(
                $"[LobbyPrefabOverrideAudit] Missing scripts/components: {t_missing}",
                t_canvas);
        }

        int t_crossBoundaryReferences =
            CountCrossBoundaryReferences(t_canvas, t_nestedRoots);
        if (t_crossBoundaryReferences > 0)
        {
            t_warnings++;
            Debug.LogWarning(
                $"[LobbyPrefabOverrideAudit] References into nested prefab " +
                $"children: {t_crossBoundaryReferences}",
                t_canvas);
        }

        string t_summary =
            $"[LobbyPrefabOverrideAudit] roots={t_nestedRoots.Count}, " +
            $"errors={t_errors}, warnings={t_warnings}, " +
            $"crossBoundaryRefs={t_crossBoundaryReferences}";
        if (t_errors == 0)
            Debug.Log(t_summary, t_canvas);
        else
            Debug.LogError(t_summary, t_canvas);

        PrefabUtility.UnloadPrefabContents(t_canvas);
    }

    static List<GameObject> FindNestedRoots(GameObject _canvas)
    {
        var t_roots = new List<GameObject>();
        var t_handles = new HashSet<int>();
        foreach (Transform t_transform in
                 _canvas.GetComponentsInChildren<Transform>(true))
        {
            GameObject t_object = t_transform.gameObject;
            UnityEngine.Object t_handle =
                PrefabUtility.GetPrefabInstanceHandle(t_object);
            if (t_handle == null || !t_handles.Add(t_handle.GetInstanceID()))
                continue;
            t_roots.Add(t_object);
        }
        return t_roots;
    }

    static string SourcePath(GameObject _instanceRoot)
    {
        GameObject t_source =
            PrefabUtility.GetCorrespondingObjectFromSource(_instanceRoot);
        return t_source == null ? string.Empty : AssetDatabase.GetAssetPath(t_source);
    }

    static bool IsSkinProperty(string _propertyPath)
    {
        if (string.IsNullOrEmpty(_propertyPath))
            return false;
        foreach (string t_property in s_SkinProperties)
        {
            if (_propertyPath == t_property ||
                _propertyPath.EndsWith("." + t_property, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    static int CountMissingComponents(GameObject _root)
    {
        int t_count = 0;
        foreach (Transform t_transform in
                 _root.GetComponentsInChildren<Transform>(true))
        {
            foreach (Component t_component in
                     t_transform.GetComponents<Component>())
            {
                if (t_component == null)
                    t_count++;
            }
        }
        return t_count;
    }

    static int CountCrossBoundaryReferences(
        GameObject _canvas,
        List<GameObject> _nestedRoots)
    {
        var t_rootsByHandle = new Dictionary<int, GameObject>();
        foreach (GameObject t_root in _nestedRoots)
        {
            UnityEngine.Object t_handle =
                PrefabUtility.GetPrefabInstanceHandle(t_root);
            if (t_handle != null)
                t_rootsByHandle[t_handle.GetInstanceID()] = t_root;
        }

        int t_count = 0;
        foreach (Component t_owner in
                 _canvas.GetComponentsInChildren<Component>(true))
        {
            if (t_owner == null)
                continue;

            UnityEngine.Object t_ownerHandle =
                PrefabUtility.GetPrefabInstanceHandle(t_owner.gameObject);
            var t_serializedOwner = new SerializedObject(t_owner);
            SerializedProperty t_property = t_serializedOwner.GetIterator();

            while (t_property.NextVisible(true))
            {
                if (t_property.propertyType !=
                    SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object t_reference =
                    t_property.objectReferenceValue;
                GameObject t_referenceObject = ReferencedGameObject(t_reference);
                if (t_referenceObject == null)
                    continue;

                UnityEngine.Object t_referenceHandle =
                    PrefabUtility.GetPrefabInstanceHandle(t_referenceObject);
                if (t_referenceHandle == null ||
                    !t_rootsByHandle.TryGetValue(
                        t_referenceHandle.GetInstanceID(),
                        out GameObject t_referenceRoot) ||
                    t_referenceHandle == t_ownerHandle ||
                    t_referenceObject == t_referenceRoot)
                    continue;

                t_count++;
            }
        }
        return t_count;
    }

    static GameObject ReferencedGameObject(UnityEngine.Object _reference)
    {
        if (_reference is GameObject t_object)
            return t_object;
        if (_reference is Component t_component)
            return t_component.gameObject;
        return null;
    }
}
