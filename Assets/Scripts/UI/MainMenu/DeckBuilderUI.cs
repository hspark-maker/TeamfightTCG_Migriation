using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class DeckBuilderUI : MonoBehaviour
{
    // 카드 목록은 CardRegistry(SO)가 단일 진실원. 예전엔 여기에 사본을 들고 있어서
    // 카드를 추가해도 이 리스트를 안 고치면 컬렉션에 안 뜨는 버그가 있었다.
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
                () => { SwitchSlot(TrySaveCurrentDeck() ? ShiftedForInsert(_index) : _index); },
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

    // 씬 버튼 배선용 진입점(UnityEvent는 void 메서드만 찾는다).
    public void SaveCurrentDeck() => TrySaveCurrentDeck();

    // 앞 삽입이 실제로 일어났으면 true — 기존 덱이 한 칸씩 뒤로 밀리므로 호출부가 좌표를 보정해야 한다.
    bool TrySaveCurrentDeck()
    {
        bool t_inserted = false;

        // 미완성 편성은 저장하지 않는다 — 유효 덱을 6장 미만으로 덮으면 그 자리가 구멍이 돼
        // DeckCount가 앞에서 접히고(압축 불변식 파손) 덱 목록이 통째로 비어 보인다.
        if (this.deckGroup.Deck.Count(c => c != null) != DeckSaveManager.DECK_SIZE)
        {
            Debug.LogWarning($"[DeckBuilderUI] 카드 {DeckSaveManager.DECK_SIZE}장을 채우지 않아 저장하지 않았다.");
        }
        else if (DeckSaveManager.IsSlotValid(this.currentSlotIndex))
        {
            DeckSaveManager.SaveSlot(this.currentSlotIndex, this.deckGroup.Deck);   // 기존 덱 편집은 위치 유지
        }
        else
        {
            // 빈 칸에 그대로 쓰면 구멍이 생겨 압축 불변식이 깨진다 — 신규는 맨 앞 삽입.
            string t_name = DeckSaveManager.GetName(this.currentSlotIndex);
            if (string.IsNullOrEmpty(t_name)) t_name = DeckSaveManager.SuggestNewDeckName();

            t_inserted = DeckSaveManager.TryInsertFront(
                this.deckGroup.Deck, t_name, DeckImages.PickRandomKey(), out int t_index);

            // 실패 시 -1을 필드에 굳히면 이후 모든 슬롯 접근이 범위를 벗어난다.
            if (t_inserted) this.currentSlotIndex = t_index;
        }

        this.isDirty = false;

        return t_inserted;
    }

    // 앞 삽입 뒤에는 클릭 시점의 좌표가 한 칸 뒤를 가리킨다. 범위를 넘으면 보정하지 않는다.
    static int ShiftedForInsert(int _index)
        => _index + 1 < DeckSaveManager.SLOT_COUNT ? _index + 1 : _index;

    public void DiscardChanges() => this.isDirty = false;

    public void DeleteCurrentDeck()
    {
        if (!DeckSaveManager.IsSlotValid(this.currentSlotIndex)) return;
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = $"'{DeckSaveManager.GetDisplayName(this.currentSlotIndex)}' 덱을 삭제하시겠습니까?",
            yesText   = "삭제",
            noText    = "취소",
            yesAction = () =>
            {
                DeckSaveManager.TryDeleteAt(this.currentSlotIndex);   // 삭제 + 압축(뒤 덱이 앞으로 당겨져 보이는 게 정상)
                LoadCurrentDeck();
                RefreshDeckListButtons();
            },
        });
    }

    void LoadCurrentDeck()
    {
        List<CardData> t_loaded = DeckSaveManager.Load(this.currentSlotIndex);
        this.deckGroup.SetDeck(t_loaded.ToArray());
        this.deckGroup.SetDeckName(DeckSaveManager.GetDisplayName(this.currentSlotIndex));
        this.isDirty = false;
        RefreshDeckDependentUI();
    }

    void OnDeckNameSubmit(string _name)
    {
        DeckSaveManager.SetName(this.currentSlotIndex, _name);
        // 이름만 바뀌었으므로 해당 칸만 flush(Load는 빈 슬롯에서도 null이 아닌 리스트를 준다).
        DeckSaveManager.SaveSlot(this.currentSlotIndex, DeckSaveManager.Load(this.currentSlotIndex));
        RefreshDeckListButtons();
    }

    void RefreshDeckListButtons()
    {
        if (this.deckListButtonLabels == null) return;
        for (int i = 0; i < this.deckListButtonLabels.Length; i++)
        {
            if (this.deckListButtonLabels[i] == null) continue;
            this.deckListButtonLabels[i].text = DeckSaveManager.GetDisplayName(i);
        }
    }

    // ── Collection ────────────────────────────────────────────────────────

    public void InitializeSlots()
    {
        foreach (Transform t_child in this.collectionGrid)
            Destroy(t_child.gameObject);
        this.spawnedCards.Clear();

        if (!CardCatalog.IsReady)
        {
            Debug.LogError("[DeckBuilderUI] CardCatalog 미초기화 — 부트 순서를 확인할 것.");
            return;
        }

        foreach (CardData t_card in CardCatalog.All)
        {
            if (t_card == null) continue;   // 레지스트리 빈 칸(ID 보존용)은 건너뜀
            CardElement t_element = Instantiate(this.cardElementPrefab, this.collectionGrid);
            // 아웃게임 편성 화면이라 내 카드다 → 강화 반영 체력을 넘긴다(CardElement 폴백은 전투용 마스터 값).
            t_element.Init(t_card, CardElementMod.Full, DeckPower.MaxHpOf(t_card));
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
