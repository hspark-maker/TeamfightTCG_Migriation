using System;
using System.Collections.Generic;

// 아웃게임 첫시작 튜토리얼 진행도 세이브 값 객체
[Serializable]
public class TutorialSaveData
{
    // 레거시 플랫 스텝 진행도 — 현재는 읽지도 쓰지도 않음
    public int outgameStepIndex;

    // 완료 여부 — 진행 좌표보다 항상 우선
    public bool outgameCompleted;

    // 레거시 마이그레이션 판정 완료 여부(계정당 1회)
    public bool migrationChecked;

    // 진행 좌표(챕터, 챕터 내 스텝) — 스텝 실행 전에 커밋
    public int outgameChapterIndex;
    public int outgameChapterStepIndex;

    // 완주한 트리거 튜토리얼 키(EOutgameTutorialTrigger 이름)
    public List<string> completedTriggers = new List<string>();
}
