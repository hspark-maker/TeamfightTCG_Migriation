using System.Collections.Generic;

// 서버가 이미 지급한 경험치와 레벨업 보상을 로비에서 한 번 표시한다. 잔액·소유는 수정하지 않는다.
internal static class AccountRewardHandoff
{
    internal sealed class Entry
    {
        internal List<ClaimRewardGain> Granted;
        internal List<OpenPackCard> Cards;
        internal List<ClaimRewardPack> Packs;
        internal AccountExperienceResult Experience;
    }

    static readonly Queue<Entry> s_pending = new Queue<Entry>();
    internal static bool HasPending => s_pending.Count > 0;

    internal static void Enqueue(List<ClaimRewardGain> _granted, List<OpenPackCard> _cards,
        List<ClaimRewardPack> _packs, AccountExperienceResult _experience)
    {
        if (_experience == null || (_experience.GrantedExp <= 0 && !_experience.IsLevelUp)) return;
        s_pending.Enqueue(new Entry { Granted = _granted, Cards = _cards, Packs = _packs, Experience = _experience });
    }

    internal static Entry Consume() => s_pending.Dequeue();
    internal static void ResetSession() => s_pending.Clear();
}
