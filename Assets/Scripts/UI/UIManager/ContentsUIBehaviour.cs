using System;
using UnityEngine;

/// <summary>비풀 UI의 초기화 소유권과 Contents 표시 수명. 제어 루트는 개폐하지 않는다.</summary>
public abstract class ContentsUIBehaviour : MonoBehaviour, IUIInitializationRoot
{
    [SerializeField] protected GameObject viewContents;
    bool m_initializing;
    bool m_presented;
    bool m_requestedVisible;
    PopupTransition m_transition;

    public bool IsUIInitialized { get; private set; }
    public bool IsViewVisible => viewContents != null && viewContents.activeInHierarchy;
    public int VisibilityVersion { get; private set; }
    protected virtual bool RequiresContents => true;

    protected virtual void Awake() => InitializeUI();

    public void InitializeUI()
    {
        if (IsUIInitialized) return;
        if (m_initializing) throw new InvalidOperationException($"Recursive UI initialization: {name}");
        if (RequiresContents && (viewContents == null || viewContents.transform.parent != transform))
            throw new InvalidOperationException($"{name} requires a direct child Contents view.");
        m_initializing = true;
        try
        {
            if (viewContents != null)
            {
                viewContents.SetActive(false);
                var relay = viewContents.GetComponent<ContentsVisibilityRelay>();
                if (relay == null) relay = viewContents.AddComponent<ContentsVisibilityRelay>();
                relay.Bind(this);
            }
            foreach (var component in GetComponents<MonoBehaviour>())
                if (component != this && component is IUIInitializable initializer)
                    initializer.InitializeUI();
            for (int i = 0; i < transform.childCount; i++)
                UIInitialization.InitializeHierarchy(transform.GetChild(i).gameObject);
            OnInitializeUI();
            IsUIInitialized = true;
        }
        finally { m_initializing = false; }
    }

    protected virtual void OnInitializeUI() { }
    protected virtual void OnViewShown() { }
    protected virtual void OnViewHidden() { }

    protected void SetContentsVisible(bool visible, PopupTransition transition = null)
    {
        InitializeUI();
        if (viewContents == null) return;
        if (visible && !gameObject.activeSelf) gameObject.SetActive(true);
        if (m_requestedVisible != visible) VisibilityVersion++;
        m_requestedVisible = visible;
        m_transition = transition;
        if (transition != null) transition.SetVisible(viewContents, visible);
        else viewContents.SetActive(visible);
    }

    // 표시 훅은 실제 활성화/퇴장 완료 시점에 전달한다. 닫힘 콜백과 튜토리얼 순서를 보존한다.
    internal void NotifyContentsVisibility(bool visible)
    {
        if (!IsUIInitialized || m_presented == visible) return;
        m_presented = visible;
        if (visible) OnViewShown();
        else
        {
            if (m_requestedVisible) VisibilityVersion++;
            m_requestedVisible = false;
            OnViewHidden();
        }
    }

    protected virtual void OnDisable() => ResetView();
    protected virtual void OnDestroy() => ResetView();

    void ResetView()
    {
        if (!IsUIInitialized) return;
        m_transition?.HandleDisabled(viewContents);
        if (viewContents != null) viewContents.SetActive(false);
        NotifyContentsVisibility(false);
    }
}
