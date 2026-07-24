using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비 Match 탭의 AI 대전 매칭 플로우 컨트롤러.
/// 씬 전환 없이 패널 오버레이(SetActive 토글)로 ②매칭중 → ③매칭완료 → ④패 확인/덱 선택을
/// 순차 진행한 뒤, "전투 시작"에서 DeckConfig를 확정하고 BattleScene을 로드한다.
/// 네트워크는 쓰지 않는 순수 타이머 연출이며, 상대는 AI로 고정된다.
/// (기존 LobbyMatchLauncher의 즉시 진입 역할을 흡수한다 — 매칭 연출 + 상대 패 사전 공개가 추가된 형태.)
/// </summary>
public class MatchFlowController : MonoBehaviour
{
    [Header("AI Deck")]
    // 상대(AI) 덱 풀. BattleScene의 GameInitializer가 참조하는 것과 동일한 AIDeckConfig.asset을 배선한다.
    [SerializeField] AIDeckConfig aiDeckConfig;

    [Header("Panels (② ③ ④)")]
    [SerializeField] GameObject matchingPanel;   // ② 매칭 중
    [SerializeField] GameObject foundPanel;       // ③ 매칭 완료("상대를 찾았습니다")
    [SerializeField] GameObject deckSelectPanel;  // ④ 상대 패 확인 + 출전 덱 선택

    [Header("② Matching")]
    [SerializeField] TMP_Text matchingStatusText;

    [Header("④ Enemy Hand Reveal")]
    // 확정된 AI 6장을 스폰할 컨테이너와 CardElement 프리팹(기존 CardElement 재사용).
    [SerializeField] Transform enemyCardContainer;
    [SerializeField] GameObject cardElementPrefab;
    [SerializeField] CardElementMod enemyCardMod = CardElementMod.Full;

    [Header("④ My Deck Select")]
    // 내 저장 덱 6슬롯 버튼 + 선택된 덱을 미리보기로 표시할 DeckGroup(GameReadyPanel 패턴 재사용).
    [SerializeField] Button[] deckButtons;
    [SerializeField] Image[] deckPreviewImages;
    [SerializeField] Sprite emptySlotSprite;
    [SerializeField] DeckGroup myDeckGroup;
    [SerializeField] Button startBattleButton;

    [Header("Navigation (선택)")]
    // "유효 덱 없음" 팝업에서 덱 편성 탭으로 이동시키기 위한 참조(옵션). 미배선이면 팝업만 뜨고 이동은 생략.
    [SerializeField] LobbyTabController lobbyTabController;
    [SerializeField] int deckTabIndex = 1;

    [Header("Timing (초)")]
    [SerializeField] float matchingDuration = 1.8f;   // ② 페이크 매칭 대기(1.5~2초)
    [SerializeField] float foundDuration    = 0.9f;   // ③ "상대를 찾았습니다" 노출 시간

    const string BATTLE_SCENE = "BattleScene";

    // 스폰한 상대 카드 타일(재진입 시 정리용).
    readonly List<GameObject> spawnedEnemyCards = new List<GameObject>();
    CancellationTokenSource matchCts;
    int selectedSlotIndex = -1;

    void Awake()
    {
        // 덱 슬롯 버튼 배선(GameReadyPanel 패턴). 클로저 캡처 방지를 위해 로컬 인덱스 사용.
        if (this.deckButtons != null)
        {
            for (int t_i = 0; t_i < this.deckButtons.Length; t_i++)
            {
                if (this.deckButtons[t_i] == null) continue;
                int t_slotIndex = t_i;
                this.deckButtons[t_i].onClick.AddListener(() => OnDeckSlotSelected(t_slotIndex));
            }
        }

        if (this.startBattleButton != null)
            this.startBattleButton.onClick.AddListener(OnStartBattlePressed);

        HideAllPanels();
    }

    void OnDisable()
    {
        // 패널이 꺼지면 진행 중이던 타이머를 즉시 취소해 데드락/유령 진행을 막는다.
        CancelMatch();
    }

