using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class KeywordExplainPopupUI : PooledUIBase
{
    [SerializeField] Image    iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text explainText;
    [SerializeField] float    iconGap = 8f;

    public override void Initialization(UIData _data)
    {
        if (_data is not KeywordExplainData t_d) return;
        if (this.iconImage   != null) this.iconImage.sprite = t_d.icon;
        if (this.nameText    != null) this.nameText.text    = t_d.displayName;
        if (this.explainText != null) this.explainText.text = t_d.explain;
        if (t_d.iconRect != null) PositionNearIcon(t_d.iconRect);
    }

    void PositionNearIcon(RectTransform _iconRect)
    {
        Canvas t_canvas = GetComponentInParent<Canvas>();
        if (t_canvas == null) return;

        Camera t_cam = t_canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : t_canvas.worldCamera;
        Vector2 t_screenPos = RectTransformUtility.WorldToScreenPoint(t_cam, _iconRect.position);

        RectTransform t_canvasRect = t_canvas.GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            t_canvasRect, t_screenPos, t_cam, out Vector2 t_local);

        RectTransform t_rt   = GetComponent<RectTransform>();
        float t_halfPopup    = t_rt.sizeDelta.x * 0.5f;
        float t_halfIcon     = _iconRect.sizeDelta.x * 0.5f;
        float t_offsetX      = t_halfPopup + t_halfIcon + this.iconGap;
        float t_canvasHalfW  = t_canvasRect.rect.width * 0.5f;

        Vector2 t_pos = t_local + new Vector2(t_offsetX, 0f);
        if (t_pos.x + t_halfPopup > t_canvasHalfW)
            t_pos = t_local - new Vector2(t_offsetX, 0f);

        t_rt.anchoredPosition = t_pos;
    }

    public override void Show()
    {
        this.contents.SetActive(true);
        this.isShow = true;
    }

    public override void Hide()
    {
        this.contents.SetActive(false);
        this.isShow = false;
    }
}

public class KeywordExplainData : UIData
{
    public Sprite        icon;
    public string        displayName;
    public string        explain;
    public RectTransform iconRect;
}
