using System.Collections.Generic;
using Coffee.UIEffects;
using UnityEngine;
using UnityEngine.UI;

// UI 묶음의 채도를 뺀다. 곱셈 틴트가 아니라 탈채도여야 하는 이유는 KeywordExplainItem과 같다 —
// 아이콘마다 원색이 달라 틴트로 죽이면 어떤 건 파랗게 어떤 건 누렇게 남아 "같은 상태"로 안 읽힌다.
//
// UIEffect는 Graphic 하나에 붙는 컴포넌트라 묶음 전체에 걸려면 자식을 전수로 돌아야 한다(SurvivorGoldFlight와 같은 관용구).
// 다른 점은 대상이 일회용이 아니라는 것 — 되돌릴 자리가 필요하므로 걸어 둔 목록을 부르는 쪽이 들고 있어야 한다.
public static class UiGrayscale
{
    /// <summary>되돌릴 때 쓸 한 칸. 원복이 "None 쓰기"가 아니라 "저작값 복원"이라 효과마다 원래 값을 짝지어 둔다.</summary>
    public struct Toned
    {
        public UIEffect   Effect;
        public ToneFilter Filter0;
        public float      Intensity0;
    }

    /// <summary>_root 하위 그래픽 전부를 무채색으로. _skips 하위는 건드리지 않는다(자물쇠 배지처럼 스스로는 색이 살아야 하는 것).
    ///
    /// 제외는 여럿일 수 있다 — 상태를 말하는 표식과 "무엇인지"를 말하는 표식이 따로 있을 때 둘 다 색이 살아야 한다.
    ///
    /// 꺼진 그래픽까지 훑는다 — 잠긴 동안 켜졌다 꺼지는 자식(가격 라벨·미리보기)이 빠지면 그 노드만 원색으로 남는다.</summary>
    public static List<Toned> Apply(GameObject _root, params Transform[] _skips)
    {
        var t_toned = new List<Toned>();
        if (_root == null) return t_toned;

        Graphic[] t_graphics = _root.GetComponentsInChildren<Graphic>(true);

        for (int t_i = 0; t_i < t_graphics.Length; t_i++)
        {
            Graphic t_g = t_graphics[t_i];
            if (IsSkipped(t_g.transform, _skips)) continue;

            UIEffect t_fx = Resolve(t_g);
            if (t_fx == null) continue;

            t_toned.Add(new Toned { Effect = t_fx, Filter0 = t_fx.toneFilter, Intensity0 = t_fx.toneIntensity });

            t_fx.toneFilter    = ToneFilter.Grayscale;
            t_fx.toneIntensity = 1f;
        }

        return t_toned;
    }

    /// <summary>Apply가 돌려준 목록을 저작값으로 되돌린다. 되돌린 뒤에도 컴포넌트는 남지만
    /// toneFilter가 None이면 머티리얼 변종을 만들지 않아 사실상 비용이 없다.</summary>
    public static void Restore(List<Toned> _toned)
    {
        if (_toned == null) return;

        for (int t_i = 0; t_i < _toned.Count; t_i++)
        {
            UIEffect t_fx = _toned[t_i].Effect;
            if (t_fx == null) continue;   // 대상이 이미 파괴됐을 수 있다

            t_fx.toneFilter    = _toned[t_i].Filter0;
            t_fx.toneIntensity = _toned[t_i].Intensity0;
        }

        _toned.Clear();
    }

    // 배열 자체가 null인 경우(인자 생략)와 칸이 null인 경우(삼항식이 null을 넘긴 경우)를 함께 흡수한다.
    static bool IsSkipped(Transform _target, Transform[] _skips)
    {
        if (_skips == null) return false;

        for (int t_i = 0; t_i < _skips.Length; t_i++)
        {
            Transform t_skip = _skips[t_i];
            if (t_skip == null) continue;
            if (_target == t_skip || _target.IsChildOf(t_skip)) return true;
        }

        return false;
    }

    // UIEffectBase는 [DisallowMultipleComponent]라 Replica가 자리를 차지한 그래픽엔 UIEffect를 못 붙인다 —
    // 그냥 AddComponent하면 null이 돌아와 다음 줄에서 터진다. 그런 노드는 건너뛰고 나머지만 건다.
    static UIEffect Resolve(Graphic _graphic)
    {
        var t_existing = _graphic.GetComponent<UIEffectBase>();
        if (t_existing != null) return t_existing as UIEffect;

        return _graphic.gameObject.AddComponent<UIEffect>();
    }
}