    // ── 진입 ─────────────────────────────────────────────────────────────────

    /// <summary>Match 탭 플레이 버튼 onClick 진입점. 매칭 연출을 시작한다.</summary>
    public void OnPlayPressed()
    {
        // 저장된 유효 덱이 하나도 없으면 매칭에 들어가지 않고 안내(전투 시작 전 되돌릴 곳이 없어짐 방지).
        if (!DeckSaveManager.HasAnyValidSlot())
        {
            ShowNoDeckPopup();
            return;
        }

        CancelMatch();
        this.matchCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        RunMatchFlowAsync(this.matchCts.Token).Forget();
    }

    /// <summary>②③④ 어느 단계에서든 로비 Match 탭으로 복귀(모든 오버레이 닫고 타이머 취소).</summary>
    public void OnCancelPressed()
    {
        CancelMatch();
        HideAllPanels();
    }

    // ── 플로우 ───────────────────────────────────────────────────────────────

    async UniTask RunMatchFlowAsync(CancellationToken _token)
    {
        try
        {
            // ② 매칭 중 — 이 순간 AI 덱 1개를 확정해 홀더에 저장한다.
            ShowOnly(this.matchingPanel);
            SetMatchingStatus("상대를 찾는 중...");
            ConfirmEnemyDeck();

            await UniTask.Delay(SecToMs(this.matchingDuration), cancellationToken: _token);

            // ③ 매칭 완료
            ShowOnly(this.foundPanel);
            await UniTask.Delay(SecToMs(this.foundDuration), cancellationToken: _token);

            // ④ 상대 패 공개 + 내 출전 덱 선택
            ShowOnly(this.deckSelectPanel);
            RevealEnemyHand();
            SetupMyDeckSelection();
        }
        catch (System.OperationCanceledException)
        {
            // 취소(뒤로/패널 비활성)는 정상 흐름 — 조용히 종료.
        }
    }

    // ② 상대 덱 확정 — GameInitializer가 폴백 대신 이 값을 쓰게 된다.
    void ConfirmEnemyDeck()
    {
        List<CardData> t_deck = this.aiDeckConfig != null
            ? this.aiDeckConfig.GetRandomDeck()
            : new List<CardData>();
        DeckConfig.SetEnemyDeck(t_deck);
    }

    // ④ 확정된 AI 덱 전 카드를 컨테이너에 공개 표시.
    void RevealEnemyHand()
    {
        ClearEnemyCards();

        if (this.enemyCardContainer == null || this.cardElementPrefab == null) return;

        List<CardData> t_deck = DeckConfig.EnemyDeck;
        if (t_deck == null) return;

        for (int t_i = 0; t_i < t_deck.Count; t_i++)
        {
            CardData t_card = t_deck[t_i];
            if (t_card == null) continue;

            GameObject t_obj = Instantiate(this.cardElementPrefab, this.enemyCardContainer);
            CardElement t_element = t_obj.GetComponent<CardElement>();
            if (t_element != null) t_element.Init(t_card, this.enemyCardMod);
            this.spawnedEnemyCards.Add(t_obj);
        }
    }

    // ④ 내 저장 덱 6슬롯 버튼 상태 갱신 + 첫 유효 슬롯 자동 선택(GameReadyPanel.OnEnable 패턴).
    void SetupMyDeckSelection()
    {
        RefreshDeckButtons();

        this.selectedSlotIndex = -1;
        if (this.deckButtons != null)
        {
            for (int t_i = 0; t_i < this.deckButtons.Length; t_i++)
            {
                if (!DeckSaveManager.IsSlotValid(t_i)) continue;
                OnDeckSlotSelected(t_i);
                break;
            }
        }

        RefreshStartButton();
    }

