using System.Collections.Generic;

// 아웃게임 첫시작 튜토리얼 진행도의 static 단일 창구.
// 세이브 슬롯(TutorialSaveData) 매핑을 여기서만 안다 — 러너·브리지·UI는 이 API로만 진행도를 읽고 쓴다.
// 메모리 캐시를 두지 않고 슬롯을 직접 읽는다(DataSaveManager.Load가 Data를 교체하므로 캐시는 stale 위험).
public static class OutgameTutorialProgress
{
    public static bool IsCompleted => Slot.outgameCompleted;

    /// <summary>진행 중인 챕터(기획의 "N편") 인덱스.</summary>
    public static int ChapterIndex => Slot.outgameChapterIndex;

    /// <summary>챕터 "안"에서의 스텝 순번. 시퀀스 전체 통산이 아니다.</summary>
    public static int StepIndex => Slot.outgameChapterStepIndex;

    // 슬롯 접근 단일 지점. 손상·구 세이브로 노드가 비어도 크래시 대신 기본값으로 살아난다.
    static TutorialSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.tutorial == null) t_data.tutorial = new TutorialSaveData();
            return t_data.tutorial;
        }
    }

    // 부트에서 DataSaveManager.Load() 이후 1회 호출. 레거시 세이브 마이그레이션도 여기서 판정한다.
    public static void Init()
    {
        var t_slot = Slot;
        if (t_slot.migrationChecked) return;   // 판정은 계정당 1회. 재판정은 아래 낙인의 의미를 무너뜨린다.

        // 튜토리얼 도입 이전 세이브: 소유는 있는데 진행도 0 → 이미 플레이한 유저이므로 완료 처리.
        // 소유 0인 신규 유저는 아무 것도 하지 않고 낙인만 남긴다 — 이후 러너가 한 번도 못 돌아(SO 미배선·자동구매 실패)
        // 좌표가 0에 머문 채 수동 구매로 소유가 생겨도 다시는 이 판정에 걸리지 않게 한다.
        // 진행도 항은 좌표 2개 + 동결된 구 필드까지 전부 본다. 낙인 이전 세이브라면 셋 다 0이라 사실상
        // 소유 여부만 남지만, 항을 줄이면 조건이 넓어지는 방향이라 그대로 둔다(좁히는 쪽은 언제나 안전).
        // HasAnyOwnedSaved는 세이브를 직접 읽으므로 OwnershipManager.Init 이전에 호출해도 안전하다.
        if (!t_slot.outgameCompleted
            && t_slot.outgameStepIndex == 0 && t_slot.outgameChapterIndex == 0 && t_slot.outgameChapterStepIndex == 0
            && OwnershipManager.HasAnyOwnedSaved())
            t_slot.outgameCompleted = true;

        t_slot.migrationChecked = true;
        Save();
    }

    // 진행도는 슬롯에 직접 쓰이므로 영속화만 한다(별도 캐시 flush 없음).
    public static void Save() => DataSaveManager.Save();

    // 스텝 진입 직전에 다음 좌표를 커밋한다. 씬 왕복·강제종료를 견뎌야 하므로 지연 flush에 맡기지 않고 즉시 Save
    // (OwnershipManager.Grant가 자체 Save하는 선례). 실패 롤백용으로 감소도 허용한다.
    public static void CommitStep(int _chapter, int _step)
    {
        if (_chapter < 0 || _step < 0) return;   // 음수는 진행도 오염이라 무시.

        var t_slot = Slot;
        t_slot.outgameChapterIndex     = _chapter;
        t_slot.outgameChapterStepIndex = _step;
        Save();
    }

    public static void Complete()
    {
        var t_slot = Slot;
        if (t_slot.outgameCompleted) return;

        t_slot.outgameCompleted = true;
        Save();
    }

    /// <summary>이 트리거의 튜토리얼을 이미 완주했는가. None은 발화할 것이 없으므로 완료로 본다.</summary>
    public static bool IsTriggerDone(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return true;

        var t_done = Slot.completedTriggers;
        return t_done != null && t_done.Contains(_trigger.ToString());
    }

    /// <summary>완주 낙인. 트리거당 1회만 남는다(인덱스가 아니라 enum 이름 문자열이라 값 재배치에 안전하다).</summary>
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

    // 디버그 전용: 트리거 튜토리얼 낙인만 전부 걷는다(온보딩 좌표·완료는 건드리지 않음).
    // 모르는 문자열까지 함께 지워진다 — 디버그 의도가 "전부 다시 보기"라 그게 맞다.
    public static void ClearTriggersForDebug()
    {
        var t_slot = Slot;
        if (t_slot.completedTriggers == null) t_slot.completedTriggers = new List<string>();
        else                                  t_slot.completedTriggers.Clear();

        Save();
    }

    // 디버그 전용: 진행도만 처음으로 되돌린다(소유·재화는 건드리지 않음).
    public static void ResetForDebug() => JumpForDebug(0, 0);

    // 디버그 전용: 임의 좌표로 되감는다. 완료 낙인도 함께 걷어야 러너가 다시 돈다.
    public static void JumpForDebug(int _chapter, int _step)
    {
        if (_chapter < 0 || _step < 0) return;

        var t_slot = Slot;
        t_slot.outgameChapterIndex     = _chapter;
        t_slot.outgameChapterStepIndex = _step;
        t_slot.outgameCompleted        = false;
        t_slot.migrationChecked        = true;   // 의도는 "튜토리얼 다시 보기" — 되돌리면 남아 있는 소유 탓에 다음 부트가 곧장 완료 처리한다.
        Save();
    }
}
