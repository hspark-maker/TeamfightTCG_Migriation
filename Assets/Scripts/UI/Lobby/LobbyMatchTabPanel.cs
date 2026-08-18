using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns the match tab's play-button wiring.</summary>
public sealed class LobbyMatchTabPanel : LobbyTabPanel
{
    [SerializeField] Button playButton;

    [Header("오버레이 진입")]
    [Tooltip("랭크 보상 목록을 여는 버튼. 예전에는 UnityEvent가 OverlayHost 안의 패널을 직접 가리켰지만,\n"
           + "패널이 풀에서 세워지면 저작 시점에 대상이 없어 배선할 수 없다 → 여기서 코드로 연다.")]
    [SerializeField] Button rankRewardButton;

    [SerializeField] Button keywordGrowthButton;

    public event Action PlayRequested;

    void Awake()
    {
        if (playButton != null) playButton.onClick.AddListener(HandlePlayRequested);
        if (rankRewardButton != null) rankRewardButton.onClick.AddListener(OpenRankRewards);
        if (keywordGrowthButton != null) keywordGrowthButton.onClick.AddListener(OpenKeywordGrowth);
    }

    void OnDestroy()
    {
        if (playButton != null) playButton.onClick.RemoveListener(HandlePlayRequested);
        if (rankRewardButton != null) rankRewardButton.onClick.RemoveListener(OpenRankRewards);
        if (keywordGrowthButton != null) keywordGrowthButton.onClick.RemoveListener(OpenKeywordGrowth);
    }

    /// <summary>랭크 보상 목록. 풀이 없으면(부트 미초기화) 조용히 지나가지 않고 드러낸다.</summary>
    public void OpenRankRewards() => OpenPooled<RankRewardPanel>();

    public void OpenKeywordGrowth() => OpenPooled<KeywordGrowthPanel>();

    static void OpenPooled<T>() where T : PooledUIBase
    {
        if (UIPoolManager.Instance == null)
        {
            Debug.LogError($"[LobbyMatchTabPanel] UIPoolManager가 없어 {typeof(T).Name}을 열 수 없다 — Boot 초기화를 확인할 것.");
            return;
        }

        UIPoolManager.Instance.AddOrUpdateUI<T>();
    }

    public void SetPlayInteractable(bool _interactable)
    {
        if (playButton != null) playButton.interactable = _interactable;
    }

    void HandlePlayRequested() => PlayRequested?.Invoke();
}
