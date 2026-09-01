using System.Collections.Generic;

// 앨범 페이지 하나의 런타임 뷰(스펙시트에서 파생, 런타임 불변)
public sealed class AlbumPage : AlbumSection
{
    // 테마 내 페이지 인덱스(표시용, 식별 키 아님)
    public int Index { get; }

    // 이 페이지 첫 칸의 도감 번호(테마 내 통번호, 1부터). 칸 번호는 FirstNumber + 칸 인덱스다 —
    // 화면·삽입 연출이 각자 누적을 다시 세면 같은 칸이 다른 번호로 보인다.
    public int FirstNumber { get; }

    // 칸 순서 그대로(빈 슬롯 포함 — UI가 빈 칸을 그린다)
    internal AlbumPage(
        string _key,
        int _index,
        IReadOnlyList<AlbumRewardDef> _rewards,
        string _themeKey,
        bool _hasStableKey,
        IReadOnlyList<int> _cardIds,
        int _firstNumber)
        : base(_key, _rewards, _cardIds, _hasStableKey ? "p:" + _themeKey + "/" + _key : null)
    {
        Index = _index;
        FirstNumber = _firstNumber;
    }
}
