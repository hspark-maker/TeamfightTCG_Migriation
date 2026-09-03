using System.Collections.Generic;

// 모험의 기존 node/chapter 키를 통합 Reward 테이블 키로 연결한다.
public static class AdventureSpec
{
    public static bool TryGetRewards(string _ownerKey, out List<AlbumRewardDef> _rewards)
        => RewardSpec.TryGetRewards(ERewardOwnerType.Adventure, _ownerKey, out _rewards);

    public static void Init() => RewardSpec.Init();
}
