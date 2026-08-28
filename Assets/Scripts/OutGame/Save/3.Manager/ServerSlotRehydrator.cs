using UnityEngine;

// 서버가 갈아끼운 슬롯을 매니저 캐시에 다시 태우는 창구.
// 매니저들은 부트 때 세이브를 static 캐시로 떠 놓으므로, 슬롯만 바뀌면 화면은 옛 값을 계속 보여준다.
// 여기서 부르는 것은 Init 계열(캐시 재구축)뿐이다 — 재수화가 DataSaveManager.Save()를 튀기면
// PlayerSaveCloud.AdoptServerResult가 방금 세운 업로드 기준선이 그 자리에서 깨진다
// (근거: DataSaveManager.AdoptServerSlots가 스스로 Save()·OnSaved를 발화하지 않는 이유와 같다).
internal static class ServerSlotRehydrator
{
    /// <summary>서버 슬롯 채택 통지를 구독한다. 여러 번 불러도 구독은 하나다.</summary>
    internal static void Install()
    {
        DataSaveManager.OnServerSlotsAdopted -= Rehydrate;
        DataSaveManager.OnServerSlotsAdopted += Rehydrate;
    }

    // 순서는 SaveDependentManagersStep.InstallOnce와 같다 — CardGrowth가 KeywordGrowth 통지를 구독하므로 뒤집으면 안 된다.
    static void Rehydrate(ESaveSlot _slots)
    {
        if ((_slots & ESaveSlot.Ownership) != 0) RehydrateOwnership();
        if ((_slots & ESaveSlot.KeywordGrowth) != 0) KeywordGrowthManager.Init();
        if ((_slots & ESaveSlot.CardGrowth) != 0) CardGrowthManager.Init();

        // TODO(R5+): Deck은 구독하지 않는다 — DeckSaveManager.LoadFromSave가 Compact 후 SaveAll을 타서
        // 채택 도중 저장이 튄다. deck 슬롯을 서버가 쓰기 시작하면 저장 없는 재구축 경로부터 만들 것.
        // TODO(R7·R9): Rank·AlbumReward·Tournament·Tutorial·Profile 재수화.
    }

    // Init은 카탈로그가 모르는 id를 만나면 스스로 Save()를 부른다. 슬롯 동결 뒤엔 그 저장이 거부되므로 줄어들면 드러낸다.
    static void RehydrateOwnership()
    {
        int t_before = OwnershipManager.OwnedCount;
        OwnershipManager.Init();
        int t_after = OwnershipManager.OwnedCount;

        if (t_after < t_before)
            Debug.LogError($"[ServerSlotRehydrator] 소유 카드가 재수화로 줄었다 {t_before} → {t_after} — 서버가 준 id를 클라 카탈로그가 모른다(시트/SO 드리프트).");
    }
}
