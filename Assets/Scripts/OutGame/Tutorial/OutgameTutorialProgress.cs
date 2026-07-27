// 아웃게임 첫시작 튜토리얼 진행도의 static 단일 창구.
// 세이브 슬롯(TutorialSaveData) 매핑을 여기서만 안다 — 러너·브리지·UI는 이 API로만 진행도를 읽고 쓴다.
// 메모리 캐시를 두지 않고 슬롯을 직접 읽는다(DataSaveManager.Load가 Data를 교체하므로 캐시는 stale 위험).
public static class OutgameTutorialProgress
{
    public static bool IsCompleted => Slot.outgameCompleted;
    public static int StepIndex => Slot.outgameStepIndex;

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
        // stepIndex가 0에 머문 채 수동 구매로 소유가 생겨도 다시는 이 판정에 걸리지 않게 한다.
        // HasAnyOwnedSaved는 세이브를 직접 읽으므로 OwnershipManager.Init 이전에 호출해도 안전하다.
        if (!t_slot.outgameCompleted && t_slot.outgameStepIndex == 0 && OwnershipManager.HasAnyOwnedSaved())
            t_slot.outgameCompleted = true;

        t_slot.migrationChecked = true;
        Save();
    }

    // 진행도는 슬롯에 직접 쓰이므로 영속화만 한다(별도 캐시 flush 없음).
    public static void Save() => DataSaveManager.Save();

    // 스텝 진입 직전에 다음 인덱스를 커밋한다. 씬 왕복·강제종료를 견뎌야 하므로 지연 flush에 맡기지 않고 즉시 Save
    // (OwnershipManager.Grant가 자체 Save하는 선례). 실패 롤백용으로 감소도 허용한다.
    public static void CommitStep(int _index)
    {
        if (_index < 0) return;   // 음수는 진행도 오염이라 무시.

        Slot.outgameStepIndex = _index;
        Save();
    }

    public static void Complete()
    {
        var t_slot = Slot;
        if (t_slot.outgameCompleted) return;

        t_slot.outgameCompleted = true;
        Save();
    }

    // 디버그 전용: 진행도만 초기화한다(소유·재화는 건드리지 않음).
    public static void ResetForDebug()
    {
        var t_slot = Slot;
        t_slot.outgameStepIndex = 0;
        t_slot.outgameCompleted = false;
        t_slot.migrationChecked = true;   // 리셋의 의도는 "튜토리얼 다시 보기" — 되돌리면 남아 있는 소유 탓에 다음 부트가 곧장 완료 처리한다.
        Save();
    }
}
