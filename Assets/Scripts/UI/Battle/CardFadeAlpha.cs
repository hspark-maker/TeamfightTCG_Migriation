using TMPro;
using UnityEngine;

/// <summary>이 렌더러가 <b>완전히 보이는 상태에서</b> 가질 알파. 카드 페이드의 기준값이다.
///
/// 카드 알파는 CardAnimator가 카드에 속한 렌더러 전부에 한 번에 건다 — 그 자체는 규약이라 바꾸지 않는다.
/// 문제는 "원래 반투명이어야 하는 것"(이름·키워드 뒤 배경판)까지 같이 알파 1로 올라가 버리는 것이다.
/// 그런 오브젝트에 이 컴포넌트를 붙여 두면 페이드가 절대값이 아니라 <b>이 값과의 곱</b>으로 적용된다.
///
/// 예: baseAlpha 0.45인 배경판 → 카드가 완전히 보일 때 0.45, 카드가 0.3으로 흐려지면 0.135.
/// 안 붙인 렌더러는 1로 취급하므로 기존 동작 그대로다.</summary>
[DisallowMultipleComponent]
public class CardFadeAlpha : MonoBehaviour
{
    [Range(0f, 1f)]
    [Tooltip("카드가 완전히 보일 때 이 렌더러가 가질 알파")]
    [SerializeField] float baseAlpha = 1f;

    public float BaseAlpha => Mathf.Clamp01(this.baseAlpha);

    /// <summary>해당 렌더러의 기준 알파. 컴포넌트가 없으면 1(= 종전 동작).</summary>
    public static float Of(Component _component)
    {
        if (_component == null) return 1f;
        CardFadeAlpha t_tag = _component.GetComponent<CardFadeAlpha>();
        return t_tag != null ? t_tag.BaseAlpha : 1f;
    }

    // 붙이는 순간의 알파를 기준값으로 집어온다 — 인스펙터에서 숫자를 다시 입력하지 않게.
    void Reset()
    {
        SpriteRenderer t_sr = GetComponent<SpriteRenderer>();
        if (t_sr != null) { this.baseAlpha = t_sr.color.a; return; }

        TMP_Text t_tmp = GetComponent<TMP_Text>();
        if (t_tmp != null) this.baseAlpha = t_tmp.color.a;
    }
}
