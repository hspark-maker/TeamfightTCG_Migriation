using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>실제 보상 팝업 슬롯의 비동기 카드 표시와 재사용을 검사한다. 서버·시트는 건드리지 않는다.</summary>
public static class RewardCardSlotValidation
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Rewards/Validate Card Reward Slot")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying && !CardCatalog.IsReady && !CardArtCache.IsCatalogReady,
            "Run outside play mode before game initialization.");
        var specs = (Dictionary<int, CardSpec>)typeof(CardCatalog).GetField("s_specById", Static).GetValue(null);
        var addresses = (HashSet<string>)typeof(CardArtCache).GetField("s_addresses", Static).GetValue(null);
        var entries = (IDictionary)typeof(CardArtCache).GetField("s_entries", Static).GetValue(null);
        Require(specs.Count == 0 && addresses.Count == 0 && entries.Count == 0 && !CardArtCache.IsBusy,
            "Validation requires empty caches.");
        var ready = typeof(CardCatalog).GetField("<IsReady>k__BackingField", Static);
        var catalogReady = typeof(CardArtCache).GetField("s_catalogReady", Static);
        var access = typeof(CardArtCache).GetField("s_access", Static);
        object previousAccess = access.GetValue(null);
        var scene = EditorSceneManager.NewPreviewScene();
        var host = new GameObject("RewardCardSlotValidation");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
        host.SetActive(false);
        var texture = new Texture2D(8, 12);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 12), Vector2.one * 0.5f);
        try
        {
            var spec = new CardSpec(1, "RewardSlotTest", "Reward card", default, 10,
                default, 0, 0, 0, 0, 0, "", default, Array.Empty<string>());
            specs.Add(spec.Id, spec);
            ready.SetValue(null, true);
            catalogReady.SetValue(null, true);
            string address = CardArtCache.AddressOf(spec, 0);
            addresses.Add(address);
            var entryType = typeof(CardArtCache).GetNestedType("Entry", BindingFlags.NonPublic);
            object entry = Activator.CreateInstance(entryType, true);
            entryType.GetField("Address").SetValue(entry, address);
            entries.Add(address, entry); // 적재 완료를 아래에서 직접 통지해 늦은 응답도 검사한다.
            void Arrive()
            {
                entryType.GetField("Sprite").SetValue(entry, sprite);
                entryType.GetField("Complete").SetValue(entry, true);
                entryType.GetMethod("Notify").Invoke(entry, null);
            }
            int References() => (int)entryType.GetField("References").GetValue(entry);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Assets/Prefabs/UI/PooledUI/RewardClaimPopup.prefab");
            var popup = UnityEngine.Object.Instantiate(prefab, host.transform).GetComponent<RewardClaimPopup>();
            popup.gameObject.SetActive(false);
            var slots = (CurrencyRewardSlotView[])typeof(RewardClaimPopup).GetField("rewardSlots", Instance).GetValue(popup);
            var slot = slots[0];
            slot.Root.transform.SetParent(host.transform, false);
            host.SetActive(true);
            var line = new RewardLine(new AlbumRewardDef { rewardType = ERewardType.Card, rewardId = "1", amount = 3 });
            slot.Bind(line);
            Require(!slot.Icon.enabled && slot.Amount.text.Contains("Reward card") && References() == 1,
                "Pending art must keep the card name and quantity visible.");
            Arrive();
            Require(slot.Icon.enabled && slot.Icon.sprite == sprite && slot.Amount.text == "3",
                "Card art and quantity must appear when loading completes.");
            var binding = slot.Icon.GetComponent<CardArtBinding>();
            slot.Hide();
            // 일반 MonoBehaviour의 활성 콜백은 편집 모드에서 자동 실행되지 않는다.
            typeof(CardArtBinding).GetMethod("OnDisable", Instance).Invoke(binding, null);
            Require(References() == 0, "Hidden slots must release card art.");
            slot.Root.SetActive(true);
            typeof(CardArtBinding).GetMethod("OnEnable", Instance).Invoke(binding, null);
            Require(slot.Icon.enabled && slot.Icon.sprite == sprite && References() == 1,
                "Reopened slots must reacquire card art.");
            slot.Bind(sprite, 125);
            Arrive();
            Require(References() == 0 && slot.Icon.sprite == sprite && slot.Amount.text == "125",
                "An old card callback must not overwrite a reused currency slot.");
            slot.Bind(line);
            slot.Bind(new RewardLine(new AlbumRewardDef { rewardType = ERewardType.Card, rewardId = "invalid", amount = 2 }));
            Arrive();
            Require(!slot.Icon.enabled && slot.Amount.text.Contains("2") && References() == 0,
                "Invalid card data must retain text without the previous card image.");
            Debug.Log("[RewardCardSlotValidation] PASS: card art arrival, quantity, hide/reopen, currency reuse, invalid-card fallback.");
        }
        finally
        {
            foreach (var binding in host.GetComponentsInChildren<CardArtBinding>(true))
                CardArtBinding.Clear(binding.gameObject);
            UnityEngine.Object.DestroyImmediate(host);
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
            entries.Clear();
            addresses.Clear();
            specs.Clear();
            ready.SetValue(null, false);
            catalogReady.SetValue(null, false);
            access.SetValue(null, previousAccess);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
