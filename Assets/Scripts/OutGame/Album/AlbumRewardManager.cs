using System;

// 앨범 3단(페이지·테마·앨범) 완성 보상의 static 수령 창구 — 캐시·Init 없이 세이브 슬롯 직독(부트 무접촉)
public static class AlbumRewardManager
{
    // 앨범 전체 보상의 낙인 키 — 계층 낙인 키의 유일한 예외 상수(그 외 조립은 AlbumTheme/AlbumPage만)
    const string AlbumRewardKey = "b";

    // 수령 통지 — 패널이 보상 상태를 다시 그리는 트리거
    public static event Action OnChanged;

    // 수령 가능한 보상이 있는지(알림 점용)
    public static bool HasAnyClaimable
    {
        get
        {
            var t_themes = CardAlbum.Themes;
            for (int t_i = 0; t_i < t_themes.Count; t_i++)
            {
                if (ClaimableCountOf(t_themes[t_i]) > 0) return true;
            }
            return CanClaimAlbum();
        }
    }

    static AlbumRewardSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.albumReward == null) t_data.albumReward = new AlbumRewardSaveData();
            return t_data.albumReward;
        }
    }

    public static AlbumRewardInfo GetPageInfo(AlbumPage _page)
    {
        if (_page == null) return default;
        return new AlbumRewardInfo(
            EAlbumRewardTier.Page,
            _page.Reward.currency, _page.Reward.amount, _page.Reward.icon,
            CardAlbum.OwnedCountOf(_page), CardAlbum.TotalCountOf(_page),
            StateOf(_page.RewardKey, CardAlbum.IsComplete(_page)));
    }

    public static AlbumRewardInfo GetThemeInfo(AlbumTheme _theme)
    {
        if (_theme == null) return default;
        return new AlbumRewardInfo(
            EAlbumRewardTier.Theme,
            _theme.Reward.currency, _theme.Reward.amount, _theme.Reward.icon,
            CardAlbum.OwnedCountOf(_theme), CardAlbum.TotalCountOf(_theme),
            StateOf(_theme.RewardKey, CardAlbum.IsComplete(_theme)));
    }

    // 앨범 진행도는 완성 테마 수 기준(n/N = 완성 테마/전체 테마)
    public static AlbumRewardInfo GetAlbumInfo()
    {
        var t_reward = CardAlbum.AlbumReward;
        return new AlbumRewardInfo(
            EAlbumRewardTier.Album,
            t_reward.currency, t_reward.amount, t_reward.icon,
            CardAlbum.CompletedThemeCount, CardAlbum.ThemeCount,
            StateOf(AlbumRewardKey, CardAlbum.IsAlbumComplete));
    }

    public static bool CanClaimPage(AlbumPage _page)
        => _page != null && StateOf(_page.RewardKey, CardAlbum.IsComplete(_page)) == EAlbumRewardState.Claimable;

    public static bool CanClaimTheme(AlbumTheme _theme)
        => _theme != null && StateOf(_theme.RewardKey, CardAlbum.IsComplete(_theme)) == EAlbumRewardState.Claimable;

    public static bool CanClaimAlbum()
        => StateOf(AlbumRewardKey, CardAlbum.IsAlbumComplete) == EAlbumRewardState.Claimable;

    public static bool ClaimPage(AlbumPage _page)
    {
        if (!CanClaimPage(_page)) return false;
        return Claim(_page.RewardKey, _page.Reward);
    }

    public static bool ClaimTheme(AlbumTheme _theme)
    {
        if (!CanClaimTheme(_theme)) return false;
        return Claim(_theme.RewardKey, _theme.Reward);
    }

    public static bool ClaimAlbum()
    {
        if (!CanClaimAlbum()) return false;
        return Claim(AlbumRewardKey, CardAlbum.AlbumReward);
    }

    // 테마 내 수령 가능 건수(페이지들 + 테마 자신) — 테마 버튼 알림 뱃지용
    public static int ClaimableCountOf(AlbumTheme _theme)
    {
        if (_theme == null) return 0;

        int t_count = 0;
        var t_pages = _theme.Pages;
        for (int t_i = 0; t_i < t_pages.Count; t_i++)
        {
            if (CanClaimPage(t_pages[t_i])) t_count++;
        }
        if (CanClaimTheme(_theme)) t_count++;
        return t_count;
    }

    // 낙인만 되돌린다(디버그 전용, 지급된 재화는 회수하지 않는다)
    public static void ResetForDebug()
    {
        Slot.claimedKeys.Clear();
        DataSaveManager.Save();
        OnChanged?.Invoke();
    }

    // 보상 지급 → 낙인 → 즉시 영속 → 통지
    static bool Claim(string _rewardKey, AlbumRewardDef _reward)
    {
        CurrencyManager.Earn(_reward.currency, _reward.amount);
        Slot.claimedKeys.Add(_rewardKey);

        // CurrencyManager.Save()가 재화 flush 후 DataSaveManager.Save()까지 부른다(순서 뒤집으면 재화 미반영 상태가 기록된다)
        CurrencyManager.Save();
        OnChanged?.Invoke();
        return true;
    }

    // Claimed 검사가 먼저여야 한다 — 완성 취소 후 재완성 구간에서 재수령이 뚫린다
    static EAlbumRewardState StateOf(string _rewardKey, bool _complete)
    {
        if (string.IsNullOrEmpty(_rewardKey)) return EAlbumRewardState.Locked;
        if (Slot.claimedKeys.Contains(_rewardKey)) return EAlbumRewardState.Claimed;
        return _complete ? EAlbumRewardState.Claimable : EAlbumRewardState.Locked;
    }
}

// 앨범 보상 계층
public enum EAlbumRewardTier
{
    Page,
    Theme,
    Album,
}

// 앨범 보상 상태(3종 배타)
public enum EAlbumRewardState
{
    Locked,
    Claimable,
    Claimed,
}
