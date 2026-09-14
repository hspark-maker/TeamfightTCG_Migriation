using System;
using UnityEngine;

/// <summary>
/// 제어 루트는 생성 후 활성 상태를 유지하고 Contents만 개폐하는 풀 UI.
/// 초기화는 생성 담당자가 명시적으로 호출하며 Awake는 직접 배치된 인스턴스의 준비만 보장한다.
/// 풀 등록은 UIPoolManager가 소유한다.
/// </summary>
[DisallowMultipleComponent]
public abstract class ContentsPooledUI : PooledUIBase, IUIInitializationRoot
{
    [Header("연출")]
    [Tooltip("Contents 안의 패널 본체. 전체 Contents가 아닌 팝 연출 대상을 지정한다.")]
    [SerializeField] protected PopupTransition transition = new PopupTransition();

    [Tooltip("화면이 열린 동안 요청할 공용 딤의 짙기.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    bool m_initializing;
    public bool IsUIInitialized { get; private set; }
    public int VisibilityVersion { get; private set; }
    public bool IsViewVisible => contents != null && contents.activeInHierarchy;
    public bool IsViewTransitioning => UsePopupTransition && transition.IsPlaying;

    // 기존 팝업의 표시 방식을 유지한다. 즉시 개폐·자체 딤을 쓰는 뷰는 해당 기능만 제외한다.
    protected virtual bool UsePopupTransition => true;
    protected virtual bool UseScreenDim => true;

    // 레거시 PooledUIBase의 Awake 자동 등록을 사용하지 않는다.
    protected sealed override void Awake() => this.InitializeUI();

    public void InitializeUI()
    {
        if (this.IsUIInitialized) return;
        if (this.m_initializing) throw new InvalidOperationException($"Recursive UI initialization: {name}");
        if (this.contents == null || this.contents.transform.parent != transform)
            throw new InvalidOperationException($"{name} requires a direct child Contents view.");

        this.m_initializing = true;
        try
        {
            // 표시 이벤트나 레이아웃·애니메이션을 실행하지 않고 준비한다.
            this.contents.SetActive(false);
            this.isShow = false;
            foreach (var t_component in GetComponents<MonoBehaviour>())
                if (t_component != this && t_component is IUIInitializable t_initializable)
                    t_initializable.InitializeUI();
            UIInitialization.InitializeHierarchy(this.contents);
            this.OnInitializeUI();
            this.IsUIInitialized = true;
        }
        finally { this.m_initializing = false; }
    }

    /// <summary>참조 캐싱·고정 버튼 연결만 수행한다. 서버 조회·목록 생성은 Show 쪽에서 한다.</summary>
    protected virtual void OnInitializeUI() { }

    /// <summary>숨김→표시 때 한 번 호출된다. 화면에 필요한 이벤트를 구독한다.</summary>
    protected virtual void OnViewShown() { }

    /// <summary>표시→숨김 또는 외부 비활성화 때 한 번 호출된다. 표시 구독과 대기를 정리한다.</summary>
    protected virtual void OnViewHidden() { }

    protected void SetContentsVisible(bool _visible)
    {
        this.InitializeUI();
        if (_visible && !gameObject.activeSelf) gameObject.SetActive(true);
        if (this.isShow == _visible) return;

        this.isShow = _visible;
        this.VisibilityVersion++;
        if (_visible)
        {
            this.OnViewShown();
            if (this.UseScreenDim)
                ScreenDim.Show(this, this.dimAlpha, true, this.UsePopupTransition ? this.transition.OpenDuration : 0f);
        }
        else
        {
            this.OnViewHidden();
            ScreenDim.Hide(this);
        }
        this.ApplyContentsVisibility(_visible);
    }

    protected virtual void ApplyContentsVisibility(bool _visible)
    {
        if (this.UsePopupTransition) this.transition.SetVisible(this.contents, _visible);
        else this.contents.SetActive(_visible);
    }

    // 씬 전환·상위 비활성화도 정상 Hide와 같은 구독 정리를 거친다.
    protected virtual void OnDisable() => this.ResetView();

    protected override void OnDestroy()
    {
        this.ResetView();
        base.OnDestroy();
    }

    void ResetView()
    {
        if (!this.IsUIInitialized) return;
        if (this.isShow)
        {
            this.isShow = false;
            this.VisibilityVersion++;
            this.OnViewHidden();
        }
        ScreenDim.Hide(this);
        if (this.UsePopupTransition) this.transition.HandleDisabled(this.contents);
        if (this.contents != null) this.contents.SetActive(false);
    }
}
