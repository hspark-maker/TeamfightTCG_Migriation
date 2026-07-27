using System;

// 아웃게임 첫시작 튜토리얼 진행도의 세이브 값 객체.
// 필드는 추가만(하위호환) — 의미 변경·삭제·리네임 금지. 구 세이브엔 노드가 없어 기본값(0/false)으로 읽힌다.
[Serializable]
public class TutorialSaveData
{
    // 다음 수행할 스텝 인덱스. 커밋은 스텝 실행 "전"에 한다 —
    // 실행 후 커밋이면 자동구매 직후 강제종료 시 소유만 남고 진행도가 0이라 온보딩이 영구 스킵된다.
    public int outgameStepIndex;

    // 완료 여부. completed는 항상 stepIndex보다 우선한다 —
    // 완료를 stepIndex >= steps.Count로 파생시키면 나중에 스텝을 추가했을 때 완료 유저의 튜토리얼이 되살아난다.
    public bool outgameCompleted;

    // 레거시 마이그레이션 판정을 이미 했는지. 판정은 계정당 딱 1회다 —
    // 매 부트 재판정이면 튜토리얼이 아직 안 돈 상태에서 수동으로 카드를 얻은 신규 유저까지 완료 처리된다.
    public bool migrationChecked;
}
