using System;
using System.Collections.Generic;

// 아웃게임 첫시작 튜토리얼 진행도의 세이브 값 객체.
// 필드는 추가만(하위호환) — 의미 변경·삭제·리네임 금지. 구 세이브엔 노드가 없어 기본값(0/false)으로 읽힌다.
[Serializable]
public class TutorialSaveData
{
    // 플랫 스텝 리스트 시절의 진행도. 챕터 재편(2026-07-31) 이후로는 읽지도 쓰지도 않는다 —
    // 규약상 삭제·리네임이 금지라 동결한 채 남긴다(구버전 빌드로 되돌아갔을 때의 재개 지점도 겸한다).
    public int outgameStepIndex;

    // 완료 여부. completed는 항상 진행 좌표보다 우선한다 —
    // 완료를 좌표 비교로 파생시키면 나중에 챕터·스텝을 추가했을 때 완료 유저의 튜토리얼이 되살아난다.
    public bool outgameCompleted;

    // 레거시 마이그레이션 판정을 이미 했는지. 판정은 계정당 딱 1회다 —
    // 매 부트 재판정이면 튜토리얼이 아직 안 돈 상태에서 수동으로 카드를 얻은 신규 유저까지 완료 처리된다.
    public bool migrationChecked;

    // 진행 좌표(챕터 = 기획의 "N편", 그 안의 스텝 순번). 커밋은 스텝 실행 "전"에 한다 —
    // 실행 후 커밋이면 자동구매 직후 강제종료 시 소유만 남고 좌표가 0이라 온보딩이 영구 스킵된다.
    public int outgameChapterIndex;
    public int outgameChapterStepIndex;

    // 완주한 트리거 튜토리얼 키(EOutgameTutorialTrigger 이름). 트리거마다 bool 필드를 늘리지 않으려고 리스트다
    // — 트리거를 추가해도 세이브 스키마는 그대로다. 모르는 문자열은 지우지 않고 그대로 둔다(구/신 빌드 왕복).
    public List<string> completedTriggers = new List<string>();
}
