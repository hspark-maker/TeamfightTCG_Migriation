using Coffee.UIEffects;
using Coffee.UIEffects.Editors;
using UnityEditor;

// Unity 6은 같은 대상을 편집하는 사용자 인스펙터를 타입 전체 이름순으로 선택한다.
// Coffee.UIEffects.Editors.UIEffectEditor보다 앞서는 이름을 유지한다.
[CustomEditor(typeof(UIEffect), true)]
[CanEditMultipleObjects]
internal sealed class CheckedUIEffectEditor : UIEffectEditor
{
    bool m_initialized;

    protected override void OnEnable()
    {
        this.m_initialized = false;
        // 프리팹 재임포트·도메인 재로드 중에는 편집기만 남고 대상이 사라질 수 있다.
        // 패키지의 OnEnable은 곧바로 serializedObject를 읽으므로 먼저 검사한다.
        if (!this.HasLiveTargets()) return;

        base.OnEnable();
        this.m_initialized = true;
    }

    public override void OnInspectorGUI()
    {
        if (!this.HasLiveTargets())
        {
            this.m_initialized = false;
            return;
        }

        if (!this.m_initialized) this.OnEnable();
        base.OnInspectorGUI();
    }

    bool HasLiveTargets()
    {
        var t_targets = this.targets;
        if (t_targets == null || t_targets.Length == 0) return false;
        for (int i = 0; i < t_targets.Length; i++)
            if (t_targets[i] == null) return false;
        return true;
    }
}
