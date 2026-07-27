using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class DeckBuilderUI : MonoBehaviour
{
    // 카드 목록은 CardRegistry(SO)가 단일 진실원. 예전엔 여기에 사본을 들고 있어서
    // 카드를 추가해도 이 리스트를 안 고치면 컬렉션에 안 뜨는 버그가 있었다.
    [SerializeField] CardRegistry cardRegistry;
    [SerializeField] Transform collectionGrid;
    [SerializeField] CardElement cardElementPrefab;
    [SerializeField] Canvas canvas;
    [SerializeField] DeckGroup deckGroup;
    [SerializeField] GameObject deckListPanel;
    [SerializeField] TMP_Text[] deckListButtonLabels;

    readonly List<CardElement> spawnedCards = new List<CardElement>();

    PooledCardElement dragGhost;
    CardData currentDragCard;
    int currentSlotIndex;
    bool isDirty;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    void Start()
    {
        this.deckGroup.Initialize();
        this.deckGroup.OnNameSubmit += OnDeckNameSubmit;

        InitializeSlots();
        LoadCurrentDeck();
        RefreshDeckListButtons();
    }

    void OnDestroy()
    {
        if (this.deckGroup != null)
            this.deckGroup.OnNameSubmit -= OnDeckNameSubmit;
    }

    // ── Deck Management ───────────────────────────────────────────────────

    public void SelectSlot(int _index)
    {
        if (this.isDirty)
        {
            ShowSavePopup(
                () => { SaveCurrentDeck(); SwitchSlot(_index); },
                () => { DiscardChanges();  SwitchSlot(_index); });
            return;
        }
        SwitchSlot(_index);
    }

    void SwitchSlot(int _index)
    {
        this.currentSlotIndex = _index;
        LoadCurrentDeck();
        RefreshDeckListButtons();
    }

    public void SetSlot(int _index, CardData _card)
    {
        this.deckGroup.SetSlot(_index, _card);
        this.isDirty = true;
        RefreshDeckDependentUI();
    }

    public void SaveCurrentDeck()
    {
        DeckSaveManager.Save(this.currentSlotIndex, this.deckGroup.Deck);
        DeckSaveManager.SaveToFile();
        this.isDirty = false;
    }

    public void DiscardChanges() => this.isDirty = false;

    public void DeleteCurrentDeck()
    {
        if (!DeckSaveManager.IsSlotValid(this.currentSlotIndex)) return;
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = $"'{DeckSaveManager.GetName(this.currentSlotIndex)}' 덱을 삭제하시겠습니까?",
            yesText   = "삭제",
            noText    = "취소",
            yesAction = () =>
            {
                DeckSaveManager.Delete(this.currentSlotIndex);
                LoadCurrentDeck();
                RefreshDeckListButtons();
            },
        });
    }

    void LoadCurrentDeck()
    {
        List<CardData> t_loaded = DeckSaveManager.Load(this.currentSlotIndex);
        this.deckGroup.SetDeck(t_loaded.ToArray());
        this.deckGroup.SetDeckName(DeckSaveManager.GetName(this.currentSlotIndex));
        this.isDirty = false;
        RefreshDeckDependentUI();
    }

    void OnDeckNameSubmit(string _name)
    {
        DeckSaveManager.SetName(this.currentSlotIndex, _name);
        DeckSaveManager.SaveToFile();
        RefreshDeckListButtons();
    }

    void RefreshDeckListButtons()
    {
        if (this.deckListButtonLabels == null) return;
        for (int i = 0; i < this.deckListButtonLabels.Length; i++)
        {
            if (this.deckListButtonLabels[i] == null) continue;
            this.deckListButtonLabels[i].text = DeckSaveManager.GetName(i);
        }
    }

    // ── Collection ────────────────────────────────────────────────────────

    public void InitializeSlots()
    {
        foreach (Transform t_child in this.collectionGrid)
            Destroy(t_child.gameObject);
        this.spawnedCards.Clear();

        if (this.cardRegistry == null)
        {
            Debug.LogError("[DeckBuilderUI] cardRegistry 미배선 — 컬렉션이 비어 보인다.");
            return;
        }

        foreach (CardData t_card in this.cardRegistry.All)
        {
            if (t_card == null) continue;   // 레지스트리 빈 칸(ID 보존용)은 건너뜀
            CardElement t_element = Instantiate(this.cardElementPrefab, this.collectionGrid);
            t_element.Init(t_card);
            t_element.onBeginDrag = OnCardBeginDrag;
            t_element.onDrag      = OnCardDrag;
            t_element.onEndDrag   = OnCardEndDrag;
            this.spawnedCards.Add(t_element);
        }
        RefreshCollectionInteractable();
    }

    /// <summary>덱 내용이 바뀔 때 함께 갱신돼야 하는 UI 묶음. 새 덱 파생 표시가 생기면 여기에 추가할 것.
    /// (시너지 아이콘은 DeckGroup이 SetDeck/SetSlot에서 스스로 갱신한다.)</summary>
    void RefreshDeckDependentUI()
    {
        RefreshCollectionInteractable();
    }

    void RefreshCollectionInteractable()
    {
        CardData[] t_deck = this.deckGroup.Deck;
        foreach (CardElement t_element in this.spawnedCards)
        {
            if (t_element == null) continue;
            bool t_inDeck = Array.Exists(t_deck, d => d == t_element.CardData);
            t_element.SetInteractable(!t_inDeck, true);
        }
    }

    // ── Drag ─────────────────────────────────────────────────────────────

    void OnCardBeginDrag(CardData _card, PointerEventData _eventData)
    {
        this.currentDragCard = _card;
        this.dragGhost = UIPoolManager.Instance?.AddOrUpdateUI<PooledCardElement>(
            new PooledCardElementData { card = _card, mod = CardElementMod.Simple });
        this.dragGhost.transform.SetAsLastSibling();
        MoveGhostToPointer(_eventData);
    }

    void OnCardDrag(PointerEventData _eventData)
    {
        if (this.dragGhost == null) return;
        MoveGhostToPointer(_eventData);
    }

    void OnCardEndDrag(PointerEventData _eventData)
    {
        int t_slotIndex = GetHoveredDeckSlot(_eventData);
        if (t_slotIndex >= 0 && this.currentDragCard != null)
            SetSlot(t_slotIndex, this.currentDragCard);

        UIPoolManager.Instance?.HideUI<PooledCardElement>();
        this.dragGhost      = null;
        this.currentDragCard = null;
    }

    int GetHoveredDeckSlot(PointerEventData _eventData)
    {
        CardElement[] t_slots = this.deckGroup.DeckSlots;
        if (t_slots == null) return -1;
        for (int i = 0; i < t_slots.Length; i++)
        {
            if (t_slots[i] == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)t_slots[i].transform,
                _eventData.position,
                _eventData.pressEventCamera))
                return i;
        }
        return -1;
    }

    void MoveGhostToPointer(PointerEventData _eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)this.canvas.transform,
            _eventData.position,
            _eventData.pressEventCamera,
            out Vector2 t_localPos);
        ((RectTransform)this.dragGhost.transform).anchoredPosition = t_localPos;
    }

    // ── Navigation ────────────────────────────────────────────────────────

    public void ToggleDeckList(bool _toggle) => this.deckListPanel.SetActive(_toggle);

    public void OnBackButton()
    {
        if (this.isDirty)
        {
            ShowSavePopup(
                () => { SaveCurrentDeck(); gameObject.SetActive(false); },
                () => { DiscardChanges();  gameObject.SetActive(false); });
            return;
        }
        gameObject.SetActive(false);
    }

    void ShowSavePopup(Action _onYes, Action _onNo)
    {
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "덱을 저장하시겠습니까?",
            yesText   = "저장",
            yesAction = _onYes,
            noText    = "취소",
            noAction  = _onNo,
        });
    }
}
