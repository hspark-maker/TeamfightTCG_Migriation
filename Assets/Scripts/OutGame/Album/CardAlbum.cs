using System.Collections.Generic;
using UnityEngine;

// 카드 앨범 구조의 읽기전용 static 파사드 — 진행도(소유 파생)와 완성 판정 모수는 여기서만 산출한다
public static class CardAlbum
{
    // 테마 그림(스킨)의 저작 원본. 구조·표시 텍스트의 진실원은 스펙시트다
    static CardAlbumConfig s_source;

    static IReadOnlyList<AlbumTheme> s_themes = System.Array.Empty<AlbumTheme>();

    // 조립이 끝나기 전엔 거짓 — 부분 상태를 노출하지 않는다
    public static bool IsReady { get; private set; }

    // 테마 목록(읽기 전용, 순서 = AlbumThemeInfo.order)
    public static IReadOnlyList<AlbumTheme> Themes => s_themes;

    public static int ThemeCount => Themes.Count;

    // 앨범 전체 완성 보상. 값의 진실원은 스펙시트 Reward 표뿐이다 — 저작 폴백이 없어 그 줄이 없으면 빈 목록이 된다
    public static IReadOnlyList<AlbumRewardDef> AlbumRewards
    {
        get
        {
            if (AlbumSpec.TryGetRewards(null, null, out List<AlbumRewardDef> t_spec)) return t_spec;

            return (IReadOnlyList<AlbumRewardDef>)System.Array.Empty<AlbumRewardDef>();
        }
    }

    // 완성 판정 모수 — 준비 중 테마는 세지 않는다. 카드 0장인 자리가 앨범 전체 보상을 영구 봉인하지 않게.
    public static int UnlockedThemeCount
    {
        get
        {
            var t_themes = Themes;
            int t_count = 0;
            for (int t_i = 0; t_i < t_themes.Count; t_i++)
            {
                if (!t_themes[t_i].IsLocked) t_count++;
            }
            return t_count;
        }
    }

    public static int CompletedThemeCount
    {
        get
        {
            var t_themes = Themes;
            int t_count = 0;
            for (int t_i = 0; t_i < t_themes.Count; t_i++)
            {
                if (!t_themes[t_i].IsLocked && IsComplete(t_themes[t_i])) t_count++;
            }
            return t_count;
        }
    }

    // 빈 앨범·빈 테마 포함 시 미완성 — 저작 실수가 보상으로 새지 않게 보수적으로 판정
    // 분모는 열린 테마뿐이다 — 준비 중 테마가 하나라도 있으면 앨범이 영영 완성되지 않는다.
    public static bool IsAlbumComplete => UnlockedThemeCount > 0 && CompletedThemeCount == UnlockedThemeCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => Clear();

    // 스킨 SO 주입 + 즉시 조립. SpecSource·CardCatalog 뒤에 서야 한다(초기화 순서는 OutgameConfigStep이 보장)
    public static void SetSource(CardAlbumConfig _config)
    {
        Clear();
        s_source = _config;
        s_themes = AlbumBuilder.Build(s_source);
        IsReady = true;
    }

    // 배선된 앨범 SO일 때만 재조립 — 다른 저작 SO를 만졌다고 앨범을 다시 세우지 않는다.
    // SetSource 전(에디터 임포트 시점)에는 아무것도 하지 않는다 — 시트 로드 전에 빈 앨범을 굳히지 않기 위해서다
    public static void InvalidateIfSource(CardAlbumConfig _config)
    {
        if (IsReady && s_source == _config) SetSource(_config);
    }

    public static int OwnedCountOf(AlbumSection _section)
        => _section != null ? OwnedIn(_section.CardIds) : 0;

    // 완성 판정 분모
    public static int TotalCountOf(AlbumSection _section)
        => _section != null ? _section.CardIds.Count : 0;

    // 카드 0장 구획은 미완성(저작 누락이 완성으로 새지 않게)
    public static bool IsComplete(AlbumSection _section)
    {
        int t_total = TotalCountOf(_section);
        return t_total > 0 && OwnedCountOf(_section) == t_total;
    }

    static void Clear()
    {
        IsReady = false;
        s_themes = System.Array.Empty<AlbumTheme>();
        s_source = null;
    }

    static int OwnedIn(IReadOnlyList<int> _ids)
    {
        int t_owned = 0;
        for (int t_i = 0; t_i < _ids.Count; t_i++)
        {
            if (OwnershipManager.IsOwned(_ids[t_i])) t_owned++;
        }
        return t_owned;
    }
}
