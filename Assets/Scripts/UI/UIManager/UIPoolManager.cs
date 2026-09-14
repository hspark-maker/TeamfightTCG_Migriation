using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

// 팝업 인프라는 이걸 쓰는 UI들이 켜지기 전에 서 있어야 한다 —
// 기본 순서면 로비 탭 UI의 OnEnable이 Awake보다 먼저 돌아 instance가 아직 없다.
[DefaultExecutionOrder(-100)]
public class UIPoolManager : MonoBehaviour
{
    public static UIPoolManager instance;

    static bool s_nullWarned;

    /// <summary>외부 접근용. instance가 없으면(씬에 UIPoolManager 미배치) 에러 로그를 1회 남긴다.
    /// 예전엔 `instance?.`로 조용히 무시돼 팝업/오버레이가 안 떠도 원인을 알 수 없었다(튜토리얼 정보확인 버그).</summary>
    public static UIPoolManager Instance
    {
        get
        {
            if (instance == null && !s_nullWarned)
            {
                s_nullWarned = true;
                Debug.LogError("[UIPoolManager] No instance — UIPoolManager is not placed in the current scene. " +
                               "Popups and overlays (card info, synergy explanation, YN popup, etc.) will not work.");
            }
            return instance;
        }
    }

    [SerializeField] Canvas canvas;
    [SerializeField] Transform uiRoot;

    readonly Dictionary<Type, PooledUIBase> activeUIs = new Dictionary<Type, PooledUIBase>();
    readonly Dictionary<Type, int> pendingRequests = new();
    int requestVersion;

    private void Awake()
    {
        if (!InitializeSingleton()) return;
        InitializeReferences();
    }

