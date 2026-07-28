using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class KeywordExplainItem : MonoBehaviour
{
    [SerializeField] Image    iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text explainText;

    /// <summary>_iconScale = 아이콘 오브젝트 배율. 키워드는 1(기본), 시너지는 PNG 투명 여백만큼
    /// 키워 같은 행에서 크기가 맞게 한다 — 배율의 단일 진실원은 <see cref="SynergyIconStrip.IconPadCompensation"/>.</summary>
    public void Init(Sprite _icon, string _name, string _explain, float _iconScale = 1f)
    {
        if (this.iconImage   != null)
        {
            this.iconImage.sprite = _icon;
            this.iconImage.rectTransform.localScale = Vector3.one * _iconScale;
        }
        if (this.nameText    != null) this.nameText.text    = _name;
        if (this.explainText != null) this.explainText.text = _explain;
    }
}
