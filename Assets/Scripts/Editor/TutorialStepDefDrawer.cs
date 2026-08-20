using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>튜토리얼 스텝 행의 인스펙터 표시. 스텝을 SO로 가르지 않고도 저작이 견딜 만하게 만드는 쪽이다 —
/// 접으면 한 줄 요약(순번·액션·앵커·문구), 펼치면 그 액션이 실제로 쓰는 필드만 그린다.
/// 노출 판정은 TutorialStepDef의 static 헬퍼를 그대로 쓴다(런타임과 같은 진실원).</summary>
[CustomPropertyDrawer(typeof(TutorialStepDef))]
public class TutorialStepDefDrawer : PropertyDrawer
{
    const float RowGap      = 2f;
    const float FoldoutWide = 14f;
    const float IndexWide   = 26f;
    const float ActionWide  = 104f;
    const float AnchorWide  = 152f;

    public override float GetPropertyHeight(SerializedProperty _property, GUIContent _label)
    {
        float t_height = EditorGUIUtility.singleLineHeight + RowGap;
        if (!_property.isExpanded) return t_height;

        foreach (var t_name in VisibleFields(ActionOf(_property), AnchorOf(_property)))
        {
            var t_field = _property.FindPropertyRelative(t_name);
            if (t_field == null) continue;

            t_height += EditorGUI.GetPropertyHeight(t_field, true) + RowGap;
        }

        return t_height;
    }

    public override void OnGUI(Rect _rect, SerializedProperty _property, GUIContent _label)
    {
        // 요약 줄은 열마다 Rect를 나눠 그린다 — 한 문자열로 이으면 비례폭 글꼴에서 열이 어긋난다.
        float t_lineHeight = EditorGUIUtility.singleLineHeight;
        var   t_action     = ActionOf(_property);

        var t_foldout = new Rect(_rect.x, _rect.y, FoldoutWide, t_lineHeight);
        _property.isExpanded = EditorGUI.Foldout(t_foldout, _property.isExpanded, GUIContent.none, true);

        DrawSummary(_rect, _property, t_action, t_lineHeight);

        if (!_property.isExpanded) return;

        EditorGUI.indentLevel++;
        float t_y = _rect.y + t_lineHeight + RowGap;

        foreach (var t_name in VisibleFields(t_action, AnchorOf(_property)))
        {
            var t_field = _property.FindPropertyRelative(t_name);
            if (t_field == null) continue;

            float t_fieldHeight = EditorGUI.GetPropertyHeight(t_field, true);
            EditorGUI.PropertyField(new Rect(_rect.x, t_y, _rect.width, t_fieldHeight), t_field, true);
            t_y += t_fieldHeight + RowGap;
        }

        EditorGUI.indentLevel--;
    }

    static void DrawSummary(Rect _rect, SerializedProperty _property, EOutgameTutorialAction _action, float _lineHeight)
    {
        float t_x    = _rect.x + FoldoutWide;
        float t_left = _rect.width - FoldoutWide;

        DrawColumn(ref t_x, ref t_left, _rect.y, _lineHeight, IndexOf(_property), EditorStyles.miniLabel);
        DrawColumn(ref t_x, ref t_left, _rect.y, _lineHeight, _action.ToString(), EditorStyles.boldLabel, ActionWide);
        DrawColumn(ref t_x, ref t_left, _rect.y, _lineHeight, AnchorLabel(_property, _action), EditorStyles.miniLabel, AnchorWide);

        // 남은 폭 전부가 문구 열. 자동 스텝은 문구가 없으므로 대신 핵심 참조를 보여준다.
        if (t_left <= 0f) return;

        EditorGUI.LabelField(new Rect(t_x, _rect.y, t_left, _lineHeight), TailLabel(_property, _action), TailStyle);
    }

    // EditorStyles는 static 초기화 시점에 아직 없다 — 첫 OnGUI에서 만든다.
    static GUIStyle s_tailStyle;

