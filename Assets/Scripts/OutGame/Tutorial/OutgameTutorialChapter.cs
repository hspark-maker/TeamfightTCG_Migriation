using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼 한 편(챕터) — 준비 스텝 0개 이상 → 전투 스텝으로 끝나는 묶음. 기획 문서의 "N편"과 1:1.
/// SO로 가르지 않는다 — 스텝을 SO로 가른 두 근거(종류별 필드만 노출·에셋 재사용)가 챕터엔 둘 다 없다.</summary>
[Serializable]
public class OutgameTutorialChapter
{
    [Tooltip("기획의 'N편'과 맞추는 이름. 표시·로그용일 뿐 진행도 식별은 인덱스가 한다")]
    [SerializeField] string label;

    [Tooltip("이 편의 스텝 순서. 마지막은 씬을 떠나는 전투 스텝이어야 한다(챕터 경계 = 씬 전환 경계)")]
    [SerializeField] List<OutgameTutorialStep> steps = new List<OutgameTutorialStep>();

    public string Label => label;

    public int StepCount => steps != null ? steps.Count : 0;

    /// <summary>범위 밖·빈 칸이면 false. 미배선 칸은 실행할 스텝이 없는 것과 같다.</summary>
    public bool TryGetStep(int _index, out OutgameTutorialStep _step)
    {
        _step = null;
        if (steps == null || _index < 0 || _index >= steps.Count) return false;

        _step = steps[_index];
        return _step != null;
    }
}
