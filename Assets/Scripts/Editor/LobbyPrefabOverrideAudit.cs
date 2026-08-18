using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LobbyPrefabOverrideAudit
{
    const string k_LobbyCanvasPath =
        "Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab";
    const string k_LobbyScenePath = "Assets/Scenes/LobbyScene.unity";
    const string k_OwnedPrefabRoot = "Assets/Assets/Prefabs/UI/Common";
    const string k_OverlayHostPath =
        "Assets/Assets/Prefabs/UI/LobbyUI/LobbyOverlayHost.prefab";
    const string k_VendorRoot = "Assets/Layer Lab/";
    const int k_WarningThreshold = 30;
    const int k_ErrorThreshold = 60;

    static readonly string[] s_SkinProperties =
    {
        "m_Sprite",
        "m_Color",
        "m_Text",
        "m_text"
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

        try
        {
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

            t_errors += AuditOwnedPrefabDuplicates();

            int t_unusedSceneOverrides = CountUnusedLobbySceneOverrides();
            if (t_unusedSceneOverrides > 0)
            {
                t_errors++;
                Debug.LogError(
                    $"[LobbyPrefabOverrideAudit] LobbyScene has " +
                    $"{t_unusedSceneOverrides} unused LobbyCanvas overrides. " +
                    "Run Tools/Lobby/Remove Unused Scene Overrides.");
            }

            string t_summary =
                $"[LobbyPrefabOverrideAudit] roots={t_nestedRoots.Count}, " +
                $"errors={t_errors}, warnings={t_warnings}, " +
                $"crossBoundaryRefs={t_crossBoundaryReferences}";
            if (t_errors == 0)
                Debug.Log(t_summary, t_canvas);
            else
                Debug.LogError(t_summary, t_canvas);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_canvas);
        }
    }

    static int AuditOwnedPrefabDuplicates()
    {
        int t_errors = 0;
        foreach (string t_guid in AssetDatabase.FindAssets(
                     "t:Prefab",
                     new[] { k_OwnedPrefabRoot }))
        {
            string t_path = AssetDatabase.GUIDToAssetPath(t_guid);
            string t_name = System.IO.Path.GetFileNameWithoutExtension(t_path);
            if (!t_name.StartsWith("UI_", StringComparison.Ordinal))
                continue;

            GameObject t_root = PrefabUtility.LoadPrefabContents(t_path);
            try
            {
                t_errors += CountDuplicateContent(t_root, t_path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(t_root);
            }
        }

        GameObject t_overlayHost =
            PrefabUtility.LoadPrefabContents(k_OverlayHostPath);
        try
        {
            t_errors += CountDuplicateContent(
                t_overlayHost,
                k_OverlayHostPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_overlayHost);
        }
        return t_errors;
    }

    static int CountDuplicateContent(GameObject _root, string _label)
    {
        int t_errors = 0;
        foreach (Transform t_parent in
                 _root.GetComponentsInChildren<Transform>(true))
        {
            var t_componentTypes = new HashSet<Type>();
            foreach (Component t_component in
                     t_parent.GetComponents<Component>())
            {
                if (t_component == null || t_component is Transform)
                    continue;
                if (t_componentTypes.Add(t_component.GetType()))
                    continue;

                t_errors++;
                Debug.LogError(
                    $"[LobbyPrefabOverrideAudit] {_label}: duplicate " +
                    $"component {t_component.GetType().Name} on " +
                    HierarchyPath(t_parent, _root.transform),
                    t_parent);
            }

            var t_children = new HashSet<string>();
            for (int i = 0; i < t_parent.childCount; i++)
            {
                Transform t_child = t_parent.GetChild(i);
                UnityEngine.Object t_source =
                    PrefabUtility.GetCorrespondingObjectFromSource(
                        t_child.gameObject);
                string t_sourceId = t_source == null
                    ? "added"
                    : GlobalObjectId.GetGlobalObjectIdSlow(t_source).ToString();
                string t_key = t_child.name + "|" + t_sourceId;
                if (t_children.Add(t_key))
                    continue;

                t_errors++;
                Debug.LogError(
                    $"[LobbyPrefabOverrideAudit] {_label}: duplicate child " +
                    $"{t_child.name} under " +
                    HierarchyPath(t_parent, _root.transform),
                    t_parent);
            }
        }
        return t_errors;
    }

    static string HierarchyPath(Transform _target, Transform _root)
    {
        string t_path = _target.name;
        while (_target != _root)
        {
            _target = _target.parent;
            t_path = _target.name + "/" + t_path;
        }
        return t_path;
    }

    [MenuItem("Tools/Lobby/Remove Unused Scene Overrides")]
    public static void RemoveUnusedSceneOverrides()
    {
        Scene t_scene = SceneManager.GetActiveScene();
        GameObject t_lobbyCanvas = FindSceneLobbyCanvas(t_scene);
        if (t_lobbyCanvas == null)
        {
            Debug.LogError(
                $"[LobbyPrefabOverrideAudit] Open {k_LobbyScenePath} first.");
            return;
        }

        PrefabUtility.RemoveUnusedOverrides(
            new[] { t_lobbyCanvas },
            InteractionMode.AutomatedAction);
        EditorSceneManager.SaveScene(t_scene);
        Debug.Log("[LobbyPrefabOverrideAudit] Removed unused scene overrides.");
    }

    static int CountUnusedLobbySceneOverrides()
    {
        GameObject t_lobbyCanvas = FindSceneLobbyCanvas(
            SceneManager.GetActiveScene());
        if (t_lobbyCanvas == null)
            return 0;

        int t_count = 0;
        foreach (PropertyModification t_modification in
                 PrefabUtility.GetPropertyModifications(t_lobbyCanvas) ??
                 Array.Empty<PropertyModification>())
        {
            if (t_modification.target == null)
                t_count++;
        }
        return t_count;
    }

    static GameObject FindSceneLobbyCanvas(Scene _scene)
    {
        if (!_scene.IsValid() || _scene.path != k_LobbyScenePath)
            return null;

        foreach (GameObject t_root in _scene.GetRootGameObjects())
        {
            if (SourcePath(t_root) == k_LobbyCanvasPath)
                return t_root;
        }
        return null;
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
                _propertyPath.StartsWith(
                    t_property + ".",
                    StringComparison.Ordinal) ||
                _propertyPath.EndsWith(
                    "." + t_property,
                    StringComparison.Ordinal) ||
                _propertyPath.Contains("." + t_property + "."))
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
