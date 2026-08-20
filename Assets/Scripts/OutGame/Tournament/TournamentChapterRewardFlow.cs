using System.Collections.Generic;

// 챕터 완주 보상의 수령 흐름(정점 보상 TournamentRewardFlow와 같은 순서를 따른다).
//
// 지급은 하지 않는다 — 팝업의 확인이 TournamentProgress.ClaimChapterReward를 부르고 그 안에서
// 자격 판정 · 지급 · 낙인 · 영속이 한 트랜잭션으로 끝난다.
//
// 여는 계기는 전투 복귀가 아니라 맵의 챕터 띠다 — 정점 보상 팝업과 같은 프레임에 겹치지 않고,
// 놓치더라도 띠에 수령 자격이 남아 있어 다음 진입에서 그대로 받을 수 있다.
public static class TournamentChapterRewardFlow
{
    const string TITLE_SUFFIX   = " 완주";
    const string TITLE_FALLBACK = "챕터 완주";

    // 자격 판정용 조회 버퍼 — 판정은 한 프레임 안에서 끝나고 값을 들고 있지 않는다(팝업에 넘기는 목록과 다르다).
    static readonly List<RewardLine> s_probe = new List<RewardLine>();

    /// <summary>수령 자격 = 안정 키 · 완주 · 미수령 · 받을 것이 있음. 띠의 [보상 받기] 표시가 이 판정만 본다.</summary>
    public static bool CanClaim(int _chapterIndex)
    {
        if (!TournamentProgress.TryGetChapter(_chapterIndex, out TournamentChapterDef t_chapter)) return false;
        if (!t_chapter.HasStableKey) return false;
        if (!TournamentProgress.IsChapterComplete(_chapterIndex)) return false;
        if (TournamentProgress.IsChapterRewardClaimed(t_chapter.chapterId)) return false;

        // 보상 미저작 챕터는 받을 것이 없다 — 낙인을 남길 이유도, 눌러도 아무 일 없는 버튼을 띄울 이유도 없다.
        TournamentProgress.FillChapterRewards(_chapterIndex, s_probe);
        return s_probe.Count > 0;
    }

    /// <summary>완주 보상 팝업을 연다. 자격이 없으면 아무 일도 하지 않는다.</summary>
    public static void Open(int _chapterIndex)
    {
        if (!CanClaim(_chapterIndex)) return;
        if (!TournamentProgress.TryGetChapter(_chapterIndex, out TournamentChapterDef t_chapter)) return;

        // 매번 새 리스트 — 팝업이 Show 시점 스냅샷을 들고 있다가 나중에 소비하므로 공용 버퍼를 넘기면 stale이 된다.
        var t_lines = new List<RewardLine>();
        TournamentProgress.FillChapterRewards(_chapterIndex, t_lines);

        // 팝업이 씬에 없으면 보여줄 자리 없이 지급만 한다 — 배선 전에도 루프가 닫히도록(정점 보상과 같은 폴백).
        // 보상 0건은 여기 오지 않는다(CanClaim이 걸렀다).
        if (!RewardClaimPopup.TryGet(out var t_popup))
        {
            TournamentProgress.ClaimChapterReward(t_chapter.chapterId);
            return;
        }

        // 랭크·앨범·정점과 같은 규약 — [획득] 버튼 없이 배경을 눌러 받는다.
        t_popup.Show(TitleOf(t_chapter), t_lines,
            () => TournamentProgress.ClaimChapterReward(t_chapter.chapterId), _claimOnDim: true);
    }

    // 표시명이 비었으면 챕터를 특정하지 않는 문구로 내려간다(빈 제목으로 새지 않게).
    static string TitleOf(TournamentChapterDef _chapter)
        => string.IsNullOrEmpty(_chapter.title) ? TITLE_FALLBACK : _chapter.title + TITLE_SUFFIX;
}
