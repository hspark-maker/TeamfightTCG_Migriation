using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>MultiplayerTestScene에 저작된 UGUI 디버그 패널.</summary>
public sealed class MultiplayerTestDebugPanel : MonoBehaviour
{
    [SerializeField] MultiplayerTestInitializer initializer;
    [SerializeField] Text summaryText;
    [SerializeField] Text statusText;
    [SerializeField] TMP_InputField slotInput;
    [SerializeField] Button editDeckButton;
    [SerializeField] Button[] busyLockedButtons;

    // 편집 중 숨김은 GameObject가 아니라 이 캔버스로 한다 — 오브젝트를 끄면 Update가 멎어
    // 편집기가 onExit 없이 닫히는 경로에서 패널이 영영 안 돌아온다(자가 복구 불가).
    Canvas m_canvas;
    bool m_editing;

    void Awake()
    {
        this.m_canvas = GetComponentInParent<Canvas>(true);
        if (this.initializer == null) this.initializer = FindFirstObjectByType<MultiplayerTestInitializer>();
        if (this.slotInput != null && string.IsNullOrWhiteSpace(this.slotInput.text)) this.slotInput.text = "0";
        Refresh();
    }

    void OnEnable()
    {
        if (this.initializer == null) return;
        this.initializer.OnStatusChanged += HandleStatusChanged;
        this.initializer.OnStateChanged += Refresh;
        RankManager.OnChanged += Refresh;
        DeckSaveManager.OnDeckChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (this.initializer != null)
        {
            this.initializer.OnStatusChanged -= HandleStatusChanged;
            this.initializer.OnStateChanged -= Refresh;
        }
        RankManager.OnChanged -= Refresh;
        DeckSaveManager.OnDeckChanged -= Refresh;
    }

    public void TierDown() { RankManager.StepTierForDebug(-1); Refresh(); }
    public void TierUp() { RankManager.StepTierForDebug(1); Refresh(); }

    public void UnlockAllCards()
    {
        int t_count = OwnershipManager.GrantEntireCatalog();
        SetLocalStatus($"카드 전체 해금: 새로 해금 {t_count}장");
    }

    public void MaxAllCards()
    {
        if (!CardGrowthManager.IsConfigReady)
        {
            SetLocalStatus("성장 설정이 준비되지 않았습니다.");
            return;
        }
        int t_count = CardGrowthManager.DebugMaxAll();
        SetLocalStatus($"카드 전체 성장: 변경 {t_count}장 (서버 lockDeck 검증 결과 확인 필요)");
    }

    public void EditDeck()
    {
        if (this.initializer == null) return;
        if (!int.TryParse(this.slotInput?.text, out int t_slot))
        {
            SetLocalStatus("슬롯 번호를 입력하세요.");
            return;
        }
        if (t_slot < 0 || t_slot >= DeckSaveManager.SLOT_COUNT)
        {
            SetLocalStatus($"슬롯은 0~{DeckSaveManager.SLOT_COUNT - 1} 사이여야 합니다.");
            return;
        }
        if (this.initializer.DeckEditorLoadFailed)
        {
            SetLocalStatus("덱 편집 UI 또는 카드 아트 선로드에 실패했습니다.");
            return;
        }
        if (!this.initializer.CanOpenDeckEditor)
        {
            SetLocalStatus("덱 편집 UI를 불러오는 중입니다.");
            return;
        }

        bool t_isNew = !DeckSaveManager.IsSlotValid(t_slot);
        if (t_isNew && DeckSaveManager.IsFull)
        {
            SetLocalStatus("덱 슬롯이 가득 찼습니다. 기존 슬롯을 선택하세요.");
            return;
        }

        int t_deckCountBefore = DeckSaveManager.DeckCount;
        DeckEditController t_editor = DeckEditController.OpenPooled(new DeckEditData
        {
            slotIndex = t_slot,
            isNew = t_isNew,
            showTitle = true,
            showDeckPower = false,
            showDeckStrip = false,
            onExit = () => HandleDeckEditClosed(t_slot, t_isNew, t_deckCountBefore),
        });
        if (t_editor == null)
        {
            SetLocalStatus("덱 편집 UI를 열 수 없습니다.");
            return;
        }

        // 디버그 캔버스(order 1000)가 편집 화면(order 300)을 덮지 않도록 편집 중에는 숨긴다.
        this.m_editing = true;
        SetPanelVisible(false);
    }

