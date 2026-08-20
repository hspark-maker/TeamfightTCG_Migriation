using System;
using System.Collections.Generic;
using UnityEngine;

// 튜토리얼 한 편(챕터) — 기획의 "N편"과 1:1인 스텝 묶음
[Serializable]
public class OutgameTutorialChapter
{
    [Tooltip("기획의 'N편'과 맞추는 이름. 표시·로그용일 뿐이다 — 세이브가 붙잡는 것은 스텝의 stepId이고, 챕터·스텝 인덱스는 런타임 커서다")]
    [SerializeField] string label;

    [Tooltip("이 편의 스텝 순서. 마지막은 씬을 떠나는 전투 스텝이어야 한다(챕터 경계 = 씬 전환 경계)")]
    [SerializeField] List<TutorialStepDef> stepDefs = new List<TutorialStepDef>();

    public string Label => label;

    public int StepCount => stepDefs != null ? stepDefs.Count : 0;

#if UNITY_EDITOR
    // 저작 도구 전용 — 런타임은 읽기만 한다(TryGetStep/StepCount). 편집 규칙은 TutorialSequenceEditOps에 있다.
    public List<TutorialStepDef> EditorSteps => stepDefs ??= new List<TutorialStepDef>();

    // 저작 도구 전용 — 런타임은 읽기만 한다(Label)
    public string EditorLabel { get => label; set => label = value; }
#endif

    // 순번의 스텝 조회 — 범위 밖·빈 칸이면 false
    public bool TryGetStep(int _index, out TutorialStepDef _step)
    {
        _step = null;
        if (stepDefs == null || _index < 0 || _index >= stepDefs.Count) return false;

        _step = stepDefs[_index];
        return _step != null;
    }
}
