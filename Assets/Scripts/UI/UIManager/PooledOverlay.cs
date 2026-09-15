using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>공용 풀에서 재사용하되 기존 연출의 실제 Contents 표시 수명을 유지하는 화면.</summary>
public abstract class PooledOverlay : ContentsPooledUI
{
    protected override bool UseScreenDim => false;
    protected override bool UseContentsVisibilityRelay => true;
    protected abstract int SortingOrder { get; }
    public virtual bool UsesSafeArea => false;
    public int SourceSceneHandle { get; private set; }
    public bool IsSourceSceneUnloading { get; internal set; }
    public bool IsViewVisible => contents != null && contents.activeInHierarchy;
    protected GameObject viewContents => contents;

    internal void PrepareForPool(Scene scene)
    {
        SourceSceneHandle = scene.handle;
        UiSortingOrder.LiftNested(gameObject, SortingOrder);
        // 풀과 다른 해상도 매칭을 저작한 화면만 원래 크기 규칙을 보존한다.
        var authored = GetComponent<CanvasScaler>();
        var parentScaler = GetComponent<Canvas>().rootCanvas.GetComponent<CanvasScaler>();
        if (authored != null && parentScaler != null && authored != parentScaler &&
            authored.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
            (authored.referenceResolution != parentScaler.referenceResolution ||
             authored.screenMatchMode != parentScaler.screenMatchMode ||
             !Mathf.Approximately(authored.matchWidthOrHeight, parentScaler.matchWidthOrHeight)))
        {
            var scale = gameObject.AddComponent<PooledCanvasScale>();
            scale.Initialize(authored);
        }
    }

    protected new void SetContentsVisible(bool visible) => base.SetContentsVisible(visible, null);

    public override void Initialization(UIData data) => this.data = data;

    // 연출별 Show 인자가 있는 화면은 GetOrCreateUI 후 데이터를 먼저 넣는다.
    // 일반 풀 진입도 명시적인 표시 콜백을 받으면 같은 경로를 쓴다.
    public override void Show() => this.data?.showCustomMethod?.Invoke();
    public override void Hide() => SetContentsVisible(false);
}

/// <summary>타입당 한 인스턴스와 닫힘 통지. 생성과 수명은 UIPoolManager가 소유한다.</summary>
public abstract class PooledOverlay<T> : PooledOverlay where T : PooledOverlay<T>
{
    public static bool IsOpen { get; private set; }
    public static event Action OnAnyClosed;

    protected static bool TryGetOrCreate(out T overlay)
    {
        var pool = UIPoolManager.Instance;
        overlay = pool != null ? pool.GetOrCreateUI<T>() : null;
        return overlay != null;
    }

    protected static void MarkOpen()
    {
#if UNITY_EDITOR
        if (IsOpen) Debug.LogWarning($"[PooledOverlay] {typeof(T).Name} is already open; its presentation is being replaced.");
#endif
        IsOpen = true;
    }

    protected static bool ConsumeOpen()
    {
        bool wasOpen = IsOpen;
        IsOpen = false;
        return wasOpen;
    }

    protected static void NotifyClosed(bool wasOpen)
    {
        if (wasOpen) OnAnyClosed?.Invoke();
    }

    protected static void ClearOpen() => IsOpen = false;

    public static UniTask WaitUntilClosedAsync(CancellationToken token = default)
        => UniTask.WaitUntil(() => !IsOpen, cancellationToken: token);

    public override void Hide()
    {
        bool wasOpen = ConsumeOpen();
        base.Hide();
        NotifyClosed(wasOpen);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // An instance created again during old-scene teardown owns the new state.
        if (UIPoolManager.instance == null || !UIPoolManager.instance.TryGetUI<T>(out _)) ClearOpen();
    }
}
