using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DeckGroup : MonoBehaviour
{
    [SerializeField] CardData[] deck = new CardData[6];
    [SerializeField] CardElement[] deckSlots;

    [Header("Deck Name UI")]
    [SerializeField] TMP_Text deckNameText;
    [SerializeField] TMP_InputField deckNameInput;

    public event Action<string> OnNameSubmit;

    public CardData[] Deck     => this.deck;
    public CardElement[] DeckSlots => this.deckSlots;

    public void Initialize()
    {
        if (this.deckNameInput != null)
        {
            this.deckNameInput.gameObject.SetActive(false);
            this.deckNameInput.onEndEdit.AddListener(OnDeckNameSubmit);
        }
    }

    // ── Display ───────────────────────────────────────────────────────────

    public void SetDeck(CardData[] _deck)
    {
        for (int i = 0; i < this.deck.Length; i++)
            this.deck[i] = i < _deck.Length ? _deck[i] : null;
        for (int i = 0; i < this.deckSlots.Length; i++)
            this.deckSlots[i].Init(this.deck[i], CardElementMod.Simple);
    }

    public void SetSlot(int _index, CardData _card)
    {
        this.deck[_index] = _card;
        this.deckSlots[_index].Init(_card, CardElementMod.Simple);
    }

    public void SetDeckName(string _name)
    {
        if (this.deckNameText != null)
            this.deckNameText.text = _name;
    }

    // GameReadyPanel용 — 데이터 읽어서 표시만
    public void LoadSlot(int _index)
    {
        List<CardData> t_loaded = DeckSaveManager.Load(_index);
        SetDeck(t_loaded.ToArray());
        SetDeckName(DeckSaveManager.GetName(_index));
    }

    // ── Deck Name Input ───────────────────────────────────────────────────

    public void OnDeckNameClick()
    {
        if (this.deckNameText  != null) this.deckNameText.gameObject.SetActive(false);
        if (this.deckNameInput != null)
        {
            this.deckNameInput.text = this.deckNameText?.text ?? string.Empty;
            this.deckNameInput.gameObject.SetActive(true);
            this.deckNameInput.Select();
            this.deckNameInput.ActivateInputField();
        }
    }

    void OnDeckNameSubmit(string _value)
    {
        if (this.deckNameInput != null) this.deckNameInput.gameObject.SetActive(false);
        if (this.deckNameText  != null) this.deckNameText.gameObject.SetActive(true);

        string t_trimmed = _value.Trim();
        if (!string.IsNullOrEmpty(t_trimmed))
        {
            SetDeckName(t_trimmed);
            OnNameSubmit?.Invoke(t_trimmed);
        }
    }
}