    void RefreshDeckButtons()
    {
        if (this.deckButtons == null) return;

        for (int t_i = 0; t_i < this.deckButtons.Length; t_i++)
        {
            if (this.deckButtons[t_i] == null) continue;
            bool t_valid = DeckSaveManager.IsSlotValid(t_i);
            this.deckButtons[t_i].interactable = t_valid;

            if (this.deckPreviewImages == null || t_i >= this.deckPreviewImages.Length || this.deckPreviewImages[t_i] == null) continue;
            List<CardData> t_slot = DeckSaveManager.GetSlot(t_i);
            this.deckPreviewImages[t_i].sprite = t_valid && t_slot != null && t_slot.Count > 0 && t_slot[0] != null
                ? t_slot[0].deckPreview
                : this.emptySlotSprite;
        }
    }

    void OnDeckSlotSelected(int _slotIndex)
    {
        if (!DeckSaveManager.IsSlotValid(_slotIndex)) return;

        this.selectedSlotIndex = _slotIndex;
        DeckConfig.Set(DeckSaveManager.GetSlot(_slotIndex));
        this.myDeckGroup?.LoadSlot(_slotIndex);   // 시너지 아이콘은 DeckGroup이 SetDeck에서 함께 갱신
        RefreshStartButton();
    }

    void RefreshStartButton()
    {
        if (this.startBattleButton != null)
            this.startBattleButton.interactable = DeckConfig.HasDeck;
    }

    // ── ⑤ 전투 시작 ───────────────────────────────────────────────────────────

    /// <summary>"전투 시작" 버튼. 확정된 덱으로 싱글 전투 씬을 로드한다.</summary>
    public void OnStartBattlePressed()
    {
        // 방어: 선택 덱이 유효하지 않으면 진입하지 않는다(빈 상태 로드 방지).
        if (!DeckConfig.HasDeck)
        {
            ShowNoDeckPopup();
            return;
        }

        CancelMatch();
        DeckConfig.SetMultiplayer(false);
        SceneTransitionVideo.Instance?.PlayOverlay();
        SceneManager.LoadScene(BATTLE_SCENE);
    }

    // ── 유틸 ─────────────────────────────────────────────────────────────────

    void ShowNoDeckPopup()
    {
        UIPoolManager.instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "유효한 덱이 없습니다.\n덱을 먼저 구성해 주세요.",
            yesText   = "덱 편성",
            noText    = "닫기",
            yesAction = GoToDeckTab,
        });
    }

    void GoToDeckTab()
    {
        HideAllPanels();
        this.lobbyTabController?.Select(this.deckTabIndex);
    }

    void ShowOnly(GameObject _panel)
    {
        if (this.matchingPanel   != null) this.matchingPanel.SetActive(_panel == this.matchingPanel);
        if (this.foundPanel      != null) this.foundPanel.SetActive(_panel == this.foundPanel);
        if (this.deckSelectPanel != null) this.deckSelectPanel.SetActive(_panel == this.deckSelectPanel);
    }

    void HideAllPanels()
    {
        if (this.matchingPanel   != null) this.matchingPanel.SetActive(false);
        if (this.foundPanel      != null) this.foundPanel.SetActive(false);
        if (this.deckSelectPanel != null) this.deckSelectPanel.SetActive(false);
        ClearEnemyCards();
    }

    void SetMatchingStatus(string _msg)
    {
        if (this.matchingStatusText != null) this.matchingStatusText.text = _msg;
    }

    void ClearEnemyCards()
    {
        for (int t_i = 0; t_i < this.spawnedEnemyCards.Count; t_i++)
        {
            if (this.spawnedEnemyCards[t_i] != null)
                Destroy(this.spawnedEnemyCards[t_i]);
        }
        this.spawnedEnemyCards.Clear();
    }

    void CancelMatch()
    {
        if (this.matchCts == null) return;
        this.matchCts.Cancel();
        this.matchCts.Dispose();
        this.matchCts = null;
    }

    static int SecToMs(float _sec) => Mathf.Max(0, Mathf.RoundToInt(_sec * 1000f));
}
