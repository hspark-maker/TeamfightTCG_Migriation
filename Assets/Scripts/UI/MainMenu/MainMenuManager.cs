using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] GameObject mainPanelUI;
    [SerializeField] GameObject deckPanelUI;
    [SerializeField] GameObject gameReadyPanel;
    [SerializeField] GameObject multiplayerLobbyPanel;
    [SerializeField] GameObject randomMatchPanel;

    void Start()
    {
        this.mainPanelUI.SetActive(true);
        this.deckPanelUI.SetActive(false);
        this.gameReadyPanel.SetActive(false);
        if (this.multiplayerLobbyPanel != null)
            this.multiplayerLobbyPanel.SetActive(false);
        if (this.randomMatchPanel != null)
            this.randomMatchPanel.SetActive(false);
    }

    // 메인 → 게임 준비 (덱 선택)
    public void OnStartPressed()
    {
        if (!DeckSaveManager.HasAnyValidSlot())
        {
            UIPoolManager.instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
            {
                titleText = "유효한 덱이 없습니다.\n덱 구성 화면으로 이동하시겠습니까?",
                yesText   = "이동",
                noText    = "취소",
                yesAction = OnDeckPressed,
            });
            return;
        }
        this.mainPanelUI.SetActive(false);
        this.gameReadyPanel.SetActive(true);
    }

    // GameReadyPanel → AI 대전
    public void GameStart()
    {
        if (!DeckConfig.HasDeck) { ShowInvalidDeckPopup(); return; }
        // 레거시 MainMenu 경로는 상대 덱을 사전 확정하지 않으므로, 로비 매칭이 남긴 홀더를
        // 비워 GameInitializer가 랜덤 폴백을 쓰게 한다(홀더 오염 방지).
        DeckConfig.ClearEnemyDeck();
        DeckConfig.SetMultiplayer(false);
        SceneTransitionVideo.Instance?.PlayOverlay();
        SceneManager.LoadScene("BattleScene");
    }

    // GameReadyPanel → 멀티플레이 대전 → 코드 매칭 로비
    public void OnMultiplayerStartPressed()
    {
        if (!DeckConfig.HasDeck) { ShowInvalidDeckPopup(); return; }
        if (this.multiplayerLobbyPanel == null) return;
        this.gameReadyPanel.SetActive(false);
        this.multiplayerLobbyPanel.SetActive(true);
    }

    // GameReadyPanel → 멀티플레이 대전 → 랜덤 매칭
    public void OnRandomMatchPressed()
    {
        if (!DeckConfig.HasDeck) { ShowInvalidDeckPopup(); return; }
        if (this.randomMatchPanel == null) return;
        this.gameReadyPanel.SetActive(false);
        this.randomMatchPanel.SetActive(true);
    }

    void ShowInvalidDeckPopup()
    {
        UIPoolManager.instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "유효한 덱이 없습니다.\n덱 구성 화면으로 이동하시겠습니까?",
            yesText   = "이동",
            noText    = "취소",
            yesAction = OnDeckPressed,
        });
    }

    public void OnDeckPressed()
    {
        this.mainPanelUI.SetActive(false);
        this.deckPanelUI.SetActive(true);
    }

    public void OnBackPressed()
    {
        this.gameReadyPanel.SetActive(false);
        this.deckPanelUI.SetActive(false);
        if (this.multiplayerLobbyPanel != null)
            this.multiplayerLobbyPanel.SetActive(false);
        if (this.randomMatchPanel != null)
            this.randomMatchPanel.SetActive(false);
        this.mainPanelUI.SetActive(true);
    }

    public void OnSettingButton()
    {
        UIPoolManager.instance.AddOrUpdateUI<SettingsPanel>();
    }
}
