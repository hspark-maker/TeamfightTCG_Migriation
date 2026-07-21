using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class KeywordExplainItem : MonoBehaviour
{
    [SerializeField] Image    iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text explainText;

    public void Init(Sprite _icon, string _name, string _explain)
    {
        if (this.iconImage   != null) this.iconImage.sprite = _icon;
        if (this.nameText    != null) this.nameText.text    = _name;
        if (this.explainText != null) this.explainText.text = _explain;
    }
}
