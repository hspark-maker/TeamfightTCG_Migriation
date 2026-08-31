using System;

// 세이브 문서의 슬롯 축. 서버가 어떤 슬롯을 갈아끼웠는지 통지하는 데 쓴다.
[Flags]
public enum ESaveSlot
{
    None          = 0,

    // 1 << 0 은 옛 Currency 자리다. 재화는 세이브를 떠나 지갑 문서로 갔고 그 비트는 다시 쓰지 않는다.
    Ownership     = 1 << 1,
    Deck          = 1 << 2,
    CardGrowth    = 1 << 3,
    KeywordGrowth = 1 << 4,
    Rank          = 1 << 5,
    AlbumReward   = 1 << 6,
    Tournament    = 1 << 7,
    Tutorial      = 1 << 8,
    Profile       = 1 << 9,
}
