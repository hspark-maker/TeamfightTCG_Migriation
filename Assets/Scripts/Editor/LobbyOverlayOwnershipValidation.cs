using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LobbyOverlayOwnershipValidation
{
    [MenuItem("Tools/UI/Validate Lobby Overlay Ownership")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var launcher = root.GetComponentInChildren<LobbyMatchLauncher>(true);
            var deck = launcher.GetComponentInChildren<MatchDeckShell>(true);
            Require(deck.transform.parent == launcher.transform, "Deck must belong to its launcher.");
            Require(new SerializedObject(launcher).FindProperty("matchDeckShell").objectReferenceValue == deck,
                "Launcher must reference the child instance.");
            Require(root.transform.Find("OverlayHost") == null && root.transform.Find("ScreenDim_Full") != null,
                "Host must be removed and the shared dim must belong to the canvas.");

            var outer = root.transform.Find("SafeArea").GetComponent<SafeAreaFitter>();
            outer.enabled = false;
            var safe = (RectTransform)outer.transform;
            safe.anchorMin = new Vector2(0.08f, 0.12f);
            safe.anchorMax = new Vector2(0.96f, 0.91f);
            Canvas.ForceUpdateCanvases();
            deck.GetComponent<ScreenFillRect>().SendMessage("OnEnable");
            var expected = new Vector3[4];
            var actual = new Vector3[4];
            ((RectTransform)root.transform).GetWorldCorners(expected);
            ((RectTransform)deck.transform).GetWorldCorners(actual);
            for (int i = 0; i < 4; i++) Require(Vector3.Distance(expected[i], actual[i]) < 0.1f,
                "Deck must cover the full canvas under an asymmetric safe area.");

            var inner = deck.GetComponentInChildren<SafeAreaFitter>(true);
            Require(inner != null, "Deck's inner safe area was lost.");
            inner.InitializeUI();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SafeAreaFitter).GetMethod("RefreshSuppression", flags).Invoke(inner, null);
            Require(!(bool)typeof(SafeAreaFitter).GetField("suppressedByAncestor", flags).GetValue(inner),
                "Full-screen boundary must preserve the deck's own notch inset.");

            foreach (var node in root.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) == 0, "Missing script: " + node.name);
            Debug.Log("[LobbyOverlayOwnershipValidation] PASS: owner/reference, shared dim, no host, full-screen bounds, inner SafeArea, no missing scripts.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
