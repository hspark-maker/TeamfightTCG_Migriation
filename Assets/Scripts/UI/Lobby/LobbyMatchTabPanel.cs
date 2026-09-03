using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns the match tab's play-button wiring.</summary>
public sealed class LobbyMatchTabPanel : LobbyTabPanel
{
    [SerializeField] Button playButton;

    [Header("승급전 문구")]
    [Tooltip("전투 진입 버튼의 문구 노드. 승급전 대기 상태에서 문구가 갈린다 — 지금 누를 판이 무엇인지 버튼 자신이 말하게.\n" +
             "비면 문구 축을 건너뛴다(버튼은 저작 문구 그대로 남는다).")]
    [SerializeField] TMP_Text playLabel;

    [Tooltip("승급전 대기일 때 쓸 문구. 평상시 문구는 여기가 아니라 프리팹 저작값이 정본이다 — " +
             "시작할 때 한 번 캡처해 두고 상태가 풀리면 그리로 돌아간다.")]
    [SerializeField] string promoLabelText = "승급전";

    [Header("오버레이 진입")]
    [Tooltip("랭크 보상 목록을 여는 버튼. 예전에는 UnityEvent가 OverlayHost 안의 패널을 직접 가리켰지만,\n"
           + "패널이 풀에서 세워지면 저작 시점에 대상이 없어 배선할 수 없다 → 여기서 코드로 연다.")]
    [SerializeField] Button rankRewardButton;

    [SerializeField] Button keywordGrowthButton;

    [Header("모험")]
    [Tooltip("모험 맵으로 가는 버튼. 이동 자체는 LobbyRoot가 한다 — 탭 패널은 탭 이동을 모른다.")]
    [SerializeField] Button adventureButton;

    public event Action PlayRequested;

    public event Action AdventureRequested;

    // 버튼의 저작 문구. 승급전 상태가 풀리면 여기로 돌아간다 — 평상시 문구를 코드가 다시 쓰지 않게.
    string m_defaultPlayText;

    void Awake()
    {
        if (playButton != null) playButton.onClick.AddListener(HandlePlayRequested);
        if (rankRewardButton != null) rankRewardButton.onClick.AddListener(OpenRankRewards);
        if (keywordGrowthButton != null) keywordGrowthButton.onClick.AddListener(OpenKeywordGrowth);
        if (adventureButton != null) adventureButton.onClick.AddListener(HandleAdventureRequested);

        if (playLabel != null) m_defaultPlayText = playLabel.text;

        // 잠김 룩은 코드로 얹는다 — 기능키↔버튼 짝이 아래 계산식 바로 옆에 있어야 둘이 갈리지 않는다.
        // PlayBtn만 프리팹 저작인 것은 그쪽 잠금 주체가 LobbyMatchLauncher라 중립 지점이 필요했기 때문이다.
        if (keywordGrowthButton != null) FeatureLockView.Attach(keywordGrowthButton.gameObject, EOutgameFeature.KeywordGrowth);
        if (adventureButton != null) FeatureLockView.Attach(adventureButton.gameObject, EOutgameFeature.Adventure);

        // 탭이 꺼져 있는 동안에도 신호를 받아야 한다 — 놓치면 다른 탭에 있던 사이 끝난 연출을 영영 못 따라간다.
        OutgameFeatureLock.OnChanged += ApplyFeatureLocks;
        LobbyRankEffectDirector.OnAnyFinished += RefreshPlayLabel;
    }

    // 첫 반영은 Awake가 아니라 여기다 — 디렉터의 Awake보다 먼저 물으면 "연출 없음"으로 읽혀 결말을 미리 말한다.
    void Start()
    {
        RefreshPlayLabel();
        ApplyFeatureLocks();
    }

    void OnDestroy()
    {
        if (playButton != null) playButton.onClick.RemoveListener(HandlePlayRequested);
        if (rankRewardButton != null) rankRewardButton.onClick.RemoveListener(OpenRankRewards);
        if (keywordGrowthButton != null) keywordGrowthButton.onClick.RemoveListener(OpenKeywordGrowth);
        if (adventureButton != null) adventureButton.onClick.RemoveListener(HandleAdventureRequested);

        OutgameFeatureLock.OnChanged -= ApplyFeatureLocks;
        LobbyRankEffectDirector.OnAnyFinished -= RefreshPlayLabel;
    }

    public override void OnEnter()
    {
        RefreshPlayLabel();
        ApplyFeatureLocks();
    }

    /// <summary>승급전 대기면 버튼 문구를 갈고, 아니면 저작 문구로 되돌린다.
    /// 랭크 정산 연출이 도는 중에는 갈지 않는다 — 별이 차고 배지에 광선이 붙는 결말을 버튼이 먼저 말해버린다.</summary>
    void RefreshPlayLabel()
    {
        if (playLabel == null) return;

        bool t_promo = RankManager.IsPromoPending && !LobbyRankEffectDirector.Playing;

        playLabel.text = t_promo && !string.IsNullOrEmpty(promoLabelText) ? promoLabelText : m_defaultPlayText;
    }

    /// <summary>잠긴 기능의 버튼을 죽인다. 잠김 룩(FeatureLockView)과 달리 차단은 이 패널이 소유한다 —
    /// 두 축이 같은 컴포넌트에 있으면 어느 쪽이 이겼는지가 호출 순서에 달린다.</summary>
    void ApplyFeatureLocks()
    {
        if (keywordGrowthButton != null)
            keywordGrowthButton.interactable = OutgameFeatureLock.IsUnlocked(EOutgameFeature.KeywordGrowth);

        if (adventureButton != null)
            adventureButton.interactable = OutgameFeatureLock.IsUnlocked(EOutgameFeature.Adventure);
    }

    /// <summary>랭크 보상 목록. 풀이 없으면(초기화 미초기화) 조용히 지나가지 않고 드러낸다.</summary>
    public void OpenRankRewards() => OpenPooled<RankRewardPanel>();

    public void OpenKeywordGrowth()
    {
        // 버튼을 죽여 두는 것만으로는 부족하다 — 잠김 표시는 표현 레이어 몫이고, 진입을 실제로 막는 주체는 여기다.
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.KeywordGrowth)) return;

        OpenPooled<KeywordGrowthPanel>();
    }

    static void OpenPooled<T>() where T : PooledUIBase
    {
        if (UIPoolManager.Instance == null)
        {
            Debug.LogError($"[LobbyMatchTabPanel] UIPoolManager가 없어 {typeof(T).Name}을 열 수 없다 — 초기화(InitializationRunner) 초기화를 확인할 것.");
            return;
        }

        UIPoolManager.Instance.AddOrUpdateUI<T>();
    }

    public void SetPlayInteractable(bool _interactable)
    {
        if (playButton != null) playButton.interactable = _interactable;
    }

    void HandlePlayRequested() => PlayRequested?.Invoke();

    void HandleAdventureRequested() => AdventureRequested?.Invoke();
}
