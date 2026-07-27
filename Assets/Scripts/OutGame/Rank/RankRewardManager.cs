using System;
using System.Collections.Generic;
using UnityEngine;

// 랭크 티어 달성 보상의 static 단일 창구.
// RankManager는 동결 계약이라 건드리지 않고, 보상은 여기로만 흐른다(달성 판정은 RankManager.GetInfo에 위임).
// RankManager와 같은 결로 캐시 없이 슬롯을 직접 읽는다(Init 없음 = 부트 계약 무접촉, Load가 Data를 교체해도 stale 없음).
public static class RankRewardManager
{
    static RankConfig s_config;

    // 수령 통지 — 패널이 행 상태를 다시 그리는 유일한 트리거.
    public static event Action OnChanged;

    public static int TierCount => Config.tiers != null ? Config.tiers.Count : 0;

    public static int ClaimedCount => Slot.claimedCount;

    // RankReward 진입 버튼의 알림 점용. 순차 수령이라 후보는 커서 위치 한 곳뿐이다.
    public static bool HasAnyClaimable => StateOf(Slot.claimedCount) == ERankRewardState.Claimable;

    // 미배선(전투 씬 직접 Play 등)에서도 동작하도록 기본 인스턴스로 fallback한다.
    static RankConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<RankConfig>());

    // 슬롯 접근 단일 지점. 손상·구 세이브로 노드가 비어도 크래시 대신 기본값으로 살아난다.
    static RankSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.rank == null) t_data.rank = new RankSaveData();
            return t_data.rank;
        }
    }

    /// <summary>부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지.</summary>
    public static void SetConfig(RankConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    // UI 1회 스냅샷. 범위 밖·null 원소도 예외 없이 Locked 기본값으로 떨어뜨린다.
    public static RankRewardInfo GetInfo(int _tierIndex)
    {
        RankTier t_tier = FindTier(_tierIndex);

        return new RankRewardInfo(
            _tierIndex,
            t_tier != null && t_tier.displayName != null ? t_tier.displayName : string.Empty, // TMP 소비처 NRE 방지 — null 대신 빈 문자열.
            t_tier != null ? t_tier.badge : null,                                             // 뱃지는 null 허용(뷰가 non-null일 때만 교체).
            t_tier != null ? t_tier.rewardGold : 0,
            StateOf(_tierIndex));
    }

    public static bool CanClaim(int _tierIndex) => StateOf(_tierIndex) == ERankRewardState.Claimable;

    // 지급 → 커서 갱신 → 즉시 영속 → 통지. 가드에 걸리면 아무것도 하지 않고 false.
    public static bool Claim(int _tierIndex)
    {
        if (!CanClaim(_tierIndex)) return false;

        var t_tier = FindTier(_tierIndex);
        if (t_tier == null) return false;

        CurrencyManager.Earn(ECurrencyType.Gold, t_tier.rewardGold); // 0/음수는 Earn이 무시 — 커서는 그대로 넘긴다.
        Slot.claimedCount = _tierIndex + 1;

        // CurrencyManager.Save()가 골드를 슬롯에 flush한 뒤 DataSaveManager.Save()를 부른다 —
        // 여기서 따로 Save를 앞세우면 "커서만 오르고 골드 미반영" 상태가 한 번 디스크에 쓰인다.
        CurrencyManager.Save();
        OnChanged?.Invoke();
        return true;
    }

    // 디버그 전용: 수령 커서만 되돌린다(이미 지급된 골드는 회수하지 않는다).
    public static void ResetForDebug()
    {
        Slot.claimedCount = 0;
        DataSaveManager.Save();
        OnChanged?.Invoke();
    }

    // 상태 3종 배타 판정(단일 진실원).
    // Claimed를 먼저 검사한다 — RankManager.ResetForDebug()가 points만 0으로 되돌려 claimedCount > 도달티어가 될 수 있고,
    // Claimable을 먼저 보면 그 구간에서 재수령이 뚫린다.
    // 달성했어도 차례가 아니면(순차 수령) Locked로 표현한다 — 하이라이트는 항상 1개.
    static ERankRewardState StateOf(int _tierIndex)
    {
        if (_tierIndex < 0 || _tierIndex >= TierCount) return ERankRewardState.Locked;
        if (_tierIndex < Slot.claimedCount) return ERankRewardState.Claimed;

        bool t_reached = _tierIndex <= RankManager.GetInfo().TierIndex;
        return t_reached && _tierIndex == Slot.claimedCount ? ERankRewardState.Claimable : ERankRewardState.Locked;
    }

    // 범위·null 원소 방어를 한 곳에 모은다.
    static RankTier FindTier(int _tierIndex)
    {
        List<RankTier> t_tiers = Config.tiers;
        if (t_tiers == null || _tierIndex < 0 || _tierIndex >= t_tiers.Count) return null;
        return t_tiers[_tierIndex];
    }
}

// 티어 보상 행 상태(3종 배타). "달성했지만 차례 아님"은 Locked에 포함된다(순차 수령).
public enum ERankRewardState
{
    Locked,    // 미달성 또는 아직 차례 아님 → 클릭 불가
    Claimable, // 달성 & 미수령 & 순차 차례 → 하이라이트 + 클릭 가능
    Claimed,   // 이미 수령 → 체크 마크
}

// 티어 보상 1회 스냅샷(UI용). 포인트·수령 커서가 바뀌면 값이 달라지므로 표시 시점마다 GetInfo로 다시 받는다.
public readonly struct RankRewardInfo
{
    public readonly int TierIndex;          // 티어 인덱스(0 = 최하위)
    public readonly string DisplayName;     // 티어 표시명(항상 non-null)
    public readonly Sprite Badge;           // 티어 뱃지(없을 수 있음)
    public readonly long RewardGold;        // 달성 시 1회 수령 골드
    public readonly ERankRewardState State;

    public RankRewardInfo(int _tierIndex, string _displayName, Sprite _badge, long _rewardGold, ERankRewardState _state)
    {
        TierIndex = _tierIndex;
        DisplayName = _displayName;
        Badge = _badge;
        RewardGold = _rewardGold;
        State = _state;
    }
}
