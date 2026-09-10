using System.Collections.Generic;
using UnityEngine;

/// <summary>성장 화면의 부족 재화를 실제 모험 보상과 연결하는 선택 안내. 지급·진행도를 변경하지 않는다.</summary>
public sealed class GrowthGuidanceBridge : MonoBehaviour
{
    static GrowthGuidanceBridge s_instance;
    static int s_returnCard;
    static string s_returnNode;
    static string s_user;
    OutgameTutorialGateUI m_prefab;
    LobbyMatchLauncher m_launcher;
    int m_card;
    bool m_short;
    bool m_offered;
    bool m_returned;
    bool m_rankHint;
    bool m_unlockSeen;
    string m_hintKey;
    string m_hintMessage;
    float m_nextPoll;
    readonly List<RewardLine> m_rewards = new List<RewardLine>();

    public static void Install(GameObject owner, OutgameTutorialGateUI prefab)
    {
        var bridge = owner.GetComponent<GrowthGuidanceBridge>();
        if (bridge == null) bridge = owner.AddComponent<GrowthGuidanceBridge>();
        bridge.m_prefab = prefab;
    }

    public static void ObserveCard(int card, bool eligible, bool affordable)
    {
        if (s_instance == null) return;
        if (s_instance.m_card != card) s_instance.ClearHint();
        s_instance.m_card = card;
        s_instance.m_short = eligible && !affordable;
    }

    public static void BeginCardVisit()
    {
        if (s_instance == null) return;
        s_instance.ClearHint();
        s_instance.m_offered = false;
        s_instance.m_card = 0;
    }

    void Awake()
    {
        s_instance = this;
        m_launcher = FindFirstObjectByType<LobbyMatchLauncher>();
        // 로그인 시 이미 해금된 콘텐츠는 과거 해금 안내를 소급하지 않는다.
        m_unlockSeen = AdventureTutorialRunner.IsUnlocked;
        CardDetailOverlayView.OnAnyClosed += CardClosed;
        CardDetailOverlayView.OnAnyEnhanceSettled += Enhanced;
        GuidanceCoordinator.OnMissionNoticePresentationChanged += MissionNoticeChanged;
    }

    void CardClosed() { ClearHint(); m_card = 0; m_short = false; }
    void Enhanced(EnhanceResult result)
    {
        if (m_returned && m_card == s_returnCard &&
            (result.Outcome == EEnhanceOutcome.Success || result.Outcome == EEnhanceOutcome.Failed))
            m_rankHint = true;
    }
    void MissionNoticeChanged() { if (GuidanceCoordinator.IsMissionNoticeShowing) SuspendHint(); }

    bool Blocked => m_launcher == null || !GameInitialization.IsReady || OutgameTutorialRunner.IsRunning
        || TriggeredTutorialRunner.IsRunning || AdventureTutorialRunner.IsRunning
        || GuidanceCoordinator.IsMissionNoticeShowing || CurtainView.IsBusy || LoadingCoverView.IsCovering
        || (SceneTransitionVideo.Instance != null && SceneTransitionVideo.Instance.IsPlaying)
        || (m_launcher != null && (m_launcher.IsRunning || m_launcher.IsAdventurePresentationBusy))
        || LobbyRankEffectDirector.Playing || LobbyGainEffectDirector.Playing || RankPromoteOverlay.IsOpen
        || RewardClaimPopup.IsOpen || AdventureRewardFlow.IsClaiming || PackOpenOverlay.IsOpen
        || CardDetailOverlayView.IsUnlockFxPlaying || CardDetailOverlayView.IsGrowthPresentationBusy
        || CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen || PackRewardOverlay.IsOpen
        || (UIPoolManager.instance != null && UIPoolManager.instance.HasVisibleUIExcept());

