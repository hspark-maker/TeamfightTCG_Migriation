using System;
using System.Collections.Generic;
using UnityEngine;

// 보상 토너먼트 진행도의 static 단일 창구(정점 해금 판정 · 클리어 지급 · 챕터 완주 보상 · 낙인)
public static class TournamentProgress
{
    static TournamentConfig s_config;

    // 진행 통지 — 맵이 정점 상태를 다시 그리는 트리거
    public static event Action OnChanged;

    // 전체 정점 수
    public static int NodeCount => Config.NodeCount;

    // 챕터 수
    public static int ChapterCount => Config.ChapterCount;

    // 지금 도전할 수 있는 정점(없으면 -1). 맵의 자동 스크롤·강조가 공통으로 쓰는 단일 기준.
    public static int CurrentNodeIndex
    {
        get
        {
            int t_count = NodeCount;
            for (int t_i = 0; t_i < t_count; t_i++)
                if (StateOf(t_i) == ETournamentNodeState.Playable) return t_i;

            return -1;
        }
    }

    // 지금 진행 중인 챕터(첫 미완주 챕터, 전부 완주면 마지막 · 챕터가 없으면 -1)
    public static int CurrentChapterIndex
    {
        get
        {
            int t_count = ChapterCount;
            for (int t_i = 0; t_i < t_count; t_i++)
                if (!IsChapterComplete(t_i)) return t_i;

            return t_count - 1;
        }
    }

    public static bool HasAnyPlayable => CurrentNodeIndex >= 0;

