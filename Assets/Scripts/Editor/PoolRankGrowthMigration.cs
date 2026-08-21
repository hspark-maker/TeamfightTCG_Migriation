using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// B안: RankRewardPanel · KeywordGrowthPanel을 OverlayHost 상주에서 UIPoolManager 풀로 옮긴다.
///
/// 단계를 쪼갠 이유: 프리팹 저장은 되돌리기 어렵고, 유니티가 더티 상태를 함께 흘리는 사고가 반복됐다.
/// 한 단계 = 한 메뉴 = 한 커밋으로 두고 매번 diff를 확인한다.
///
/// 순서가 중요하다 — 3단계(호스트에서 제거)를 먼저 하면 버튼이 죽은 채로 남는다.
///   1) 수령 팝업에 자체 Canvas  : 풀 캔버스(order 400)보다 위에 서게
///   2) 두 오버레이 Addressable  : 풀이 타입으로 찾을 수 있게
///   3) Tab_Match 버튼 재배선     : UnityEvent -> LobbyMatchTabPanel 코드 경로
///   4) OverlayHost에서 제거      : 상주 인스턴스 정리
/// </summary>
static class PoolRankGrowthMigration
{
    const string RankOverlay    = "Assets/Assets/Prefabs/UI/PooledUI/RankRewardOverlay.prefab";
    const string GrowthOverlay  = "Assets/Assets/Prefabs/UI/PooledUI/KeywordGrowthOverlay.prefab";
    const string ClaimPopup     = "Assets/Assets/Prefabs/UI/PooledUI/RewardClaimPopup.prefab";

    /// <summary>풀 uiRoot(Boot 캔버스)가 400이다. 수령 팝업은 그 위에 서야 한다.</summary>
    const int ClaimPopupSortingOrder = 410;

    [MenuItem("Tools/Lobby/B - 1. Claim Popup Canvas")]
    static void Step1_ClaimPopupCanvas()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(ClaimPopup);
        if (t_root == null) return;

