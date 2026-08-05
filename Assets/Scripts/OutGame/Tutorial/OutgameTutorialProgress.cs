using System.Collections.Generic;

// 아웃게임 첫시작 튜토리얼 진행도의 static 단일 창구(세이브 슬롯 매핑을 여기서만 안다)
public static class OutgameTutorialProgress
{
    public static bool IsCompleted => Slot.outgameCompleted;

    // 진행 중인 챕터(기획의 "N편") 인덱스
    public static int ChapterIndex => Slot.outgameChapterIndex;

    // 챕터 안에서의 스텝 순번(시퀀스 전체 통산이 아니다)
    public static int StepIndex => Slot.outgameChapterStepIndex;

    static TutorialSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.tutorial == null) t_data.tutorial = new TutorialSaveData();
            return t_data.tutorial;
        }
    }

    // 부트에서 DataSaveManager.Load() 이후 1회 호출 — 레거시 세이브 완료 판정 포함
    public static void Init()
    {
        var t_slot = Slot;
        if (t_slot.migrationChecked) return;

        if (!t_slot.outgameCompleted
            && t_slot.outgameStepIndex == 0 && t_slot.outgameChapterIndex == 0 && t_slot.outgameChapterStepIndex == 0
            && OwnershipManager.HasAnyOwnedSaved())
            t_slot.outgameCompleted = true;

        t_slot.migrationChecked = true;
        Save();
    }

    // 진행도 영속화
    public static void Save() => DataSaveManager.Save();

    // 진행 좌표를 즉시 커밋(실패 롤백용 감소도 허용)
    public static void CommitStep(int _chapter, int _step)
    {
        if (_chapter < 0 || _step < 0) return;

        var t_slot = Slot;
        t_slot.outgameChapterIndex     = _chapter;
        t_slot.outgameChapterStepIndex = _step;
        Save();
    }

    // 온보딩 완료 낙인
    public static void Complete()
    {
        var t_slot = Slot;
        if (t_slot.outgameCompleted) return;

        t_slot.outgameCompleted = true;
        Save();
    }

    // 이 트리거의 튜토리얼을 이미 완주했는가(None은 완료로 본다)
    public static bool IsTriggerDone(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return true;

        var t_done = Slot.completedTriggers;
        return t_done != null && t_done.Contains(_trigger.ToString());
    }

    // 트리거 튜토리얼 완주 낙인(트리거당 1회)
    public static void MarkTriggerDone(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return;

        var t_slot = Slot;
        if (t_slot.completedTriggers == null) t_slot.completedTriggers = new List<string>();

        string t_key = _trigger.ToString();
        if (t_slot.completedTriggers.Contains(t_key)) return;

        t_slot.completedTriggers.Add(t_key);
        Save();
    }

    // 디버그 전용 — 트리거 낙인만 전부 걷는다(온보딩 좌표·완료는 유지)
    public static void ClearTriggersForDebug()
    {
        var t_slot = Slot;
        if (t_slot.completedTriggers == null) t_slot.completedTriggers = new List<string>();
        else                                  t_slot.completedTriggers.Clear();

        Save();
    }

    // 디버그 전용 — 진행도만 처음으로 되돌린다(소유·재화는 유지)
    public static void ResetForDebug() => JumpForDebug(0, 0);

    // 디버그 전용 — 임의 좌표로 되감고 완료 낙인도 걷는다
    public static void JumpForDebug(int _chapter, int _step)
    {
        if (_chapter < 0 || _step < 0) return;

        var t_slot = Slot;
        t_slot.outgameChapterIndex     = _chapter;
        t_slot.outgameChapterStepIndex = _step;
        t_slot.outgameCompleted        = false;
        t_slot.migrationChecked        = true;
        Save();
    }
}
