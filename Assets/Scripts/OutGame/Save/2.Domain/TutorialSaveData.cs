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

    // 직전 부팅 때의 진행 좌표와 그 좌표가 이어진 부팅 횟수 — 진행이 막혔는지 판정하는 데만 쓴다.
    // -1은 "아직 한 번도 관측하지 않음". 0으로 두면 시작 좌표(0-0)와 우연히 같아 첫 부팅이 재관측으로 세어진다.
    public int lastBootChapterIndex = -1;
    public int lastBootStepIndex    = -1;
    public int sameCoordBootCount;

    // 완주한 트리거 튜토리얼 키(EOutgameTutorialTrigger 이름)
    public List<string> completedTriggers = new List<string>();

    // 전면으로 한 번 안내한 해금 개념의 키(UnlockIntro.Key). 목록에 없으면 아직 안 본 것이다 —
    // 그래서 구 세이브(키 없음 → 빈 목록)가 곧 "전부 처음"으로 읽힌다.
    public List<string> seenUnlockIntros = new List<string>();
}
