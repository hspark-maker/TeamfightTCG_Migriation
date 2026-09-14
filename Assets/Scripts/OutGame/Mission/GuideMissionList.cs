using System;
using System.Collections.Generic;

/// <summary>서버 정의로 가이드의 막별 목록과 달성 보상 행을 구성한다.</summary>
internal sealed class GuideMissionList
{
    internal sealed class Group
    {
        readonly List<MissionDefinition> m_rows = new List<MissionDefinition>();

        internal GuideMissionTrack.GuideAct Act { get; }
        internal IReadOnlyList<MissionDefinition> Rows => m_rows;
        internal int Claimed { get; private set; }
        internal int Total { get; private set; }
        internal MissionDefinition LastMission { get; private set; }
        internal bool HasHeader => Act.Id > 0;

        internal Group(GuideMissionTrack.GuideAct _act) => Act = _act;

        internal void Add(MissionDefinition _definition, bool _claimed, bool _completion)
        {
            Total++;
            if (_claimed) Claimed++;
            LastMission = _definition;
            if (!_completion) m_rows.Add(_definition);
        }
    }

    readonly List<Group> m_groups = new List<Group>();
    readonly List<MissionDefinition> m_definitions = new List<MissionDefinition>();

    internal IReadOnlyList<Group> Groups => m_groups;
    internal IReadOnlyList<MissionDefinition> Definitions => m_definitions;
    internal MissionDefinition Completion { get; private set; }

    internal static GuideMissionList Build(IReadOnlyList<MissionDefinition> _definitions,
        Func<string, bool> _isClaimed, bool _useCompletion)
    {
        var t_list = new GuideMissionList();
        bool t_hasActs = true;
        for (int i = 0; i < _definitions.Count; i++)
        {
            MissionDefinition t_definition = _definitions[i];
            if (!GuideMissionTrack.IsGuide(t_definition)) continue;
            t_list.m_definitions.Add(t_definition);
            if (t_definition.GuideActId <= 0 || string.IsNullOrWhiteSpace(t_definition.GuideActName))
                t_hasActs = false;
        }
        t_list.m_definitions.Sort(Compare);
        if (_useCompletion)
        {
            foreach (MissionDefinition t_definition in t_list.m_definitions)
                if (t_list.Completion == null || t_definition.SortOrder > t_list.Completion.SortOrder)
                    t_list.Completion = t_definition;
        }

        var t_groupsById = new Dictionary<int, Group>();
        foreach (MissionDefinition t_definition in t_list.m_definitions)
        {
            // 구버전 응답은 막 전체를 숨겨 진행 순서대로 표시한다.
            int t_id = t_hasActs ? t_definition.GuideActId : 0;
            if (!t_groupsById.TryGetValue(t_id, out Group t_group))
            {
                var t_act = t_hasActs
                    ? new GuideMissionTrack.GuideAct(t_id, t_list.m_groups.Count + 1, t_definition.GuideActName)
                    : default;
                t_group = new Group(t_act);
                t_groupsById.Add(t_id, t_group);
                t_list.m_groups.Add(t_group);
            }
            t_group.Add(t_definition, _isClaimed != null && _isClaimed(t_definition.Id),
                ReferenceEquals(t_definition, t_list.Completion));
        }
        return t_list;
    }

    static int Compare(MissionDefinition _left, MissionDefinition _right)
    {
        int t_order = _left.SortOrder.CompareTo(_right.SortOrder);
        return t_order != 0 ? t_order : string.CompareOrdinal(_left.Id, _right.Id);
    }
}