    bool InitializeSingleton()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return false;
        }

        instance = this;
        s_nullWarned = false;
        DontDestroyOnLoad(transform.root.gameObject);   // 초기화 프리팹의 자식이라 루트 기준(단독 배치면 자기 자신)
        return true;
    }

    void InitializeReferences()
    {
        if (this.canvas == null)
            this.canvas = GetComponent<Canvas>();
        if (this.uiRoot == null)
            this.uiRoot = this.canvas.transform;
    }

    public void DestroyAllUI()
    {
        this.pendingRequests.Clear();
        foreach (var ui in activeUIs)
        {
            Destroy(ui.Value.gameObject);
        }
        this.activeUIs.Clear();
    }

    public T GetUI<T>() where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var t_ui))
            return t_ui as T;

        LogUtil.Log($"No Such UI {typeof(T).Name}");
        return null;
    }

    /// <summary>알림이 기존 팝업을 덮지 않도록 조회한다. 자신의 표시만 제외할 수 있다.</summary>
    public bool HasVisibleUIExcept(PooledUIBase except = null)
    {
        foreach (var ui in activeUIs.Values)
        {
            if (ui == null || ui == except || !ui.isShow || !ui.gameObject.activeInHierarchy) continue;
            // 미션 컷인은 알림을 기다리기 위해 상시 열려 있다. 실제 재생 중일 때만 화면을 점유한다.
            if (ui is MissionCutInView && !MissionCutInView.IsPlaying) continue;
            return true;
        }
        return false;
    }

    public T HideUI<T>() where T : PooledUIBase
    {
        this.pendingRequests.Remove(typeof(T));
        if (this.activeUIs.TryGetValue(typeof(T), out var t_ui))
        {
            t_ui.Hide();
            return t_ui as T;
        }

        LogUtil.Log($"No Such UI {typeof(T).Name}");
        return null;
    }

    public T ShowUI<T>() where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var t_ui))
        {
            t_ui.Show();
            return t_ui as T;
        }

        LogUtil.Log($"No Such UI {typeof(T).Name}");
        return null;
    }

    public T ToggleUI<T>() where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var t_existingUI))
        {
            if (t_existingUI.isShow)
                t_existingUI.Hide();
            else
                t_existingUI.Show();

            return t_existingUI as T;
        }

        LogUtil.Log($"No Such UI {typeof(T).Name}");
        return null;
    }

    public T AddOrUpdateUI<T>(UIData _data = null) where T : PooledUIBase
    {
        if (this.activeUIs.TryGetValue(typeof(T), out var existingUI))
        {
            if (existingUI is IUIInitializable t_existingInitializer) t_existingInitializer.InitializeUI();
            existingUI.transform.SetAsLastSibling();
            existingUI.Initialization(_data);
            existingUI.Show();
            return existingUI as T;
        }

        GameObject uiPrefab = DataLibrary.instance.GetUI<T>();
        if (uiPrefab == null)
        {
            Debug.LogError($"UI Prefab Not Exist: {typeof(T).Name}");
            return null;
        }

        GameObject t_instance = Instantiate(uiPrefab, uiRoot);
        T uiInstance = t_instance.GetComponent<T>();
        if (uiInstance == null)
        {
            Debug.LogError($"UI Component Not Exist: {typeof(T).Name}");
            Destroy(t_instance);
            return null;
        }

        try
        {
            if (_data == null || _data.order == -1)
                uiInstance.transform.SetAsLastSibling();
            else
                uiInstance.transform.SetSiblingIndex(_data.order);

            // 활성화로 Awake가 실행되기를 기다리지 않고, 비활성 Contents까지 준비한 뒤 등록한다.
            if (uiInstance is IUIInitializable t_initializer) t_initializer.InitializeUI();
            this.RegisterUI(uiInstance);
            if (uiInstance is ContentsPooledUI) uiInstance.gameObject.SetActive(true);
            uiInstance.Initialization(_data);
            uiInstance.Show();
            return uiInstance;
        }
        catch
        {
            // 잘못된 Contents 배선·초기화 실패로 풀 밖에 인스턴스가 누적되지 않게 한다.
            this.UnregisterUI(uiInstance);
            t_instance.SetActive(false);
            Destroy(t_instance);
            throw;
        }
    }

    /// <summary>버튼 진입점. 첫 적재 동안 입력을 막고, 호출 화면이 닫혔으면 뒤늦게 팝업을 열지 않는다.</summary>
    public void RequestUI<T>(MonoBehaviour _owner, UIData _data = null) where T : PooledUIBase
    {
        if (!IsRequestOwnerVisible(_owner)) return;
        if (UiPrefabCache.TryGet(typeof(T), out _))
        {
            this.pendingRequests.Remove(typeof(T));
            AddOrUpdateUI<T>(_data);
            return;
        }
        int t_version = ++this.requestVersion;
        this.pendingRequests[typeof(T)] = t_version;
        OpenDeferredAsync<T>(_owner, _data, t_version).Forget();
    }

    async UniTask OpenDeferredAsync<T>(MonoBehaviour _owner, UIData _data, int _version) where T : PooledUIBase
    {
        int t_ownerVersion = _owner is ContentsPooledUI t_pool ? t_pool.VisibilityVersion
            : _owner is ContentsUIBehaviour t_view ? t_view.VisibilityVersion : 0;
        object t_waitOwner = new object();
        Exception t_error = null;
        UniTask<GameObject> t_load = default;
        bool t_started = false;
        bool t_observed = false;
        bool t_finished = false;
        ServerWaitOverlay.Hold(t_waitOwner);
        try
        {
            t_load = UiPrefabCache.LoadAsync<T>();
            t_started = true;
            var t_token = _owner.GetCancellationTokenOnDestroy();
            while (t_load.Status == UniTaskStatus.Pending)
            {
                await UniTask.Yield(t_token);
                if (this == null || !IsRequestOwnerVisible(_owner, t_ownerVersion) ||
                    !this.pendingRequests.TryGetValue(typeof(T), out int t_pendingVersion) || t_pendingVersion != _version)
                    return;
            }
            t_observed = true;
            await t_load;
            t_finished = true;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception t_exception) { t_error = t_exception; t_finished = true; }
        finally
        {
            ServerWaitOverlay.Release(t_waitOwner);
            if (!t_finished && this.pendingRequests.TryGetValue(typeof(T), out int t_pendingVersion) && t_pendingVersion == _version)
                this.pendingRequests.Remove(typeof(T));
            // 호출 화면은 닫혔어도 공유 적재는 끝까지 진행된다. 소비자가 사라진 요청의 예외도 회수한다.
            if (t_started && !t_observed) t_load.Forget(_exception => { });
        }

        if (!this.pendingRequests.TryGetValue(typeof(T), out int t_version) || t_version != _version) return;
        this.pendingRequests.Remove(typeof(T));
        if (this == null || !IsRequestOwnerVisible(_owner, t_ownerVersion)) return;
        if (t_error != null)
        {
            Debug.LogException(t_error);
            AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
            {
                titleText = "화면을 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.",
                yesText = "확인",
                noText = "닫기"
            });
            return;
        }
        AddOrUpdateUI<T>(_data);
    }

    static bool IsRequestOwnerVisible(MonoBehaviour _owner, int? _version = null)
    {
        if (_owner == null || !_owner.isActiveAndEnabled) return false;
        // 제어 루트는 닫아도 켜져 있다. 화면 표시와 같은 개폐 세션인지 함께 확인한다.
        if (_owner is ContentsPooledUI t_panel)
            return t_panel.isShow && (!_version.HasValue || t_panel.VisibilityVersion == _version.Value);
        if (_owner is ContentsUIBehaviour t_view)
            return t_view.IsViewVisible && (!_version.HasValue || t_view.VisibilityVersion == _version.Value);
        return true;
    }

    public void RegisterUI(PooledUIBase _ui)
    {
        this.activeUIs[_ui.GetType()] = _ui;
    }

    public void UnregisterUI(PooledUIBase _ui)
    {
        Type t_type = _ui.GetType();
        if (this.activeUIs.TryGetValue(t_type, out PooledUIBase t_existing) && t_existing == _ui)
            this.activeUIs.Remove(t_type);
    }

    public void CleanupInactiveUIs()
    {
        var t_inactiveUIs = new List<Type>();
        foreach (var t_kvp in activeUIs)
        {
            if (!t_kvp.Value.isShow)
                t_inactiveUIs.Add(t_kvp.Key);
        }

        foreach (var key in t_inactiveUIs)
        {
            if (activeUIs.TryGetValue(key, out var ui))
            {
                Destroy(ui.gameObject);
                activeUIs.Remove(key);
            }
        }
    }
}
