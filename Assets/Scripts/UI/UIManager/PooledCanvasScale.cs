using UnityEngine;
using UnityEngine.UI;

/// <summary>독립 캔버스의 저작 배율을 풀의 중첩 캔버스에서도 유지한다.</summary>
public sealed class PooledCanvasScale : MonoBehaviour
{
    RectTransform rect;
    Canvas canvas;
    CanvasScaler authored;
    Vector2 lastSize;
    float lastParentScale;

    public void Initialize(CanvasScaler scaler)
    {
        rect = (RectTransform)transform;
        canvas = GetComponent<Canvas>();
        authored = scaler;
        Apply();
    }

    void LateUpdate() => Apply();

    void Apply()
    {
        if (authored == null || canvas == null || canvas.isRootCanvas) return;
        Vector2 size = canvas.rootCanvas.renderingDisplaySize;
        float parentScale = canvas.rootCanvas.scaleFactor;
        if (size.x <= 0 || size.y <= 0 || parentScale <= 0) return;
        if (size == lastSize && Mathf.Approximately(parentScale, lastParentScale)) return;
        lastSize = size;
        lastParentScale = parentScale;

        // CanvasScaler.HandleScaleWithScreenSize와 같은 식. 중첩 CanvasScaler는 직접 실행되지 않는다.
        float x = size.x / authored.referenceResolution.x;
        float y = size.y / authored.referenceResolution.y;
        float scale = authored.screenMatchMode switch
        {
            CanvasScaler.ScreenMatchMode.Expand => Mathf.Min(x, y),
            CanvasScaler.ScreenMatchMode.Shrink => Mathf.Max(x, y),
            _ => Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(x, 2f), Mathf.Log(y, 2f), authored.matchWidthOrHeight))
        };
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size / scale;
        rect.localScale = Vector3.one * (scale / parentScale);
    }
}
