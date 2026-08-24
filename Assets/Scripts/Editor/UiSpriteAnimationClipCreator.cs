using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UI 스프라이트 시트에서 컨트롤러 없이 재생 가능한 AnimationClip을 만든다.
/// EmoteStickerView 계약에 맞춰 루트 Image.m_Sprite만 애니메이션하며, 마지막 프레임도 한 박 유지하도록
/// 마지막 프레임을 복제한 종료 키를 추가한다.
/// </summary>
public static class UiSpriteAnimationClipCreator
{
    const string MenuPath = "Assets/UI 애니메이션 만들기";
    [MenuItem(MenuPath, false, 2000)]
    static void CreateFromMenu()
    {
        Dictionary<string, List<Sprite>> t_groups = CollectSelection();
        if (t_groups.Count == 0) return;

        int t_created = 0;
        foreach (KeyValuePair<string, List<Sprite>> t_group in t_groups.OrderBy(_pair => _pair.Key, StringComparer.Ordinal))
        {
            List<Sprite> t_frames = t_group.Value
                                                  .Where(_sprite => _sprite != null)
                                                  .Distinct()
                                                  .ToList();
            t_frames.Sort(CompareSprites);
            if (t_frames.Count == 0) continue;

            string t_clipPath = GetClipPath(t_group.Key);

            AnimationClip t_clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(t_clipPath);
            if (t_clip != null && !EditorUtility.DisplayDialog(
                    "UI 애니메이션 덮어쓰기",
                    $"{t_clipPath}\n\n기존 클립의 커브와 타이밍을 선택한 스프라이트로 교체할까요?",
                    "덮어쓰기",
                    "취소"))
                continue;

            bool t_isNew = t_clip == null;
            if (!t_isNew) Undo.RecordObject(t_clip, "UI 애니메이션 덮어쓰기");
            t_clip = UiSpriteAnimationClipWriter.CreateOrUpdateClip(t_group.Key, t_frames);
            if (t_clip == null) continue;
            t_created++;
            Selection.activeObject = t_clip;
            Debug.Log($"[UI Animation] {(t_isNew ? "생성" : "덮어쓰기")}: {t_clipPath} ({t_frames.Count} frames)", t_clip);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (t_created == 0) Debug.Log("[UI Animation] 생성하거나 변경한 클립이 없습니다.");
    }

    [MenuItem(MenuPath, true)]
    static bool ValidateCreateClips()
    {
        return Selection.objects.Any(_asset => _asset is Sprite || _asset is Texture2D);
    }

    static Dictionary<string, List<Sprite>> CollectSelection()
    {
        var t_groups = new Dictionary<string, List<Sprite>>(StringComparer.Ordinal);
        foreach (UnityEngine.Object t_asset in Selection.objects)
        {
            if (t_asset is Sprite t_sprite)
            {
                Add(t_groups, AssetDatabase.GetAssetPath(t_sprite), t_sprite);
                continue;
            }

            if (!(t_asset is Texture2D)) continue;
            string t_path = AssetDatabase.GetAssetPath(t_asset);
            foreach (Sprite t_child in AssetDatabase.LoadAllAssetsAtPath(t_path).OfType<Sprite>())
                Add(t_groups, t_path, t_child);
        }

        if (t_groups.Count == 0)
            Debug.LogWarning("[UI Animation] Sprite 또는 Sprite가 포함된 Texture2D를 선택하세요.");
        return t_groups;
    }

    static string GetClipPath(string _sourcePath)
    {
        string t_directory = Path.GetDirectoryName(_sourcePath)?.Replace('\\', '/');
        string t_textureName = Path.GetFileNameWithoutExtension(_sourcePath);
        return $"{t_directory}/Anim_{t_textureName}.anim";
    }

    static void Add(Dictionary<string, List<Sprite>> _groups, string _path, Sprite _sprite)
    {
        if (string.IsNullOrEmpty(_path) || _sprite == null) return;
        if (!_groups.TryGetValue(_path, out List<Sprite> t_sprites))
        {
            t_sprites = new List<Sprite>();
            _groups.Add(_path, t_sprites);
        }
        t_sprites.Add(_sprite);
    }

    static int CompareSprites(Sprite _left, Sprite _right)
    {
        int t_nameOrder = EditorUtility.NaturalCompare(_left.name, _right.name);
        if (t_nameOrder != 0) return t_nameOrder;

        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_left, out string _, out long t_leftId);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_right, out string _, out long t_rightId);
        return t_leftId.CompareTo(t_rightId);
    }

}
