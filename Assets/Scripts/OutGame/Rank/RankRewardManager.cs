using System;
using System.Collections.Generic;
using UnityEngine;

// 랭크 티어 달성 보상의 static 단일 창구(도달한 보상은 순서 무관하게 수령)
public static class RankRewardManager
{
    static RankConfig s_config;
    static bool s_configured;
    static bool s_warnedDefault;

    public static bool IsConfigured => s_configured;

    // 수령 통지 — 패널이 행 상태를 다시 그리는 트리거
    public static event Action OnChanged;

    // 전체 티어 수
    public static int TierCount => Config.TierCount;

    // 수령 가능한 티어 중 가장 높은 인덱스(없으면 -1). 강조 표식과 열기 스크롤의 공통 기준.
    public static int TopClaimableIndex
    {
        get
        {
            int t_from = Mathf.Min(RankManager.GetInfo().TierIndex, TierCount - 1);
            for (int t_i = t_from; t_i >= 0; t_i--)
                if (StateOf(t_i) == ERankRewardState.Claimable) return t_i;

            return -1;
        }
    }

    // 수령 가능한 보상이 있는지(알림 점용)
    public static bool HasAnyClaimable => TopClaimableIndex >= 0;

    static RankConfig Config
    {
        get
        {
            if (s_config != null) return s_config;
            WarnDefaultConfig();
            return s_config = ScriptableObject.CreateInstance<RankConfig>();
        }
    }

    static RankSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.Rank == null) t_data.Rank = new RankSaveData();
            return t_data.Rank;
        }
    }

    // 초기화에서 실제 애셋 주입(선택). null이면 기본 유지
    public static void SetConfig(RankConfig _config)
    {
        if (_config == null) return;
        s_config = _config;
        s_configured = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_config = null;
        s_configured = false;
        s_warnedDefault = false;
        OnChanged = null;
    }

    static void WarnDefaultConfig()
    {
        if (s_warnedDefault) return;
        s_warnedDefault = true;
        Debug.LogWarning("[RankRewardManager] RankConfig가 주입되지 않아 기본값으로 동작합니다.");
    }

    // 티어 보상 행 1회 스냅샷(범위 밖은 None + Locked)
    public static RankRewardInfo GetInfo(int _tierIndex)
    {
        Config.TryGetTier(_tierIndex, out RankTier t_tier);

        var t_state = StateOf(_tierIndex);

        // 매번 새 리스트 — 팝업이 Show 시점 스냅샷을 들고 있다가 나중에 소비하므로 공용 버퍼를 돌려주면 stale이 된다
        var t_rewards = new List<RewardLine>();
        Config.FillRewards(_tierIndex, t_rewards);

        return new RankRewardInfo(
            _tierIndex,
            t_tier.DisplayName,
            t_tier.Badge,
            t_rewards,
            t_state,
            t_state == ERankRewardState.Claimable && _tierIndex == TopClaimableIndex);
    }

    public static bool CanClaim(int _tierIndex) => StateOf(_tierIndex) == ERankRewardState.Claimable;

    // 보상 수령 — 지급 → 낙인 → 즉시 영속 → 통지
    public static bool Claim(int _tierIndex)
    {
        if (!CanClaim(_tierIndex)) return false;

        var t_rewards = new List<RewardLine>();
        Config.FillRewards(_tierIndex, t_rewards);

        for (int t_i = 0; t_i < t_rewards.Count; t_i++)
            CurrencyManager.Earn(t_rewards[t_i].Gain.Type, t_rewards[t_i].Gain.Amount);

        Slot.ClaimedTiers.Add(_tierIndex);

        // CurrencyManager.Save()가 골드 flush 후 DataSaveManager.Save()까지 부른다(순서 뒤집으면 골드 미반영 상태가 기록된다)
        CurrencyManager.Save();
        OnChanged?.Invoke();
        return true;
    }

    // 수령 낙인만 지운다(디버그 전용, 지급된 골드는 회수하지 않는다)
    public static void ResetForDebug()
    {
        Slot.ClaimedTiers.Clear();
        DataSaveManager.Save();
        OnChanged?.Invoke();
    }

    // Claimed 검사가 먼저여야 한다 — 강등 등으로 도달 티어가 내려간 구간에서 수령 표시가 풀린다
    // 도달 판정은 티어 인덱스가 아니라 포인트로 한다 — 인덱스는 미도달(언랭크)도 0으로 폴백해서
    // 첫 티어 보상이 튜토리얼 시작부터 수령 가능으로 보인다.
    static ERankRewardState StateOf(int _tierIndex)
    {
        if (_tierIndex < 0 || _tierIndex >= TierCount) return ERankRewardState.Locked;
        if (Slot.ClaimedTiers.Contains(_tierIndex)) return ERankRewardState.Claimed;

        if (!Config.TryGetTier(_tierIndex, out RankTier t_tier)) return ERankRewardState.Locked;

        return RankManager.Points >= t_tier.RequiredPoints
            ? ERankRewardState.Claimable
            : ERankRewardState.Locked;
    }
}

// 티어 보상 행 상태(3종 배타, 미도달만 Locked)
public enum ERankRewardState
{
    Locked,
    Claimable,
    Claimed,
}

// 티어 보상 1회 스냅샷(UI용)
public readonly struct RankRewardInfo
{
    public readonly int TierIndex;
    public readonly string DisplayName;
    public readonly Sprite Badge;
    public readonly IReadOnlyList<RewardLine> Rewards;
    public readonly ERankRewardState State;

    // 수령 가능한 행 중 최상위 — 강조 표식 대상. 상태 enum에 섞지 않는다(State == Claimable 검사가 조용히 깨진다).
    public readonly bool IsTopClaimable;

    public RankRewardInfo(int _tierIndex, string _displayName, Sprite _badge, IReadOnlyList<RewardLine> _rewards,
                          ERankRewardState _state, bool _isTopClaimable)
    {
        TierIndex = _tierIndex;
        DisplayName = _displayName;
        Badge = _badge;
        Rewards = _rewards ?? System.Array.Empty<RewardLine>();
        State = _state;
        IsTopClaimable = _isTopClaimable;
    }
}
