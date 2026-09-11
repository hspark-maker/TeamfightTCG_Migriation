using System;
using System.Collections.Generic;
using UnityEngine;

// 아웃게임 튜토리얼 챕터(기획의 "N편"). 강제 챕터는 시퀀스 앞쪽에 연속으로 서고, 자율 챕터가 그 뒤를 잇는다
[Serializable]
public class OutgameTutorialChapter
{
    [Tooltip("기획의 'N편'과 맞추는 이름. 표시·로그용일 뿐이다 — 세이브가 붙잡는 것은 스텝의 stepId이고, 챕터·스텝 인덱스는 런타임 커서다")]
    [SerializeField] string label;

    [Tooltip("강제 = 첫 시작 시퀀스(세이브 좌표·기능 잠금). 자율 = 졸업 뒤 트리거로 깨어나는 안내.\n"
           + "자율 챕터 규약: 로비 안에서 시작해 로비 안에서 끝난다 · locks/unlocks를 쓰지 않는다 · 진행은 메모리에만 남아 화면을 떠나면 다음에 처음부터 · 완주해야 낙인이 찍힌다.\n"
           + "자율 챕터는 강제 챕터 뒤에만 올 수 있다(앞에 끼우면 뒤의 강제 챕터가 잘린다)")]
    [SerializeField] EOutgameTutorialChapterKind kind;

    [Tooltip("자율 챕터를 깨우는 사건. 완주 낙인의 식별자이기도 하다 — 바꾸면 이미 완주한 계정이 이 챕터를 다시 본다. 강제 챕터는 읽지 않는다")]
    [SerializeField] EOutgameTutorialTrigger trigger;

    [Tooltip("자율 챕터가 서려면 열려 있어야 하는 기능. 잠겨 있는 동안은 발화하지 않고 알림 점도 뜨지 않는다. None이면 조건 없음")]
    [SerializeField] EOutgameFeature prerequisite;

    [Tooltip("이 편의 스텝 순서. 씬을 떠나지 않고 끝나면 같은 씬에서 다음 챕터로 이어진다. 덱 게이트를 열었다면 같은 챕터에서 전투를 시작해야 한다")]
    [SerializeField] List<TutorialStepDef> stepDefs = new List<TutorialStepDef>();

    public string Label => label;

    public EOutgameTutorialChapterKind Kind => kind;

    public bool IsGuided => kind == EOutgameTutorialChapterKind.Guided;

    // 강제 챕터에 값이 남아 있어도 읽지 않는다 — 발화 키는 자율 챕터만의 것이다
    public EOutgameTutorialTrigger Trigger => IsGuided ? trigger : EOutgameTutorialTrigger.None;

    public EOutgameFeature Prerequisite => IsGuided ? prerequisite : EOutgameFeature.None;

    public int StepCount => stepDefs != null ? stepDefs.Count : 0;

#if UNITY_EDITOR
    // 저작 도구 전용 — 런타임은 읽기만 한다(TryGetStep/StepCount). 편집 규칙은 TutorialSequenceEditOps에 있다.
    public List<TutorialStepDef> EditorSteps => stepDefs ??= new List<TutorialStepDef>();

    // 저작 도구 전용 — 런타임은 읽기만 한다(Label)
    public string EditorLabel { get => label; set => label = value; }

    public EOutgameTutorialChapterKind EditorKind { get => kind; set => kind = value; }

    // 저작 도구 전용 — 이 값이 완주 낙인 식별자라 바꾸면 낙인이 갈린다
    public EOutgameTutorialTrigger EditorTrigger { get => trigger; set => trigger = value; }

    public EOutgameFeature EditorPrerequisite { get => prerequisite; set => prerequisite = value; }
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
