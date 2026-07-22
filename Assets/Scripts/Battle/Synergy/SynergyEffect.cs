using UnityEngine;

// 시너지 효과 1건. CardPassive와 동형(추상 SO) — 효과=에셋으로 추가하는 개방-폐쇄 구조.
// 서브클래스/에셋을 새로 만들어 확장하며, 엔진 층(Applier)은 수정하지 않는다.
public abstract class SynergyEffect : ScriptableObject
{
    public abstract void Apply(CardInstance card, SynergyState state);
}
