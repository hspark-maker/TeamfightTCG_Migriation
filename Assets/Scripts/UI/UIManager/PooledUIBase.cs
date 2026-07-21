using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public interface IUIController
{
    void Show();
    void Hide();
}

public abstract class PooledUIBase : MonoBehaviour, IUIController
{
    [SerializeField] protected UIAnimator animator;
    public GameObject contents;
    protected UIData data;

    public abstract void Initialization(UIData _data);
    public abstract void Show();
    public abstract void Hide();
    public bool isShow;

    protected virtual void Awake()
    {
        UIPoolManager.instance?.RegisterUI(this);
    }

    protected virtual void OnDestroy()
    {
        UIPoolManager.instance?.UnregisterUI(this);
    }
}


public class UIData
{
    public int order = -1;
    public Action showCustomMethod;
    public Action onHide;
}
