using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 모험 진행도의 static 단일 창구(정점 해금 판정 · 클리어 지급 · 챕터 완주 보상 · 낙인)
public static class AdventureProgress
{
    static AdventureConfig s_config;

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
                if (StateOf(t_i) == EAdventureNodeState.Playable) return t_i;

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

    static AdventureConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<AdventureConfig>());

    // 세이브 슬롯 직독 — 캐시를 두면 초기화를 안 거친 씬에서 빈 낙인이 기존 기록을 덮어쓴다
    static AdventureSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.Adventure == null) t_data.Adventure = new AdventureSaveData();
            return t_data.Adventure;
        }
    }

    // 역직렬화가 null을 남긴 경우에만 도는 보정이다(수동 편집·부분 문서 대비)
    static List<string> ClaimedChapters
    {
        get
        {
            AdventureSaveData t_slot = Slot;
            if (t_slot.ClaimedChapterIds == null) t_slot.ClaimedChapterIds = new List<string>();
            return t_slot.ClaimedChapterIds;
        }
    }


    // 초기화에서 실제 애셋 주입(선택). null이면 기본 유지
    public static void SetConfig(AdventureConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    public static bool TryGetNode(int _index, out AdventureNodeDef _node) => Config.TryGetNode(_index, out _node);

    public static int IndexOf(string _nodeId) => Config.IndexOf(_nodeId);

    public static bool TryGetChapter(int _chapterIndex, out AdventureChapterDef _chapter)
        => Config.TryGetChapter(_chapterIndex, out _chapter);

    // 챕터가 랭크 미달로 통째로 잠겼는가. 진행 낙인과 무관한 파생값이라 정점 상태(StateOf)와 축이 다르다 —
    // 포인트가 오르면 저작을 건드리지 않아도 저절로 풀린다.
    public static bool IsChapterRankLocked(int _chapterIndex)
    {
        if (!Config.TryGetChapter(_chapterIndex, out AdventureChapterDef t_chapter)) return false;

        return RankManager.CurrentGrade < t_chapter.requiredGrade;
    }

    // 정점이 속한 챕터가 랭크로 잠겼는가(정점 뷰가 챕터를 다시 세지 않게 하는 창구)
    public static bool IsRankLocked(int _index)
        => IsChapterRankLocked(Config.ChapterIndexOfNode(_index));

    // 챕터를 여는 데 필요한 등급(범위 밖이면 false). 챕터 띠가 잠김 문구를 그리는 재료다.
    public static bool TryGetRequiredGrade(int _chapterIndex, out ERankGrade _grade)
    {
        bool t_found = Config.TryGetChapter(_chapterIndex, out AdventureChapterDef t_chapter);
        _grade = t_found ? t_chapter.requiredGrade : default;
        return t_found;
    }

    // 정점 상태(4종 배타). 클리어 검사가 해금 검사보다 먼저다 — 앞 정점 키를 고쳐 사슬이 끊겨도 기클리어는 유지된다
    // 표시용 낙관 판정이다 — 해금의 진실원은 서버 reportAdventureWin 이고 여기와 엇갈리면 서버가 이긴다
    // (CardPackOpener.Precheck 와 같은 성격). 이 값으로 낙인을 만들지 않는다.
    public static EAdventureNodeState StateOf(int _index)
    {
        if (!Config.TryGetNode(_index, out AdventureNodeDef t_node) || !t_node.HasStableKey)
            return EAdventureNodeState.Locked;

        if (Slot.ClearedNodeIds.Contains(t_node.nodeId)) return EAdventureNodeState.Cleared;

        // 미수령은 클리어가 아니다 — 다음 정점 해금도 링크 점등도 수령(ClearNode)이 열쇠다
        if (t_node.nodeId == PendingRewardNodeId) return EAdventureNodeState.RewardPending;

        if (_index == 0) return EAdventureNodeState.Playable;

        if (Config.TryGetNode(_index - 1, out AdventureNodeDef t_prev)
            && t_prev.HasStableKey
            && Slot.ClearedNodeIds.Contains(t_prev.nodeId))
            return EAdventureNodeState.Playable;

        return EAdventureNodeState.Locked;
    }

    /// <summary>정점 뷰가 그릴 상태. 수령 왕복이 도는 동안 그 정점만 미리 Cleared 로 답한다 —
    /// 팝업은 응답을 기다리지 않고 1초 안에 닫히므로, 낙관 표시가 없으면 연출이 끝난 뒤에도
    /// 정점이 미수령으로 남아 있다가 응답이 오는 순간 툭 바뀐다(앨범·랭크와 같은 이유).
    ///
    /// <b>표시 전용이다.</b> 해금 사슬(<see cref="StateOf"/> 가 직전 정점을 되짚는 자리) · 진입 자격
    /// (<see cref="CanEnter"/>) · 챕터 완주 판정은 이 값을 쓰지 않는다 —
    /// 정점의 Cleared 는 자기 표시로 끝나지 않고 <b>다음 칸의 자격</b>이라, 낙관을 그쪽까지 태우면
    /// 확정되지 않은 클리어가 다음 정점을 미리 열고 해금 연출 표식이 세이브에까지 남는다.
    /// 그래서 낙관은 여기서 멈추고, 길 점등과 다음 정점 해금은 서버가 확정한 뒤에 따라온다.</summary>
    public static EAdventureNodeState DisplayStateOf(int _index)
    {
        EAdventureNodeState t_state = StateOf(_index);
        if (t_state != EAdventureNodeState.RewardPending) return t_state;

        return IsNodeClaiming(_index) ? EAdventureNodeState.Cleared : t_state;
    }

    /// <summary>이 정점의 수령 왕복이 도는 중인가. 낙관 표시의 유일한 근거이며 세이브에는 남지 않는다.</summary>
    public static bool IsNodeClaiming(int _index)
    {
        if (!RewardClaimCommand.HasAnyInFlight) return false;

        return Config.TryGetNode(_index, out AdventureNodeDef t_node)
               && RewardClaimCommand.IsInFlight(RewardClaimCommand.OwnerAdventure, t_node.nodeId);
    }

    // 진입 자격 — 아직 깨지 않은 정점만 도전할 수 있다. 클리어한 정점은 재진입을 막는다:
    // 재도전 승리는 서버가 중복 신고를 거절해 보상이 없어, 유저에게 남는 것이 헛걸음뿐이다.
    // 미수령 정점도 제외한다 — 진입이 아니라 수령이 남은 자리다.
    // 랭크 잠금을 여기서 곱한다 — 진입 게이트가 맵과 로비 둘로 갈려 있어 상태 판정에 섞는 것보다 여기가 단일 지점이다.
    public static bool CanEnter(int _index)
    {
        if (IsRankLocked(_index)) return false;

        return StateOf(_index) == EAdventureNodeState.Playable;
    }

    public static bool IsCleared(string _nodeId)
        => !string.IsNullOrEmpty(_nodeId) && Slot.ClearedNodeIds.Contains(_nodeId);

    public static bool IsRewardPending(int _index)
        => StateOf(_index) == EAdventureNodeState.RewardPending;

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
        //
        // 통지는 창구가 왕복 시작·종료에 한 번씩 울려 준다(앨범·랭크와 같은 배선) — 시작 통지가 도장을 찍고,
        // 종료 통지가 성공이면 서버 낙인으로 확정하고 거절이면 되돌린다. 낙관이 닿는 것은 표시뿐이라
        // 다음 정점의 해금은 이 왕복이 끝난 뒤에야 열린다(DisplayStateOf 주석 참고).
        var t_outcome = await RewardClaimCommand.ClaimAsync(RewardClaimCommand.OwnerAdventure, _nodeId,
                                                            t_pending, () => OnChanged?.Invoke());

        return t_outcome.Succeeded ? t_outcome : default;
    }

    // 챕터의 모든 정점이 Cleared인가. 정점 0개 챕터는 완주로 통과시킨다 — 저작 실수로 진행이 영영 막히지 않게
    // (검증기가 Error로 잡는 몫이다).
    // 표시용 낙관 판정 — 완주 자격의 진실원은 서버 claimReward(AdventureChapter 표 모수)다.
    public static bool IsChapterComplete(int _chapterIndex)
    {
        if (!Config.TryGetNodeRange(_chapterIndex, out int t_start, out int t_count)) return false;
        if (t_count <= 0) return true;

        for (int t_i = 0; t_i < t_count; t_i++)
            if (StateOf(t_start + t_i) != EAdventureNodeState.Cleared) return false;

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
            if (StateOf(t_start + t_i) == EAdventureNodeState.Cleared) _cleared++;

        return true;
    }

    /// <summary>완주 보상을 이미 받았는지. 서버 낙인이 아직 서지 않은 왕복 구간도 받은 것으로 답한다 —
    /// 그래야 띠 버튼과 알림 점이 누른 프레임에 꺼진다.</summary>
    public static bool IsChapterRewardClaimed(string _chapterId)
        => !string.IsNullOrEmpty(_chapterId)
           && (ClaimedChapters.Contains(_chapterId)
               || RewardClaimCommand.IsInFlight(RewardClaimCommand.OwnerAdventure, _chapterId));

    /// <summary>완주 보상 수령 자격 = 안정 키 · 완주 · 미수령 · 받을 것이 있음. 띠의 [보상 받기] 표시와 알림 점이 이 판정만 본다.</summary>
    public static bool CanClaimChapterReward(int _chapterIndex)
    {
        if (!TryGetChapter(_chapterIndex, out AdventureChapterDef t_chapter)) return false;
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
        var t_outcome = await RewardClaimCommand.ClaimAsync(RewardClaimCommand.OwnerAdventure, _chapterId,
                                                           t_pending, () => OnChanged?.Invoke());

        return t_outcome.Succeeded ? t_outcome : default;
    }

    // 정점 _index의 보상 스냅샷(범위 밖·미저작이면 빈 목록)
    public static void FillRewards(int _index, List<RewardLine> _sink)
    {
        Config.TryGetNode(_index, out AdventureNodeDef t_node);
        Config.FillRewards(t_node.nodeId, _sink);
    }

    // 챕터 _chapterIndex의 완주 보상 스냅샷(범위 밖·미저작이면 빈 목록)
    public static void FillChapterRewards(int _chapterIndex, List<RewardLine> _sink)
    {
        Config.TryGetChapter(_chapterIndex, out AdventureChapterDef t_chapter);
        Config.FillChapterRewards(t_chapter.chapterId, _sink);
    }

    /// <summary>아직 연출을 보여주지 않은 해금을 모아 낸다(차분만 낸다 — 확정은 MarkUnlockSeen이 한다).</summary>
    // 랭크 승급으로 열린 챕터도 여기서 잡힌다 — 판정이 IsChapterRankLocked 직독이라 통지가 없어도 다음 진입에서 드러난다
    public static bool TryTakeUnlockShowcase(out AdventureUnlockShowcase _showcase)
    {
        var t_chapters = new List<int>();
        var t_nodes = new List<int>();

        int t_chapterCount = ChapterCount;
        for (int t_i = 0; t_i < t_chapterCount; t_i++)
        {
            if (IsChapterRankLocked(t_i)) continue;
            if (!TryGetChapter(t_i, out AdventureChapterDef t_chapter) || !t_chapter.HasStableKey) continue;
            if (AdventureUnlockSeenStore.Contains(t_chapter.chapterId)) continue;

            t_chapters.Add(t_i);
        }

        int t_nodeCount = NodeCount;
        for (int t_i = 0; t_i < t_nodeCount; t_i++)
        {
            if (!IsNodeUnlocked(t_i)) continue;
            if (!TryGetNode(t_i, out AdventureNodeDef t_node) || !t_node.HasStableKey) continue;
            if (AdventureUnlockSeenStore.Contains(t_node.nodeId)) continue;

            t_nodes.Add(t_i);
        }

        _showcase = new AdventureUnlockShowcase(t_chapters, t_nodes);
        return _showcase.HasAny;
    }

    /// <summary>연출을 보여준 챕터·정점을 한 번에 표식한다(저장은 한 번만 튄다).</summary>
    public static void MarkUnlockSeen(in AdventureUnlockShowcase _showcase)
    {
        if (!_showcase.HasAny) return;

        bool t_marked = false;

        if (_showcase.Chapters != null)
        {
            for (int t_i = 0; t_i < _showcase.Chapters.Count; t_i++)
            {
                if (!TryGetChapter(_showcase.Chapters[t_i], out AdventureChapterDef t_chapter)) continue;
                if (!t_chapter.HasStableKey) continue;

                t_marked |= AdventureUnlockSeenStore.Add(t_chapter.chapterId);
            }
        }

        if (_showcase.Nodes != null)
        {
            for (int t_i = 0; t_i < _showcase.Nodes.Count; t_i++)
            {
                if (!TryGetNode(_showcase.Nodes[t_i], out AdventureNodeDef t_node)) continue;
                if (!t_node.HasStableKey) continue;

                t_marked |= AdventureUnlockSeenStore.Add(t_node.nodeId);
            }
        }

        if (t_marked) AdventureUnlockSeenStore.Flush();
    }

    /// <summary>정점 하나를 본 것으로 표식(맵이 한 칸씩 열어 보이는 자리에서 부른다).</summary>
    public static void MarkNodeUnlockSeen(int _index)
    {
        if (!TryGetNode(_index, out AdventureNodeDef t_node) || !t_node.HasStableKey) return;

        if (AdventureUnlockSeenStore.Add(t_node.nodeId)) AdventureUnlockSeenStore.Flush();
    }

    // 표식 없이 진행 흔적만 있는 세이브를 지나온 자리만큼 소급 표식한다(기기 교체·재설치·캐시 삭제 대비).
    // 표식이 기기 로컬이라 이 자리를 지나는 일이 드물지 않다 —
    // 진행 흔적(ClearedNodeIds · PendingRewardNodeId)은 계정을 따라오므로 그것으로 판정한다.
    //
    // 열려 있는 것을 전부 덮지는 않는다. 지금 도전할 정점은 남겨야 그 해금 연출이 새 기기에서도 한 번은 서고,
    // 그러지 않으면 표식이 비는 순간마다 앞으로 볼 연출까지 조용히 소모되어 다시는 나오지 않는다.
    internal static void BackfillSeenUnlocks()
    {
        if (AdventureUnlockSeenStore.Count > 0) return;

        List<string> t_cleared = Slot.ClearedNodeIds;
        bool t_hasProgress = (t_cleared != null && t_cleared.Count > 0) || !string.IsNullOrEmpty(PendingRewardNodeId);
        if (!t_hasProgress) return;   // 온보딩 신규 유저 — 첫 챕터·첫 정점 연출이 정상적으로 터져야 한다

        bool t_marked = false;
        int t_nodeCount = NodeCount;

        for (int t_i = 0; t_i < t_nodeCount; t_i++)
        {
            // 깬 정점만 지나온 자리다. 수령이 남았어도 그 정점의 해금은 이미 본 것이다.
            EAdventureNodeState t_state = StateOf(t_i);
            if (t_state != EAdventureNodeState.Cleared && t_state != EAdventureNodeState.RewardPending) continue;

            if (TryGetNode(t_i, out AdventureNodeDef t_node) && t_node.HasStableKey)
                t_marked |= AdventureUnlockSeenStore.Add(t_node.nodeId);

            // 그 정점이 속한 장의 띠도 이미 봤다 — 정점을 깼다는 것은 그 장에 들어섰다는 뜻이다.
            if (TryGetChapter(Config.ChapterIndexOfNode(t_i), out AdventureChapterDef t_chapter) && t_chapter.HasStableKey)
                t_marked |= AdventureUnlockSeenStore.Add(t_chapter.chapterId);
        }

        if (t_marked) AdventureUnlockSeenStore.Flush();
    }

    // 클리어·수령 낙인만 지운다(디버그 전용, 지급된 재화는 회수하지 않는다)
    public static void ResetForDebug()
    {
        Slot.ClearedNodeIds.Clear();
        ClaimedChapters.Clear();
        AdventureUnlockSeenStore.Clear();
        Slot.PendingRewardNodeId = "";
        DataSaveManager.Save();
        OnChanged?.Invoke();
    }

    // 정점이 열려 있는가 — 진행 사슬과 랭크 잠금을 곱한 해금 판정(진입 자격 CanEnter와 달리 클리어한 정점도 열린 것이다)
    static bool IsNodeUnlocked(int _index)
        => StateOf(_index) != EAdventureNodeState.Locked && !IsRankLocked(_index);
}

// 이번 진입에서 처음 열린 것들. 두 축이 한 사건이라 갈라 넘기지 않는다
public readonly struct AdventureUnlockShowcase
{
    /// <summary>새로 열린 챕터 인덱스(오름차순).</summary>
    public readonly List<int> Chapters;

    /// <summary>새로 열린 평탄 정점 번호(오름차순).</summary>
    public readonly List<int> Nodes;

    public AdventureUnlockShowcase(List<int> _chapters, List<int> _nodes)
    {
        Chapters = _chapters;
        Nodes = _nodes;
    }

    public bool HasAny => (Chapters != null && Chapters.Count > 0) || (Nodes != null && Nodes.Count > 0);
}

// 정점 상태(4종 배타)
public enum EAdventureNodeState
{
    Locked,
    Playable,
    RewardPending,   // 깼지만 보상을 아직 안 받았다 — 진입이 아니라 수령이 남은 자리
    Cleared,
}