    void Update()
    {
        string user = FirebaseAuthService.Instance.UserId;
        if (s_user != user)
        {
            s_user = user; s_returnCard = 0; s_returnNode = null;
            m_returned = m_rankHint = false;
            m_card = 0; m_short = m_offered = false;
            m_unlockSeen = AdventureTutorialRunner.IsUnlocked;
            m_nextPoll = 0;
            ClearHint();
        }
        if (Blocked) { SuspendHint(); return; }
        if (Time.unscaledTime < m_nextPoll) return;
        m_nextPoll = Time.unscaledTime + .25f;
        if (m_rankHint)
        {
            Show("rank", "랭크전에 다시 도전해 보세요.", "랭크전으로 돌아가기", () =>
            {
                ResetReturn(); CardDetailOverlayView.Close(); AlbumPageOverlayView.CloseOpen();
                m_launcher?.ReturnToRankedLobby();
            }, ResetReturn);
            return;
        }
        if (!m_returned && s_returnCard > 0 && AdventureProgress.IsCleared(s_returnNode))
        {
            if (!OwnershipManager.IsOwned(s_returnCard) || !CardGrowthManager.TryGetNextStep(s_returnCard, out var step))
            { ClearHint(); ResetReturn(); return; }
            if (CurrencyManager.CanAfford(step.Currency, step.Cost))
                Show("return", "재화를 확보했어요. 카드 성장을 계속해 보세요.", "카드로 돌아가기", () =>
                {
                    if (!CardGrowthManager.TryGetNextStep(s_returnCard, out var latest) ||
                        !CurrencyManager.CanAfford(latest.Currency, latest.Cost)) return;
                    m_returned = true; m_launcher?.ReturnToRankedLobby();
                    CardDetailOverlayView.Open(s_returnCard);
                }, ResetReturn);
            else if (TryFindSource(step.Currency, out int additionalSource))
                Show("more", "다음 성장에 재화가 더 필요해요. 다른 모험 보상을 확인해 보세요.", "모험 보기", () =>
                {
                    if (!CardGrowthManager.TryGetNextStep(s_returnCard, out var latest) ||
                        !TryFindSource(latest.Currency, out int source)) return;
                    AdventureProgress.TryGetNode(source, out var definition);
                    s_returnNode = definition.nodeId;
                    m_launcher.OpenAdventureForResource(source);
                }, ResetReturn);
            else
                Show("more", "다음 성장에 재화가 더 필요해요.", null, null, ResetReturn);
            return;
        }
        if (CardDetailOverlayView.IsOpen && m_short && (!m_offered || m_hintKey == "shortage") &&
            CardGrowthManager.TryGetNextStep(m_card, out var next) &&
            !CurrencyManager.CanAfford(next.Currency, next.Cost) && TryFindSource(next.Currency, out int node))
        {
            int card = m_card;
            long missing = next.Cost - CurrencyManager.GetBalance(next.Currency);
            if (Show("shortage", $"{CurrencyLook.NameOf(next.Currency)}가 {missing:N0} 부족해요. 모험 보상을 확인해 보세요.",
                "모험 보기", () =>
                {
                    // 안내 도중 잔액·정점 상태가 바뀌면 최신 조건으로 다시 선택한다.
                    if (!CardGrowthManager.TryGetNextStep(card, out var current) ||
                        CurrencyManager.CanAfford(current.Currency, current.Cost) ||
                        !TryFindSource(current.Currency, out int source) || m_launcher == null) return;
                    AdventureProgress.TryGetNode(source, out var definition);
                    s_returnCard = card; s_returnNode = definition.nodeId; m_returned = false;
                    CardDetailOverlayView.Close(); AlbumPageOverlayView.CloseOpen();
                    m_launcher.OpenAdventureForResource(source);
                }, () => { })) m_offered = true;
            return;
        }
        if (m_hintKey == "shortage") ClearHint();
        if (!m_unlockSeen && AdventureTutorialRunner.IsUnlocked && !CardDetailOverlayView.IsOpen
            && !AlbumPageOverlayView.IsOpen)
        {
            if (Show("unlock", "모험이 열렸어요. 성장 재화가 부족하면 모험 보상을 확인해 보세요.", null, null,
                () => { })) m_unlockSeen = true;
        }
    }

    bool TryFindSource(ECurrencyType currency, out int index)
    {
        index = -1;
        if (!AdventureTutorialRunner.IsUnlocked) return false;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < AdventureProgress.NodeCount; i++)
            {
                if (pass == 0 ? !AdventureProgress.IsRewardPending(i) : !AdventureProgress.CanEnter(i)) continue;
                m_rewards.Clear(); AdventureProgress.FillRewards(i, m_rewards);
                foreach (var reward in m_rewards)
                    if (reward.IsCurrency && reward.Gain.Type == currency && reward.Amount > 0)
                    { index = i; return true; }
            }
        return false;
    }

    bool Show(string key, string message, string action, System.Action onAction, System.Action onDismiss)
    {
        if (m_prefab == null) return false;
        var gate = OutgameTutorialGateUI.Ensure(m_prefab);
        if (OutgameTutorialGateUI.IsShowing && gate.IsOwnedBy(this) && m_hintKey == key && m_hintMessage == message) return true;
        if (!gate.TryShowHint(this, message, action,
            onAction == null ? null : () => { m_hintKey = null; onAction(); },
            () => { m_hintKey = null; onDismiss?.Invoke(); })) return false;
        m_hintKey = key; m_hintMessage = message;
        return true;
    }
    void SuspendHint()
    {
        if (m_hintKey == "shortage") m_offered = false;
        if (m_hintKey == "unlock") m_unlockSeen = false;
        ClearHint();
    }
    void ClearHint()
    {
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear(this);
        m_hintKey = m_hintMessage = null;
    }
    void ResetReturn() { s_returnCard = 0; s_returnNode = null; m_returned = m_rankHint = false; }
    void OnDestroy()
    {
        CardDetailOverlayView.OnAnyClosed -= CardClosed;
        CardDetailOverlayView.OnAnyEnhanceSettled -= Enhanced;
        GuidanceCoordinator.OnMissionNoticePresentationChanged -= MissionNoticeChanged;
        ClearHint(); if (s_instance == this) s_instance = null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { s_instance = null; s_returnCard = 0; s_returnNode = s_user = null; }
}
