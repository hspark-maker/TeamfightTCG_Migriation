using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>CardData의 직접 Sprite 참조를 Addressables 참조(*Ref)로 옮기는 일회성 이관 도구.
///
/// 왜 필요한가: 빌드 3개 씬이 전부 CardRegistry.asset을 직참조하고, 그게 CardData 40개를 물고,
/// 각 CardData가 Sprite를 직참조한다 → 부팅하는 순간 전 카드 아트가 메모리에 올라온다.
/// 사슬을 끊으려면 **구 Sprite 필드를 비우는 것까지** 해야 한다. 참조만 추가하고 구 필드를 남기면
/// 강참조가 그대로라 아무것도 절약되지 않는다.
///
/// 안전장치: 전수 검증을 먼저 돌려 하나라도 걸리면 **아무것도 바꾸지 않고** 중단한다.
/// 되돌리기는 git이다(에셋 40개 + Addressables 그룹 파일).</summary>
static class CardArtAddressableBake
{
    const string CardDataRoot = "Assets/SO/Cards";
    const string GroupName    = "CardArt";
    const string CardLabel    = "Cards";

    /// <summary>한 카드에서 옮길 아트 한 칸. stage 0 = 미진화(battleImage), -1 = deckPreview.</summary>
    readonly struct ArtSlot
    {
        public readonly CardData Card;
        public readonly int      Stage;
        public readonly Sprite   Sprite;

        public ArtSlot(CardData _card, int _stage, Sprite _sprite)
        {
            Card = _card; Stage = _stage; Sprite = _sprite;
        }

        public string Label => Stage == -1 ? "deckPreview"
                             : Stage == 0  ? "battleImage"
                             : $"evolvedArts[{Stage - 1}]";
    }

    [MenuItem("Tools/Assets/Cards/Preview Card Art Addressable Bake")]
    static void Preview()
    {
        if (!TryCollect(out List<ArtSlot> t_slots, out List<Sprite> t_sprites)) return;

        foreach (ArtSlot t_slot in t_slots)
            Debug.Log($"[CardArtBake] {t_slot.Card.name}.{t_slot.Label} -> {AssetDatabase.GetAssetPath(t_slot.Sprite)}");

        Debug.Log($"[CardArtBake] 검증 완료: 카드 {t_slots.Select(_s => _s.Card).Distinct().Count()}장, " +
                  $"아트 칸 {t_slots.Count}개, 고유 스프라이트 {t_sprites.Count}장");
    }

