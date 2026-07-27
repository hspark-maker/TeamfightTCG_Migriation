using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] GameObject mainPanelUI;
    [SerializeField] GameObject deckPanelUI;
    [SerializeField] GameObject gameReadyPanel;
    [SerializeField] GameObject multiplayerLobbyPanel;
    [SerializeField] GameObject randomMatchPanel;

    // 튜토리얼 전투 시나리오 SO. 덱/스크립트는 코드 하드코딩 금지 — 인스펙터에서 배선한다.
    // 예: Assets/SO/TutorialScenario.asset
    [SerializeField] TutorialScenarioData tutorialScenario;

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
            UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
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
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "유효한 덱이 없습니다.\n덱 구성 화면으로 이동하시겠습니까?",
            yesText   = "이동",
            noText    = "취소",
            yesAction = OnDeckPressed,
        });
    }

    // 메인 → 튜토리얼 전투 진입. 버튼 onClick에 배선.
    // TutorialConfig.Begin이 IsActive/고정덱/스크립트큐를 세팅하면
    // BattleScene의 GameInitializer가 나머지(오버레이·고정덱 초기화)를 자동 처리한다.
    public void OnTutorialPressed()
    {
        if (this.tutorialScenario == null)
        {
            // 시나리오 미배선이면 씬 로드 금지(빈 튜토리얼로 진입해 크래시/무한대기 방지).
            Debug.LogWarning("[MainMenuManager] tutorialScenario SO가 배선되지 않았습니다. 튜토리얼 진입 취소.");
            return;
        }

        // TODO(outgame-save): 튜토리얼 "완료" 영속 플래그가 세이브 레이어에 없다.
        // 첫 실행 자동 진입/1회성 재진입 방지를 하려면 UserSaveData에 진행 도메인(예: progress.tutorialDone)
        // 추가가 선행돼야 함(세이브 스키마 변경 = 공유 계약, 별도 스코프). 현재는 수동 진입 버튼만 제공.

        TutorialConfig.Begin(this.tutorialScenario);
        SceneTransitionVideo.Instance?.PlayOverlay();
        SceneManager.LoadScene("BattleScene");
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
        UIPoolManager.Instance?.AddOrUpdateUI<SettingsPanel>();
    }
}
