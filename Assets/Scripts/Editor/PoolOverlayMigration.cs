using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// C안: CardDetailOverlay · RewardClaimPopup을 LobbyOverlayHost 상주에서 UIPoolManager 풀로 옮긴다.
///
/// <see cref="PoolRankGrowthMigration"/>(B안)과 같은 규약이다 — 한 단계 = 한 메뉴 = 한 커밋으로 두고
/// 매번 diff를 확인한다. 프리팹 저장은 되돌리기 어렵고, 유니티가 더티 상태를 함께 흘리는 사고가 반복됐다.
///
/// 순서가 중요하다 — 3단계(호스트에서 제거)를 먼저 하면 두 오버레이가 어디서도 안 뜬다.
///   1) 상세 프리팹 rect 베이크 : 호스트 인스턴스 오버라이드(-150/-75)를 프리팹 저작값으로
///   2) Addressable + 라벨      : 풀(DataLibrary)이 타입으로 찾을 수 있게
///   3) OverlayHost에서 제거    : 상주 인스턴스 정리
///
/// ⚠ 1·2를 건너뛰고 3만 돌리면 카드 롱프레스와 보상 수령이 통째로 죽는다.
/// </summary>
static class PoolOverlayMigration
{
    const string CardDetail  = "Assets/Assets/Prefabs/UI/PooledUI/CardDetailOverlay.prefab";
    const string ClaimPopup  = "Assets/Assets/Prefabs/UI/PooledUI/RewardClaimPopup.prefab";
    const string OverlayHost = "Assets/Assets/Prefabs/UI/LobbyUI/LobbyOverlayHost.prefab";

    /// <summary>호스트 인스턴스가 들고 있던 상세 오버레이의 rect. 상단 재화 바를 비켜 앉은 크기다
    /// (배경판이 바를 덮으면 강화 비용을 볼 수 없다 — CardDetailOverlayView.OnEnable 주석).
    ///
    /// 지금까지는 LobbyOverlayHost 인스턴스의 오버라이드로만 존재했다 → 풀이 프리팹에서 세우면 사라진다.
    /// 그래서 프리팹 저작값으로 옮긴다.</summary>
    static readonly Vector2 DetailSizeDelta       = new Vector2(0f, -150f);
    static readonly Vector2 DetailAnchoredPosition = new Vector2(0f, -75f);

    [MenuItem("Tools/Lobby/C - 1. Bake CardDetail Rect Into Prefab")]
    static void Step1_BakeDetailRect()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(CardDetail);
        if (t_root == null)
        {
            Debug.LogError($"[C-1] 프리팹을 못 찾음: {CardDetail}");
            return;
        }

        try
        {
            var t_rect = (RectTransform)t_root.transform;

            t_rect.anchorMin        = Vector2.zero;
            t_rect.anchorMax        = Vector2.one;
            t_rect.pivot            = new Vector2(0.5f, 0.5f);
            t_rect.sizeDelta        = DetailSizeDelta;
            t_rect.anchoredPosition = DetailAnchoredPosition;

            PrefabUtility.SaveAsPrefabAsset(t_root, CardDetail);
            Debug.Log($"[C-1] CardDetailOverlay rect 베이크 — sizeDelta={DetailSizeDelta}, " +
                      $"anchoredPosition={DetailAnchoredPosition}\n" +
                      "SafeArea 밖(풀 캔버스)으로 나가므로 노치 기기에서 상하 가장자리까지 덮는다 — " +
                      "전면 딤이라 그게 맞다(SafeAreaFitter 주석 참고). 노치 기기에서 한 번 눈으로 볼 것.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    [MenuItem("Tools/Lobby/C - 2. Addressable + UIPrefab Label")]
    static void Step2_Addressables()
    {
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null)
        {
            Debug.LogError("[C-2] Addressables 설정이 없다.");
            return;
        }

        AddressableAssetGroup t_group = t_settings.DefaultGroup;

        foreach (string t_path in new[] { CardDetail, ClaimPopup })
        {
            string t_guid = AssetDatabase.AssetPathToGUID(t_path);
            if (string.IsNullOrEmpty(t_guid))
            {
                Debug.LogError($"[C-2] 에셋을 못 찾음: {t_path}");
                continue;
            }

            AddressableAssetEntry t_entry = t_settings.CreateOrMoveEntry(t_guid, t_group);

            // 주소는 파일명으로 맞춘다 — 기존 오버레이(RankRewardOverlay 등)와 같은 규약이다.
            // 풀 조회는 주소가 아니라 라벨로 모아 컴포넌트 타입에 색인하므로(DataLibrary.LoadUIPrefab)
            // 주소가 타입명과 달라도(CardDetailOverlay ≠ CardDetailOverlayView) 동작한다.
            t_entry.address = System.IO.Path.GetFileNameWithoutExtension(t_path);
            t_entry.SetLabel("UIPrefab", true, true);

            Debug.Log($"[C-2] Addressable 등록: {t_entry.address} (label UIPrefab)");
        }

        EditorUtility.SetDirty(t_settings);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Lobby/C - 3. Remove Overlays From OverlayHost")]
    static void Step3_RemoveFromHost()
    {
        // 라벨 등록이 끝나기 전에 호스트에서 빼면 두 오버레이가 어디서도 안 뜬다.
        if (!IsLabeled(CardDetail) || !IsLabeled(ClaimPopup))
        {
            Debug.LogError("[C-3] C-2(Addressable + UIPrefab Label)를 먼저 돌릴 것 — " +
                           "라벨 없이 호스트에서 빼면 카드 상세·보상 수령이 통째로 죽는다.");
            return;
        }

        GameObject t_root = PrefabUtility.LoadPrefabContents(OverlayHost);
        if (t_root == null)
        {
            Debug.LogError($"[C-3] 프리팹을 못 찾음: {OverlayHost}");
            return;
        }

        try
        {
            int t_removed = 0;
            foreach (Transform t_child in t_root.transform.Cast<Transform>().ToArray())
            {
                if (t_child.GetComponentInChildren<CardDetailOverlayView>(true) == null &&
                    t_child.GetComponentInChildren<RewardClaimPopup>(true) == null) continue;

                Debug.Log($"[C-3] OverlayHost에서 제거: {t_child.name}");
                Object.DestroyImmediate(t_child.gameObject);
                t_removed++;
            }

            if (t_removed > 0) PrefabUtility.SaveAsPrefabAsset(t_root, OverlayHost);

            Debug.Log($"[C-3] 제거 {t_removed}건 — 호스트에 남는 것은 MatchDeckRoot · ScreenDim_Full 둘뿐이다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    /// <summary>UIPrefab 라벨이 실제로 붙었는가. 풀은 이 라벨로만 프리팹을 모은다(DataLibrary.LoadUIPrefab).</summary>
    static bool IsLabeled(string _path)
    {
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null) return false;

        string t_guid = AssetDatabase.AssetPathToGUID(_path);
        if (string.IsNullOrEmpty(t_guid)) return false;

        AddressableAssetEntry t_entry = t_settings.FindAssetEntry(t_guid);

        return t_entry != null && t_entry.labels != null && t_entry.labels.Contains("UIPrefab");
    }

    /// <summary>세 단계를 순서대로. 각 단계가 자기 로그를 남기므로 실패 지점은 콘솔에서 바로 보인다.</summary>
    [MenuItem("Tools/Lobby/C - All (1 → 2 → 3)")]
    static void RunAll()
    {
        Step1_BakeDetailRect();
        Step2_Addressables();
        Step3_RemoveFromHost();
    }
}
