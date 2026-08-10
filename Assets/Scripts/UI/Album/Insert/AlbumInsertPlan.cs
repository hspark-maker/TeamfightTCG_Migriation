using System.Collections.Generic;
using UnityEngine;

// 삽입 스텝 하나 — "이 테마 이 페이지 이 칸에 이 카드를 꽂는다"
public readonly struct AlbumInsertStep
{
    public readonly AlbumTheme Theme;
    public readonly int PageIndex;
    public readonly int SlotIndex;
    public readonly CardData Card;

    // 빈 칸에 찍히는 도감 번호(테마 내 통번호). 삽입 씰도 같은 번호를 보여야 대상 칸과 같은 그림이다.
    public readonly int Number;

    public AlbumInsertStep(AlbumTheme _theme, int _pageIndex, int _slotIndex, CardData _card, int _number)
    {
        Theme = _theme;
        PageIndex = _pageIndex;
        SlotIndex = _slotIndex;
        Card = _card;
        Number = _number;
    }
}

// 카드 목록을 앨범 배치 순서(테마→페이지→칸)의 스텝 배열로 해석한다.
// 앨범을 순회하며 대상 카드를 집으므로 정렬이 공짜로 나온다 — 정렬을 따로 하면
// 같은 페이지를 왔다 갔다 하며 페이지 전환 연출이 낭비된다.
public static class AlbumInsertPlan
{
    // _unplaced: 앨범 어느 칸에도 없는 카드(저작 드리프트). 호출자가 위장을 즉시 풀어야 한다.
    public static List<AlbumInsertStep> Build(IReadOnlyList<CardData> _cards, out List<CardData> _unplaced)
    {
        var t_steps = new List<AlbumInsertStep>();
        _unplaced = new List<CardData>();

        if (_cards == null || _cards.Count == 0) return t_steps;

        // 같은 카드가 여러 칸에 저작돼 있어도 한 번만 꽂는다.
        var t_want = new HashSet<int>();
        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            int t_id = CardCatalog.IdOf(_cards[t_i]);
            if (t_id > 0) t_want.Add(t_id);
        }

        var t_themes = CardAlbum.Themes;
        for (int t_t = 0; t_t < t_themes.Count; t_t++)
        {
            var t_theme = t_themes[t_t];

            // 도감 번호는 페이지가 아니라 테마 내 통번호다(AlbumPageOverlayView.RefreshPage와 같은 규칙).
            int t_base = 0;
            for (int t_p = 0; t_p < t_theme.Pages.Count; t_p++)
            {
                var t_pageCards = t_theme.Pages[t_p].Cards;
                for (int t_s = 0; t_s < t_pageCards.Count; t_s++)
                {
                    var t_card = t_pageCards[t_s];
                    int t_id = CardCatalog.IdOf(t_card);
                    if (t_id <= 0 || !t_want.Remove(t_id)) continue;

                    t_steps.Add(new AlbumInsertStep(t_theme, t_p, t_s, t_card, t_base + t_s + 1));
                }
                t_base += t_pageCards.Count;
            }
        }

        if (t_want.Count == 0) return t_steps;

        // 남은 건 꽂을 칸이 없는 카드다 — 조용히 버리면 그 카드가 영영 빈 칸으로 남는다.
        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            var t_card = _cards[t_i];
            int t_id = CardCatalog.IdOf(t_card);
            if (t_id > 0 && t_want.Contains(t_id)) _unplaced.Add(t_card);
        }
        Debug.LogWarning($"[AlbumInsertPlan] 앨범에 배치되지 않은 카드 {_unplaced.Count}장 — 삽입에서 제외한다(CardAlbumConfig 저작 확인).");

        return t_steps;
    }
}
