using System;
using UnityEngine;

// 랭크 티어 달성 보상의 static 단일 창구(순차 수령)
public static class RankRewardManager
{
    static RankConfig s_config;

    // 수령 통지 — 패널이 행 상태를 다시 그리는 트리거
    public static event Action OnChanged;

    // 전체 티어 수
    public static int TierCount => Config.TierCount;

    // 지금까지 수령한 티어 개수(= 다음 수령 대상 인덱스)
    public static int ClaimedCount => Slot.claimedCount;

    // 수령 가능한 보상이 있는지(알림 점용)
    public static bool HasAnyClaimable => StateOf(Slot.claimedCount) == ERankRewardState.Claimable;

    static RankConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<RankConfig>());

    static RankSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.rank == null) t_data.rank = new RankSaveData();
            return t_data.rank;
        }
    }

    // 부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지
    public static void SetConfig(RankConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    // 티어 보상 행 1회 스냅샷(범위 밖은 None + Locked)
    public static RankRewardInfo GetInfo(int _tierIndex)
    {
        Config.TryGetTier(_tierIndex, out RankTier t_tier);

        return new RankRewardInfo(
            _tierIndex,
            t_tier.DisplayName,
            t_tier.Badge,
            t_tier.Reward,
            StateOf(_tierIndex));
    }

    public static bool CanClaim(int _tierIndex) => StateOf(_tierIndex) == ERankRewardState.Claimable;

    // 보상 수령 — 지급 → 커서 갱신 → 즉시 영속 → 통지
    public static bool Claim(int _tierIndex)
    {
        if (!CanClaim(_tierIndex)) return false;

        if (!Config.TryGetTier(_tierIndex, out RankTier t_tier)) return false;

        CurrencyManager.Earn(t_tier.Reward.Type, t_tier.Reward.Amount);
        Slot.claimedCount = _tierIndex + 1;

        // CurrencyManager.Save()가 골드 flush 후 DataSaveManager.Save()까지 부른다(순서 뒤집으면 골드 미반영 상태가 기록된다)
        CurrencyManager.Save();
        OnChanged?.Invoke();
        return true;
    }

    // 수령 커서만 되돌린다(디버그 전용, 지급된 골드는 회수하지 않는다)
    public static void ResetForDebug()
    {
        Slot.claimedCount = 0;
        DataSaveManager.Save();
        OnChanged?.Invoke();
    }

    // Claimed 검사가 먼저여야 한다 — claimedCount > 도달티어인 구간에서 재수령이 뚫린다
    static ERankRewardState StateOf(int _tierIndex)
    {
        if (_tierIndex < 0 || _tierIndex >= TierCount) return ERankRewardState.Locked;
        if (_tierIndex < Slot.claimedCount) return ERankRewardState.Claimed;

        bool t_reached = _tierIndex <= RankManager.GetInfo().TierIndex;
        return t_reached && _tierIndex == Slot.claimedCount ? ERankRewardState.Claimable : ERankRewardState.Locked;
    }
}

// 티어 보상 행 상태(3종 배타, "달성했지만 차례 아님"은 Locked)
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
    public readonly CurrencyGain Reward;
    public readonly ERankRewardState State;

    public RankRewardInfo(int _tierIndex, string _displayName, Sprite _badge, CurrencyGain _reward, ERankRewardState _state)
    {
        TierIndex = _tierIndex;
        DisplayName = _displayName;
        Badge = _badge;
        Reward = _reward;
        State = _state;
    }
}
