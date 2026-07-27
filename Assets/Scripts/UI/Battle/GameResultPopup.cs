using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameResultPopup : MonoBehaviour
{
    [SerializeField] RectTransform panel;
    [SerializeField] Button mainMenuButton;
    [SerializeField] string mainMenuScene = "LobbyScene";
    [SerializeField] float enterDuration = 0.45f;
    [SerializeField] float rewardRevealDuration = 0.3f; // 패널 등장 뒤 보상 라인이 팝하는 시간.
    [SerializeField] TMP_Text rewardGoldText; // 지급된 골드 표시용(표시 전용, 재계산·재지급 없음).

    Sequence revealSeq; // 진행 중 등장 연출. 재진입 시 통째로 Kill해 좀비 시퀀스 누적 방지.

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

        // 이전 등장 연출을 통째로 정리(중첩 트윈까지) 후 초기 상태로 리셋.
        this.revealSeq?.Kill();
        this.panel.localScale = Vector3.zero;

        if (this.rewardGoldText != null)
        {
            // 라벨('골드')·코인 아이콘은 프리팹의 정적 요소, 여기선 획득 수치만 채운다.
            this.rewardGoldText.text = $"+{_rewardGold:N0}";
            this.rewardGoldText.transform.localScale = Vector3.zero;
        }

        // 패널이 먼저 등장한 뒤 보상 수치가 팝하도록 순차 연출.
        this.revealSeq = DOTween.Sequence();
        this.revealSeq.Append(this.panel.DOScale(1f, this.enterDuration).SetEase(Ease.OutBack));
        if (this.rewardGoldText != null)
            this.revealSeq.Append(this.rewardGoldText.transform.DOScale(1f, this.rewardRevealDuration).SetEase(Ease.OutBack));
    }

    void GoMainMenu()
    {
        BattleCleanup.LoadScene(this.mainMenuScene);
    }
}
