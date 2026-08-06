using System;
using System.Collections.Generic;

// 앨범 보상 수령 낙인 세이브 값 객체 — 진행도는 저장하지 않는다(소유 파생)
[Serializable]
public class AlbumRewardSaveData
{
    public const int VERSION = 1;

    public int version = VERSION;

    // 수령한 보상의 낙인 키("p:테마/페이지" · "t:테마" · "b")
    public List<string> claimedKeys = new List<string>();
}
