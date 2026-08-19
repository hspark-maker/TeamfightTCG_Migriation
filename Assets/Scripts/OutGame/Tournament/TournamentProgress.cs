using System;
using System.Collections.Generic;
using UnityEngine;

// 보상 토너먼트 진행도의 static 단일 창구(정점 해금 판정 · 클리어 지급 · 낙인)
public static class TournamentProgress
{
    static TournamentConfig s_config;

    // 진행 통지 — 맵이 정점 상태를 다시 그리는 트리거
    public static event Action OnChanged;

    // 전체 정점 수
    public static int NodeCount => Config.NodeCount;

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

    // 부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지
    public static void SetConfig(TournamentConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    public static bool TryGetNode(int _index, out TournamentNodeDef _node) => Config.TryGetNode(_index, out _node);

    public static int IndexOf(string _nodeId) => Config.IndexOf(_nodeId);

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

    // 정점 _index의 보상 스냅샷(범위 밖·미저작이면 빈 목록)
    public static void FillRewards(int _index, List<RewardLine> _sink)
    {
        Config.TryGetNode(_index, out TournamentNodeDef t_node);
        Config.FillRewards(t_node.nodeId, _sink);
    }

    // 클리어 낙인만 지운다(디버그 전용, 지급된 재화는 회수하지 않는다)
    public static void ResetForDebug()
    {
        Slot.clearedNodeIds.Clear();
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
}

// 정점 상태(3종 배타)
public enum ETournamentNodeState
{
    Locked,
    Playable,
    Cleared,
}
