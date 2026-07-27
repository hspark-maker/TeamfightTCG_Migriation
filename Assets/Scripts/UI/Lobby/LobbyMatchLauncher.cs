using UnityEngine;
using UnityEngine.SceneManagement;

/// 로비 버튼 → AI 대전 진입.
/// 패널 on/off(MainMenuManager) 방식을 쓰지 않고, MainMenuManager.GameStart()와
/// 동일한 전환만 재현한다: 멀티 플래그 해제 → 전환 영상 오버레이 → BattleScene 로드.
/// 로비엔 덱 선택 UI가 없으므로, DeckConfig가 비어 있으면 첫 유효 슬롯을 자동 적용한다.
public class LobbyMatchLauncher : MonoBehaviour
{
    public void StartAiBattle()
    {
        if (!DeckConfig.HasDeck && !TryApplyFirstValidDeck())
        {
            UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
            {
                titleText = "유효한 덱이 없습니다.\n덱을 먼저 구성해 주세요.",
                yesText   = "확인",
                noText    = "닫기",
            });
            return;
        }

        // 이 진입은 상대 덱을 사전 확정하지 않는 즉시 AI 대전 → 이전 매칭이 남긴 상대 덱 홀더를
        // 비워 GameInitializer가 랜덤 폴백을 쓰게 한다(홀더 오염 방지).
        DeckConfig.ClearEnemyDeck();
        DeckConfig.SetMultiplayer(false);
        //SceneTransitionVideo.Instance?.PlayOverlay();
        SceneManager.LoadScene("BattleScene");
    }

    // 저장된 슬롯 중 첫 유효 덱을 DeckConfig에 적용. 없으면 false.
    static bool TryApplyFirstValidDeck()
    {
        for (int i = 0; i < DeckSaveManager.SLOT_COUNT; i++)
        {
            if (!DeckSaveManager.IsSlotValid(i)) continue;
            DeckConfig.Set(DeckSaveManager.Load(i));
            return true;
        }
        return false;
    }
}
