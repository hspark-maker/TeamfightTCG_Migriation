using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
    public static string PendingRewardNodeId => Slot.PendingRewardNodeId ?? string.Empty;

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

    // 세이브 슬롯 직독 — 캐시를 두면 초기화를 안 거친 씬에서 빈 낙인이 기존 기록을 덮어쓴다
    static TournamentSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.Tournament == null) t_data.Tournament = new TournamentSaveData();
            return t_data.Tournament;
        }
    }

    // 역직렬화가 null을 남긴 경우에만 도는 보정이다(수동 편집·부분 문서 대비)
    static List<string> ClaimedChapters
    {
        get
        {
            TournamentSaveData t_slot = Slot;
            if (t_slot.ClaimedChapterIds == null) t_slot.ClaimedChapterIds = new List<string>();
            return t_slot.ClaimedChapterIds;
        }
    }

    // 초기화에서 실제 애셋 주입(선택). null이면 기본 유지
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
    // 표시용 낙관 판정이다 — 해금의 진실원은 서버 reportTournamentWin 이고 여기와 엇갈리면 서버가 이긴다
    // (CardPackOpener.Precheck 와 같은 성격). 이 값으로 낙인을 만들지 않는다.
    public static ETournamentNodeState StateOf(int _index)
    {
        if (!Config.TryGetNode(_index, out TournamentNodeDef t_node) || !t_node.HasStableKey)
            return ETournamentNodeState.Locked;

        if (Slot.ClearedNodeIds.Contains(t_node.nodeId)) return ETournamentNodeState.Cleared;

        // 미수령은 클리어가 아니다 — 다음 정점 해금도 링크 점등도 수령(ClearNode)이 열쇠다
        if (t_node.nodeId == PendingRewardNodeId) return ETournamentNodeState.RewardPending;

        if (_index == 0) return ETournamentNodeState.Playable;

        if (Config.TryGetNode(_index - 1, out TournamentNodeDef t_prev)
            && t_prev.HasStableKey
            && Slot.ClearedNodeIds.Contains(t_prev.nodeId))
            return ETournamentNodeState.Playable;

        return ETournamentNodeState.Locked;
    }

    // 진입 자격 — 클리어한 정점도 다시 도전할 수 있다(재도전 승리는 ClearNode가 중복으로 걸러 보상이 없다).
    // 미수령 정점은 진입이 아니라 수령이 남은 자리라 제외한다.
    // 랭크 잠금을 여기서 곱한다 — 진입 게이트가 맵과 로비 둘로 갈려 있어 상태 판정에 섞는 것보다 여기가 단일 지점이다.
    // 진입을 여기서 막는 것은 헛걸음을 줄이려는 것뿐이다 — 뚫고 들어가 이겨도 서버가 신고를 거절한다.
    public static bool CanEnter(int _index)
    {
        if (IsRankLocked(_index)) return false;

        ETournamentNodeState t_state = StateOf(_index);
        return t_state == ETournamentNodeState.Playable || t_state == ETournamentNodeState.Cleared;
    }

    public static bool IsCleared(string _nodeId)
        => !string.IsNullOrEmpty(_nodeId) && Slot.ClearedNodeIds.Contains(_nodeId);

    public static bool IsRewardPending(int _index)
        => StateOf(_index) == ETournamentNodeState.RewardPending;

    // 서버가 낙인을 갈아끼운 뒤 화면에 알린다. 값을 다시 만들지 않는다 — Slot 이 세이브 직독이라
    // 채택이 끝난 시점에 이미 새 값이다(ServerSlotRehydrator 의 다른 슬롯들과 다른 이유).
    internal static void NotifyRehydrated()
    {
        OnChanged?.Invoke();
    }

    /// <summary>정점 클리어 확정 — 보상 지급까지 서버에 맡긴다(수령 팝업의 onConfirm이 이 메서드를 부른다).
    /// 이 도메인은 "수령 = 클리어 확정"이라 지급·클리어 낙인·미수령 해제가 한 트랜잭션이어야 한다.</summary>
    public static async UniTask<RewardClaimOutcome> ClearNodeAsync(string _nodeId)
    {
        if (string.IsNullOrEmpty(_nodeId)) return default;
        if (Slot.ClearedNodeIds.Contains(_nodeId)) return default;

        // 첫 await 이전에 걸어야 한다 — 뒤로 밀리면 팝업의 숫자 롤업이 옛 잔액을 목표로 잡아 역주행한다.
        var t_rewards = new List<RewardLine>();
        Config.FillRewards(_nodeId, t_rewards);
        var t_pending = CurrencyPendingTicket.Hold(t_rewards);

        // 보상 미저작 정점도 서버를 거친다 — 클라가 "받을 게 없다"고 판정해 스스로 낙인을 남기면
        // 변조된 클라가 정점을 마음대로 열 수 있다. 서버가 지급 0건이어도 클리어를 확정해 준다.
        var t_outcome = await RewardClaimCommand.ClaimAsync(RewardClaimCommand.OwnerTournament, _nodeId, t_pending);
        if (!t_outcome.Succeeded) return default;

        OnChanged?.Invoke();
        return t_outcome;
    }

    // 챕터의 모든 정점이 Cleared인가. 정점 0개 챕터는 완주로 통과시킨다 — 저작 실수로 진행이 영영 막히지 않게
    // (검증기가 Error로 잡는 몫이다).
    // 표시용 낙관 판정 — 완주 자격의 진실원은 서버 claimReward(TournamentChapter 표 모수)다.
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

    /// <summary>완주 보상을 이미 받았는지. 서버 낙인이 아직 서지 않은 왕복 구간도 받은 것으로 답한다 —
    /// 그래야 띠 버튼과 알림 점이 누른 프레임에 꺼진다.</summary>
    public static bool IsChapterRewardClaimed(string _chapterId)
        => !string.IsNullOrEmpty(_chapterId)
           && (ClaimedChapters.Contains(_chapterId)
               || RewardClaimCommand.IsInFlight(RewardClaimCommand.OwnerTournament, _chapterId));

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

    /// <summary>챕터 완주 보상 수령 — 자격 판정 · 지급 · 낙인을 서버가 한 트랜잭션으로 끝낸다.
    /// 서버가 준 목록째로 돌려준다(팝업이 이 값으로 연출을 정한다).</summary>
    // 앞의 세 검사는 왕복을 아끼는 낙관 검사다 — 정점 수령과 같이 이기는 쪽은 언제나 서버다.
    public static async UniTask<RewardClaimOutcome> ClaimChapterRewardAsync(string _chapterId)
    {
        int t_index = Config.ChapterIndexOf(_chapterId);
        if (t_index < 0) return default;
        if (!IsChapterComplete(t_index)) return default;
        if (ClaimedChapters.Contains(_chapterId)) return default;

        // 첫 await 이전에 걸어야 한다 — 뒤로 밀리면 팝업의 숫자 롤업이 옛 잔액을 목표로 잡아 역주행한다.
        var t_rewards = new List<RewardLine>();
        Config.FillChapterRewards(_chapterId, t_rewards);
        var t_pending = CurrencyPendingTicket.Hold(t_rewards);

        // 챕터 id 를 그대로 ownerId 로 보낸다 — 서버가 chapter_ 접두사를 보고 정점과 가른다.
        // 통지는 창구가 왕복 시작·종료에 한 번씩 울려 준다 — 시작 통지가 띠를 즉시 수령 완료로 그리고,
        // 종료 통지가 성공이면 서버 낙인으로 확정하고 거절이면 원래 상태로 되돌린다.
        var t_outcome = await RewardClaimCommand.ClaimAsync(RewardClaimCommand.OwnerTournament, _chapterId,
                                                           t_pending, () => OnChanged?.Invoke());

        return t_outcome.Succeeded ? t_outcome : default;
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
        Slot.ClearedNodeIds.Clear();
        ClaimedChapters.Clear();
        Slot.PendingRewardNodeId = "";
        DataSaveManager.Save();
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
