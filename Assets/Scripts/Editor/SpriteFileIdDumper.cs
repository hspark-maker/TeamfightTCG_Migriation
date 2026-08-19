using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>선택한 텍스처(멀티 스프라이트)의 하위 스프라이트 **fileID**를 콘솔에 찍는다.
///
/// 왜 필요한가: 에셋·프리팹을 YAML로 직접 저작할 때 슬라이스된 스프라이트를 참조하려면
/// `{fileID: ..., guid: ..., type: 3}`의 fileID가 필요한데, 이 값은 .meta에 안 적혀 있다
/// (내부 ID 테이블이 비어 있으면 Unity가 spriteID에서 파생시킨다). 손으로는 계산할 수 없어서
/// AssetDatabase에 물어보는 창구를 하나 둔다.</summary>
public static class SpriteFileIdDumper
{
    /// <summary>선택 없이도 쓰는 경로 고정판 — 이모트 시트는 여기 한 폴더에만 들어온다.</summary>
    [MenuItem("Tools/이모트 스프라이트 fileID 출력")]
    static void DumpEmotes()
    {
        foreach (string t_guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Assets/Images/Emotes" }))
            DumpPath(AssetDatabase.GUIDToAssetPath(t_guid));
    }

    /// <summary>이모트 클립이 실제로 어떤 커브를 들고 임포트됐는지 확인한다 —
    /// YAML이 그럴듯해 보여도 바인딩이 죽으면 화면에서는 조용히 아무 일도 안 일어난다.</summary>
    [MenuItem("Tools/이모트 클립 바인딩 출력")]
    static void DumpEmoteClips()
    {
        foreach (string t_guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Assets/Images/Emotes" }))
        {
            string t_path = AssetDatabase.GUIDToAssetPath(t_guid);
            var t_clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(t_path);
            if (t_clip == null) { Debug.LogWarning($"[클립] {t_path} 로드 실패"); continue; }

            var t_sb = new StringBuilder();
            t_sb.AppendLine($"[클립] {t_path} length={t_clip.length} legacy={t_clip.legacy} loop={t_clip.isLooping}");

            foreach (EditorCurveBinding t_b in AnimationUtility.GetObjectReferenceCurveBindings(t_clip))
            {
                t_sb.AppendLine($"  PPtr path='{t_b.path}' type={t_b.type} prop={t_b.propertyName}");
                foreach (ObjectReferenceKeyframe t_k in AnimationUtility.GetObjectReferenceCurve(t_clip, t_b))
                    t_sb.AppendLine($"    t={t_k.time} -> {(t_k.value != null ? t_k.value.name : "null")}");
            }
            foreach (EditorCurveBinding t_b in AnimationUtility.GetCurveBindings(t_clip))
                t_sb.AppendLine($"  float path='{t_b.path}' type={t_b.type} prop={t_b.propertyName}");

            Debug.Log(t_sb.ToString());
        }
    }

    [MenuItem("Tools/스프라이트 fileID 출력")]
    static void Dump()
    {
        Object t_selected = Selection.activeObject;
        if (t_selected == null)
        {
            Debug.LogWarning("[SpriteFileIdDumper] 텍스처를 먼저 선택해라.");
            return;
        }

        string t_path = AssetDatabase.GetAssetPath(t_selected);
        if (string.IsNullOrEmpty(t_path))
        {
            Debug.LogWarning("[SpriteFileIdDumper] 에셋 경로를 못 찾았다.");
            return;
        }

        DumpPath(t_path);
    }

    static void DumpPath(string _path)
    {
        var t_sb = new StringBuilder();
        t_sb.AppendLine($"[SpriteFileIdDumper] {_path}");

        foreach (Object t_sub in AssetDatabase.LoadAllAssetsAtPath(_path))
        {
            if (!(t_sub is Sprite)) continue;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t_sub, out string t_guid, out long t_fileId)) continue;

            t_sb.AppendLine($"  {t_sub.name} -> {{fileID: {t_fileId}, guid: {t_guid}, type: 3}}");
        }

        Debug.Log(t_sb.ToString());
    }
}
