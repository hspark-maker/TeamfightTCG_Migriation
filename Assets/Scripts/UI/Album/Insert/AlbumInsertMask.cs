using System;
using System.Collections.Generic;
using UnityEngine;

// 삽입 연출 중 "소유는 확정됐지만 아직 화면에 안 꽂은 카드"의 단일 진실원.
//
// 소유(OwnershipManager)는 팩 개봉 시점에 이미 끝났다 — 여기서 감추는 건 오직 그림이다.
// 세이브·CardAlbum·AlbumRewardManager를 읽지도 쓰지도 않는다(카드 번호 집합만 안다).
//
// ⚠ 위장을 켠 채 세션이 죽으면 카드가 영영 빈 칸으로 보인다. 해제 경로를 지우지 말 것
//   (AlbumInsertSession.OnDisable / AlbumPageOverlayView.OnDisable / 아래 씬 로드 초기화).
public static class AlbumInsertMask
{
    static readonly HashSet<int> s_hidden = new HashSet<int>();

    /// <summary>위장이 바뀐 순간 1회. 앨범 뷰들이 이걸로 다시 그린다.</summary>
    public static event Action OnChanged;

    /// <summary>하나라도 숨겨져 있는가. 평상시 빠른 탈출용.</summary>
    public static bool Active => s_hidden.Count > 0;

    public static int HiddenTotal => s_hidden.Count;

    public static void HideAll(IReadOnlyList<CardData> _cards)
    {
        if (_cards == null || _cards.Count == 0) return;

        bool t_changed = false;
        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            int t_id = CardCatalog.IdOf(_cards[t_i]);
            if (t_id <= 0) continue;

            t_changed |= s_hidden.Add(t_id);
        }

        if (t_changed) OnChanged?.Invoke();
    }

    public static void Reveal(CardData _card)
    {
        int t_id = CardCatalog.IdOf(_card);
        if (t_id <= 0) return;
        if (!s_hidden.Remove(t_id)) return;

        OnChanged?.Invoke();
    }

    public static void Clear()
    {
        if (s_hidden.Count == 0) return;

        s_hidden.Clear();
        OnChanged?.Invoke();
    }

    public static bool IsHidden(CardData _card)
    {
        if (s_hidden.Count == 0) return false;

        int t_id = CardCatalog.IdOf(_card);
        return t_id > 0 && s_hidden.Contains(t_id);
    }

    // 게이지 표시값에서 뺄 몫. 실제 보상 판정(AlbumRewardManager)은 건드리지 않는다.
    public static int HiddenCountIn(AlbumPage _page)
    {
        if (s_hidden.Count == 0 || _page == null) return 0;

        return CountHidden(_page.Cards);
    }

    public static int HiddenCountIn(AlbumTheme _theme)
    {
        if (s_hidden.Count == 0 || _theme == null) return 0;

        return CountHidden(_theme.Cards);
    }

    // 도메인 리로드를 꺼두면 static 집합이 이전 플레이를 물고 넘어온다 — 시작마다 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        s_hidden.Clear();
        OnChanged = null;
    }

    static int CountHidden(IReadOnlyList<CardData> _cards)
    {
        if (_cards == null) return 0;

        int t_count = 0;
        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            int t_id = CardCatalog.IdOf(_cards[t_i]);
            if (t_id > 0 && s_hidden.Contains(t_id)) t_count++;
        }
        return t_count;
    }
}
