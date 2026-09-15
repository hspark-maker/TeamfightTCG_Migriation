using UnityEngine;

/// <summary>Contents의 실제 표시 수명을 상시 활성 제어 루트에 전달한다.</summary>
[DisallowMultipleComponent]
public sealed class ContentsVisibilityRelay : MonoBehaviour
{
    ContentsUIBehaviour owner;
    ContentsPooledUI pooledOwner;
    public void Bind(ContentsUIBehaviour value) { owner = value; pooledOwner = null; }
    public void Bind(ContentsPooledUI value) { pooledOwner = value; owner = null; }
    void OnEnable() { owner?.NotifyContentsVisibility(true); pooledOwner?.NotifyContentsVisibility(true); }
    void OnDisable() { owner?.NotifyContentsVisibility(false); pooledOwner?.NotifyContentsVisibility(false); }
}
