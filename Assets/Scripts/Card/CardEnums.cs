/// <summary>카드가 노출되는 콘텐츠 채널.</summary>
public enum ECardChannel
{
    TestOnly = 0,
    Live = 1,
}

/// <summary>
/// 카드 희소 등급. 플레이어 랭크 등급(<see cref="ERankGrade"/>)과는 다른 축이다.
/// 직렬화·스펙 호환을 위해 기존 숫자값을 유지한다.
/// </summary>
public enum ECardGrade
{
    Unknown = 0,
    Common = 1,
    Rare = 2,
    Arcane = 3,
    Mythic = 4,
}
