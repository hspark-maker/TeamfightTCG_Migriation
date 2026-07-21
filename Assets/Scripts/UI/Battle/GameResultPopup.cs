using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class GameResultPopup : MonoBehaviour
{
    [SerializeField] RectTransform panel;
    [SerializeField] Button mainMenuButton;
    [SerializeField] string mainMenuScene = "MainMenu";
    [SerializeField] float enterDuration = 0.45f;

    void Awake()
    {
        this.panel.localScale = Vector3.zero;
        this.mainMenuButton?.onClick.AddListener(GoMainMenu);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        this.panel.DOKill();
        this.panel.localScale = Vector3.zero;
        this.panel.DOScale(1f, this.enterDuration).SetEase(Ease.OutBack);
    }

    void GoMainMenu()
    {
        BattleCleanup.LoadScene(this.mainMenuScene);
    }
}
