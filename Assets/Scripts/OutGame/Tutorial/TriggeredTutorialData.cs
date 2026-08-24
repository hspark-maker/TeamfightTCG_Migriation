using System;
using System.Collections.Generic;
using UnityEngine;

// 트리거 하나에 대응하는 스텝 묶음(로비 안에서 시작해 로비 안에서 끝난다)
[Serializable]
public class TriggeredTutorialEntry
{
    [Tooltip("이 묶음을 깨우는 발화 키. 완주 낙인 식별도 이 값이 한다(None이면 발화하지 않는다)")]
    [SerializeField] EOutgameTutorialTrigger trigger;

    [Tooltip("에디터에서 알아보기 위한 이름. 표시·로그용일 뿐이다")]
    [SerializeField] string label;

    [Tooltip("이 트리거의 스텝 순서. 진행 좌표는 메모리에만 남는다(앱 종료 시 처음부터)")]
    [SerializeField] List<TutorialStepDef> stepDefs = new List<TutorialStepDef>();

    public EOutgameTutorialTrigger Trigger => trigger;

    public string Label => label;

    public int StepCount => stepDefs != null ? stepDefs.Count : 0;

#if UNITY_EDITOR
    // 저작 도구 전용 — 런타임은 읽기만 한다(TryGetStep/StepCount). 편집 규칙은 TutorialSequenceEditOps에 있다.
    public List<TutorialStepDef> EditorSteps => stepDefs ??= new List<TutorialStepDef>();

    // 저작 도구 전용 — 런타임은 읽기만 한다(Label)
    public string EditorLabel { get => label; set => label = value; }

    // 저작 도구 전용 — 런타임은 읽기만 한다(Trigger). 이 값이 완주 낙인 식별자라 바꾸면 낙인이 갈린다.
    public EOutgameTutorialTrigger EditorTrigger { get => trigger; set => trigger = value; }
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

// 트리거 발화 튜토리얼 목록(에디터 저작, SO)
[CreateAssetMenu(fileName = "TriggeredTutorial", menuName = "Card Battle/Triggered Tutorial")]
public class TriggeredTutorialData : ScriptableObject
{
    [Header("트리거별 스텝 묶음 (완주하면 그 트리거는 계정당 1회로 닫힌다)")]
    public List<TriggeredTutorialEntry> entries = new List<TriggeredTutorialEntry>();
}
