using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckPileUI : MonoBehaviour
{
    [SerializeField] BattleField field;
    [SerializeField] TMP_Text countText;
    [SerializeField] Button deckButton;
    [SerializeField] GameObject panel;
    [SerializeField] Transform cardListRoot;

    [Header("Player")]
    [SerializeField] CardElement cardElementPrefab;

    [Header("Enemy")]
    [SerializeField] GameObject faceDownEntryPrefab;

    static DeckPileUI currentOpen;

    readonly List<CardElement> cardElementPool = new List<CardElement>();
    readonly List<GameObject> faceDownPool = new List<GameObject>();

    bool panelOpen;

    void Start()
    {
        this.deckButton.onClick.AddListener(Toggle);
    }

    public void Refresh()
    {
        if (this.countText != null)
            this.countText.text = this.field.WaitingCount.ToString();
    }

    void Toggle()
    {
        if (this.panelOpen)
        {
            Close();
        }
        else
        {
            if (currentOpen != null && currentOpen != this)
                currentOpen.Close();
            Open();
        }
    }

    void Open()
    {
        this.panelOpen = true;
        this.panel.SetActive(true);
        TurnState.InputAllowed = false;
        currentOpen = this;
        PopulateList();
    }

    void Close()
    {
        this.panelOpen = false;
        this.panel.SetActive(false);
        TurnState.InputAllowed = true;
        if (currentOpen == this) currentOpen = null;
    }

    void PopulateList()
    {
        foreach (CardElement t_e in this.cardElementPool) t_e.gameObject.SetActive(false);
        foreach (GameObject t_e in this.faceDownPool) t_e.SetActive(false);

        int t_ceIdx = 0;
        int t_fdIdx = 0;

        foreach (CardInstance t_card in this.field.GetWaitingCards())
        {
            bool t_showCard = this.field.OwnerIndex == TurnState.LocalOwnerIndex || t_card.wasEverRevealed;

            if (t_showCard)
            {
                CardElement t_entry;
                if (t_ceIdx < this.cardElementPool.Count)
                {
                    t_entry = this.cardElementPool[t_ceIdx];
                    t_entry.gameObject.SetActive(true);
                }
                else
                {
                    t_entry = Instantiate(this.cardElementPrefab, this.cardListRoot);
                    this.cardElementPool.Add(t_entry);
                }
                t_entry.Init(t_card, CardElementMod.Full);
                t_entry.SetInteractable(false, false);
                t_ceIdx++;
            }
            else
            {
                GameObject t_entry;
                if (t_fdIdx < this.faceDownPool.Count)
                {
                    t_entry = this.faceDownPool[t_fdIdx];
                    t_entry.SetActive(true);
                }
                else
                {
                    t_entry = Instantiate(this.faceDownEntryPrefab, this.cardListRoot);
                    this.faceDownPool.Add(t_entry);
                }
                t_fdIdx++;
            }
        }
    }
}
