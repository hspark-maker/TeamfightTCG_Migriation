using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CardAcquisitionRow : MonoBehaviour
{
    [SerializeField] Image packArt;
    [SerializeField] TMP_Text packName;
    [SerializeField] TMP_Text condition;
    [SerializeField] Button moveButton;
    [SerializeField] TMP_Text moveLabel;

    internal void Bind(CardAcquisitionSources.Source _source, bool _canNavigate, Action<string> _navigate)
    {
        this.packArt.sprite = PackSpec.Art(_source.PackId);
        this.packArt.enabled = this.packArt.sprite != null;
        this.packName.text = PackSpec.DisplayName(_source.PackId);
        this.condition.text = _source.Condition;
        this.condition.color = _source.Available
            ? new Color(0.22f, 0.48f, 0.27f) : new Color(0.48f, 0.42f, 0.36f);
        this.moveLabel.text = _source.Available ? "이동" : "잠김";
        this.moveButton.interactable = _source.Available && _canNavigate;
        this.moveButton.onClick.RemoveAllListeners();
        this.moveButton.onClick.AddListener(() => _navigate(_source.PackId));
    }

    internal void BindCraft(int _card, bool _canNavigate, Action _navigate)
    {
        bool t_owned = OwnershipManager.IsOwned(_card);
        bool t_ready = CardCraftCommands.IsReady;
        bool t_available = CardCraftCommands.TryGetRecipe(_card, out var t_recipe) && t_recipe.Available;
        var t_currency = t_available ? t_recipe.Currency : ECurrencyType.CardDust;
        this.packArt.sprite = CurrencyLook.IconOf(t_currency);
        this.packArt.enabled = this.packArt.sprite != null;
        this.packName.text = "카드 제작";
        this.condition.text = t_owned ? "이미 보유한 카드입니다."
            : t_available ? $"{CurrencyLook.NameOf(t_currency)} {t_recipe.Cost:N0}개로 제작"
            : t_ready ? "이 카드는 제작할 수 없습니다."
            : CardCraftCommands.IsRefreshing ? "제작 정보를 불러오는 중입니다."
            : "제작 화면에서 확인할 수 있습니다.";
        this.condition.color = t_available && !t_owned
            ? new Color(0.22f, 0.48f, 0.27f) : new Color(0.48f, 0.42f, 0.36f);
        this.moveLabel.text = t_owned ? "보유" : t_ready && !t_available ? "불가"
            : _canNavigate ? "이동" : "잠김";
        this.moveButton.interactable = !t_owned && (!t_ready || t_available) && _canNavigate;
        this.moveButton.onClick.RemoveAllListeners();
        this.moveButton.onClick.AddListener(() => _navigate());
    }
}