        try
        {
            Canvas t_canvas = t_root.GetComponent<Canvas>() ?? t_root.AddComponent<Canvas>();

            // overrideSorting은 프로퍼티로 쓰면 유니티가 되돌린다 — 부모 캔버스가 없는 상태(프리팹 편집)에서는
            // 의미가 없다고 보고 무시하기 때문이다. 직렬화 값에 직접 박아야 실제 계층에서 살아난다.
            var t_so = new SerializedObject(t_canvas);
            t_so.FindProperty("m_OverrideSorting").boolValue = true;
            t_so.FindProperty("m_SortingOrder").intValue     = ClaimPopupSortingOrder;
            t_so.ApplyModifiedPropertiesWithoutUndo();

            if (t_root.GetComponent<GraphicRaycaster>() == null)
                t_root.AddComponent<GraphicRaycaster>();

            PrefabUtility.SaveAsPrefabAsset(t_root, ClaimPopup);
            Debug.Log($"[B-1] RewardClaimPopup Canvas 설정 — overrideSorting=1, order={ClaimPopupSortingOrder}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    [MenuItem("Tools/Lobby/B - 2. Addressable + UIPrefab Label")]
    static void Step2_Addressables()
    {
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null)
        {
            Debug.LogError("[B-2] Addressables 설정이 없다.");
            return;
        }

        AddressableAssetGroup t_group = t_settings.DefaultGroup;

        foreach (string t_path in new[] { RankOverlay, GrowthOverlay })
        {
            string t_guid = AssetDatabase.AssetPathToGUID(t_path);
            if (string.IsNullOrEmpty(t_guid))
            {
                Debug.LogError($"[B-2] 에셋을 못 찾음: {t_path}");
                continue;
            }

            AddressableAssetEntry t_entry = t_settings.CreateOrMoveEntry(t_guid, t_group);

            // 주소는 타입명으로 맞춘다 — 기존 오버레이 3종(CardRewardOverlay 등)과 같은 규약이라
            // 단독 씬 폴백(RuntimeOverlayPrefabs)이 쓰는 키와 어긋나지 않는다.
            t_entry.address = System.IO.Path.GetFileNameWithoutExtension(t_path);
            t_entry.SetLabel("UIPrefab", true, true);

            Debug.Log($"[B-2] Addressable 등록: {t_entry.address} (label UIPrefab)");
        }

        EditorUtility.SetDirty(t_settings);
        AssetDatabase.SaveAssets();
    }

    const string TabMatch    = "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Match.prefab";
    const string LobbyCanvas = "Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab";
    const string OverlayHost = "Assets/Assets/Prefabs/UI/LobbyUI/LobbyOverlayHost.prefab";

    [MenuItem("Tools/Lobby/B - 3. Rewire Tab_Match Buttons")]
    static void Step3_RewireButtons()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(TabMatch);
        if (t_root == null) return;

        try
        {
            var t_panel = t_root.GetComponent<LobbyMatchTabPanel>();
            if (t_panel == null)
            {
                Debug.LogError("[B-3] Tab_Match 루트에 LobbyMatchTabPanel이 없다.");
                return;
            }

            Button t_rank = TakeOverButton(t_root, "RankRewardPanel");
            Button t_growth = TakeOverButton(t_root, "KeywordGrowthPanel");

            var t_so = new SerializedObject(t_panel);
            if (t_rank != null) t_so.FindProperty("rankRewardButton").objectReferenceValue = t_rank;
            if (t_growth != null) t_so.FindProperty("keywordGrowthButton").objectReferenceValue = t_growth;
            t_so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(t_root, TabMatch);
            Debug.Log($"[B-3] 배선 이관 — rank={(t_rank != null ? t_rank.name : "못 찾음")}, "
                    + $"growth={(t_growth != null ? t_growth.name : "못 찾음")}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    /// <summary>지정한 타입의 Open을 부르던 UnityEvent 항목을 지우고, 그 버튼을 돌려준다.</summary>
    static Button TakeOverButton(GameObject _root, string _targetTypeName)
    {
        foreach (Button t_button in _root.GetComponentsInChildren<Button>(true))
        {
            var t_so = new SerializedObject(t_button);
            SerializedProperty t_calls = t_so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (t_calls == null) continue;

            for (int i = t_calls.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty t_call = t_calls.GetArrayElementAtIndex(i);
                string t_type = t_call.FindPropertyRelative("m_TargetAssemblyTypeName")?.stringValue ?? "";
                string t_method = t_call.FindPropertyRelative("m_MethodName")?.stringValue ?? "";

                if (!t_type.StartsWith(_targetTypeName) || t_method != "Open") continue;

                t_calls.DeleteArrayElementAtIndex(i);
                t_so.ApplyModifiedPropertiesWithoutUndo();
                return t_button;
            }
        }

        return null;
    }

    [MenuItem("Tools/Lobby/B - 4. Remove Panels From OverlayHost")]
    static void Step4_RemoveFromHost()
    {
        // 먼저 LobbyCanvas에 남은 죽은 m_Target 오버라이드를 걷는다 — B-3이 UnityEvent 항목을 지웠으므로
        // 이 오버라이드들은 가리킬 자리가 없다. 남겨 두면 다음 저장 때 고아로 굳는다.
        DropDeadTargetOverrides();

        GameObject t_root = PrefabUtility.LoadPrefabContents(OverlayHost);
        if (t_root == null) return;

        try
        {
            int t_removed = 0;
            foreach (Transform t_child in t_root.transform.Cast<Transform>().ToArray())
            {
                if (t_child.GetComponentInChildren<RankRewardPanel>(true) == null &&
                    t_child.GetComponentInChildren<KeywordGrowthPanel>(true) == null) continue;

                Debug.Log($"[B-4] OverlayHost에서 제거: {t_child.name}");
                Object.DestroyImmediate(t_child.gameObject);
                t_removed++;
            }

            if (t_removed > 0) PrefabUtility.SaveAsPrefabAsset(t_root, OverlayHost);
            Debug.Log($"[B-4] 제거 {t_removed}건");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    [MenuItem("Tools/Lobby/B - 5. Move DragController Wiring To Lobby")]
    static void Step5_DragControllerToLobby()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(LobbyCanvas);
        if (t_root == null) return;

        try
        {
            var t_tabs = t_root.GetComponentInChildren<LobbyTabController>(true);
            var t_drag = t_root.GetComponentInChildren<DeckEditDragController>(true);
            if (t_tabs == null || t_drag == null)
            {
                Debug.LogError($"[B-5] 못 찾음 — tabs={t_tabs != null}, drag={t_drag != null}");
                return;
            }

            // 1) 로비 소유 필드에 배선한다. 둘 다 LobbyCanvas 소유라 오버라이드가 생기지 않는다.
            var t_so = new SerializedObject(t_tabs);
            t_so.FindProperty("dragController").objectReferenceValue = t_drag;
            t_so.ApplyModifiedPropertiesWithoutUndo();

            // 2) 탭 인스턴스에 남은 dragController 오버라이드를 걷는다 — 이제 코드가 넘긴다.
            int t_dropped = 0;
            foreach (Transform t_child in t_root.GetComponentsInChildren<Transform>(true))
            {
                GameObject t_go = t_child.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t_go)) continue;
                if (PrefabUtility.GetOutermostPrefabInstanceRoot(t_go) != t_go) continue;

                PropertyModification[] t_mods = PrefabUtility.GetPropertyModifications(t_go);
                if (t_mods == null) continue;

                PropertyModification[] t_keep =
                    t_mods.Where(m => m.propertyPath != "dragController").ToArray();
                if (t_keep.Length == t_mods.Length) continue;

                t_dropped += t_mods.Length - t_keep.Length;
                PrefabUtility.SetPropertyModifications(t_go, t_keep);
                Debug.Log($"[B-5] {t_go.name}: dragController 오버라이드 제거");
            }

            PrefabUtility.SaveAsPrefabAsset(t_root, LobbyCanvas);
            Debug.Log($"[B-5] LobbyTabController.dragController 배선 완료, 오버라이드 {t_dropped}건 제거");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    const string BottomTabBar = "Assets/Assets/Prefabs/UI/Common/UI_BottomTabBar.prefab";

    /// <summary>
    /// 탭 줄의 자식에 <see cref="LayoutElement"/>를 저작해 둔다.
    ///
    /// 왜: 확대 연출은 <c>LayoutElement.flexibleWidth</c> 가중치를 트윈하는데(LobbyTabBarView),
    /// 프리팹에 그 컴포넌트가 없어서 런타임에 AddComponent로 붙는다. 그러면
    /// **에디터 계산(균등)과 런타임 계산(가중치)이 달라지고**, 플레이 뒤 저장하면 그 차이가
    /// 오버라이드 25건으로 굳는다(청소해도 매번 재생성됐다).
    ///
    /// flexibleWidth = 1 이 중립값이다 — 전부 1이면 균등, 선택된 칸만 코드가 1.25로 올린다.
    /// </summary>
    [MenuItem("Tools/Lobby/B - 7. Author Tab LayoutElements")]
    static void Step7_AuthorLayoutElements()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(BottomTabBar);
        if (t_root == null) return;

        try
        {
            HorizontalLayoutGroup t_group = t_root
                .GetComponentsInChildren<HorizontalLayoutGroup>(true)
                .FirstOrDefault(g => g.childControlWidth);

            if (t_group == null)
            {
                Debug.LogError("[B-7] childControlWidth가 켜진 HorizontalLayoutGroup을 못 찾았다.");
                return;
            }

            int t_added = 0, t_fixed = 0;
            foreach (Transform t_child in t_group.transform)
            {
                LayoutElement t_element = t_child.GetComponent<LayoutElement>();
                if (t_element == null)
                {
                    t_element = t_child.gameObject.AddComponent<LayoutElement>();
                    t_added++;
                }
                else t_fixed++;

                t_element.minWidth       = 0f;
                t_element.preferredWidth = 0f;
                t_element.flexibleWidth  = 1f;   // 중립 — 선택 칸만 코드가 올린다
                Debug.Log($"[B-7] {t_child.name}: LayoutElement flexibleWidth=1");
            }

            PrefabUtility.SaveAsPrefabAsset(t_root, BottomTabBar);
            Debug.Log($"[B-7] 부모={t_group.name} — 신규 {t_added}, 기존 갱신 {t_fixed}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    const string TabPack = "Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Pack.prefab";

    /// <summary>
    /// 복제용 템플릿을 프리팹에서 꺼 둔다.
    ///
    /// 왜: <c>Dot_Template</c>이 켜진 채로 HorizontalLayoutGroup의 자식이라, 그룹이 매 패스마다
    /// 위치를 다시 써서 오버라이드 4건이 계속 재생성된다. 런타임에는 어차피
    /// <c>PackCarouselDotsView</c>가 첫 갱신에서 끄므로(SetActive(false)) 저작 상태만 실제와 어긋나 있었다.
    /// </summary>
    [MenuItem("Tools/Lobby/B - 8. Deactivate Dot Template")]
    static void Step8_DeactivateTemplate()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(TabPack);
        if (t_root == null) return;

        try
        {
            Transform t_template = t_root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Dot_Template");

            if (t_template == null)
            {
                Debug.LogError("[B-8] Dot_Template을 못 찾았다.");
                return;
            }

            if (!t_template.gameObject.activeSelf)
            {
                Debug.Log("[B-8] 이미 꺼져 있다 — 건너뛴다.");
                return;
            }

            t_template.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(t_root, TabPack);
            Debug.Log("[B-8] Dot_Template 비활성화 — 레이아웃 자식에서 빠진다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    [MenuItem("Tools/Lobby/B - 6. Drop Dead Overrides")]
    static void Step6_DropDeadOverrides()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(LobbyCanvas);
        if (t_root == null) return;

        try
        {
            int t_dropped = 0;

            foreach (Transform t_child in t_root.GetComponentsInChildren<Transform>(true))
            {
                GameObject t_go = t_child.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t_go)) continue;
                if (PrefabUtility.GetOutermostPrefabInstanceRoot(t_go) != t_go) continue;

                PropertyModification[] t_mods = PrefabUtility.GetPropertyModifications(t_go);
                if (t_mods == null) continue;

                var t_keep = new System.Collections.Generic.List<PropertyModification>(t_mods.Length);
                foreach (PropertyModification t_mod in t_mods)
                {
                    if (IsDead(t_mod))
                    {
                        Debug.Log($"[B-6] {t_go.name}: 죽은 오버라이드 제거 — {t_mod.propertyPath}");
                        t_dropped++;
                        continue;
                    }

                    t_keep.Add(t_mod);
                }

                if (t_keep.Count != t_mods.Length)
                    PrefabUtility.SetPropertyModifications(t_go, t_keep.ToArray());
            }

            if (t_dropped > 0) PrefabUtility.SaveAsPrefabAsset(t_root, LobbyCanvas);
            Debug.Log($"[B-6] 죽은 오버라이드 {t_dropped}건 제거");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    /// <summary>가리킬 프로퍼티 자체가 사라진 오버라이드인가.
    ///
    /// 값이 같아서 무의미한 것(RedundantOverrideCleaner)과는 다른 축이다 — 여기는 <b>자리가 없는</b> 경우다.
    /// 예: 버튼의 UnityEvent 항목을 지웠는데 상위 인스턴스가 그 항목의 m_Target을 계속 채우고 있는 상태.
    /// 판단이 조금이라도 막히면 살린다.</summary>
    static bool IsDead(PropertyModification _mod)
    {
        if (_mod == null || _mod.target == null) return false;

        try
        {
            var t_so = new SerializedObject(_mod.target);
            return t_so.FindProperty(_mod.propertyPath) == null;
        }
        catch
        {
            return false;
        }
    }

    static void DropDeadTargetOverrides()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(LobbyCanvas);
        if (t_root == null) return;

        try
        {
            int t_dropped = 0;
            foreach (Transform t_child in t_root.GetComponentsInChildren<Transform>(true))
            {
                GameObject t_go = t_child.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t_go)) continue;
                if (PrefabUtility.GetOutermostPrefabInstanceRoot(t_go) != t_go) continue;

                PropertyModification[] t_mods = PrefabUtility.GetPropertyModifications(t_go);
                if (t_mods == null) continue;

                PropertyModification[] t_keep = t_mods
                    .Where(m => !(m.propertyPath.Contains("m_PersistentCalls") &&
                                  m.propertyPath.EndsWith(".m_Target") &&
                                  m.objectReference == null))
                    .ToArray();

                if (t_keep.Length == t_mods.Length) continue;

                t_dropped += t_mods.Length - t_keep.Length;
                PrefabUtility.SetPropertyModifications(t_go, t_keep);
            }

            if (t_dropped > 0) PrefabUtility.SaveAsPrefabAsset(t_root, LobbyCanvas);
            Debug.Log($"[B-4] LobbyCanvas 죽은 m_Target 오버라이드 {t_dropped}건 제거");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }
}
