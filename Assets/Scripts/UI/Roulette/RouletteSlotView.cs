using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 룰렛 판의 쐐기 1칸. 상품 그림과 수량, 그리고 최고 상품 표식만 그린다 — 추첨도 회전도 모른다.
public class RouletteSlotView : MonoBehaviour
{
    [Tooltip("상품 재화 그림. 표(CurrencyLook)에 그림이 없으면 프리팹에 저작된 그림을 그대로 둔다.")]
    [SerializeField] Image icon;

    [SerializeField] TMP_Text amountText;

    [Tooltip("수량 표기 형식. 12,345 처럼 자리수 구분이 들어간다.")]
    [SerializeField] string amountFormat = "N0";

    [Tooltip("최고 상품 칸에만 켜는 강조입니다. 판 그림이 여덟 쐐기를 균일하게 그리고 아이콘은 재화 종류만 " +
             "말하기 때문에, 이것이 없으면 다이아 300 칸과 다이아 30 칸을 수량 글자로만 가려야 합니다 — " +
             "판이 도는 동안 그 글자는 읽히지 않습니다.\n\n" +
             "어느 칸이 최고 상품인지는 저작이 아니라 RouletteSlot 시트의 가중치가 정합니다. " +
             "여기 배선한 노드는 코드가 켜고 끄므로, 저작에서는 꺼 둔 채로 두세요.\n\n" +
             "상품 아이콘을 가리지 않도록 이 노드는 칸의 자식이 아니라 형제로 두고 뒤에 깝니다.")]
    [SerializeField] GameObject topMark;

    /// <summary>이 칸이 내줄 상품과 최고 상품 여부를 그린다. 어느 칸이 최고인지는 판정해 온 값을 받을 뿐이다.</summary>
    public void Bind(ECurrencyType _currency, long _amount, bool _isTop)
    {
        if (this.icon != null)
        {
            Sprite t_sprite = CurrencyLook.IconOf(_currency);
            if (t_sprite != null) this.icon.sprite = t_sprite;
        }

        if (this.amountText != null) this.amountText.text = _amount.ToString(this.amountFormat);

        if (this.topMark != null) this.topMark.SetActive(_isTop);
    }

    /// <summary>당첨된 칸을 한 박 튀긴다. 판이 멈춘 뒤 어디에 섰는지를 칸 자신이 말한다.</summary>
    public void PlayWinPunch() => UiPunch.Play(transform);
}
