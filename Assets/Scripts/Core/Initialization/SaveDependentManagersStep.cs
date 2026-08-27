using Cysharp.Threading.Tasks;
using UnityEngine;

// 세이브가 채택된 뒤에야 설 수 있는 매니저들. 부트의 마지막 단계이고, 여기 끝이 Ready다.
public sealed class SaveDependentManagersStep : MainInitializer
{
    // 신규 유저에게 기본 지급할 스타터덱(CardPackData의 pool 6장을 고정 순서로 쓴다). 미배선이면 지급을 건너뛴다.
    [SerializeField] CardPackData starterDeck;

    static bool s_installed;

    /// <summary>재화 flush·진행도 표시가 "부트가 끝났는가"를 이 값으로 본다.
    /// 게이트가 아니라 설치 여부로 판정한다 — 세션 중 복구 요구가 뜨면 IsReady가 false로 떨어지는데,
    /// 그때 잔액 flush까지 멈추면 이미 번 재화가 로컬 캐시에도 안 남는다.</summary>
    internal static bool IsInstalled => s_installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_installed = false;

    public override UniTask Initialize(InitializationContext _context)
    {
        GameInitialization.SetState(EGameInitState.InstallingManagers);

        // MarkReady()는 조기 return 바깥이다 — 재시도로 다시 들어오면 설치는 이미 끝나 있어 여기서 걷어차이는데,
        // 그 경로가 Ready 전이까지 삼키면 게이트가 영영 Ready에 닿지 못한다.
        // (MarkReady 자체가 InstallingManagers일 때만 전이하므로 무조건 호출이 안전하다.)
        InstallOnce();

        // 워처도 MarkReady와 같은 이유로 조기 return 바깥이다 — 설치 중 업로드가 던지면 once 플래그만 서고
        // 워처는 미설치로 남아, 재시도가 조기 return하는 순간 그 세션은 배너도 차단 모달도 영영 못 띄운다.
        // (Install 자체가 멱등이라 무조건 호출이 안전하다.)
        CloudSyncStatusWatcher.Install();
        GameInitialization.MarkReady();
        return UniTask.CompletedTask;
    }

    void InstallOnce()
    {
        if (s_installed) return;

        // 클라우드 채택이 끝난 뒤여야 한다 — 채택 전에 슬롯을 갈아엎으면 채택이 그대로 덮어써 무효가 된다.
        OutgameTutorialRewind.ApplyWipeIfScheduled();

        // 스타터 지급의 유일한 근거는 "원격 문서가 없다"이다. 오프라인 폴백 세션은 IsFreshAccount가 false다.
        CurrencyManager.Init(PlayerSaveCloud.IsFreshAccount);
        ProfileManager.Init();
        OwnershipManager.Init();
        OutgameTutorialProgress.Init();
        if (OutgameTutorialProgress.IsCompleted) RankManager.TryEnterFirstTier(out _);
        KeywordGrowthManager.Init();
        CardGrowthManager.Init();
        DeckSaveManager.LoadFromSave();
        StarterDeck.GrantIfNoDeck(starterDeck);
        OutgameTutorialRunner.ResolveProgressAnchor();
        OutgameTutorialRunner.RewindToPendingBattleEntry();
        OutgameTutorialRewind.ApplyReplayIfScheduled();

        s_installed = true;

        // 신규 계정의 첫 문서를 여기서 한 번에 만든다. 이 업로드가 실패하면 원격 문서는 여전히 없으므로
        // 다음 부트도 신규로 판정되고 지급이 정확히 한 번만 남는다(멱등).
        DataSaveManager.SaveImmediate();
    }
}
