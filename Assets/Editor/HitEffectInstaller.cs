using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CardView.prefab에 피격 연출(HitEffect)을 심고 CardAnimator.hitEffect에 배선.
/// 구조: HitEffect(루트, HitEffectView) ─ Boom(SpriteRenderer, PictoIcon_Boom) / DmgText(TMP 데미지 숫자).
/// 메뉴 1회 실행. 이미 있으면 갱신.
/// </summary>
public static class HitEffectInstaller
{
    const string PrefabPath = "Assets/Assets/Prefabs/CardView.prefab";
    const string BoomSprite = "Assets/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_PictoIcons/256/PictoIcon_Boom.Png";

    [MenuItem("Tools/Install HitEffect Into CardView")]
    public static void Install()
    {
        var t_boom = AssetDatabase.LoadAssetAtPath<Sprite>(BoomSprite);
        if (t_boom == null) { Debug.LogError($"[HitEffect] 붐 스프라이트 없음: {BoomSprite}"); return; }

        GameObject t_root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (t_root == null) { Debug.LogError($"[HitEffect] 프리팹 못 엶: {PrefabPath}"); return; }

        try
        {
            Transform t_exist = t_root.transform.Find("HitEffect");
            if (t_exist != null) Object.DestroyImmediate(t_exist.gameObject);

            // 루트.
            var t_hit = new GameObject("HitEffect");
            t_hit.transform.SetParent(t_root.transform, false);
            var t_view = t_hit.AddComponent<HitEffectView>();

            // Boom 자식(스프라이트).
            var t_boomGo = new GameObject("Boom");
            t_boomGo.transform.SetParent(t_hit.transform, false);
            var t_sr = t_boomGo.AddComponent<SpriteRenderer>();
            t_sr.sprite           = t_boom;
            t_sr.sharedMaterial   = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");   // 아틀라스 머티리얼 대신 기본 스프라이트 머티리얼(안 보이는 문제 해결).
            t_sr.color            = new Color(1f, 0.257f, 0f, 0.8f);
            t_sr.sortingLayerName = "Card";
            t_sr.sortingOrder     = 20;
            t_sr.drawMode         = SpriteDrawMode.Simple;
            t_boomGo.transform.localScale = Vector3.one * 1.687f;   // 씬 붐과 유사 크기.

            // DmgText 자식(TMP 데미지 숫자).
            var t_txtGo = new GameObject("DmgText");
            t_txtGo.transform.SetParent(t_hit.transform, false);
            t_txtGo.transform.localPosition = new Vector3(0f, -0.35f, 0f);
            var t_tmp = t_txtGo.AddComponent<TextMeshPro>();
            t_tmp.text                 = "-0";
            t_tmp.fontSize             = 10f;
            t_tmp.alignment            = TextAlignmentOptions.Center;
            t_tmp.color                = Color.white;
            t_tmp.fontStyle            = FontStyles.Bold;
            t_tmp.rectTransform.sizeDelta = new Vector2(4f, 1.5f);
            var t_tmpRend = t_tmp.GetComponent<MeshRenderer>();
            if (t_tmpRend != null) { t_tmpRend.sortingLayerName = "Card"; t_tmpRend.sortingOrder = 21; }
            t_txtGo.SetActive(false);

            // HitEffectView 배선.
            var t_vso = new SerializedObject(t_view);
            t_vso.FindProperty("boom").objectReferenceValue    = t_boomGo.transform;
            t_vso.FindProperty("sr").objectReferenceValue      = t_sr;
            t_vso.FindProperty("dmgText").objectReferenceValue = t_tmp;
            t_vso.ApplyModifiedPropertiesWithoutUndo();

            t_hit.SetActive(false);

            // CardAnimator.hitEffect 배선.
            var t_anim = t_root.GetComponent<CardAnimator>();
            if (t_anim == null) { Debug.LogError("[HitEffect] 루트에 CardAnimator 없음"); return; }
            var t_aso = new SerializedObject(t_anim);
            t_aso.FindProperty("hitEffect").objectReferenceValue = t_view;
            t_aso.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(t_root, PrefabPath);
            Debug.Log("[HitEffect] CardView.prefab에 HitEffect(붐+데미지숫자) 심고 배선 완료.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }
}
