using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIPoolManager : MonoBehaviour
{
    public static UIPoolManager instance;

    [SerializeField] Canvas canvas;
    [SerializeField] Transform uiRoot;

    readonly Dictionary<Type, PooledUIBase> activeUIs = new Dictionary<Type, PooledUIBase>();

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
        DontDestroyOnLoad(gameObject);
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

    public T HideUI<T>() where T : PooledUIBase
    {
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

        T uiInstance = Instantiate(uiPrefab, uiRoot).GetComponent<T>();
        if (uiInstance == null)
        {
            Debug.LogError($"UI Component Not Exist: {typeof(T).Name}");
            return null;
        }

        if (_data == null || _data.order == -1)
            uiInstance.transform.SetAsLastSibling();
        else
            uiInstance.transform.SetSiblingIndex(_data.order);

        this.activeUIs[typeof(T)] = uiInstance;
        uiInstance.Initialization(_data);
        uiInstance.Show();

        return uiInstance;
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
