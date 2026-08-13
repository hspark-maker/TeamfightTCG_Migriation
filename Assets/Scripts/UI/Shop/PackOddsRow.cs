using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 확률 고지 목록의 한 줄. 배선 안 된 필드는 조용히 건너뛴다(아이콘 없는 간소 행도 그대로 쓰게).
public class PackOddsRow : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI cardNameText;
    [SerializeField] TextMeshProUGUI rateText;

    public void Bind(PackOddsEntry _entry)
    {
        CardData t_card = _entry.Card;
        
        if (this.cardNameText != null)
            this.cardNameText.text = t_card != null ? t_card.displayName : string.Empty;

        if (this.rateText != null)
            this.rateText.text = PackOdds.FormatRate(_entry.Rate);
    }
}
