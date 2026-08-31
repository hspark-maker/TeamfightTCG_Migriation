using System.Collections.Generic;

// 앨범의 기존 claim 키를 통합 Reward 테이블 키로 연결한다.
public static class AlbumSpec
{
    public static bool TryGetRewards(string _themeId, string _pageId, out List<AlbumRewardDef> _rewards)
        => RewardSpec.TryGetRewards(ERewardOwnerType.Album, OwnerIdOf(_themeId, _pageId), out _rewards);

    public static void Init() => RewardSpec.Init();

    static string OwnerIdOf(string _themeId, string _pageId)
    {
        if (string.IsNullOrEmpty(_themeId)) return "b";
        if (string.IsNullOrEmpty(_pageId)) return "t:" + _themeId;
        return "p:" + _themeId + "/" + _pageId;
    }
}
