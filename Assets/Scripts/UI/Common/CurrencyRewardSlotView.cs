using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 재화 보상 칸 하나(아이콘 + 수량) — MonoBehaviour가 아니라 뷰가 필드로 소유한다.
// 도감 보상 요약과 랭크 보상 행·팝업이 같은 칸을 쓴다.
[System.Serializable]
public class CurrencyRewardSlotView
{
    [SerializeField] GameObject root;
    [SerializeField] Image icon;
    [SerializeField] TMP_Text amountLabel;
    [SerializeField] CardVisualView cardVisual;
    [SerializeField] CanvasGroup cardGroup;
    [Tooltip("보상 아이콘 뒤의 파티클 묶음. 아이콘 퇴장과 같은 시간에 함께 숨긴다.")]
    [SerializeField] CanvasGroup backgroundEffects;
    Color m_amountColor;
    bool m_hasAmountColor;
    Color m_iconColor;
    bool m_hasIconColor;

    // 칸 '안'을 안무하는 쪽(랭크 보상 오버레이)을 위한 손잡이. 같은 노드를 저쪽에서 또 배선하면 진실원이 갈린다.
    public GameObject Root => this.root;
    public Image Icon => this.icon;
    public TMP_Text Amount => this.amountLabel;
    public CanvasGroup BackgroundEffects => this.backgroundEffects;
    public CanvasGroup CardGroup => this.cardGroup;
    public RectTransform VisualTransform => this.cardGroup != null && this.cardGroup.gameObject.activeSelf
        ? (RectTransform)this.cardGroup.transform : this.icon != null ? this.icon.rectTransform : null;

    /// <summary>저작한 아이콘을 유지하고 보상 수량만 표시한다.</summary>
    public void BindAmount(long _amount)
    {
        if (amountLabel != null)
        {
            if (!m_hasAmountColor) { m_amountColor = amountLabel.color; m_hasAmountColor = true; }
            amountLabel.color = m_amountColor;
        }
        if (root != null) root.SetActive(true);
        if (amountLabel != null) amountLabel.text = _amount.ToString("N0");
    }

    public void Bind(Sprite _icon, long _amount)
    {
        BindAmount(_amount);
        if (icon != null)
        {
            if (!m_hasIconColor) { m_iconColor = icon.color; m_hasIconColor = true; }
            icon.color = m_iconColor;
        }
        if (icon != null) CardArtBinding.Clear(icon.gameObject);
        if (cardGroup != null) cardGroup.gameObject.SetActive(false);
        if (cardVisual != null) cardVisual.gameObject.SetActive(false);
        if (icon != null) icon.enabled = true;
        if (icon != null && _icon != null) icon.sprite = _icon;   // null이면 목업 스프라이트 보존
    }

    public void Hide()
    {
        if (cardVisual != null) cardVisual.gameObject.SetActive(false);
        if (cardGroup != null) cardGroup.gameObject.SetActive(false);
        if (root != null) root.SetActive(false);
    }

    public void Bind(RewardLine _line)
    {
        Bind(_line.Icon, _line.Amount);
        if (icon != null) icon.enabled = _line.Icon != null;
        if (_line.Type == ERewardType.Card && cardVisual != null && cardGroup != null &&
            int.TryParse(_line.RewardId, out int t_cardId) && CardCatalog.TryGetSpec(t_cardId, out _))
        {
            if (icon != null) icon.enabled = false;
            cardGroup.gameObject.SetActive(true);
            cardVisual.Bind(t_cardId, _owned: true, _mine: true);
            return;
        }
        if (!_line.IsCurrency && _line.Icon == null && amountLabel != null)
            amountLabel.text = RewardItemDisplay.NameOf(_line.Type.ToString(), _line.RewardId) + " ×" + _line.Amount;

        if (_line.Type == ERewardType.Title)
        {
            if (icon != null) icon.color = RewardItemDisplay.ItemColor("Title", _line.RewardId);
            if (amountLabel != null)
            {
                amountLabel.text = RewardItemDisplay.NameOf("Title", _line.RewardId);
                if (_line.IsNewItem.HasValue)
                    amountLabel.color = _line.IsNewItem.Value ? new Color(1f, 0.78f, 0.22f) : Color.gray;
            }
        }

        if (_line.Type == ERewardType.Avatar || _line.Type == ERewardType.Frame || _line.Type == ERewardType.Emote)
        {
            if (icon != null) icon.color = RewardItemDisplay.ItemColor(_line.Type.ToString(), _line.RewardId);
            if (amountLabel != null && _line.IsNewItem.HasValue)
            {
                string t_status = _line.IsNewItem.Value ? "NEW" : "보유 중";
                amountLabel.text = _line.Icon != null ? t_status
                    : RewardItemDisplay.NameOf(_line.Type.ToString(), _line.RewardId) + " · " + t_status;
                amountLabel.color = _line.IsNewItem.Value ? new Color(1f, 0.78f, 0.22f) : Color.gray;
            }
        }

        // 카드 아트는 비동기 로드된다. 슬롯의 활성 수명에 묶어 숨김·재사용 시 이전 요청을 해제한다.
        if (_line.Type == ERewardType.Card && icon != null &&
            int.TryParse(_line.RewardId, out int t_iconCardId) && CardCatalog.TryGetSpec(t_iconCardId, out var t_spec))
        {
            CardArtBinding.Bind(icon, CardArtCache.AddressOf(t_spec, 0), t_sprite =>
            {
                if (amountLabel != null)
                    amountLabel.text = t_sprite != null ? _line.Amount.ToString("N0")
                        : RewardItemDisplay.NameOf(_line.Type.ToString(), _line.RewardId) + " ×" + _line.Amount;
            });
        }
    }
}
