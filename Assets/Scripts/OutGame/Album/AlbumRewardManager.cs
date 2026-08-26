using System;
using System.Collections.Generic;

// 앨범 3단(페이지·테마·앨범) 완성 보상의 static 수령 창구 — 캐시·Init 없이 세이브 슬롯 직독(부트 무접촉)
public static class AlbumRewardManager
{
    // 앨범 전체 보상의 낙인 키 — 계층 낙인 키의 유일한 예외 상수(그 외 조립은 AlbumSection 파생만)
    const string AlbumRewardKey = "b";

    // 수령 통지 — 패널이 보상 상태를 다시 그리는 트리거
    public static event Action OnChanged;

    // 세이브 슬롯 직독 — 캐시를 두면 부트를 안 거친 씬에서 빈 낙인이 기존 기록을 덮어쓴다
    static AlbumRewardSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.albumReward == null) t_data.albumReward = new AlbumRewardSaveData();
            return t_data.albumReward;
        }
    }

    public static AlbumRewardInfo GetPageInfo(AlbumPage _page) => InfoOf(_page);

    public static AlbumRewardInfo GetThemeInfo(AlbumTheme _theme) => InfoOf(_theme);

    // 앨범 진행도는 완성 테마 수 기준(n/N = 완성 테마/전체 테마)
    public static AlbumRewardInfo GetAlbumInfo()
    {
        var t_rewards = CardAlbum.AlbumRewards;
        return new AlbumRewardInfo(
            t_rewards,
            CardAlbum.CompletedThemeCount, CardAlbum.ThemeCount,
            AlbumState());
    }

    public static bool CanClaimPage(AlbumPage _page) => StateOf(_page) == EAlbumRewardState.Claimable;

    public static bool CanClaimTheme(AlbumTheme _theme) => StateOf(_theme) == EAlbumRewardState.Claimable;

    public static bool CanClaimAlbum() => AlbumState() == EAlbumRewardState.Claimable;

    public static bool ClaimPage(AlbumPage _page) => Claim(_page);

    public static bool ClaimTheme(AlbumTheme _theme) => Claim(_theme);

    public static bool ClaimAlbum()
    {
        if (!CanClaimAlbum()) return false;

        Payout(AlbumRewardKey, CardAlbum.AlbumRewards);
        return true;
    }

    static AlbumRewardInfo InfoOf(AlbumSection _section)
    {
        if (_section == null) return default;

        return new AlbumRewardInfo(
            _section.Rewards,
            CardAlbum.OwnedCountOf(_section), CardAlbum.TotalCountOf(_section),
            StateOf(_section));
    }

    static bool Claim(AlbumSection _section)
    {
        if (StateOf(_section) != EAlbumRewardState.Claimable) return false;

        Payout(_section.RewardKey, _section.Rewards);
        return true;
    }

    // 보상 지급(리스트 전량) → 낙인 → 즉시 영속 → 통지
    static void Payout(string _rewardKey, IReadOnlyList<AlbumRewardDef> _rewards)
    {
        for (int t_i = 0; t_i < _rewards.Count; t_i++)
        {
            if (_rewards[t_i].amount <= 0) continue;
            CurrencyManager.Earn(_rewards[t_i].currency, _rewards[t_i].amount);
        }
        Slot.claimedKeys.Add(_rewardKey);

        // CurrencyManager.Save()가 재화 flush 후 DataSaveManager.Save()까지 부른다(순서 뒤집으면 재화 미반영 상태가 기록된다)
        CurrencyManager.Save();
        OnChanged?.Invoke();
    }

    static EAlbumRewardState StateOf(AlbumSection _section)
        => _section != null
            ? StateOf(_section.RewardKey, CardAlbum.IsComplete(_section), _section.Rewards.Count > 0)
            : EAlbumRewardState.Locked;

    static EAlbumRewardState AlbumState()
        => StateOf(AlbumRewardKey, CardAlbum.IsAlbumComplete, CardAlbum.AlbumRewards.Count > 0);

    // Claimed 검사가 먼저여야 한다 — 완성 취소 후 재완성 구간에서 재수령이 뚫린다.
    // 빈 보상 리스트는 Claimable 불성립(줄 게 없는 낙인 소모 방지) — 기수령 낙인은 보상이 비어도 Claimed
    static EAlbumRewardState StateOf(string _rewardKey, bool _complete, bool _hasReward)
    {
        if (string.IsNullOrEmpty(_rewardKey)) return EAlbumRewardState.Locked;
        if (Slot.claimedKeys.Contains(_rewardKey)) return EAlbumRewardState.Claimed;
        return _complete && _hasReward ? EAlbumRewardState.Claimable : EAlbumRewardState.Locked;
    }
}

// 앨범 보상 상태(3종 배타)
public enum EAlbumRewardState
{
    Locked,
    Claimable,
    Claimed,
}
