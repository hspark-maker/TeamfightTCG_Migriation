using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class KeywordExplainPopupUI : PooledUIBase
{
    [SerializeField] Image    iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text explainText;
    [SerializeField] float    iconGap = 8f;

    RectTransform     m_anchor;
    Rect              m_lastSafeArea;
    Vector2Int        m_lastScreen;
    ScreenOrientation m_lastOrientation;

    public override void Initialization(UIData _data)
    {
        if (_data is not KeywordExplainData t_d) return;
        if (this.iconImage   != null) this.iconImage.sprite = t_d.icon;
        if (this.nameText    != null) this.nameText.text    = t_d.displayName;
        if (this.explainText != null) this.explainText.text = t_d.explain;
        this.m_anchor = t_d.iconRect;
        if (this.m_anchor != null) PositionNearIcon(this.m_anchor);
    }

    // 배치 규칙은 PopupPlacer가 단독 소유(시너지 팝업/툴팁과 동일 구현).
    void PositionNearIcon(RectTransform _iconRect)
    {
        PopupPlacer.PlaceBesideAnchor((RectTransform)transform, _iconRect, this.iconGap);
        CaptureScreenState();
    }

    void LateUpdate()
    {
        if (!this.isShow || this.m_anchor == null || !ScreenStateChanged()) return;
        PositionNearIcon(this.m_anchor);
    }

    bool ScreenStateChanged()
        => Screen.safeArea != this.m_lastSafeArea
           || Screen.width != this.m_lastScreen.x || Screen.height != this.m_lastScreen.y
           || Screen.orientation != this.m_lastOrientation;

    void CaptureScreenState()
    {
        this.m_lastSafeArea    = Screen.safeArea;
        this.m_lastScreen      = new Vector2Int(Screen.width, Screen.height);
        this.m_lastOrientation = Screen.orientation;
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
