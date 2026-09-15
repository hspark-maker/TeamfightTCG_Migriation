using System;
using UnityEngine;

/// <summary>오버레이의 공통 암막 요청과 색상 펄스를 소유한다.</summary>
[Serializable]
public sealed class OverlayDim
{
    [SerializeField, Range(0f, 1f)] float alpha = 0.72f;
    [SerializeField] Color color = Color.black;
    [SerializeField] Color darkColor = new Color(0.02f, 0.02f, 0.05f, 1f);
    [SerializeField] Color brightColor = new Color(0.30f, 0.28f, 0.45f, 1f);

    ScreenDim.Handle _handle;
    ScreenDim.Handle _releasedHandle;
    float _level;

    /// <summary>기본색을 기준으로 한 밝기 펄스.</summary>
    public float Level
    {
        get => _level;
        set
        {
            _level = value;
            _handle?.SetColor(value < 0f ? Color.Lerp(color, darkColor, -value)
                : Color.Lerp(color, brightColor, value));
        }
    }

    public void Show(object owner, int sortingOrder, float duration)
    {
        _level = 0f;
        _handle = Hold(owner, sortingOrder, duration);
        _releasedHandle = null;
    }

    /// <summary>동일한 저작 설정으로 다음 페이지까지 암막을 유지한다.</summary>
    public ScreenDim.Handle Hold(object owner, int sortingOrder, float duration)
        => ScreenDim.Acquire(owner, new ScreenDim.Options(alpha, color, true, duration, sortingOrder));

    public void Hide(float duration)
    {
        if (_handle == null) return;
        _handle.Release(duration);
        _releasedHandle = _handle;
        _handle = null;
    }

    public void Clear()
    {
        _handle?.Cancel();
        _releasedHandle?.Cancel();
        _handle = null;
        _releasedHandle = null;
    }

    public void Reset() => Level = 0f;
}
