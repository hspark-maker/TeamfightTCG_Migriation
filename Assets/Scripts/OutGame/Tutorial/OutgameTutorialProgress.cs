using System.Collections.Generic;
using UnityEngine;

// 아웃게임 첫시작 튜토리얼 진행도의 static 단일 창구(세이브 슬롯 매핑을 여기서만 안다)
public static class OutgameTutorialProgress
{
    // 같은 좌표로 이만큼 더 부팅하면 진행이 막힌 것으로 본다(0=직후, 2=세 번째 부팅).
    // 두 번까지 봐주는 이유: 튜토 도중 앱을 끄는 것은 흔한 일이고, 오탐 대가(기능이 미리 열림)보다
    // 놓쳤을 때 대가(게임 자체를 못 함)가 훨씬 크다.
    const int STALL_BOOT_COUNT = 2;

    public static bool IsCompleted => Slot.OutgameCompleted;

    // 진행 중인 챕터(기획의 "N편") 인덱스
    public static int ChapterIndex => Slot.ChapterIndex;

    // 챕터 안에서의 스텝 순번(시퀀스 전체 통산이 아니다)
    public static int StepIndex => Slot.ChapterStepIndex;

    // 서 있는 스텝의 불변 번호(0 = 앵커 없음). 좌표가 커서라면 이쪽이 세이브의 정체성이다.
    public static int StepId => Slot.StepId;

    static TutorialSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.Tutorial == null) t_data.Tutorial = new TutorialSaveData();
            return t_data.Tutorial;
        }
    }

    // 초기화에서 클라우드 세이브 채택 이후 1회 호출 — 진행 정지 판정
    public static void Init()
    {
        DetectStall();
    }

    // 진행도 영속화
    public static void Save() => DataSaveManager.Save();

    // 진행 좌표를 즉시 커밋(실패 롤백용 감소도 허용)
    public static void CommitStep(int _chapter, int _step)
    {
        if (_chapter < 0 || _step < 0) return;

        var t_slot = Slot;
        t_slot.ChapterIndex     = _chapter;
        t_slot.ChapterStepIndex = _step;

        // 좌표가 움직이는 모든 런타임 경로가 이 창구를 지나므로, 앵커도 여기서만 갱신하면 된다.
        // 시퀀스가 아직 주입되기 전이면 0이 들어가는데, 그러면 다음 초기화가 좌표에서 다시 채운다.
        t_slot.StepId = OutgameTutorialRunner.StepIdAt(_chapter, _step);

        Save();
    }

    // 정지 판정을 지금 좌표에서 다시 세기 시작한다 — 초기화마다 자가 복구가 도는 좌표는 "막힌 좌표"가 아니다.
    public static void ResetStallWatch()
    {
        var t_slot = Slot;
        t_slot.LastBootChapterIndex = t_slot.ChapterIndex;
        t_slot.LastBootStepIndex    = t_slot.ChapterStepIndex;
        t_slot.SameCoordBootCount   = 0;
        Save();
    }

    // 온보딩 완료 낙인
    public static void Complete()
    {
        var t_slot = Slot;
        if (t_slot.OutgameCompleted) return;

        t_slot.OutgameCompleted = true;
        Save();
    }

    // 이 트리거의 튜토리얼을 이미 완주했는가(None은 완료로 본다)
    public static bool IsTriggerDone(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return true;

        var t_done = Slot.CompletedTriggers;
        return t_done != null && t_done.Contains(_trigger.ToString());
    }

    // 트리거 튜토리얼 완주 낙인(트리거당 1회)
    public static void MarkTriggerDone(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return;

        var t_slot = Slot;
        if (t_slot.CompletedTriggers == null) t_slot.CompletedTriggers = new List<string>();

        string t_key = _trigger.ToString();
        if (t_slot.CompletedTriggers.Contains(t_key)) return;

        t_slot.CompletedTriggers.Add(t_key);
        Save();
    }

    // 디버그 전용 — 트리거 낙인을 전부 걷는다(온보딩 좌표·완료는 유지)
    public static void ClearTriggersForDebug()
    {
        var t_slot = Slot;
        if (t_slot.CompletedTriggers == null) t_slot.CompletedTriggers = new List<string>();
        else                                  t_slot.CompletedTriggers.Clear();

        Save();
    }

    // 디버그 전용 — 진행도만 처음으로 되돌린다(소유·재화는 유지)
    public static void ResetForDebug() => JumpForDebug(0, 0);

    // 디버그 전용 — 임의 좌표로 되감고 완료 낙인도 걷는다
    public static void JumpForDebug(int _chapter, int _step)
    {
        if (_chapter < 0 || _step < 0) return;

        var t_slot = Slot;
        t_slot.ChapterIndex     = _chapter;
        t_slot.ChapterStepIndex = _step;
        t_slot.StepId           = OutgameTutorialRunner.StepIdAt(_chapter, _step);
        t_slot.OutgameCompleted = false;
        Save();

        // 손으로 되감은 좌표는 "막힌 좌표"가 아니다 — 옛 카운트를 이어 세면 몇 초기화 만에 오탐 정지가 뜬다.
        // 이미 선 판정도 함께 걷어야 잠금이 실제로 돌아온다(둘을 갈라 두면 한쪽만 풀려 검증이 오염된다).
        ResetStallWatch();
        OutgameFeatureLock.ClearStall();
    }

    // 부팅을 거듭해도 좌표가 그대로면 그 스텝은 스스로 풀릴 수 없는 것이다 — 안내는 멈추더라도 게임은 열어 준다.
    // 진입 실패는 브리지가 그 자리에서 잡고, 여기는 신호를 영영 못 받는 대기형 정지(앵커 미등록 등)를 잡는다.
    static void DetectStall()
    {
        var t_slot = Slot;
        if (t_slot.OutgameCompleted) return;

        if (t_slot.LastBootChapterIndex != t_slot.ChapterIndex
         || t_slot.LastBootStepIndex    != t_slot.ChapterStepIndex)
        {
            t_slot.LastBootChapterIndex = t_slot.ChapterIndex;
            t_slot.LastBootStepIndex    = t_slot.ChapterStepIndex;
            t_slot.SameCoordBootCount   = 0;
            Save();
            return;
        }

        t_slot.SameCoordBootCount++;
        Save();

        if (t_slot.SameCoordBootCount < STALL_BOOT_COUNT) return;

        Debug.LogWarning($"[OutgameTutorialProgress] 좌표 {t_slot.ChapterIndex}-{t_slot.ChapterStepIndex}에서 "
                       + $"{t_slot.SameCoordBootCount + 1}번째 부팅 — 진행이 막힌 것으로 보고 기능 잠금을 해제합니다.");
        OutgameFeatureLock.NotifyStalled();
    }
}
