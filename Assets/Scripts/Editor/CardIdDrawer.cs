using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(CardIdAttribute))]
public sealed class CardIdDrawer : PropertyDrawer
{
    static int[] s_values;
    static GUIContent[] s_labels;

    public override void OnGUI(Rect _position, SerializedProperty _property, GUIContent _label)
    {
        if (_property.propertyType != SerializedPropertyType.Integer)
        {
            EditorGUI.PropertyField(_position, _property, _label, true);
            return;
        }

        EnsureOptions();
        EditorGUI.IntPopup(_position, _property, s_labels, s_values, _label);
    }

    static void EnsureOptions()
    {
        if (s_values != null) return;
        var t_specs = new Dictionary<int, CardSpec>();
        try
        {
            SpecSource.Init();
            Add(SpecSource.LoadCards(EContentRunMode.Live), t_specs);
            Add(SpecSource.LoadCards(EContentRunMode.Test), t_specs);
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[CardIdDrawer] 카드 표를 읽지 못했습니다: {t_exception.Message}");
        }

        var t_rows = new List<CardSpec>(t_specs.Values);
        t_rows.Sort((a, b) => a.Id.CompareTo(b.Id));
        s_values = new int[t_rows.Count + 1];
        s_labels = new GUIContent[t_rows.Count + 1];
        s_labels[0] = new GUIContent("None (0)");
        for (int t_i = 0; t_i < t_rows.Count; t_i++)
        {
            CardSpec t_spec = t_rows[t_i];
            s_values[t_i + 1] = t_spec.Id;
            s_labels[t_i + 1] = new GUIContent($"{t_spec.Id} - {t_spec.DisplayName}");
        }
    }

    static void Add(Dictionary<int, CardSpec> _source, Dictionary<int, CardSpec> _into)
    {
        foreach (KeyValuePair<int, CardSpec> t_pair in _source) _into[t_pair.Key] = t_pair.Value;
    }
}
