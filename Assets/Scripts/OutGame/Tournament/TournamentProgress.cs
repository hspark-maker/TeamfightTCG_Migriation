using System;
using System.Collections.Generic;
using UnityEngine;

// 보상 토너먼트 진행도의 static 단일 창구(정점 해금 판정 · 클리어 지급 · 챕터 완주 보상 · 낙인)
public static class TournamentProgress
{
    static TournamentConfig s_config;

    // 챕터 수령 자격 판정용 조회 버퍼 — 판정은 한 프레임 안에서 끝나고 값을 들고 있지 않는다.
    static readonly List<RewardLine> s_chapterProbe = new List<RewardLine>();

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

    // 맵이 처음 보여줘야 할 정점 — 받을 선물이 있으면 그쪽이 먼저다(없으면 도전할 정점)
    public static int FocusNodeIndex
    {
        get
        {
            string t_pending = PendingRewardNodeId;
            if (string.IsNullOrEmpty(t_pending)) return CurrentNodeIndex;

            int t_index = IndexOf(t_pending);
            return t_index >= 0 ? t_index : CurrentNodeIndex;
        }
    }

    // 깼지만 아직 보상을 받지 않은 정점(없으면 빈 문자열)
    public static string PendingRewardNodeId => Slot.pendingRewardNodeId ?? string.Empty;

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

    // 지금 받을 수 있는 것이 있는가(미수령 정점 · 미수령 완주 챕터). 싼 조건부터 본다 — 챕터 훑기가 가장 비싸다.
    public static bool HasAnyClaimable
    {
        get
        {
            if (!string.IsNullOrEmpty(PendingRewardNodeId)) return true;

            int t_count = ChapterCount;
            for (int t_i = 0; t_i < t_count; t_i++)
                if (CanClaimChapterReward(t_i)) return true;

            return false;
        }
    }

    // 모험에 유저를 부를 이유가 있는가 — 받을 것이 있거나, 지금 들어갈 수 있는 정점이 있거나.
    // CanEnter를 곱해야 한다: StateOf는 랭크 잠금을 보지 않아 도전 정점 유무만으로는 못 들어갈 곳까지 참이 된다.
    public static bool HasAnyWaiting
    {
        get
        {
            if (HasAnyClaimable) return true;

            int t_index = CurrentNodeIndex;   // 전수 스캔이라 한 번만 구한다
            return t_index >= 0 && CanEnter(t_index);
        }
    }

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

    // 챕터가 랭크 미달로 통째로 잠겼는가. 진행 낙인과 무관한 파생값이라 정점 상태(StateOf)와 축이 다르다 —
    // 포인트가 오르면 저작을 건드리지 않아도 저절로 풀린다.
    public static bool IsChapterRankLocked(int _chapterIndex)
    {
        if (!Config.TryGetChapter(_chapterIndex, out TournamentChapterDef t_chapter)) return false;

        return RankManager.CurrentGrade < t_chapter.requiredGrade;
    }

    // 정점이 속한 챕터가 랭크로 잠겼는가(정점 뷰가 챕터를 다시 세지 않게 하는 창구)
    public static bool IsRankLocked(int _index)
        => IsChapterRankLocked(Config.ChapterIndexOfNode(_index));

    // 챕터를 여는 데 필요한 등급(범위 밖이면 false). 챕터 띠가 잠김 문구를 그리는 재료다.
    public static bool TryGetRequiredGrade(int _chapterIndex, out ERankGrade _grade)
    {
        bool t_found = Config.TryGetChapter(_chapterIndex, out TournamentChapterDef t_chapter);
        _grade = t_found ? t_chapter.requiredGrade : default;
        return t_found;
    }

