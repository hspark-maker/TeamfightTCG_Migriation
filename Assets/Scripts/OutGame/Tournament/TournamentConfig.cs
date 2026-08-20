using System;
using System.Collections.Generic;
using UnityEngine;

// 보상 토너먼트 경로 저작 데이터 — 정점 목록(순서 = 진행 순서)과 정점별 보상의 단일 진실원
[CreateAssetMenu(fileName = "TournamentConfig", menuName = "Card Battle/Tournament Config")]
public class TournamentConfig : ScriptableObject
{
    [Header("정점 목록 (순서 = 진행 순서, 아래로 갈수록 뒤)")]
    [SerializeField] List<TournamentNodeDef> nodes = new List<TournamentNodeDef>();

    // 전체 정점 수. 소비처는 맵 셀 수를 이 값에서 파생한다
    public int NodeCount => nodes != null ? nodes.Count : 0;

    public IReadOnlyList<TournamentNodeDef> Nodes
        => nodes != null ? nodes : (IReadOnlyList<TournamentNodeDef>)Array.Empty<TournamentNodeDef>();

    // 정점 저작값 조회(범위 밖이면 false + 빈 값)
    public bool TryGetNode(int _index, out TournamentNodeDef _node)
    {
        _node = default;
        if (nodes == null || _index < 0 || _index >= nodes.Count) return false;

        _node = nodes[_index];
        return true;
    }

    // 안정 키 → 인덱스(미저작·미존재는 -1)
    public int IndexOf(string _nodeId)
    {
        if (nodes == null || string.IsNullOrEmpty(_nodeId)) return -1;

        for (int t_i = 0; t_i < nodes.Count; t_i++)
            if (string.Equals(nodes[t_i].nodeId, _nodeId, StringComparison.Ordinal)) return t_i;

        return -1;
    }

    /// <summary>정점 _nodeId의 보상을 _sink에 담는다(Clear는 이 메서드가 한다).
    /// 저작값(<see cref="AlbumRewardDef"/>) → 공용 <see cref="RewardLine"/> 변환의 표준 지점 —
    /// 소비처가 저작 포맷을 직접 읽지 않게 한다.</summary>
    public void FillRewards(string _nodeId, List<RewardLine> _sink)
    {
        if (_sink == null) return;
        _sink.Clear();

        int t_index = IndexOf(_nodeId);
        if (t_index < 0) return;

        List<AlbumRewardDef> t_rewards = nodes[t_index].rewards;
        if (t_rewards == null) return;

        for (int t_i = 0; t_i < t_rewards.Count; t_i++)
        {
            AlbumRewardDef t_def = t_rewards[t_i];
            if (t_def.amount <= 0) continue;

            _sink.Add(new RewardLine(new CurrencyGain(t_def.currency, t_def.amount), t_def.icon));
        }
    }

#if UNITY_EDITOR
    [ContextMenu("토너먼트 저작 검증")]
    void ValidateTournament() => TournamentValidator.Validate(this);
#endif
}
