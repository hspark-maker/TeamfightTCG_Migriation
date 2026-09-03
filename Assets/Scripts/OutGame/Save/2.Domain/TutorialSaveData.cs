using System.Collections.Generic;
using Firebase.Firestore;

// 아웃게임 첫시작 튜토리얼 진행도 세이브 값 객체
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class TutorialSaveData
{
    // 완료 여부 — 진행 좌표보다 항상 우선
    [FirestoreProperty("outgameCompleted")] public bool OutgameCompleted { get; set; }

    // 진행 좌표(챕터, 챕터 내 스텝) — 스텝 실행 전에 커밋. 런타임 커서일 뿐 세이브의 앵커는 아래 StepId다.
    [FirestoreProperty("chapterIndex")] public int ChapterIndex { get; set; }
    [FirestoreProperty("chapterStepIndex")] public int ChapterStepIndex { get; set; }

    // 서 있는 스텝의 불변 번호(TutorialStepDef.stepId). 0 = 앵커 없음(그때는 위 좌표가 정본이다).
    // 좌표만으로는 저작이 바뀌면 다른 스텝을 가리키게 된다 — 초기화에서 이 번호로 좌표를 되찾는다.
    [FirestoreProperty("stepId")] public int StepId { get; set; }

    // 직전 초기화 때의 진행 좌표와 그 좌표가 이어진 초기화 횟수 — 진행이 막혔는지 판정하는 데만 쓴다.
    // -1은 "아직 한 번도 관측하지 않음". 0으로 두면 시작 좌표(0-0)와 우연히 같아 첫 초기화가 재관측으로 세어진다.
    // 아래 세 필드의 FirestoreProperty 문자열은 이미 배포된 세이브 문서의 키다 —
    // C# 이름만 초기화 용어로 맞추고 문자열은 건드리지 않는다. 바꾸면 기존 진행도가 끊긴다.
    [FirestoreProperty("lastBootChapterIndex")] public int LastInitChapterIndex { get; set; } = -1;
    [FirestoreProperty("lastBootStepIndex")] public int LastInitStepIndex { get; set; } = -1;
    [FirestoreProperty("sameCoordBootCount")] public int SameCoordInitCount { get; set; }

    // 완주한 트리거 튜토리얼 키(EOutgameTutorialTrigger 이름)
    [FirestoreProperty("completedTriggers")] public List<string> CompletedTriggers { get; set; } = new List<string>();
}
