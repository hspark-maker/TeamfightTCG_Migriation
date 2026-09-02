using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 랭크 티어 달성 보상의 static 단일 창구(도달한 보상은 순서 무관하게 수령)
public static class RankRewardManager
{
    static RankConfig s_config;
    static bool s_configured;

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
            return RankGradeSpec.UninitializedConfig;
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
        if (_config == null) throw new ArgumentNullException(nameof(_config));
        s_config = _config;
        s_configured = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_config = null;
        s_configured = false;
        OnChanged = null;
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

    /// <summary>보상 수령을 서버에 요청한다. 지급과 낙인(claimedTiers)을 서버가 한 트랜잭션으로 끝내고,
    /// 응답 채택이 재화·랭크 슬롯을 갈아끼운다. 서버가 준 목록째로 돌려준다(팝업이 이 값으로 연출을 정한다).</summary>
    // CanClaim은 왕복을 아끼는 낙관 검사다 — 자격의 진실원은 서버이고, 여기서 통과해도 거절될 수 있다.
    public static async UniTask<RewardClaimOutcome> ClaimAsync(int _tierIndex)
    {
        if (!CanClaim(_tierIndex)) return default;

        // 첫 await 이전에 걸어야 한다 — 뒤로 밀리면 팝업의 숫자 롤업이 옛 잔액을 목표로 잡아 역주행한다.
        var t_rewards = new List<RewardLine>();
        Config.FillRewards(_tierIndex, t_rewards);
        var t_pending = CurrencyPendingTicket.Hold(t_rewards);

        // 티어 인덱스 문자열이 스펙시트 Reward.ownerId 와 같은 키다(RankConfig.FillRewards와 같은 표기).
        // 통지는 창구가 왕복 시작·종료에 한 번씩 울려 준다 — 시작 통지가 행을 즉시 수령 완료로 그리고,
        // 종료 통지가 성공이면 서버 낙인으로 확정하고 거절이면 원래 상태로 되돌린다.
        var t_outcome = await RewardClaimCommand.ClaimAsync(RewardClaimCommand.OwnerRank, _tierIndex.ToString(),
                                                           t_pending, () => OnChanged?.Invoke());

        return t_outcome.Succeeded ? t_outcome : default;
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

        // 서버가 낙인을 돌려주기 전까지의 틈 — 이걸 안 보면 행이 왕복 내내 "받을 수 있음"으로 남는다.
        // HasAnyInFlight 선검사가 평상시 문자열 할당을 막는다(GetInfo가 행마다 TopClaimableIndex를 돈다).
        if (RewardClaimCommand.HasAnyInFlight
            && RewardClaimCommand.IsInFlight(RewardClaimCommand.OwnerRank, _tierIndex.ToString()))
            return ERankRewardState.Claimed;

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
