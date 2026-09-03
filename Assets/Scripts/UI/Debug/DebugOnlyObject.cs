using UnityEngine;

/// <summary>붙은 오브젝트를 릴리스 빌드에서 끈다.
/// 디버그 패널처럼 "저작상 켜 두지만 출시본에는 없어야 하는" 것에 붙인다 —
/// 프리팹 저작에만 의존하면 켜 둔 채로 빌드가 나가는 사고가 반복된다.</summary>
[DisallowMultipleComponent]
public class DebugOnlyObject : MonoBehaviour
{
    void Awake()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        this.gameObject.SetActive(false);
#endif
    }
}
