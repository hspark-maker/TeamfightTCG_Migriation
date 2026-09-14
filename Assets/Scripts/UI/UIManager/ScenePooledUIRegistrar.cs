using UnityEngine;

/// <summary>씬에 직접 저작된 풀 UI만 명시적으로 준비하고 등록한다.</summary>
[DisallowMultipleComponent]
public sealed class ScenePooledUIRegistrar : MonoBehaviour
{
    [SerializeField] ContentsPooledUI view;

    void Awake()
    {
        if (this.view == null) this.view = GetComponent<ContentsPooledUI>();
        if (this.view == null) return;
        this.view.InitializeUI();
        UIPoolManager.Instance?.RegisterUI(this.view);
    }
}
