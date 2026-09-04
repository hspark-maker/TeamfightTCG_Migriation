using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 룰렛 판의 쐐기 1칸. 상품 그림과 수량만 그린다 — 추첨도 회전도 모른다.
public class RouletteSlotView : MonoBehaviour
{
    [Tooltip("상품 재화 그림. 표(CurrencyLook)에 그림이 없으면 프리팹에 저작된 그림을 그대로 둔다.")]
    [SerializeField] Image icon;

    [SerializeField] TMP_Text amountText;

    [Tooltip("수량 표기 형식. 12,345 처럼 자리수 구분이 들어간다.")]
    [SerializeField] string amountFormat = "N0";

    /// <summary>이 칸이 내줄 상품을 그린다. 잭팟 여부는 판 그림이 저작으로 말한다 — 코드가 표식을 켜지 않는다.</summary>
    public void Bind(ECurrencyType _currency, long _amount)
    {
        if (this.icon != null)
        {
            Sprite t_sprite = CurrencyLook.IconOf(_currency);
            if (t_sprite != null) this.icon.sprite = t_sprite;
        }

        if (this.amountText != null) this.amountText.text = _amount.ToString(this.amountFormat);
    }

    /// <summary>당첨된 칸을 한 박 튀긴다. 판이 멈춘 뒤 어디에 섰는지를 칸 자신이 말한다.</summary>
    public void PlayWinPunch() => UiPunch.Play(transform);
}
