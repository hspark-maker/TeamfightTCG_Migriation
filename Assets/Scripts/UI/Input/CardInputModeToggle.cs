using UnityEngine;
using TMPro;

public class CardInputModeToggle : MonoBehaviour
{
    [SerializeField] TMP_Text modeLabel;

    void Start()
    {
        CardView.currentInputMode = CardView.InputMode.DragToEnemy;
        RefreshLabel();
    }

    public void ToggleMode()
    {
        CardView.currentInputMode = CardView.currentInputMode == CardView.InputMode.DragBack
            ? CardView.InputMode.DragToEnemy
            : CardView.InputMode.DragBack;

        RefreshLabel();
    }

    void RefreshLabel()
    {
        if (this.modeLabel == null) return;
        this.modeLabel.text = CardView.currentInputMode == CardView.InputMode.DragBack
            ? "모드: 드래그백"
            : "모드: 적 드래그";
    }
}
