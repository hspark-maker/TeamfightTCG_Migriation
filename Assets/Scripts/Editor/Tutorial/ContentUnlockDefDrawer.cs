using UnityEditor;
using UnityEngine;

/// <summary>선택한 콘텐츠 해금 기준에 필요한 값만 표시한다.</summary>
[CustomPropertyDrawer(typeof(ContentUnlockDef))]
public sealed class ContentUnlockDefDrawer : PropertyDrawer
{
    static readonly GUIContent[] s_conditions =
    {
        new GUIContent("계정 레벨"),
        new GUIContent("랭크"),
        new GUIContent("FTUE 졸업"),
    };
    static readonly int[] s_conditionValues =
    {
        (int)EContentUnlockCondition.AccountLevel,
        (int)EContentUnlockCondition.Rank,
        (int)EContentUnlockCondition.FtueCompleted,
    };

    public override float GetPropertyHeight(SerializedProperty _property, GUIContent _label)
    {
        int t_rows = _property.FindPropertyRelative("condition").hasMultipleDifferentValues ? 2 : ConditionOf(_property) switch
        {
            EContentUnlockCondition.AccountLevel => 3,
            EContentUnlockCondition.Rank => 4,
            _ => 2,
        };
        return t_rows * EditorGUIUtility.singleLineHeight
            + (t_rows - 1) * EditorGUIUtility.standardVerticalSpacing;
    }

    public override void OnGUI(Rect _rect, SerializedProperty _property, GUIContent _label)
    {
        EditorGUI.BeginProperty(_rect, _label, _property);
        var t_row = new Rect(_rect.x, _rect.y, _rect.width, EditorGUIUtility.singleLineHeight);
        DrawField(ref t_row, _property, "feature", "콘텐츠");
        var t_condition = _property.FindPropertyRelative("condition");
        EditorGUI.IntPopup(t_row, t_condition, s_conditions, s_conditionValues, new GUIContent("해금 기준"));
        Advance(ref t_row);
        if (!t_condition.hasMultipleDifferentValues)
            switch (ConditionOf(_property))
            {
                case EContentUnlockCondition.AccountLevel:
                    DrawField(ref t_row, _property, "minAccountLevel", "최소 계정 레벨");
                    break;
                case EContentUnlockCondition.Rank:
                    DrawField(ref t_row, _property, "minRankGrade", "최소 랭크 등급");
                    DrawField(ref t_row, _property, "minRankDivision", "최소 랭크 단계");
                    break;
            }
        EditorGUI.EndProperty();
    }

    static EContentUnlockCondition ConditionOf(SerializedProperty _property)
        => (EContentUnlockCondition)_property.FindPropertyRelative("condition").intValue;

    static void DrawField(ref Rect _rect, SerializedProperty _property, string _name, string _label)
    {
        var t_field = _property.FindPropertyRelative(_name);
        EditorGUI.PropertyField(_rect, t_field, new GUIContent(_label, t_field.tooltip));
        Advance(ref _rect);
    }

    static void Advance(ref Rect _rect)
        => _rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
}
