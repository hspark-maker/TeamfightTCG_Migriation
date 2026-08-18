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
    const string RankOverlay    = "Assets/Assets/Prefabs/UI/LobbyUI/RankRewardOverlay.prefab";
    const string GrowthOverlay  = "Assets/Assets/Prefabs/UI/LobbyUI/KeywordGrowthOverlay.prefab";
    const string ClaimPopup     = "Assets/Assets/Prefabs/UI/LobbyUI/RewardClaimPopup.prefab";

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
}
