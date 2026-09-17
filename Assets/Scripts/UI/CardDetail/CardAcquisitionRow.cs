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
}
