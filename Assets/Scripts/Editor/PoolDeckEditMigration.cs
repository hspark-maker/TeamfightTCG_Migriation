using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// D안: 덱 편집 화면을 UIPoolManager 풀로 옮기고 <b>MatchDeckEditPanel 배리언트를 없앤다</b>.
///
/// 지금까지 저작본이 둘이었다 — 로비 탭 안 DeckEditPanel과 매치 오버레이 안 MatchDeckEditPanel(배리언트).
/// 배리언트에 레이아웃 오버라이드가 46개 노드에 걸쳐 쌓여 원본 수정이 매치 화면에 전파되지 않았다.
///
/// C# 쪽은 이미 끝났다(DeckEditController : PooledUIBase + DeckEditData). 여기는 에셋 작업이다.
/// <see cref="PoolRankGrowthMigration"/>·<see cref="PoolOverlayMigration"/>과 같은 규약 —
/// 한 단계 = 한 메뉴 = 한 커밋, 매번 diff 확인.
///
/// 순서:
///   1) 원본에 옵션 노드 배선   : Title·DeckPower를 코드가 켜고 끌 수 있게(DeckStrip 축은 이후 제거됨)
///   2) 배리언트의 삭제 3건 복원 : Apply가 원본에서 Title·BackButton·DeckPower를 지우지 않게
///   3) (수동) 배리언트 Apply All: 매치 레이아웃을 원본에 올린다 — 눈으로 보며 해야 한다
///   4) Addressable + 폴더 이동  : 풀이 타입으로 찾을 수 있게
///   5) 두 호스트에서 인스턴스 제거 + 배리언트 삭제
///
/// ⚠ 3단계 뒤 원본 = 매치 레이아웃이 된다. 로비 덱 편집 화면의 겉모습이 바뀐다 —
///   풀드 화면은 두 호스트 모두 전체화면 캔버스에 서므로 매치 쪽 저작이 맞는 컨테이너다.
/// ⚠ DragLayer는 아직 로비 캔버스에 있다. 원본 프리팹 안으로 옮기지 않으면 드래그 고스트가
///   편집 화면(order 400) 뒤로 깔린다. 이건 손으로 저작해야 한다(3단계와 함께).
/// </summary>
static class PoolDeckEditMigration
{
    const string BasePanel = "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Deck/DeckEditPanel.prefab";
    const string Variant   = "Assets/Assets/Prefabs/UI/MatchUI/MatchDeckEditPanel.prefab";
    const string TabDeck   = "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Deck/Tab_Deck.prefab";
    const string MatchRoot = "Assets/Assets/Prefabs/UI/MatchUI/MatchDeckRoot.prefab";
    const string PooledDir = "Assets/Assets/Prefabs/UI/PooledUI";
    const string MovedPath = PooledDir + "/DeckEditPanel.prefab";

    /// <summary>배리언트가 삭제해 둔 노드들. 삭제가 아니라 <b>비활성</b>으로 바뀌어야 원본에 남는다 —
    /// BackButton은 통합 후 두 화면 모두의 유일한 종료 경로라 절대 지우면 안 된다.</summary>
    static readonly string[] RemovedInVariant = { "Title", "BackButton", "DeckPower" };

    [MenuItem("Tools/Deck/D - 1. Wire Optional Nodes In Base")]
    static void Step1_WireOptionalNodes()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(BasePanel);
        if (t_root == null) { Debug.LogError($"[D-1] 프리팹을 못 찾음: {BasePanel}"); return; }

