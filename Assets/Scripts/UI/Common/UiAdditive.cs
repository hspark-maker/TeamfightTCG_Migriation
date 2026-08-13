using UnityEngine;

// UI 그래픽 하나를 가산 합성으로 바꾼다. 프로젝트에 범용 UI Additive 머티리얼이 없어
// UIEffect로 블렌드만 갈아끼우는 것이 관용구다(빛·번쩍임처럼 '달아올라야' 하는 그림에 쓴다).
//
// ⚠ blendType 세터는 쓰지 않는다 — 넘긴 값을 필드에 넣지 않고 기존 값으로 되돌리는 패키지 버그가 있다.
//   dst를 먼저 지정해야 세터가 Additive로 역산한다.
public static class UiAdditive
{
    public static void Apply(GameObject _target)
    {
        if (_target == null) return;

        var t_fx = _target.GetComponent<Coffee.UIEffects.UIEffect>();
        if (t_fx == null) t_fx = _target.AddComponent<Coffee.UIEffects.UIEffect>();

        t_fx.dstBlendMode = UnityEngine.Rendering.BlendMode.One;
        t_fx.srcBlendMode = UnityEngine.Rendering.BlendMode.One;
    }
}
