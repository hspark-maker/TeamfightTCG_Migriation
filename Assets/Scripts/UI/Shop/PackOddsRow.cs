using TMPro;
using UnityEngine;

// 확률 고지 목록의 한 줄. 배선 안 된 필드는 조용히 건너뛴다(아이콘 없는 간소 행도 그대로 쓰게).
public class PackOddsRow : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI cardNameText;
    [SerializeField] TextMeshProUGUI rateText;
    [SerializeField] CardVisualView cardVisual;

    public void Bind(PackOddsEntry _entry)
    {
        int t_card = _entry.CardId;
        
        if (this.cardNameText != null)
            this.cardNameText.text = t_card > 0 ? CardCatalog.RequireSpec(t_card).DisplayName : string.Empty;

        if (this.rateText != null)
            this.rateText.text = PackOdds.FormatRate(_entry.Rate);

        if (this.cardVisual != null)
            this.cardVisual.Bind(t_card, true);
    }
}
