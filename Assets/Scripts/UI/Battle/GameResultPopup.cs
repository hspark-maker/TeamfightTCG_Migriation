using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameResultPopup : MonoBehaviour
{
    [SerializeField] RectTransform panel;
    [SerializeField] Button mainMenuButton;
    [SerializeField] string mainMenuScene = "MainMenu";
    [SerializeField] float enterDuration = 0.45f;
    [SerializeField] TMP_Text rewardGoldText; // 지급된 골드 표시용(표시 전용, 재계산·재지급 없음).

    void Awake()
    {
        this.panel.localScale = Vector3.zero;
        this.mainMenuButton?.onClick.AddListener(GoMainMenu);
    }

    /// <summary>
    /// 결과 팝업 노출. _rewardGold는 이미 지급·영속화된 값을 그대로 표시만 한다.
    /// </summary>
    public void Show(long _rewardGold)
    {
        gameObject.SetActive(true);
        this.panel.DOKill();
        this.panel.localScale = Vector3.zero;
        this.panel.DOScale(1f, this.enterDuration).SetEase(Ease.OutBack);

        if (this.rewardGoldText != null)
            this.rewardGoldText.text = _rewardGold.ToString("N0");
    }

    void GoMainMenu()
    {
        BattleCleanup.LoadScene(this.mainMenuScene);
    }
}