    static GUIStyle TailStyle =>
        s_tailStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };

    static void DrawColumn(ref float _x, ref float _left, float _y, float _height, string _text, GUIStyle _style, float _width = IndexWide)
    {
        float t_width = Mathf.Min(_width, Mathf.Max(0f, _left));
        if (t_width > 0f) EditorGUI.LabelField(new Rect(_x, _y, t_width, _height), _text, _style);

        _x    += t_width;
        _left -= t_width;
    }

    // 액션이 실제로 쓰는 필드만, 저작 순서대로. 안 쓰는 필드는 값이 남아 있어도 런타임이 무시한다.
    // 앵커까지 받는 이유: 대상이 여럿인 앵커(도감 칸)만 "어느 것"을 저작받는다.
    static IEnumerable<string> VisibleFields(EOutgameTutorialAction _action, EOutgameTutorialAnchor _anchor)
    {
        yield return "action";

        if (TutorialStepDef.UsesAnchor(_action))         yield return "anchor";
        if (TutorialStepDef.UsesAnchor(_action) && TutorialStepDef.UsesAnchorCard(_anchor)) yield return "anchorCard";
        if (TutorialStepDef.ShowsGuideMessage(_action))  yield return "guideMessage";
        if (TutorialStepDef.UsesMessagePlacement(_action)) yield return "messageAtBottom";
        if (TutorialStepDef.UsesFreeOfCharge(_action))   yield return "freeOfCharge";
        if (TutorialStepDef.UsesWaitUnlockIntro(_action)) yield return "waitUnlockIntro";
        if (TutorialStepDef.UsesPack(_action))           yield return "pack";
        if (TutorialStepDef.UsesPackPriceLabel(_action)) yield return "packPriceLabel";
        if (TutorialStepDef.UsesScenario(_action))       yield return "scenario";
        if (TutorialStepDef.UsesCard(_action))           yield return "card";
        if (TutorialStepDef.UsesCards(_action))          yield return "cards";
        if (TutorialStepDef.UsesRewardTitle(_action))    yield return "rewardTitle";
        if (TutorialStepDef.UsesParallelGain(_action))   yield return "parallelGain";
        if (TutorialStepDef.UsesShowDeckGate(_action))   yield return "showDeckGate";
        if (TutorialStepDef.UsesDeckName(_action))       yield return "deckName";
        if (TutorialStepDef.UsesFailurePolicy(_action))  yield return "onFailure";
        if (TutorialStepDef.UsesDim(_action))            yield return "useDim";

        // 해금·일시 잠금은 자동 스텝에도 의미가 있다(좌표에서 파생되므로) — 항상 노출한다.
        yield return "unlocksAll";
        yield return "unlocks";
        yield return "locks";
    }

    static EOutgameTutorialAction ActionOf(SerializedProperty _property)
    {
        var t_field = _property.FindPropertyRelative("action");
        return t_field != null ? (EOutgameTutorialAction)t_field.enumValueIndex : EOutgameTutorialAction.WaitClick;
    }

    // 저작된 앵커 그대로(액션이 앵커를 쓰는지는 보지 않는다 — 필드 노출 판정에 쓰이는 값이라 저작값이 곧 답이다)
    static EOutgameTutorialAnchor AnchorOf(SerializedProperty _property)
    {
        var t_field = _property.FindPropertyRelative("anchor");
        return t_field != null ? (EOutgameTutorialAnchor)t_field.enumValueIndex : EOutgameTutorialAnchor.None;
    }

    static string AnchorLabel(SerializedProperty _property, EOutgameTutorialAction _action)
    {
        if (!TutorialStepDef.UsesAnchor(_action)) return "—";

        var t_anchor = AnchorOf(_property);
        return t_anchor == EOutgameTutorialAnchor.None ? "—" : t_anchor.ToString();
    }

    // 문구가 있으면 문구, 없으면 이 액션을 식별하는 참조(팩·시나리오)를 보여준다 — 자동 스텝도 한 줄로 구분되게.
    static string TailLabel(SerializedProperty _property, EOutgameTutorialAction _action)
    {
        if (TutorialStepDef.ShowsGuideMessage(_action))
        {
            var t_message = _property.FindPropertyRelative("guideMessage");
            string t_text = t_message != null ? t_message.stringValue : null;
            if (!string.IsNullOrEmpty(t_text)) return Truncate(t_text.Replace("\n", " "), 64);
        }

        if (TutorialStepDef.UsesScenario(_action)) return NameOf(_property, "scenario");
        if (TutorialStepDef.UsesPack(_action))     return NameOf(_property, "pack");
        if (TutorialStepDef.UsesCard(_action))     return NameOf(_property, "card");
        if (TutorialStepDef.UsesCards(_action))    return CountOf(_property, "cards");

        return string.Empty;
    }

    // 카드 묶음은 이름 하나로 줄일 수 없다 — 접힌 줄에서는 장수만 보여 준다.
    static string CountOf(SerializedProperty _property, string _field)
    {
        var t_list = _property.FindPropertyRelative(_field);
        if (t_list == null || !t_list.isArray) return "(미배선)";

        return t_list.arraySize > 0 ? $"{t_list.arraySize}장" : "(미배선)";
    }

    static string NameOf(SerializedProperty _property, string _field)
    {
        var t_ref = _property.FindPropertyRelative(_field);
        var t_obj = t_ref != null ? t_ref.objectReferenceValue : null;

        return t_obj != null ? t_obj.name : "(미배선)";
    }

    static string Truncate(string _text, int _max) => _text.Length <= _max ? _text : _text.Substring(0, _max) + "…";

    // 배열 요소의 순번. 요약 줄의 첫 열이라 propertyPath에서 뽑는다("...stepDefs.Array.data[3]").
    static string IndexOf(SerializedProperty _property)
    {
        string t_path  = _property.propertyPath;
        int    t_open  = t_path.LastIndexOf('[');
        int    t_close = t_path.LastIndexOf(']');

        return t_open >= 0 && t_close > t_open ? t_path.Substring(t_open + 1, t_close - t_open - 1) : string.Empty;
    }
}
