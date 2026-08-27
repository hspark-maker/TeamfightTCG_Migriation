using UnityEngine;

/// <summary>클라우드 세이브 상태를 읽어 유저 표면(지연 배너 · 재시작 모달)을 정하는 유일한 구독자.
/// 판정을 MonoBehaviour 밖에 두는 이유: 배너 프리팹 로드가 실패해도 차단 모달만은 반드시 떠야 한다.</summary>
internal static class CloudSyncStatusWatcher
{
    static bool s_installed;
    static bool s_blockedShown;

    /// <summary>상태 구독을 건다. 여러 번 불러도 안전하며, 설치 즉시 현재 상태를 1회 평가한다.</summary>
    internal static void Install()
    {
        if (s_installed) return;

        s_installed = true;
        PlayerSaveCloud.OnStateChanged += Evaluate;
        Evaluate();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_installed = false;
        s_blockedShown = false;
    }

    static void Evaluate()
    {
        if (PlayerSaveCloud.State == EPlayerSaveCloudState.Blocked)
        {
            CloudSyncBannerView.SetVisible(false);   // 두 표면이 겹치면 안 된다 — 차단은 모달이 전부 말한다
            ShowRestartModal();
            return;
        }

        CloudSyncBannerView.SetVisible(PlayerSaveCloud.ShouldShowSyncBanner);
    }

    static void ShowRestartModal()
    {
        if (s_blockedShown) return;

        UIPoolManager t_pool = UIPoolManager.Instance;
        if (t_pool == null)
        {
            Debug.LogError("[CloudSyncStatusWatcher] UIPoolManager가 없어 재시작 안내를 띄우지 못했습니다.");
            return;
        }

        SimpleYNPopup t_popup = t_pool.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = BlockedNotice(),
            yesText   = "종료",
            yesAction = QuitApp,
            noText    = "계속",
        });

        // 플래그는 실제로 열린 뒤에 세운다 — Blocked는 종점 상태라 OnStateChanged가 다시 오지 않아,
        // 미등록 프리팹 등으로 null이 돌아온 자리에 플래그부터 세우면 차단 안내가 영구 소실된다.
        if (t_popup == null)
        {
            Debug.LogError("[CloudSyncStatusWatcher] 재시작 안내 팝업을 열지 못했습니다.");
            return;
        }

        s_blockedShown = true;
    }

    // 차단 사유가 셋(먼저 저장됨 / 용량 초과 / 세션 불가)인데 문구가 하나면, 나머지 둘을 뒤집어쓴 유저는
    // 재시작만 반복하다 원인에 못 닿는다. 유저가 다음에 할 일이 사유마다 다르므로 문구도 갈라야 한다.
    static string BlockedNotice()
    {
        switch (PlayerSaveCloud.BlockReason)
        {
            case ECloudBlockReason.RemoteAhead:
                return "다른 기기에서 먼저 저장했습니다.\n앱을 다시 시작하면 최신 기록으로 이어집니다.";

            case ECloudBlockReason.DocumentTooLarge:
                return "저장할 기록이 너무 많아 더 이상 저장하지 못합니다.\n" +
                       "지금부터의 진행은 남지 않으니 고객센터로 문의해 주세요.";

            default:
                return "저장을 계속할 수 없습니다.\n앱을 다시 시작해 주세요.";
        }
    }

    // "계속"을 남겨 두는 이유: 이번 세션을 마저 보게 해 줄 뿐이다 — 이 뒤의 진행분은 서버에 올라가지 않는다.
    static void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
