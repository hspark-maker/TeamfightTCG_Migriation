using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class KeywordExplainItem : MonoBehaviour
{
    [SerializeField] Image    iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text explainText;

    [Tooltip("아직 활성이 아닌 시너지 행 위에 덮는 회색 판(선택). 미배선이면 글자·아이콘 색만 죽인다")]
    [SerializeField] GameObject inactiveOverlay;

    // 오버레이가 없는 프리팹에서도 비활성이 읽히게 하는 대비색. 오버레이가 있으면 그쪽이 주가 된다.
    static readonly Color InactiveTint = new Color(0.55f, 0.55f, 0.6f, 0.85f);

    Color iconColor0, nameColor0, explainColor0;
    bool  cachedColors;

    /// <summary>_iconScale = 아이콘 오브젝트 배율. 키워드는 1(기본), 시너지는 PNG 투명 여백만큼
    /// 키워 같은 행에서 크기가 맞게 한다 — 배율의 단일 진실원은 <see cref="SynergyIconStrip.IconPadCompensation"/>.
    ///
    /// _active=false면 "덱에 장수가 모자라 아직 안 켜진 시너지" — 회색 판을 덮고 색을 죽인다.
    /// 키워드 행은 활성 개념이 없으므로 항상 true로 온다.</summary>
    public void Init(Sprite _icon, string _name, string _explain, float _iconScale = 1f, bool _active = true)
    {
        CacheColors();

        if (this.iconImage   != null)
        {
            this.iconImage.sprite = _icon;
            this.iconImage.rectTransform.localScale = Vector3.one * _iconScale;
        }
        if (this.nameText    != null) this.nameText.text    = _name;
        if (this.explainText != null) this.explainText.text = _explain;

        // 행은 풀에서 재사용되지 않고 매번 새로 만들어지지만, 켜고 끄는 쪽을 한 곳에 모아 둔다.
        if (this.inactiveOverlay != null) this.inactiveOverlay.SetActive(!_active);

        if (this.iconImage   != null) this.iconImage.color   = _active ? this.iconColor0    : InactiveTint;
        if (this.nameText    != null) this.nameText.color    = _active ? this.nameColor0    : InactiveTint;
        if (this.explainText != null) this.explainText.color = _active ? this.explainColor0 : InactiveTint;
    }

    // 저작된 원래 색을 첫 Init 때 기억한다 — 비활성 색을 덮어쓴 뒤 활성으로 돌아올 때 되돌릴 기준.
    void CacheColors()
    {
        if (this.cachedColors) return;
        this.cachedColors = true;

        if (this.iconImage   != null) this.iconColor0    = this.iconImage.color;
        if (this.nameText    != null) this.nameColor0    = this.nameText.color;
        if (this.explainText != null) this.explainColor0 = this.explainText.color;
    }
}