    static TournamentConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<TournamentConfig>());

    // 세이브 슬롯 직독 — 캐시를 두면 부트를 안 거친 씬에서 빈 낙인이 기존 기록을 덮어쓴다
    static TournamentSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.tournament == null) t_data.tournament = new TournamentSaveData();
            return t_data.tournament;
        }
    }

    // JsonUtility는 기본 생성자를 태워 이 목록을 빈 리스트로 채운다 — 수동 편집·다른 역직렬화 경로만 대비한 보정이다
    static List<string> ClaimedChapters
    {
        get
        {
            TournamentSaveData t_slot = Slot;
            if (t_slot.claimedChapterIds == null) t_slot.claimedChapterIds = new List<string>();
            return t_slot.claimedChapterIds;
        }
    }

    // 부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지
    public static void SetConfig(TournamentConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    public static bool TryGetNode(int _index, out TournamentNodeDef _node) => Config.TryGetNode(_index, out _node);

    public static int IndexOf(string _nodeId) => Config.IndexOf(_nodeId);

    public static bool TryGetChapter(int _chapterIndex, out TournamentChapterDef _chapter)
        => Config.TryGetChapter(_chapterIndex, out _chapter);

    // 정점 상태(3종 배타). 클리어 검사가 해금 검사보다 먼저다 — 앞 정점 키를 고쳐 사슬이 끊겨도 기클리어는 유지된다
    public static ETournamentNodeState StateOf(int _index)
    {
        if (!Config.TryGetNode(_index, out TournamentNodeDef t_node) || !t_node.HasStableKey)
            return ETournamentNodeState.Locked;

        if (Slot.clearedNodeIds.Contains(t_node.nodeId)) return ETournamentNodeState.Cleared;
        if (_index == 0) return ETournamentNodeState.Playable;

        if (Config.TryGetNode(_index - 1, out TournamentNodeDef t_prev)
            && t_prev.HasStableKey
            && Slot.clearedNodeIds.Contains(t_prev.nodeId))
            return ETournamentNodeState.Playable;

        return ETournamentNodeState.Locked;
    }

    // 진입 자격 — 클리어한 정점도 다시 도전할 수 있다(재도전 승리는 ClearNode가 중복으로 걸러 보상이 없다)
    public static bool CanEnter(int _index) => StateOf(_index) != ETournamentNodeState.Locked;

    public static bool IsCleared(string _nodeId)
        => !string.IsNullOrEmpty(_nodeId) && Slot.clearedNodeIds.Contains(_nodeId);

    // 정점 클리어 확정 — 보상 지급까지 여기서 한다(수령 팝업의 onConfirm이 이 메서드를 부른다)
    public static bool ClearNode(string _nodeId)
    {
        if (string.IsNullOrEmpty(_nodeId)) return false;
        if (Slot.clearedNodeIds.Contains(_nodeId)) return false;

        Payout(_nodeId);
        return true;
    }

    // 챕터의 모든 정점이 Cleared인가. 정점 0개 챕터는 완주로 통과시킨다 — 저작 실수로 진행이 영영 막히지 않게
    // (검증기가 Error로 잡는 몫이다).
    public static bool IsChapterComplete(int _chapterIndex)
    {
        if (!Config.TryGetNodeRange(_chapterIndex, out int t_start, out int t_count)) return false;
        if (t_count <= 0) return true;

        for (int t_i = 0; t_i < t_count; t_i++)
            if (StateOf(t_start + t_i) != ETournamentNodeState.Cleared) return false;

        return true;
    }

    // 챕터 진행 눈금(클리어 수 / 정점 수). 띠가 "3 / 6"을 그리는 단일 기준이라 세는 자리를 화면에 두지 않는다.
    public static bool TryGetChapterProgress(int _chapterIndex, out int _cleared, out int _total)
    {
        _cleared = 0;
        _total = 0;

        if (!Config.TryGetNodeRange(_chapterIndex, out int t_start, out int t_count)) return false;

        _total = t_count;
        for (int t_i = 0; t_i < t_count; t_i++)
            if (StateOf(t_start + t_i) == ETournamentNodeState.Cleared) _cleared++;

        return true;
    }

    public static bool IsChapterRewardClaimed(string _chapterId)
        => !string.IsNullOrEmpty(_chapterId) && ClaimedChapters.Contains(_chapterId);

    // 챕터 완주 보상 수령(자격 = 완주 && 미수령). 지급·낙인·영속·통지가 한 트랜잭션이다
    public static bool ClaimChapterReward(string _chapterId)
    {
        int t_index = Config.ChapterIndexOf(_chapterId);
        if (t_index < 0) return false;
        if (!IsChapterComplete(t_index)) return false;
        if (ClaimedChapters.Contains(_chapterId)) return false;

        PayoutChapter(_chapterId);
        return true;
    }

    // 정점 _index의 보상 스냅샷(범위 밖·미저작이면 빈 목록)
    public static void FillRewards(int _index, List<RewardLine> _sink)
    {
        Config.TryGetNode(_index, out TournamentNodeDef t_node);
        Config.FillRewards(t_node.nodeId, _sink);
    }

    // 챕터 _chapterIndex의 완주 보상 스냅샷(범위 밖·미저작이면 빈 목록)
    public static void FillChapterRewards(int _chapterIndex, List<RewardLine> _sink)
    {
        Config.TryGetChapter(_chapterIndex, out TournamentChapterDef t_chapter);
        Config.FillChapterRewards(t_chapter.chapterId, _sink);
    }

    // 클리어·수령 낙인만 지운다(디버그 전용, 지급된 재화는 회수하지 않는다)
    public static void ResetForDebug()
    {
        Slot.clearedNodeIds.Clear();
        ClaimedChapters.Clear();
        DataSaveManager.Save();
        OnChanged?.Invoke();
    }

    // 보상 지급(리스트 전량) → 낙인 → 즉시 영속 → 통지
    static void Payout(string _nodeId)
    {
        var t_rewards = new List<RewardLine>();
        Config.FillRewards(_nodeId, t_rewards);

        for (int t_i = 0; t_i < t_rewards.Count; t_i++)
            CurrencyManager.Earn(t_rewards[t_i].Gain.Type, t_rewards[t_i].Gain.Amount);

        Slot.clearedNodeIds.Add(_nodeId);

        // CurrencyManager.Save()가 재화 flush 후 DataSaveManager.Save()까지 부른다(순서 뒤집으면 재화 미반영 상태가 기록된다)
        CurrencyManager.Save();
        OnChanged?.Invoke();
    }

    // 챕터 완주 보상 지급 → 낙인 → 즉시 영속 → 통지(정점 Payout과 같은 순서)
    static void PayoutChapter(string _chapterId)
    {
        var t_rewards = new List<RewardLine>();
        Config.FillChapterRewards(_chapterId, t_rewards);

        for (int t_i = 0; t_i < t_rewards.Count; t_i++)
            CurrencyManager.Earn(t_rewards[t_i].Gain.Type, t_rewards[t_i].Gain.Amount);

        ClaimedChapters.Add(_chapterId);

        CurrencyManager.Save();
        OnChanged?.Invoke();
    }
}

// 정점 상태(3종 배타)
public enum ETournamentNodeState
{
    Locked,
    Playable,
    Cleared,
}
