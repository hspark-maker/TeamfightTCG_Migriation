using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 세이브 커밋의 단일 진입점.
// 어느 도메인이 커밋을 걸든 캐시를 가진 전 도메인이 함께 flush되므로,
// "재화는 차감 전인데 진행도는 차감 후"인 세이브가 구조적으로 생기지 않는다.
//
// 커밋이 네트워크로 나가면서 지연·실패가 실재하게 됐고, 대기 표시와 실패 알림도 여기 한 곳에 문다 —
// Request()도 CommitAsync()를 지나므로 호출부 15곳이 각자 알 필요가 없다.
public static class SaveTransaction
{
    // 이보다 빨리 끝난 커밋은 대기 표시를 아예 띄우지 않는다 — 한 프레임 깜빡이는 오버레이가 지연보다 더 눈에 띈다.
    const int BUSY_DELAY_MS = 250;

    // 실패 팝업이 떠 있는가. 연속 커밋이 줄줄이 실패해도 팝업은 한 장이다.
    static bool s_failurePopupOpen;

    // 커밋이 진행 중인가(쓰기 대기 중 같은 커맨드가 두 번 들어오는 것을 막는 판정)
    public static bool IsBusy => DataSaveManager.IsWriting;

    /// <summary>전 도메인을 세이브 슬롯에 반영한 뒤 디스크에 1회 쓴다.
    /// 부트가 끝난 Cloud 모드에서는 커밋이 길어지면 입력 차단 오버레이가 서고, 실패하면 사유별 팝업이 뜬다.</summary>
    public static async UniTask<ESaveWriteResult> CommitAsync()
    {
        FlushAll();

        // Local은 파일 1회 쓰기라 기다릴 지연도 사용자가 손쓸 실패도 없다(UI 스택이 없는 에디터 단독 씬도 이 모드다).
        // 부트가 끝나기 전 커밋은 화면의 주인이 부트 흐름이다 — 로딩 커버 위에 겹쳐 세우면
        // 재시도 창구가 둘이 되고, BootInstaller가 쥔 복구 판정(MarkBlockedRetryable)과 엇갈린다.
        if (SaveSourceMode.Current != ESaveSourceMode.Cloud || GameManager.BootState != EGameBootState.Ready)
            return await DataSaveManager.SaveAsync();

        bool t_busyShown   = false;
        var  t_busyCancel  = new CancellationTokenSource();

        ShowBusyAfterDelay().Forget();

        ESaveWriteResult t_result;
        try
        {
            t_result = await DataSaveManager.SaveAsync();
        }
        finally
        {
            // 예외 경로까지 반드시 지나야 하는 자리다 — 오버레이가 남으면 화면이 영구 입력 불가가 된다.
            t_busyCancel.Cancel();
            t_busyCancel.Dispose();
            if (t_busyShown) SaveBusyOverlay.Hide();
        }

        if (t_result != ESaveWriteResult.Success) ShowFailurePopup(t_result);
        return t_result;

        // 취소는 커밋 완료가 건다. 지연이 지나기 전에 끝나면 표시가 아예 서지 않고,
        // 늦게 깨어나 이미 끝난 커밋 위에 오버레이를 세우는 일도 없다.
        async UniTaskVoid ShowBusyAfterDelay()
        {
            bool t_canceled = await UniTask
                .Delay(BUSY_DELAY_MS, ignoreTimeScale: true, cancellationToken: t_busyCancel.Token)
                .SuppressCancellationThrow();
            if (t_canceled) return;

            t_busyShown = true;
            SaveBusyOverlay.Show();
        }
    }

    /// <summary>결과를 기다리지 않는 커밋. 동기 커맨드가 쓰는 창구다.
    /// 실패를 받을 호출자가 없으므로 여기서 로그로라도 남긴다(알림 자체는 CommitAsync가 띄운다).</summary>
    public static void Request() => ReportAsync().Forget();

    static async UniTaskVoid ReportAsync()
    {
        ESaveWriteResult t_result = await CommitAsync();
        if (t_result == ESaveWriteResult.Success) return;

        string t_reason = t_result switch
        {
            ESaveWriteResult.Blocked  => "상위 버전 세이브가 로드돼 쓰기가 봉쇄됐다",
            ESaveWriteResult.Conflict => "다른 기기가 먼저 저장해 서버 문서가 앞서 있다",
            ESaveWriteResult.Offline  => "서버에 도달하지 못했다",
            _                         => "저장 매체 쓰기에 실패했다",
        };

        // Offline은 재시도로 풀리는 일시 상태라 에러로 올리지 않는다.
        if (t_result == ESaveWriteResult.Offline)
            Debug.LogWarning($"[SaveTransaction] 커밋 보류({t_result}): {t_reason}");
        else
            Debug.LogError($"[SaveTransaction] 커밋 실패({t_result}): {t_reason}");
    }

