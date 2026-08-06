using System.Collections.Generic;

// 앨범 보상 1건 UI 스냅샷 (보상은 복수 — 재화 종류별 리스트)
public readonly struct AlbumRewardInfo
{
    public readonly EAlbumRewardTier Tier;
    public readonly IReadOnlyList<AlbumRewardDef> Rewards;
    public readonly int Owned;
    public readonly int Total;
    public readonly EAlbumRewardState State;

    public AlbumRewardInfo(
        EAlbumRewardTier _tier,
        IReadOnlyList<AlbumRewardDef> _rewards,
        int _owned,
        int _total,
        EAlbumRewardState _state)
    {
        Tier = _tier;
        Rewards = _rewards ?? System.Array.Empty<AlbumRewardDef>();
        Owned = _owned;
        Total = _total;
        State = _state;
    }
}
