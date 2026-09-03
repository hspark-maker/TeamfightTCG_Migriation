using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>AdventureChapter 표의 전투 수치를 SO 표현 데이터와 결합해 런타임 설정을 만든다.</summary>
public static class AdventureNodeSpec
{
    static AdventureConfig s_runtime;
    static bool s_updateRequired;

    public static bool UpdateRequired => s_updateRequired;

    public static bool TryValidateRequired(out string _error)
    {
        s_updateRequired = false;
        return TryReadRows(out _, out _error);
    }

    public static bool TryGetBattleSpec(
        string _nodeId, out IReadOnlyList<int> _enemyDeck, out int _aiCardLevel, out string _error)
    {
        _enemyDeck = Array.Empty<int>();
        _aiCardLevel = CardGrowth.BaseLevel;
        if (!TryReadRows(out List<ParsedNode> t_rows, out _error)) return false;

        foreach (ParsedNode t_row in t_rows)
        {
            if (!string.Equals(t_row.NodeId, _nodeId, StringComparison.Ordinal)) continue;
            _enemyDeck = t_row.EnemyDeck;
            _aiCardLevel = t_row.AiCardLevel;
            return true;
        }

        _error = $"AdventureChapter에 정점 '{_nodeId}'가 없다.";
        return false;
    }

    public static bool TryBuildRuntime(
        AdventureConfig _authoredSkin, out AdventureConfig _runtime, out string _error)
    {
        _runtime = null;
        if (_authoredSkin == null)
        {
            _error = "AdventureConfig 스킨이 배선되지 않았다.";
            return false;
        }
        s_updateRequired = false;
        if (!TryReadRows(out List<ParsedNode> t_rows, out _error)) return false;

        var t_authoredNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (AdventureNodeDef t_node in _authoredSkin.Nodes)
            if (t_node.HasStableKey) t_authoredNodeIds.Add(t_node.nodeId);

        foreach (ParsedNode t_row in t_rows)
            if (!t_authoredNodeIds.Contains(t_row.NodeId))
            {
                s_updateRequired = true;
                _error = $"AdventureChapter의 정점 '{t_row.NodeId}'를 이 앱의 AdventureConfig가 모른다.";
                return false;
            }

        if (t_authoredNodeIds.Count != t_rows.Count)
        {
            _error = $"AdventureConfig 정점 수 {t_authoredNodeIds.Count}와 AdventureChapter 행 수 {t_rows.Count}가 다르다.";
            return false;
        }

        if (s_runtime != null) UnityEngine.Object.Destroy(s_runtime);
        AdventureConfig t_runtime = UnityEngine.Object.Instantiate(_authoredSkin);
        t_runtime.name = _authoredSkin.name + " (ServerSpec)";
        t_runtime.hideFlags = HideFlags.DontSave;

        foreach (ParsedNode t_row in t_rows)
            if (!t_runtime.TrySetNodeBattleSpec(t_row.NodeId, t_row.EnemyDeck, t_row.AiCardLevel))
            {
                UnityEngine.Object.Destroy(t_runtime);
                _error = $"AdventureConfig에 정점 '{t_row.NodeId}'가 없다.";
                return false;
            }

        s_runtime = t_runtime;
        _runtime = t_runtime;
        return true;
    }

    static bool TryReadRows(out List<ParsedNode> _parsed, out string _error)
    {
        _parsed = new List<ParsedNode>();
        _error = null;

        IReadOnlyList<AdventureChapter> t_source = SpecSource.Manager?.AdventureChapter?.All;
        if (t_source == null || t_source.Count == 0)
        {
            _error = "AdventureChapter 서버 표가 비어 있다.";
            return false;
        }

        var t_seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (AdventureChapter t_row in t_source)
        {
            if (t_row == null)
            {
                _error = "AdventureChapter 서버 표에 null 행이 있다.";
                return false;
            }
            if (string.IsNullOrEmpty(t_row.nodeId) || !t_seen.Add(t_row.nodeId))
            {
                _error = $"AdventureChapter nodeId가 비었거나 중복이다: '{t_row.nodeId}'.";
                return false;
            }
            if (string.IsNullOrEmpty(t_row.aiDeckId))
            {
                _error = $"AdventureChapter '{t_row.nodeId}'의 aiDeckId가 비어 있다.";
                return false;
            }
            if (t_row.aiCardLevel < CardGrowth.BaseLevel)
            {
                _error = $"AdventureChapter '{t_row.nodeId}'의 aiCardLevel {t_row.aiCardLevel}이 유효하지 않다.";
                return false;
            }
            if (!AIDeckSpec.TryGetDeck(t_row.aiDeckId, out IReadOnlyList<int> t_deck))
            {
                _error = $"AdventureChapter '{t_row.nodeId}'가 없는 AIDeck '{t_row.aiDeckId}'를 참조한다.";
                return false;
            }

            _parsed.Add(new ParsedNode(t_row.nodeId, new List<int>(t_deck), t_row.aiCardLevel));
        }

        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_runtime = null;
        s_updateRequired = false;
    }

    readonly struct ParsedNode
    {
        public readonly string NodeId;
        public readonly IReadOnlyList<int> EnemyDeck;
        public readonly int AiCardLevel;

        public ParsedNode(string _nodeId, IReadOnlyList<int> _enemyDeck, int _aiCardLevel)
        {
            NodeId = _nodeId;
            EnemyDeck = _enemyDeck;
            AiCardLevel = _aiCardLevel;
        }
    }
}