    [MenuItem("Tools/Assets/Cards/Bake Card Art To Addressables")]
    static void Bake()
    {
        if (!TryCollect(out List<ArtSlot> t_slots, out List<Sprite> t_sprites)) return;
        if (t_slots.Count == 0)
        {
            Debug.Log("[CardArtBake] 옮길 아트가 없다 — 이미 이관된 상태다.");
            return;
        }

        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup t_group = EnsureGroup(t_settings);
        if (t_group == null) return;

        // 1) 스프라이트를 Addressables 그룹에 등록한다. 주소는 파일명(카드 아트는 주소로 조회하지 않아 표시용이다).
        foreach (Sprite t_sprite in t_sprites)
        {
            string t_guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t_sprite));
            AddressableAssetEntry t_entry = t_settings.CreateOrMoveEntry(t_guid, t_group);
            t_entry.address = t_sprite.name;
            t_entry.SetLabel(CardLabel, true, true);
        }

        // 2) CardData의 *Ref에 guid를 쓰고 구 Sprite 필드를 비운다. 둘은 반드시 같은 트랜잭션이어야 한다 —
        //    비우지 않으면 강참조가 남아 이관 효과가 0이고, Ref 없이 비우면 그림이 사라진다.
        int t_baked = 0;
        foreach (IGrouping<CardData, ArtSlot> t_byCard in t_slots.GroupBy(_s => _s.Card))
        {
            var t_so = new SerializedObject(t_byCard.Key);

            foreach (ArtSlot t_slot in t_byCard)
            {
                SerializedProperty t_sprite = FindSpriteProp(t_so, t_slot.Stage);
                SerializedProperty t_ref    = FindRefProp(t_so, t_slot.Stage);
                if (t_sprite == null || t_ref == null)
                {
                    Debug.LogError($"[CardArtBake] 직렬화 프로퍼티를 못 찾음: {t_byCard.Key.name}.{t_slot.Label}");
                    continue;
                }

                t_ref.FindPropertyRelative("m_AssetGUID").stringValue =
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t_slot.Sprite));
                t_sprite.objectReferenceValue = null;
                t_baked++;
            }

            t_so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(t_byCard.Key);
        }

        t_settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CardArtBake] 이관 완료: 아트 칸 {t_baked}개, Addressables 등록 {t_sprites.Count}장 (라벨 {CardLabel})");
    }

    /// <summary>옮길 대상을 모으고 전수 검증한다. 하나라도 문제면 false — 호출부는 아무것도 바꾸지 않는다.</summary>
    static bool TryCollect(out List<ArtSlot> _slots, out List<Sprite> _sprites)
    {
        _slots = new List<ArtSlot>();
        _sprites = new List<Sprite>();

        if (AddressableAssetSettingsDefaultObject.Settings == null)
        {
            Debug.LogError("[CardArtBake] Addressables 설정이 없다.");
            return false;
        }

        string[] t_guids = AssetDatabase.FindAssets("t:CardData", new[] { CardDataRoot });
        if (t_guids.Length == 0)
        {
            Debug.LogError($"[CardArtBake] {CardDataRoot} 아래 CardData 에셋이 없다.");
            return false;
        }

        var t_unique = new Dictionary<string, Sprite>();
        foreach (string t_guid in t_guids.OrderBy(_g => _g))
        {
            var t_card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(t_guid));
            if (t_card == null) continue;

            Collect(t_card, -1, t_card.deckPreview, _slots, t_unique);
            Collect(t_card, 0, t_card.battleImage, _slots, t_unique);

            for (int t_stage = 1; t_stage <= CardData.MaxEvolutionStage; t_stage++)
            {
                CardArtSet t_art = t_card.GetEvolvedArt(t_stage);
                if (t_art != null) Collect(t_card, t_stage, t_art.battleImage, _slots, t_unique);
            }
        }

        // 스프라이트 시트(한 텍스처에 여러 스프라이트)는 guid만으로 조각을 특정할 수 없다.
        // CardArtCache가 guid 하나로 로드하는 구조라, 시트가 섞이면 다른 조각이 뜬다.
        foreach (Sprite t_sprite in t_unique.Values)
        {
            string t_path = AssetDatabase.GetAssetPath(t_sprite);
            if (AssetDatabase.LoadAllAssetRepresentationsAtPath(t_path).Length > 1)
            {
                Debug.LogError($"[CardArtBake] 스프라이트 시트라 이관 불가(조각 특정 불가): {t_path}");
                return false;
            }
        }

        _sprites = t_unique.Values.ToList();
        return true;
    }

    static void Collect(CardData _card, int _stage, Sprite _sprite,
                        List<ArtSlot> _slots, Dictionary<string, Sprite> _unique)
    {
        if (_sprite == null) return;   // 이미 이관됐거나 원래 빈 칸

        _slots.Add(new ArtSlot(_card, _stage, _sprite));
        _unique[AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_sprite))] = _sprite;
    }

    static SerializedProperty FindSpriteProp(SerializedObject _so, int _stage)
        => _stage == -1 ? _so.FindProperty("deckPreview")
         : _stage == 0  ? _so.FindProperty("battleImage")
         : _so.FindProperty("evolvedArts").GetArrayElementAtIndex(_stage - 1).FindPropertyRelative("battleImage");

    static SerializedProperty FindRefProp(SerializedObject _so, int _stage)
        => _stage == -1 ? _so.FindProperty("deckPreviewRef")
         : _stage == 0  ? _so.FindProperty("battleImageRef")
         : _so.FindProperty("evolvedArts").GetArrayElementAtIndex(_stage - 1).FindPropertyRelative("battleImageRef");

    static AddressableAssetGroup EnsureGroup(AddressableAssetSettings _settings)
    {
        AddressableAssetGroup t_group = _settings.FindGroup(GroupName);
        if (t_group != null) return t_group;

        t_group = _settings.CreateGroup(GroupName, false, false, false, null,
                                        typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        if (t_group == null) Debug.LogError($"[CardArtBake] 그룹 생성 실패: {GroupName}");
        return t_group;
    }
}
