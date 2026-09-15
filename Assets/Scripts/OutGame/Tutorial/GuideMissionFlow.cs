using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>가이드 미션에 연결된 해금 소개·이동·온보딩 저작.</summary>
[Serializable]
public sealed class GuideMissionFlow
{
    [Tooltip("현재 활성 가이드 미션 ID. 비워 두면 FTUE 졸업 후 시작 흐름이다.")]
    public string missionId;
    [Tooltip("진행할 자율 챕터. None이면 해금 소개와 이동만 진행한다.")]
    public EOutgameTutorialTrigger tutorial;
    [Tooltip("이 순서로 해금 소개를 마친 뒤 목적지로 이동한다. 콘텐츠 이용 자격은 별도 ContentUnlockConfig 에셋에서 정한다.")]
    public List<EContentUnlockIntro> contentIntros = new List<EContentUnlockIntro>();
    [Tooltip("해금 소개 후 이동할 화면. None이면 이동하지 않는다.")]
    public EOutgameFeature destination;
}

/// <summary>가이드 흐름의 미션 조건과 콘텐츠 연결을 조회한다.</summary>
public static class GuideMissionFlows
{
    public static IReadOnlyList<GuideMissionFlow> All
        => OutgameTutorialRunner.Data?.guide.guideFlows ?? (IReadOnlyList<GuideMissionFlow>)Array.Empty<GuideMissionFlow>();

    public static bool TryGet(EOutgameTutorialTrigger _tutorial, out GuideMissionFlow _flow)
    {
        if (_tutorial != EOutgameTutorialTrigger.None)
            foreach (GuideMissionFlow t_flow in All)
                if (t_flow != null && t_flow.tutorial == _tutorial)
                {
                    _flow = t_flow;
                    return true;
                }
        _flow = null;
        return false;
    }

    public static bool IsEligible(GuideMissionFlow _flow)
        => _flow != null && OutgameTutorialProgress.IsCompleted
            && (string.IsNullOrEmpty(_flow.missionId) || GuideMissionProgress.IsCurrent(_flow.missionId));

}
