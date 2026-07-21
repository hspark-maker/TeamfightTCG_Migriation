using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameReadyPanel : MonoBehaviour
{
    [SerializeField] Button[] deckButtons;
    [SerializeField] Image[] deckPreviewImages;
    [SerializeField] Sprite emptySlotSprite;
    [SerializeField] DeckGroup deckGroup;

    [SerializeField] GameObject matchingTypePanel;
    [SerializeField] GameObject gameTypePanel;
    [SerializeField] MainMenuManager mainMenuManager;

    void Start()
    {
        for (int t_i = 0; t_i < this.deckButtons.Length; t_i++)
        {
            if (this.deckButtons[t_i] == null) continue;
            int t_slotIndex = t_i;
            this.deckButtons[t_i].onClick.AddListener(() => OnDeckSelected(t_slotIndex));
        }
    }

    void OnEnable()
    {
        RefreshButtons();
        for (int t_i = 0; t_i < this.deckButtons.Length; t_i++)
        {
            if (!DeckSaveManager.IsSlotValid(t_i)) continue;
            OnDeckSelected(t_i);
            break;
        }
    }

    private void OnDisable()
    {
        this.matchingTypePanel.SetActive(false);
        this.gameTypePanel.SetActive(true);
    }

    public void OnClickMultiPlay()
    {
        this.matchingTypePanel.SetActive(true);
        this.gameTypePanel.SetActive(false);
    }
    public void OnBackButton()
    {
        if (this.matchingTypePanel.activeSelf)
        {
            this.matchingTypePanel.SetActive(false);
            this.gameTypePanel.SetActive(true);
        }
        else
        {
            this.mainMenuManager?.OnBackPressed();
        }
    }

    public void RefreshButtons()
    {
        for (int t_i = 0; t_i < this.deckButtons.Length; t_i++)
        {
            if (this.deckButtons[t_i] == null) continue;
            bool t_valid = DeckSaveManager.IsSlotValid(t_i);
            this.deckButtons[t_i].interactable = t_valid;

            if (t_i >= this.deckPreviewImages.Length || this.deckPreviewImages[t_i] == null) continue;
            List<CardData> t_deck = DeckSaveManager.GetSlot(t_i);
            this.deckPreviewImages[t_i].sprite = t_valid && t_deck != null && t_deck.Count > 0 && t_deck[0] != null
                ? t_deck[0].deckPreview
                : this.emptySlotSprite;
        }
    }

    void OnDeckSelected(int _slotIndex)
    {
        if (!DeckSaveManager.IsSlotValid(_slotIndex)) return;
        DeckConfig.Set(DeckSaveManager.GetSlot(_slotIndex));
        this.deckGroup?.LoadSlot(_slotIndex);
    }
}