        try
        {
            var t_controller = t_root.GetComponent<DeckEditController>();
            if (t_controller == null)
            {
                Debug.LogError("[D-1] 루트에 DeckEditController가 없다.");
                return;
            }

            GameObject t_title     = FindChild(t_root, "Title");
            GameObject t_deckPower = FindChild(t_root, "DeckPower");

            var t_so = new SerializedObject(t_controller);
            SetRef(t_so, "titleNode",     t_title);
            SetRef(t_so, "deckPowerNode", t_deckPower);
            t_so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(t_root, BasePanel);

            Debug.Log($"[D-1] 배선 — title={(t_title != null)}, deckPower={(t_deckPower != null)}");
        }
        finally { PrefabUtility.UnloadPrefabContents(t_root); }
    }

    [MenuItem("Tools/Deck/D - 2. Restore Variant-Removed Nodes As Inactive")]
    static void Step2_RestoreRemoved()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(Variant);
        if (t_root == null) { Debug.LogError($"[D-2] 배리언트를 못 찾음: {Variant}"); return; }

        try
        {
            // 배리언트 루트는 원본의 인스턴스다 — 삭제 목록을 되돌린 뒤 비활성으로 저작한다.
            // 이래야 3단계 Apply All이 원본에서 노드를 지우지 않고 "꺼진 상태"만 올린다.
            GameObject[] t_removed = PrefabUtility
                .GetRemovedGameObjects(t_root)
                .Select(r => r.assetGameObject)
                .Where(go => go != null)
                .ToArray();

            if (t_removed.Length == 0)
            {
                Debug.Log("[D-2] 되돌릴 삭제가 없다 — 이미 처리됐거나 배리언트가 아무것도 지우지 않았다.");
                return;
            }

            foreach (GameObject t_asset in t_removed)
                Debug.Log($"[D-2] 삭제 복원 대상: {t_asset.name}");

            foreach (var t_entry in PrefabUtility.GetRemovedGameObjects(t_root).ToArray())
                PrefabUtility.RevertRemovedGameObject(t_entry.assetGameObject, t_root,
                                                     InteractionMode.AutomatedAction);

            // 되살아난 노드를 끈다. BackButton만은 켠 채 둔다 — 통합 후 유일한 종료 경로다.
            int t_off = 0;
            foreach (string t_name in RemovedInVariant)
            {
                if (t_name == "BackButton") continue;

                GameObject t_node = FindChild(t_root, t_name);
                if (t_node == null) continue;

                t_node.SetActive(false);
                t_off++;
            }

            PrefabUtility.SaveAsPrefabAsset(t_root, Variant);
            Debug.Log($"[D-2] 복원 {t_removed.Length}건, 비활성 {t_off}건 (BackButton은 켠 채 유지).\n"
                    + "다음: 배리언트를 열어 Overrides ▸ Apply All — 매치 레이아웃이 원본으로 올라간다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(t_root); }
    }

    [MenuItem("Tools/Deck/D - 4. Move To PooledUI + Addressable")]
    static void Step4_MoveAndRegister()
    {
        if (!AssetDatabase.IsValidFolder(PooledDir))
        {
            Debug.LogError($"[D-4] 폴더 없음: {PooledDir}");
            return;
        }

        string t_error = AssetDatabase.MoveAsset(BasePanel, MovedPath);
        if (!string.IsNullOrEmpty(t_error) && AssetDatabase.AssetPathToGUID(MovedPath) == "")
        {
            Debug.LogError($"[D-4] 이동 실패: {t_error}");
            return;
        }

        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null) { Debug.LogError("[D-4] Addressables 설정이 없다."); return; }

        string t_guid = AssetDatabase.AssetPathToGUID(MovedPath);
        AddressableAssetEntry t_entry = t_settings.CreateOrMoveEntry(t_guid, t_settings.DefaultGroup);
        t_entry.address = "DeckEditPanel";
        t_entry.SetLabel("UIPrefab", true, true);

        EditorUtility.SetDirty(t_settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[D-4] {MovedPath} 이동 + Addressable 등록(label UIPrefab).");
    }

    [MenuItem("Tools/Deck/D - 5. Remove Instances From Hosts")]
    static void Step5_RemoveInstances()
    {
        if (!IsLabeled(MovedPath))
        {
            Debug.LogError("[D-5] D-4를 먼저 돌릴 것 — 라벨 없이 호스트에서 빼면 덱 편집이 아예 안 열린다.");
            return;
        }

        RemoveEditPanelFrom(TabDeck);
        RemoveEditPanelFrom(MatchRoot);

        Debug.Log("[D-5] 제거 완료. 배리언트(MatchDeckEditPanel.prefab)는 이제 참조가 없다 — "
                + "diff 확인 후 손으로 지울 것.");
    }

    static void RemoveEditPanelFrom(string _hostPath)
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(_hostPath);
        if (t_root == null) { Debug.LogError($"[D-5] 프리팹을 못 찾음: {_hostPath}"); return; }

        try
        {
            int t_removed = 0;
            foreach (DeckEditController t_editor in t_root.GetComponentsInChildren<DeckEditController>(true))
            {
                Debug.Log($"[D-5] {System.IO.Path.GetFileName(_hostPath)}에서 제거: {t_editor.name}");
                Object.DestroyImmediate(t_editor.gameObject);
                t_removed++;
            }

            if (t_removed > 0) PrefabUtility.SaveAsPrefabAsset(t_root, _hostPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(t_root); }
    }

    static GameObject FindChild(GameObject _root, string _name)
    {
        Transform t_found = _root.GetComponentsInChildren<Transform>(true)
                                 .FirstOrDefault(t => t.name == _name);

        return t_found != null ? t_found.gameObject : null;
    }

    static void SetRef(SerializedObject _so, string _path, Object _value)
    {
        SerializedProperty t_prop = _so.FindProperty(_path);
        if (t_prop == null)
        {
            Debug.LogError($"[D-1] 필드를 못 찾음: {_path}");
            return;
        }

        t_prop.objectReferenceValue = _value;
    }

    static bool IsLabeled(string _path)
    {
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null) return false;

        string t_guid = AssetDatabase.AssetPathToGUID(_path);
        if (string.IsNullOrEmpty(t_guid)) return false;

        AddressableAssetEntry t_entry = t_settings.FindAssetEntry(t_guid);

        return t_entry != null && t_entry.labels != null && t_entry.labels.Contains("UIPrefab");
    }
}
