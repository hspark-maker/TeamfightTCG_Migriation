using Cysharp.Threading.Tasks;
using UnityEngine;

// 세이브가 채택된 뒤에야 설 수 있는 매니저들. 초기화의 마지막 단계이고, 여기 끝이 Ready다.
public sealed class SaveDependentManagersStep : MainInitializer
{
    // 신규 유저 스타터덱의 CardPack.packId. CardPackDrop 앞 6장을 고정 순서로 쓴다.
    [SerializeField] string starterDeckPackId = "StarterPack";

    static bool s_installed;

    // 설치가 되감기 서버 왕복을 기다리는 동안 복구 재시도가 이 스텝을 다시 태울 수 있다 —
    // 그 재진입을 여기 합류시키지 않으면 세이브를 두 번 민다.
    static UniTaskCompletionSource s_installing;

    /// <summary>재화 flush·진행도 표시가 "초기화가 끝났는가"를 이 값으로 본다.
    /// 게이트가 아니라 설치 여부로 판정한다 — 세션 중 복구 요구가 뜨면 IsReady가 false로 떨어지는데,
    /// 그때 잔액 flush까지 멈추면 이미 번 재화가 로컬 캐시에도 안 남는다.</summary>
    internal static bool IsInstalled => s_installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_installed  = false;
        s_installing = null;
    }

    public override async UniTask Initialize(InitializationContext _context)
    {
        GameInitialization.SetState(EGameInitState.InstallingManagers);

        // MarkReady()는 조기 return 바깥이다 — 재시도로 다시 들어오면 설치는 이미 끝나 있어 여기서 걷어차이는데,
        // 그 경로가 Ready 전이까지 삼키면 게이트가 영영 Ready에 닿지 못한다.
        // (MarkReady 자체가 InstallingManagers일 때만 전이하므로 무조건 호출이 안전하다.)
        await InstallOnce();

        // 워처도 MarkReady와 같은 이유로 조기 return 바깥이다 — 설치 중 업로드가 던지면 once 플래그만 서고
        // 워처는 미설치로 남아, 재시도가 조기 return하는 순간 그 세션은 배너도 차단 모달도 영영 못 띄운다.
        // (Install 자체가 멱등이라 무조건 호출이 안전하다.)
        CloudSyncStatusWatcher.Install();

        // 전역 터치 이펙트도 워처와 같은 상시 오버레이다 — 자체 멱등이라 재시도 경로에서도 무조건 호출한다.
        TouchEffectOverlay.Install();

        GameInitialization.MarkReady();
    }

    async UniTask InstallOnce()
    {
        // 앞선 설치가 되감기 왕복 중이면 합류해 기다린다. 그것이 실패로 끝났다면 s_installed가 서지 않으므로
        // 이 호출이 이어서 설치를 맡는다 — 재시도가 조용히 미설치로 빠지지 않게 하는 자리다.
        while (s_installing != null) await s_installing.Task;
        if (s_installed) return;

        UniTaskCompletionSource t_gate = new UniTaskCompletionSource();
        s_installing = t_gate;

        try
        {
            // 클라우드 채택이 끝난 뒤여야 한다 — 채택 전에 밀면 채택이 슬롯을 그대로 덮어써 무효가 된다.
            // 매니저 Init()들보다 앞이어야 하는 것도 같은 이유다(캐싱된 슬롯 참조는 갱신되지 않는다).
            await OutgameTutorialRewind.ApplyWipeIfScheduled();

            // 정상 초기화의 스타터는 서버(ensureAccount)가 이미 문서에 넣어 왔다.
            // 아래 Init과 GrantIfNoDeck은 위 되감기가 슬롯을 비웠을 때만 서는 안전망이다.
            ProfileManager.Init();
            OwnershipManager.Init();
            OutgameTutorialProgress.Init();
            if (OutgameTutorialProgress.IsCompleted) RankManager.TryEnterFirstTier(out _);
            KeywordGrowthManager.Init();
            CardGrowthManager.Init();
            DeckSaveManager.LoadFromSave();
            StarterDeck.GrantIfNoDeck(starterDeckPackId);
            OutgameTutorialRunner.ResolveProgressAnchor();
            OutgameTutorialRunner.RewindToPendingBattleEntry();
            OutgameTutorialRewind.ApplyReplayIfScheduled();
            ServerSlotRehydrator.Install();

            s_installed = true;

            // 문서는 이미 서버(ensureAccount)가 만들어 뒀다 — 여기 업로드는 설치 중 생긴 변경분을 올린다.
            // 지급의 멱등은 그 callable이 진다(문서가 있으면 쓰지 않는다).
            DataSaveManager.SaveImmediate();
        }
        finally
        {
            s_installing = null;
            t_gate.TrySetResult();
        }
    }
}
