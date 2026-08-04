using UnityEditor;
using UnityEngine;

/// <summary>연출 테스트 씬에서 플레이를 누르면 <see cref="AttackAnimTester"/>를 자동으로 선택한다 —
/// 조작 패널이 인스펙터에 바로 뜨게 하려는 것. 매번 하이어라키에서 찾아 클릭하는 수고를 없앤다.
///
/// 씬을 가리지 않고 <b>그 씬에 테스터가 있을 때만</b> 동작하므로 전투/로비 씬 플레이에는 영향이 없다.
/// 메뉴 <c>Tools/연출 테스터/플레이 시 자동 선택</c>으로 끌 수 있다(인스펙터를 다른 데 두고 보고 싶을 때).</summary>
[InitializeOnLoad]
static class AttackAnimTesterAutoSelect
{
    const string MenuPath = "Tools/연출 테스터/플레이 시 자동 선택";
    const string PrefKey  = "AttackAnimTester.AutoSelectOnPlay";

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static AttackAnimTesterAutoSelect()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem(MenuPath)]
    static void Toggle() => Enabled = !Enabled;

    [MenuItem(MenuPath, isValidateFunction: true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    static void OnPlayModeChanged(PlayModeStateChange _state)
    {
        if (_state != PlayModeStateChange.EnteredPlayMode) return;
        if (!Enabled) return;

        // 비활성 오브젝트까지 뒤지지는 않는다 — 꺼져 있는 테스터는 이번 플레이에서 쓰지 않는다는 뜻이다.
        var t_tester = Object.FindFirstObjectByType<AttackAnimTester>();
        if (t_tester == null) return;

        Selection.activeGameObject = t_tester.gameObject;
        EditorGUIUtility.PingObject(t_tester.gameObject);   // 하이어라키에서도 어디 있는지 보이게
    }
}
