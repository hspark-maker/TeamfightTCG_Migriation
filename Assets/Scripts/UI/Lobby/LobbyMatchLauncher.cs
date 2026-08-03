using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 로비 PlayBtn → 출전 덱 확정 → AI 대전 진입.
/// 전투가 소비하는 DeckConfig.PlayerDeck을 채우는 지점은 이 진입점이 여는 덱 화면(MatchDeckShell) 하나뿐이다.
/// 배틀 씬은 확정된 값을 읽기만 한다 — 확정 지점이 씬을 넘어 둘로 갈리지 않게.
public class LobbyMatchLauncher : MonoBehaviour
{
    [SerializeField] MatchDeckShell shell;          // 미배선이면 게이트 없이 첫 유효 덱으로 진입(구 동작)
    [SerializeField] AIDeckConfig   aiDeckConfig;   // BattleScene GameInitializer가 참조하는 것과 동일 에셋

    [Header("유효 덱 없음 안내")]
    [SerializeField] LobbyTabController lobbyTabController;
    [SerializeField] int deckTabIndex = 3;   // LobbyTabController.tabs: 0 Shop · 1 Pack · 2 Match · 3 Deck · 4 Collection

    const string BATTLE_SCENE = "BattleScene";

    // 게이트가 열려 있는 동안 PlayBtn 재클릭을 막는다 — 두 번째 진입이 셸의 선택 상태를 덮고,
    // Confirm 한 번에 두 await가 동시에 깨어 LoadScene이 두 번 돈다.
    bool m_running;

    public void StartAiBattle()
    {
        if (m_running) return;

        DeckConfig.SetMultiplayer(false);

        // 덱 화면을 거치지 않는 튜토리얼 챕터. 저장된 덱이 아직 없으므로 유효 덱 검사보다 반드시 앞이다.
        if (TutorialConfig.IsActive && !TutorialConfig.ShowDeckGate)
        {
            SceneManager.LoadScene(BATTLE_SCENE);
            return;
        }

        // 셸이 세이브 슬롯 좌표로 동작하므로 판정도 세이브 기준이다(DeckConfig는 아직 비어 있어도 된다).
        if (!DeckSaveManager.HasAnyValidSlot())
        {
            ShowNoDeckPopup();
            return;
        }

        ConfirmEnemyDeck();

        if (shell == null)
        {
            Debug.LogWarning("[LobbyMatchLauncher] 덱 화면 미배선 — 첫 유효 덱으로 전투에 진입한다.");
            if (TryApplyFirstValidDeck()) SceneManager.LoadScene(BATTLE_SCENE);
            return;
        }

        RunGateAsync().Forget();
    }

    // 덱 화면이 "전투 시작"으로 닫히면 그때 씬을 로드한다. 포기면 셸이 스스로 닫고 로비가 그대로 남는다.
    async UniTaskVoid RunGateAsync()
    {
        var t_ct = this.GetCancellationTokenOnDestroy();

        bool t_confirmed;
        m_running = true;
        try
        {
            t_confirmed = await shell.RunSelectionAsync(t_ct);
        }
        finally
        {
            m_running = false;
        }

        // 씬이 내려가며 취소된 경우 — 파괴 중인 오브젝트를 건드리지 않는다.
        if (t_ct.IsCancellationRequested) return;

        if (t_confirmed) SceneManager.LoadScene(BATTLE_SCENE);
    }

    // 상대 덱을 전투 전에 확정한다 — 덱 화면의 EnemySection과 실제 전투가 같은 값을 보게 하는 유일한 지점.
    // 튜토리얼은 전투가 TutorialConfig.EnemyDeck으로 초기화되므로(GameInitializer) 여기서 랜덤을 뽑으면
    // 화면에 그린 6장이 실제 상대와 달라진다 — "상대 덱을 미리 확인한다"는 안내가 거짓이 된다.
    void ConfirmEnemyDeck()
    {
        if (TutorialConfig.IsActive && TutorialConfig.EnemyDeck != null)
        {
            DeckConfig.SetEnemyDeck(TutorialConfig.EnemyDeck);
            return;
        }

        DeckConfig.SetEnemyDeck(aiDeckConfig != null ? aiDeckConfig.GetRandomDeck() : new List<CardData>());
    }

    void ShowNoDeckPopup()
    {
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "유효한 덱이 없습니다.\n덱을 먼저 구성해 주세요.",
            yesText   = "덱 편성",
            noText    = "닫기",
            yesAction = GoToDeckTab,
        });
    }

    void GoToDeckTab()
    {
        lobbyTabController?.Select(deckTabIndex);
    }

    // 셸 미배선 폴백 전용. 저장된 슬롯 중 첫 유효 덱을 DeckConfig에 적용하고, 없으면 false.
    static bool TryApplyFirstValidDeck()
    {
        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
        {
            if (!DeckSaveManager.IsSlotValid(t_i)) continue;

            DeckConfig.Set(DeckSaveManager.Load(t_i));
            return true;
        }
        return false;
    }
}