    // 정점 상태(4종 배타). 클리어 검사가 해금 검사보다 먼저다 — 앞 정점 키를 고쳐 사슬이 끊겨도 기클리어는 유지된다
    public static ETournamentNodeState StateOf(int _index)
    {
        if (!Config.TryGetNode(_index, out TournamentNodeDef t_node) || !t_node.HasStableKey)
            return ETournamentNodeState.Locked;

        if (Slot.clearedNodeIds.Contains(t_node.nodeId)) return ETournamentNodeState.Cleared;

        // 미수령은 클리어가 아니다 — 다음 정점 해금도 링크 점등도 수령(ClearNode)이 열쇠다
        if (t_node.nodeId == PendingRewardNodeId) return ETournamentNodeState.RewardPending;

        if (_index == 0) return ETournamentNodeState.Playable;

        if (Config.TryGetNode(_index - 1, out TournamentNodeDef t_prev)
            && t_prev.HasStableKey
            && Slot.clearedNodeIds.Contains(t_prev.nodeId))
            return ETournamentNodeState.Playable;

        return ETournamentNodeState.Locked;
    }

    // 진입 자격 — 클리어한 정점도 다시 도전할 수 있다(재도전 승리는 ClearNode가 중복으로 걸러 보상이 없다).
    // 미수령 정점은 진입이 아니라 수령이 남은 자리라 제외한다.
    // 랭크 잠금을 여기서 곱한다 — 진입 게이트가 맵과 로비 둘로 갈려 있어 상태 판정에 섞는 것보다 여기가 단일 지점이다.
    public static bool CanEnter(int _index)
    {
        if (IsRankLocked(_index)) return false;

        ETournamentNodeState t_state = StateOf(_index);
        return t_state == ETournamentNodeState.Playable || t_state == ETournamentNodeState.Cleared;
    }

    public static bool IsCleared(string _nodeId)
        => !string.IsNullOrEmpty(_nodeId) && Slot.clearedNodeIds.Contains(_nodeId);

    public static bool IsRewardPending(int _index)
        => StateOf(_index) == ETournamentNodeState.RewardPending;

    // 승리 낙인 — 지급은 하지 않는다. 수령(ClearNode)이 지급·해금·낙인 해제를 마저 한다.
    // 전투 씬에서 불린다: 로비까지 미루면 로딩 중 종료가 승리를 삼킨다.
    public static bool MarkRewardPending(string _nodeId)
    {
        if (string.IsNullOrEmpty(_nodeId)) return false;
        if (IsCleared(_nodeId)) return false;
        if (Slot.pendingRewardNodeId == _nodeId) return false;

        Slot.pendingRewardNodeId = _nodeId;

        SaveTransaction.Request();
        OnChanged?.Invoke();
        return true;
    }

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

    /// <summary>완주 보상 수령 자격 = 안정 키 · 완주 · 미수령 · 받을 것이 있음. 띠의 [보상 받기] 표시와 알림 점이 이 판정만 본다.</summary>
    public static bool CanClaimChapterReward(int _chapterIndex)
    {
        if (!TryGetChapter(_chapterIndex, out TournamentChapterDef t_chapter)) return false;
        if (!t_chapter.HasStableKey) return false;
        if (!IsChapterComplete(_chapterIndex)) return false;
        if (IsChapterRewardClaimed(t_chapter.chapterId)) return false;

        // 보상 미저작 챕터는 받을 것이 없다 — 낙인을 남길 이유도, 눌러도 아무 일 없는 버튼을 띄울 이유도 없다.
        FillChapterRewards(_chapterIndex, s_chapterProbe);
        return s_chapterProbe.Count > 0;
    }

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
        Slot.pendingRewardNodeId = "";
        SaveTransaction.Request();
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

        // 미수령 낙인 해제도 같은 트랜잭션이다 — 따로 떼면 지급됐는데 선물이 남는 상태가 저장될 수 있다
        if (Slot.pendingRewardNodeId == _nodeId) Slot.pendingRewardNodeId = "";

        SaveTransaction.Request();
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

        SaveTransaction.Request();
        OnChanged?.Invoke();
    }
}

// 정점 상태(4종 배타)
public enum ETournamentNodeState
{
    Locked,
    Playable,
    RewardPending,   // 깼지만 보상을 아직 안 받았다 — 진입이 아니라 수령이 남은 자리
    Cleared,
}
