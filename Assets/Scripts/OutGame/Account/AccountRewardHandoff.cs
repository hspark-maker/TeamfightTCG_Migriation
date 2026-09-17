using System.Collections.Generic;

// 전투에서 예상 경험치를 이미 표시했다면 서버 응답은 레벨업 아이템만 로비에 넘긴다. 잔액·소유는 수정하지 않는다.
internal static class AccountRewardHandoff
{
    internal sealed class Entry
    {
        internal string MatchId;
        internal List<ClaimRewardGain> Granted;
        internal List<OpenPackCard> Cards;
        internal List<ClaimRewardPack> Packs;
        internal AccountExperienceResult Experience;
    }

    static readonly List<Entry> s_pending = new List<Entry>();
    static readonly HashSet<string> s_experienceShown = new HashSet<string>();
    internal static bool HasPending => s_pending.Count > 0;

    internal static void Enqueue(string _matchId, List<ClaimRewardGain> _granted, List<OpenPackCard> _cards,
        List<ClaimRewardPack> _packs, AccountExperienceResult _experience)
    {
        if (_experience == null || (_experience.GrantedExp <= 0 && !_experience.IsLevelUp)) return;
        s_pending.Add(new Entry { MatchId = _matchId, Granted = _granted, Cards = _cards, Packs = _packs, Experience = _experience });
        if (s_experienceShown.Remove(_matchId)) ConsumeExperience(_matchId);
    }

    internal static void MarkExperienceShown(string _matchId)
    {
        if (string.IsNullOrEmpty(_matchId)) return;
        // 응답이 먼저 도착한 경우와 결과 화면 뒤에 도착한 경우를 모두 처리한다.
        if (ConsumeExperience(_matchId) == null) s_experienceShown.Add(_matchId);
    }

    static AccountExperienceResult ConsumeExperience(string _matchId)
    {
        if (string.IsNullOrEmpty(_matchId)) return null;
        int t_index = s_pending.FindIndex(_entry => _entry.MatchId == _matchId && _entry.Experience.GrantedExp > 0);
        if (t_index < 0) return null;
        Entry t_entry = s_pending[t_index];
        AccountExperienceResult t_experience = t_entry.Experience;
        if ((t_entry.Granted?.Count ?? 0) > 0 || (t_entry.Cards?.Count ?? 0) > 0 || (t_entry.Packs?.Count ?? 0) > 0)
        {
            // 로비에는 아이템과 레벨업 제목만 남겨 경험치 게이지를 두 번 재생하지 않는다.
            t_entry.Experience = new AccountExperienceResult
            {
                PreviousLevel = t_experience.PreviousLevel,
                Level = t_experience.Level,
                TotalExp = t_experience.TotalExp,
            };
        }
        else s_pending.RemoveAt(t_index);
        return t_experience;
    }

    internal static Entry Consume()
    {
        Entry t_entry = s_pending[0];
        s_pending.RemoveAt(0);
        return t_entry;
    }
    internal static void ResetSession()
    {
        s_pending.Clear();
        s_experienceShown.Clear();
    }
}
