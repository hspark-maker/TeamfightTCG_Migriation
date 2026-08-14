using System.Collections.Generic;
using UnityEngine;

// 아웃게임 첫시작 튜토리얼 진행도의 static 단일 창구(세이브 슬롯 매핑을 여기서만 안다)
public static class OutgameTutorialProgress
{
    // 같은 좌표로 이만큼 더 부팅하면 진행이 막힌 것으로 본다(0=직후, 2=세 번째 부팅).
    // 두 번까지 봐주는 이유: 튜토 도중 앱을 끄는 것은 흔한 일이고, 오탐 대가(기능이 미리 열림)보다
    // 놓쳤을 때 대가(게임 자체를 못 함)가 훨씬 크다.
    const int STALL_BOOT_COUNT = 2;

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

    // 부트에서 DataSaveManager.Load() 이후 1회 호출 — 레거시 세이브 완료 판정과 진행 정지 판정
    public static void Init()
    {
        MigrateLegacyCompletion();
        DetectStall();
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

    // 정지 판정을 지금 좌표에서 다시 세기 시작한다 — 부트마다 자가 복구가 도는 좌표는 "막힌 좌표"가 아니다.
    public static void ResetStallWatch()
    {
        var t_slot = Slot;
        t_slot.lastBootChapterIndex = t_slot.outgameChapterIndex;
        t_slot.lastBootStepIndex    = t_slot.outgameChapterStepIndex;
        t_slot.sameCoordBootCount   = 0;
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

    // 이 해금 개념을 전면으로 안내한 적이 있는가(키는 UnlockIntro가 단독으로 만든다)
    public static bool IsUnlockIntroSeen(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return true;

        var t_seen = Slot.seenUnlockIntros;
        return t_seen != null && t_seen.Contains(_key);
    }

    // 해금 안내 낙인(개념당 1회)
    public static void MarkUnlockIntroSeen(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return;

        var t_slot = Slot;
        if (t_slot.seenUnlockIntros == null) t_slot.seenUnlockIntros = new List<string>();

        if (t_slot.seenUnlockIntros.Contains(_key)) return;

        t_slot.seenUnlockIntros.Add(_key);
        Save();
    }

    // 디버그 전용 — 트리거 낙인과 해금 안내 낙인을 전부 걷는다(온보딩 좌표·완료는 유지)
    public static void ClearTriggersForDebug()
    {
        var t_slot = Slot;
        if (t_slot.completedTriggers == null) t_slot.completedTriggers = new List<string>();
        else                                  t_slot.completedTriggers.Clear();

        if (t_slot.seenUnlockIntros == null) t_slot.seenUnlockIntros = new List<string>();
        else                                 t_slot.seenUnlockIntros.Clear();

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

    // 소유 카드가 이미 있는 구 세이브는 튜토리얼을 마친 것으로 본다(계정당 1회)
    static void MigrateLegacyCompletion()
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

    // 부팅을 거듭해도 좌표가 그대로면 그 스텝은 스스로 풀릴 수 없는 것이다 — 안내는 멈추더라도 게임은 열어 준다.
    // 진입 실패는 브리지가 그 자리에서 잡고, 여기는 신호를 영영 못 받는 대기형 정지(앵커 미등록 등)를 잡는다.
    static void DetectStall()
    {
        var t_slot = Slot;
        if (t_slot.outgameCompleted) return;

        if (t_slot.lastBootChapterIndex != t_slot.outgameChapterIndex
         || t_slot.lastBootStepIndex    != t_slot.outgameChapterStepIndex)
        {
            t_slot.lastBootChapterIndex = t_slot.outgameChapterIndex;
            t_slot.lastBootStepIndex    = t_slot.outgameChapterStepIndex;
            t_slot.sameCoordBootCount   = 0;
            Save();
            return;
        }

        t_slot.sameCoordBootCount++;
        Save();

        if (t_slot.sameCoordBootCount < STALL_BOOT_COUNT) return;

        Debug.LogWarning($"[OutgameTutorialProgress] 좌표 {t_slot.outgameChapterIndex}-{t_slot.outgameChapterStepIndex}에서 "
                       + $"{t_slot.sameCoordBootCount + 1}번째 부팅 — 진행이 막힌 것으로 보고 기능 잠금을 해제합니다.");
        OutgameFeatureLock.NotifyStalled();
    }
}
