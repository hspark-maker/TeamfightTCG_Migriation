using UnityEngine;

// 앨범 보상 1건 UI 스냅샷
public readonly struct AlbumRewardInfo
{
    public readonly EAlbumRewardTier Tier;
    public readonly ECurrencyType Currency;
    public readonly long Amount;
    public readonly Sprite Icon;
    public readonly int Owned;
    public readonly int Total;
    public readonly EAlbumRewardState State;

    public AlbumRewardInfo(
        EAlbumRewardTier _tier,
        ECurrencyType _currency,
        long _amount,
        Sprite _icon,
        int _owned,
        int _total,
        EAlbumRewardState _state)
    {
        Tier = _tier;
        Currency = _currency;
        Amount = _amount;
        Icon = _icon;
        Owned = _owned;
        Total = _total;
        State = _state;
    }
}
