using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>확인창 없이 UI sprite 클립을 생성하거나 기존 GUID를 유지해 갱신하는 순수 저작 경로.</summary>
public static class UiSpriteAnimationClipWriter
{
    const float FrameInterval = 0.15f;

    public static AnimationClip CreateOrUpdateClip(string _sourcePath, IReadOnlyList<Sprite> _frames)
    {
        if (string.IsNullOrEmpty(_sourcePath) || _frames == null || _frames.Count == 0) return null;

        List<Sprite> t_frames = _frames.Where(_sprite => _sprite != null).Distinct().ToList();
        t_frames.Sort(CompareSprites);
        if (t_frames.Count == 0) return null;

        string t_directory = Path.GetDirectoryName(_sourcePath)?.Replace('\\', '/');
        string t_textureName = Path.GetFileNameWithoutExtension(_sourcePath);
        string t_clipPath = $"{t_directory}/Anim_{t_textureName}.anim";
        AnimationClip t_clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(t_clipPath);
        if (t_clip == null && AssetDatabase.LoadMainAssetAtPath(t_clipPath) != null)
        {
            Debug.LogError($"[UI Animation] 같은 경로에 AnimationClip이 아닌 에셋이 있습니다: {t_clipPath}");
            return null;
        }

        if (t_clip == null)
        {
            t_clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(t_clipPath) };
            ConfigureClip(t_clip, t_frames);
            AssetDatabase.CreateAsset(t_clip, t_clipPath);
        }
        else
        {
            ConfigureClip(t_clip, t_frames);
        }

        EditorUtility.SetDirty(t_clip);
        return t_clip;
    }

    /// <summary>에셋 생성 없이 대상 클립의 UI sprite 커브와 반복 설정만 구성한다.</summary>
    public static bool ConfigureClip(AnimationClip _clip, IReadOnlyList<Sprite> _frames)
    {
        if (_clip == null || _frames == null || _frames.Count == 0) return false;
        List<Sprite> t_frames = _frames.Where(_sprite => _sprite != null).Distinct().ToList();
        t_frames.Sort(CompareSprites);
        if (t_frames.Count == 0) return false;

        ClearCurves(_clip);
        _clip.frameRate = 1f / FrameInterval;
        _clip.wrapMode = WrapMode.Loop;
        var t_binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(Image),
            propertyName = "m_Sprite"
        };
        var t_keys = new ObjectReferenceKeyframe[t_frames.Count + 1];
        for (int i = 0; i < t_frames.Count; i++)
            t_keys[i] = new ObjectReferenceKeyframe { time = i * FrameInterval, value = t_frames[i] };
        t_keys[t_frames.Count] = new ObjectReferenceKeyframe
        {
            time = t_frames.Count * FrameInterval,
            value = t_frames[t_frames.Count - 1]
        };
        AnimationUtility.SetObjectReferenceCurve(_clip, t_binding, t_keys);

        AnimationClipSettings t_settings = AnimationUtility.GetAnimationClipSettings(_clip);
        t_settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(_clip, t_settings);
        return true;
    }

    static int CompareSprites(Sprite _left, Sprite _right)
    {
        int t_nameOrder = NaturalCompare(_left.name, _right.name);
        if (t_nameOrder != 0) return t_nameOrder;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_left, out string _, out long t_leftId);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_right, out string _, out long t_rightId);
        return t_leftId.CompareTo(t_rightId);
    }

    static int NaturalCompare(string _left, string _right)
    {
        int t_left = 0;
        int t_right = 0;
        while (t_left < _left.Length && t_right < _right.Length)
        {
            bool t_leftDigit = char.IsDigit(_left[t_left]);
            bool t_rightDigit = char.IsDigit(_right[t_right]);
            if (t_leftDigit && t_rightDigit)
            {
                long t_leftNumber = 0;
                long t_rightNumber = 0;
                while (t_left < _left.Length && char.IsDigit(_left[t_left]))
                {
                    int t_digit = _left[t_left++] - '0';
                    t_leftNumber = t_leftNumber > (long.MaxValue - t_digit) / 10L
                        ? long.MaxValue
                        : t_leftNumber * 10L + t_digit;
                }
                while (t_right < _right.Length && char.IsDigit(_right[t_right]))
                {
                    int t_digit = _right[t_right++] - '0';
                    t_rightNumber = t_rightNumber > (long.MaxValue - t_digit) / 10L
                        ? long.MaxValue
                        : t_rightNumber * 10L + t_digit;
                }
                int t_numberOrder = t_leftNumber.CompareTo(t_rightNumber);
                if (t_numberOrder != 0) return t_numberOrder;
                continue;
            }

            int t_charOrder = _left[t_left].CompareTo(_right[t_right]);
            if (t_charOrder != 0) return t_charOrder;
            t_left++;
            t_right++;
        }
        return (_left.Length - t_left).CompareTo(_right.Length - t_right);
    }

    static void ClearCurves(AnimationClip _clip)
    {
        foreach (EditorCurveBinding t_binding in AnimationUtility.GetCurveBindings(_clip))
            AnimationUtility.SetEditorCurve(_clip, t_binding, null);
        foreach (EditorCurveBinding t_binding in AnimationUtility.GetObjectReferenceCurveBindings(_clip))
            AnimationUtility.SetObjectReferenceCurve(_clip, t_binding, null);
    }
}
