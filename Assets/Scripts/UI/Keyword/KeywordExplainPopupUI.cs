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

    // 배치 규칙은 PopupPlacer가 단독 소유(시너지 팝업/툴팁과 동일 구현).
    // 세로 클램프는 기존 동작 보존을 위해 끈 상태 — 위/아래로 넘치는 게 문제가 되면 true로.
    void PositionNearIcon(RectTransform _iconRect)
        => PopupPlacer.PlaceBesideAnchor((RectTransform)transform, _iconRect, this.iconGap, _clampVertical: false);

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
