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
        UIPoolManager.Instance?.RegisterUI(this);
    }

    // 조용한 instance를 쓴다 — Instance는 없을 때 에러를 찍는 게터라, 풀 없이 파괴되는 경우
    // (에디터에서 저작본 삭제, 씬 언로드 순서상 풀이 먼저 사라진 경우)마다 없는 문제를 보고한다.
    // 등록(Awake)은 반대다: 그때 풀이 없으면 이 UI는 영영 열리지 않으므로 드러나야 한다.
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
