using UnityEngine;

/// <summary>Contents의 실제 표시 수명을 상시 활성 제어 루트에 전달한다.</summary>
[DisallowMultipleComponent]
public sealed class ContentsVisibilityRelay : MonoBehaviour
{
    ContentsUIBehaviour owner;
    public void Bind(ContentsUIBehaviour value) => owner = value;
    void OnEnable() => owner?.NotifyContentsVisibility(true);
    void OnDisable() => owner?.NotifyContentsVisibility(false);
}
