using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 중첩 프리팹 인스턴스에 걸린 **무의미한 오버라이드**(값이 원본과 이미 같은 것)만 골라 지운다.
///
/// 왜 "값이 같은 것"만 지우나 — 그것만이 지워도 화면이 바뀌지 않는다고 증명 가능한 집합이기 때문이다.
/// 값이 다른 오버라이드는 누군가의 저작 의도이므로 여기서 판단하지 않는다(프리팹으로 흡수할지는 사람이 정한다).
///
/// 인스턴스 루트의 Transform·m_Name은 건드리지 않는다 — 유니티가 저장할 때마다 다시 쓰므로
/// 지워봐야 diff만 왕복한다.
/// </summary>
static class RedundantOverrideCleaner
{
    const string LobbyCanvasPath = "Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab";

    [MenuItem("Tools/Lobby/Redundant Overrides - Dry Run")]
    static void DryRun() => Run(LobbyCanvasPath, false);

    [MenuItem("Tools/Lobby/Redundant Overrides - Apply")]
    static void Apply() => Run(LobbyCanvasPath, true);

    static void Run(string _prefabPath, bool _apply)
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(_prefabPath);
        if (t_root == null)
        {
            Debug.LogError($"[RedundantOverrideCleaner] 프리팹을 열지 못했다: {_prefabPath}");
            return;
        }

        var t_report = new StringBuilder();
        int t_before = 0, t_removed = 0, t_kept = 0;

        try
        {
            foreach (Transform t_child in t_root.GetComponentsInChildren<Transform>(true))
            {
                GameObject t_go = t_child.gameObject;
                if (t_go == t_root) continue;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t_go)) continue;

                // **이 프리팹이 소유한 오버라이드만 만진다.** 중첩의 중첩(예: Tab_Deck 안의 Slot_0)에서
                // GetPropertyModifications가 돌려주는 값은 그 자식 프리팹 파일의 소유다 —
                // 여기서 손대면 남의 집 오버라이드를 이 파일로 끌어와 새로 쓰게 된다.
                if (PrefabUtility.GetOutermostPrefabInstanceRoot(t_go) != t_go) continue;

                PropertyModification[] t_mods = PrefabUtility.GetPropertyModifications(t_go);
                if (t_mods == null || t_mods.Length == 0) continue;

                var t_keep = new List<PropertyModification>(t_mods.Length);
                var t_drop = new List<PropertyModification>();

                foreach (PropertyModification t_mod in t_mods)
                {
                    if (IsInstanceRootBookkeeping(t_go, t_mod) || !IsRedundant(t_mod))
                        t_keep.Add(t_mod);
                    else
                        t_drop.Add(t_mod);
                }

                t_before += t_mods.Length;
                t_removed += t_drop.Count;
                t_kept += t_keep.Count;

                t_report.AppendLine($"  {t_go.name,-24} {t_mods.Length,4} -> {t_keep.Count,4}   (제거 {t_drop.Count})");
                foreach (PropertyModification t_d in t_drop)
                    t_report.AppendLine($"      - {Describe(t_d)}");

                if (_apply && t_drop.Count > 0)
                    PrefabUtility.SetPropertyModifications(t_go, t_keep.ToArray());
            }

            if (_apply && t_removed > 0)
                PrefabUtility.SaveAsPrefabAsset(t_root, _prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }

        Debug.Log($"[RedundantOverrideCleaner] {(_apply ? "APPLY" : "DRY RUN")} {_prefabPath}\n"
                + $"오버라이드 {t_before} -> {t_kept} (무의미 {t_removed} 제거)\n{t_report}");
    }

    /// <summary>인스턴스 루트의 Transform 값과 이름은 유니티가 늘 다시 쓴다 — 판단 대상에서 뺀다.</summary>
    static bool IsInstanceRootBookkeeping(GameObject _instanceRoot, PropertyModification _mod)
    {
        if (_mod.target == null) return true;

        if (_mod.propertyPath == "m_Name") return true;

        Object t_sourceTransform = PrefabUtility.GetCorrespondingObjectFromSource(_instanceRoot.transform);
        return t_sourceTransform != null && _mod.target == t_sourceTransform;
    }

    /// <summary>이 오버라이드가 원본이 이미 갖고 있는 값과 같은가.
    /// 판단이 조금이라도 막히면 false — 확실히 무의미한 것만 지운다.</summary>
    static bool IsRedundant(PropertyModification _mod)
    {
        try { return IsRedundantCore(_mod); }
        catch { return false; }
    }

    static bool IsRedundantCore(PropertyModification _mod)
    {
        if (_mod.target == null) return false;

        var t_so = new SerializedObject(_mod.target);
        SerializedProperty t_prop = t_so.FindProperty(_mod.propertyPath);
        if (t_prop == null) return false;   // 못 읽으면 손대지 않는다

        switch (t_prop.propertyType)
        {
            // Boolean은 boolValue로 읽는다 — longValue를 쓰면 유니티가 "type is not a supported int value"를
            // 콘솔에 흘리고 판단도 못 한다(예외가 아니라 로그라 try로도 안 잡힌다).
            case SerializedPropertyType.Boolean:
                return int.TryParse(_mod.value, out int t_b) && (t_b != 0) == t_prop.boolValue;

            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.Character:
                return IsIntBacked(t_prop)
                    && long.TryParse(_mod.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t_l)
                    && t_l == t_prop.longValue;

            case SerializedPropertyType.Float:
                return t_prop.numericType == SerializedPropertyNumericType.Float
                    && float.TryParse(_mod.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float t_f)
                    && t_f.Equals(t_prop.floatValue);

            case SerializedPropertyType.String:
                return _mod.value == t_prop.stringValue;

            case SerializedPropertyType.ObjectReference:
                return _mod.objectReference == t_prop.objectReferenceValue;

            default:
                return false;   // 판단 못 하는 타입은 보존(Enum·Vector·Color 등)
        }
    }

    static bool IsIntBacked(SerializedProperty _prop)
    {
        switch (_prop.numericType)
        {
            case SerializedPropertyNumericType.Int8:
            case SerializedPropertyNumericType.UInt8:
            case SerializedPropertyNumericType.Int16:
            case SerializedPropertyNumericType.UInt16:
            case SerializedPropertyNumericType.Int32:
            case SerializedPropertyNumericType.UInt32:
            case SerializedPropertyNumericType.Int64:
            case SerializedPropertyNumericType.UInt64:
                return true;
            default:
                return false;
        }
    }

    static string Describe(PropertyModification _mod)
    {
        string t_target = _mod.target != null ? $"{_mod.target.GetType().Name}:{_mod.target.name}" : "<null>";
        string t_value  = _mod.objectReference != null ? _mod.objectReference.name : _mod.value;
        return $"{t_target}.{_mod.propertyPath} = {t_value}";
    }
}