    /// <summary>앱 종료 경로 전용 동기 커밋. 종료 콜백은 비동기 완료를 기다려주지 않는다.</summary>
    internal static ESaveWriteResult CommitBlocking()
    {
        FlushAll();

        // Cloud는 종료 콜백 안에서 네트워크 쓰기를 끝낼 수 없다 — 로컬 저널로 남기고 다음 부팅이 서버와 대조해 올린다.
        return SaveSourceMode.Current == ESaveSourceMode.Cloud
            ? DataSaveManager.WriteJournalBlocking()
            : DataSaveManager.SaveBlocking();
    }

    static void FlushAll()
    {
        CurrencyManager.FlushToData();
        OwnershipManager.FlushToData();
        ProfileManager.FlushToData();
        DeckSaveManager.FlushToData();
        CardGrowthManager.FlushToData();
        KeywordGrowthManager.FlushToData();
    }

    // 실패는 눈에 보여야 한다 — Request()로 던진 커밋도 사용자에게는 같은 저장이라 여기 한 곳에서 알린다.
    static void ShowFailurePopup(ESaveWriteResult _result)
    {
        if (s_failurePopupOpen && IsFailurePopupStillOnScreen()) return;

        var t_data = new SimpleYNPopupData
        {
            titleText = FailureTitleOf(_result),
            onHide    = () => s_failurePopupOpen = false,
        };

        if (_result == ESaveWriteResult.Conflict)
        {
            // Conflict에는 재시도를 주지 않는다 — 쓰기 선조건이 부팅 때 읽은 revision이라
            // 다시 밀어도 같은 자리에서 막혀 무한 반복이 된다. 나가는 문은 재시작뿐이다.
            t_data.yesText   = "앱 종료";
            t_data.yesAction = QuitApp;
            t_data.noText    = "계속";
        }
        else
        {
            t_data.yesText   = "다시 시도";
            t_data.yesAction = RetryCommit;
            t_data.noText    = "나중에";
        }

        // 팝업을 못 띄워도 저장 로직은 살아야 한다(UIPoolManager가 아직 없는 부트 구간).
        if (UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(t_data) == null)
        {
            Debug.LogWarning($"[SaveTransaction] 저장 실패 팝업을 띄우지 못했다({_result}).");
            return;
        }

        s_failurePopupOpen = true;
    }

    // SimpleYNPopup은 한 장을 돌려 쓴다 — 다른 화면이 가져가면 우리 onHide가 영영 안 와서
    // 중첩 방지 플래그가 선 채로 굳는다(그 뒤로 저장 실패가 한 번도 안 보인다). 그래서 화면을 직접 확인한다.
    static bool IsFailurePopupStillOnScreen()
    {
        SimpleYNPopup t_popup = UIPoolManager.instance != null
            ? UIPoolManager.instance.GetUI<SimpleYNPopup>()
            : null;

        if (t_popup != null && t_popup.isShow) return true;

        s_failurePopupOpen = false;
        return false;
    }

    static string FailureTitleOf(ESaveWriteResult _result) => _result switch
    {
        ESaveWriteResult.Offline  => "서버에 연결하지 못해\n저장하지 못했습니다.\n네트워크를 확인해 주세요.",
        ESaveWriteResult.Conflict => "다른 기기에서 먼저 저장해\n이 기기의 변경사항을 반영할 수 없습니다.\n앱을 다시 시작해 주세요.",
        _                         => "저장에 실패했습니다.\n다시 시도해 주세요.",
    };

    // 재시도는 한 프레임 뒤에 건다 — 중첩 방지 플래그를 푸는 팝업의 Hide()가 이 콜백 **뒤에** 오기 때문에,
    // 즉시 다시 커밋해 또 실패하면 그 팝업이 자기 자신에게 막힌다.
    static void RetryCommit() => RetryAsync().Forget();

    static async UniTaskVoid RetryAsync()
    {
        await UniTask.NextFrame();
        Request();
    }

    static void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