    void SetPanelVisible(bool _visible)
    {
        if (this.m_canvas != null) this.m_canvas.enabled = _visible;
        else gameObject.SetActive(_visible);   // 캔버스를 못 찾은 경우의 최후 수단
    }

    // 편집기가 어떤 경로로 닫히든 패널을 되돌린다. onExit만 믿으면 저장 실패·외부 HidePooled 같은
    // 경로에서 디버그 화면이 숨은 채로 남는다.
    void RestoreAfterEdit()
    {
        this.m_editing = false;
        SetPanelVisible(true);
    }

    public void StartRankedMatchmaking() => this.initializer?.StartRankedMatchmaking();
    public void ConnectDirectRoom() => this.initializer?.Connect();
    public void Disconnect() => this.initializer?.Disconnect();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void NewTestAccount() => this.initializer?.SwitchToNewTestAccount();
#endif

    void HandleStatusChanged(string _status)
    {
        if (this.statusText != null) this.statusText.text = $"상태: {_status}";
        Refresh();
    }

    void SetLocalStatus(string _status)
    {
        if (this.statusText != null) this.statusText.text = $"상태: {_status}";
        Debug.Log($"[MpTestUI] {_status}");
    }

    void Update()
    {
        if (this.m_editing)
        {
            DeckEditController t_editor = DeckEditController.Pooled();
            if (t_editor == null || !t_editor.isShow) RestoreAfterEdit();
            return;
        }

        if (this.editDeckButton != null && this.initializer != null)
            this.editDeckButton.interactable = !this.initializer.IsBusy && this.initializer.CanOpenDeckEditor;
    }

    void HandleDeckEditClosed(int _requestedSlot, bool _wasNew, int _deckCountBefore)
    {
        // 최종 좌표는 편집기만 안다(신규는 TryInsertFront가 정하고, 하단 바로 덱을 갈아탈 수도 있다).
        // 로비 호스트(DeckTabController.HideEditor)와 같이 **내리기 전에** 회수한다.
        DeckEditController t_editor = DeckEditController.Pooled();
        int t_finalSlot = t_editor != null ? t_editor.CurrentSlot : -1;

        DeckEditController.HidePooled();
        RestoreAfterEdit();

        int t_slot = t_finalSlot >= 0
            ? t_finalSlot
            : (_wasNew ? (DeckSaveManager.DeckCount > _deckCountBefore ? 0 : -1) : _requestedSlot);
        if (t_slot >= 0 && this.initializer.TryApplySavedDeck(t_slot, out string t_message))
        {
            // 신규 덱은 DeckSaveManager.TryInsertFront가 맨 앞(슬롯 0)에 꽂고 나머지를 한 칸씩 민다 —
            // 입력한 슬롯 번호와 결과 좌표가 다르므로 그 사실을 알린다.
            if (_wasNew && _requestedSlot != t_slot) t_message += $" (신규 덱은 슬롯 {t_slot}에 꽂힌다)";
            SetLocalStatus(t_message);
            return;
        }

        SetLocalStatus(_wasNew
            ? "완성된 새 덱이 저장되지 않았습니다."
            : $"슬롯 {_requestedSlot}에 완성된 덱이 없어 기존 출전 덱을 유지합니다.");
    }

    void Refresh()
    {
        if (this.initializer == null) return;
        if (this.summaryText != null)
        {
            string t_deck = this.initializer.ResolvedDeck == null
                ? "-"
                : string.Join(", ", this.initializer.ResolvedDeck);
            this.summaryText.text = $"티어 {RankManager.TierIndex} / 포인트 {RankManager.Points}\n" +
                                    $"출전 덱: {t_deck}\n슬롯 번호 입력 후 [덱 편집 열기]";
        }
        if (this.statusText != null) this.statusText.text = $"상태: {this.initializer.Status}";

        bool t_interactable = !this.initializer.IsBusy;
        if (this.busyLockedButtons == null) return;
        foreach (Button t_button in this.busyLockedButtons)
            if (t_button != null) t_button.interactable = t_interactable;
        if (this.editDeckButton != null)
            this.editDeckButton.interactable = t_interactable && this.initializer.CanOpenDeckEditor;
    }
}
