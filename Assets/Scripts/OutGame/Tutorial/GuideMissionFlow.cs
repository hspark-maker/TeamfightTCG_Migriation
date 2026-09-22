using System;
using System.Collections.Generic;
using UnityEngine;

public enum EGuideFlowActivation { Mission = 0, ContentUnlock = 1 }

/// <summary>미션 또는 콘텐츠 해금에 연결된 소개·이동·온보딩 저작.</summary>
[Serializable]
public sealed class GuideMissionFlow
{
    public EGuideFlowActivation activation;
    [Tooltip("콘텐츠 해금형의 대상. 미션형은 None으로 둔다.")]
    public EOutgameFeature content;
    [Tooltip("미션형의 활성 가이드 미션 ID. 콘텐츠 해금형은 비워 둔다.")]
    public string missionId;
    [Tooltip("진행할 자율 챕터. None이면 해금 소개로 끝낸다.")]
    public EOutgameTutorialTrigger tutorial;
    [Tooltip("이 순서로 해금 소개를 마친 뒤 목적지로 이동한다. 콘텐츠 이용 자격은 별도 ContentUnlockConfig 에셋에서 정한다.")]
    public List<EContentUnlockIntro> contentIntros = new List<EContentUnlockIntro>();
    [Tooltip("해금 소개 후 온보딩을 진행할 화면. 챕터가 없거나 None이면 이동하지 않는다.")]
    public EOutgameFeature destination;
}

/// <summary>가이드 흐름의 미션 조건과 콘텐츠 연결을 조회한다.</summary>
public static class GuideMissionFlows
{
    public static IReadOnlyList<GuideMissionFlow> All
        => OutgameTutorialRunner.Data?.guide.guideFlows ?? (IReadOnlyList<GuideMissionFlow>)Array.Empty<GuideMissionFlow>();

    public static bool TryGet(EOutgameTutorialTrigger _tutorial, out GuideMissionFlow _flow)
        => TryGet(All, _tutorial, out _flow);

    public static bool IsEligible(GuideMissionFlow _flow)
        => (_flow?.tutorial == EOutgameTutorialTrigger.CollectionTabFirstEnter
                && OutgameTutorialRunner.IsDefeatEnhanceInterlude)
            || IsEligible(_flow, OutgameTutorialProgress.IsCompleted, GuideMissionProgress.Current?.Id,
                _flow != null && ContentUnlockManager.TryGetKey(_flow.content, out string t_key)
                    && ContentUnlockManager.IsUnlocked(t_key));

    /// <summary>졸업·레벨·랭크 순으로 소개한다. 같은 조건값은 저작 순서, 미션형은 마지막이다.</summary>
    public static IEnumerable<GuideMissionFlow> InPresentationOrder()
    {
        var t_flows = new List<GuideMissionFlow>();
        foreach (var t_flow in All)
        {
            if (t_flow == null) continue;
            int t_index = t_flows.FindIndex(t_other => OrderOf(t_other).CompareTo(OrderOf(t_flow)) > 0);
            if (t_index < 0) t_flows.Add(t_flow);
            else t_flows.Insert(t_index, t_flow);
        }
        return t_flows;
    }

    internal static bool TryGet(IReadOnlyList<GuideMissionFlow> _flows,
        EOutgameTutorialTrigger _tutorial, out GuideMissionFlow _flow)
    {
        if (_flows != null && _tutorial != EOutgameTutorialTrigger.None)
            foreach (GuideMissionFlow t_flow in _flows)
                if (t_flow != null && t_flow.tutorial == _tutorial)
                {
                    _flow = t_flow;
                    return true;
                }
        _flow = null;
        return false;
    }

    internal static bool IsEligible(GuideMissionFlow _flow, bool _ftueCompleted, string _currentMissionId,
        bool _contentUnlocked = false)
        => _flow != null && _ftueCompleted
            && (_flow.activation == EGuideFlowActivation.ContentUnlock ? _contentUnlocked
                : _flow.activation == EGuideFlowActivation.Mission && !string.IsNullOrEmpty(_flow.missionId)
                    && string.Equals(_flow.missionId, _currentMissionId, StringComparison.Ordinal));

    static (int Group, int Threshold) OrderOf(GuideMissionFlow _flow)
    {
        if (_flow.activation != EGuideFlowActivation.ContentUnlock
            || !ContentUnlockManager.TryGetKey(_flow.content, out string t_key)
            || !ContentUnlockConfig.TryGet(t_key, out var t_rule)) return (3, 0);
        if (t_rule.Condition == EContentUnlockCondition.FtueCompleted) return (0, 0);
        if (t_rule.Condition == EContentUnlockCondition.AccountLevel) return (1, t_rule.MinAccountLevel);
        return (2, ContentUnlockConfig.TryGetRequiredTier(t_rule, out int t_tier) ? t_tier : int.MaxValue);
    }
}
