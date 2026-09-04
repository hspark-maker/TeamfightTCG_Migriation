using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>막지 않는 버전 표면(권장 업데이트 · 공지)을 띄우는 유일한 구독자.
///
/// <para>차단(<see cref="EAppVersionAction.Blocked"/>)은 여기 오지 않는다 — 그쪽은 초기화를 끊고
/// <c>LoadingCoverView</c> 의 업데이트 화면이 전부 말한다. 여기는 <b>게임에 들어온 뒤</b>의 안내다.</para>
///
/// <para>로딩 커버가 걷힌 뒤에 띄운다. 커버는 다음 씬 위까지 살아남으므로 그 전에 띄우면
/// 팝업이 커버 밑에 깔린 채로 유저가 못 보고 지나간다.</para></summary>
internal static class AppUpdateNoticeWatcher
{
    /// <summary>커버가 걷히기를 기다리는 상한(초). 넘기면 안내를 포기한다 —
    /// 걷히지 않는 화면 위에 팝업을 쌓아 봐야 아무도 못 본다.</summary>
    const float CoverWaitSeconds = 30f;

    static bool s_installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_installed = false;

    /// <summary>안내를 예약한다. 여러 번 불러도 안전하고, 띄울 것이 없으면 아무것도 하지 않는다.</summary>
    internal static void Install()
    {
        if (s_installed) return;
        if (!AppVersionGate.HasPendingRecommendation &&
            !AppVersionGate.HasPendingNotice &&
            !ContentUpdateNotice.HasPending) return;

        // 튜토리얼 중에는 띄우지 않는다. 커버가 걷힌 자리가 로비라는 보장이 없고(첫 스텝이 전투 직행이면
        // 전투 씬이다), 로비라 해도 튜토리얼 손가락·탭 캐처 위에 모달이 겹치면 진행이 막힌다.
        // 어차피 방금 설치한 유저라 이 안내가 가장 필요 없는 대상이다 — 다음 실행에서 뜬다.
        if (!OutgameTutorialProgress.IsCompleted)
        {
            Debug.Log("[AppUpdateNoticeWatcher] 튜토리얼 진행 중이라 버전 안내를 미룹니다.");
            return;
        }

        s_installed = true;
        RunAsync().Forget();
    }

    static async UniTaskVoid RunAsync()
    {
        if (!await WaitForVisibleScreenAsync()) return;

        // 권장 업데이트가 먼저다 — 공지보다 유저가 할 일이 분명하다.
        if (AppVersionGate.HasPendingRecommendation)
        {
            await ShowAsync(BuildRecommendation());
            await UniTask.NextFrame();   // 풀이 같은 인스턴스를 재사용하므로 한 프레임 비운 뒤 다음 것을 올린다
        }

        if (AppVersionGate.HasPendingNotice)
        {
            await ShowAsync(BuildAppNotice());
            await UniTask.NextFrame();
        }

        if (ContentUpdateNotice.HasPending) await ShowAsync(BuildContentNotice());
    }

    static async UniTask<bool> WaitForVisibleScreenAsync()
    {
        float t_waited = 0f;
        while (LoadingCoverView.IsCovering)
        {
            if (GameInitialization.IsTerminated) return false;
            if (t_waited >= CoverWaitSeconds)
            {
                Debug.LogWarning("[AppUpdateNoticeWatcher] 로딩 커버가 걷히지 않아 버전 안내를 띄우지 못했습니다.");
                return false;
            }

            await UniTask.Delay(100, DelayType.Realtime);
            t_waited += 0.1f;
        }

        return true;
    }

    static SimpleYNPopupData BuildRecommendation()
    {
        string t_message = string.IsNullOrEmpty(AppVersionGate.LatestText)
            ? "새 버전이 출시되었습니다.\n스토어에서 업데이트해 주세요."
            : $"새 버전({AppVersionGate.LatestText})이 출시되었습니다.\n스토어에서 업데이트해 주세요.";

        // 주소가 없으면 "업데이트" 버튼이 눌러도 아무 일 없는 버튼이 된다 — 그때는 확인만 남긴다.
        bool t_canOpenStore = AppVersionGate.HasStoreUrl;

        return new SimpleYNPopupData
        {
            titleText = t_message,
            yesText   = t_canOpenStore ? "업데이트" : "확인",
            yesAction = t_canOpenStore ? (Action)OpenStore : null,
            noText    = "나중에",
            onHide    = AppVersionGate.MarkRecommendationHandled,
        };
    }

    static SimpleYNPopupData BuildAppNotice()
    {
        string t_title = AppVersionGate.NoticeTitle;
        string t_message = string.IsNullOrEmpty(t_title)
            ? AppVersionGate.NoticeBody
            : $"{t_title}\n\n{AppVersionGate.NoticeBody}";

        return new SimpleYNPopupData
        {
            titleText = t_message,
            yesText   = "확인",
            noText    = "닫기",
            // 닫은 뒤에야 봤다고 남긴다 — 띄우기 전에 남기면 팝업 생성이 실패한 세션에서 공지가 영영 소실된다.
            onHide    = AppVersionGate.MarkNoticeSeen,
        };
    }

    static SimpleYNPopupData BuildContentNotice()
    {
        string t_title = ContentUpdateNotice.Title;
        string t_message = string.IsNullOrEmpty(t_title)
            ? ContentUpdateNotice.Body
            : $"{t_title}\n\n{ContentUpdateNotice.Body}";

        return new SimpleYNPopupData
        {
            titleText = t_message,
            yesText   = "확인",
            noText    = "닫기",
            onHide    = ContentUpdateNotice.MarkSeen,
        };
    }

    static async UniTask ShowAsync(SimpleYNPopupData _data)
    {
        UIPoolManager t_pool = UIPoolManager.Instance;
        if (t_pool == null)
        {
            Debug.LogError("[AppUpdateNoticeWatcher] UIPoolManager가 없어 버전 안내를 띄우지 못했습니다.");
            return;
        }

        var t_closed = new UniTaskCompletionSource();
        Action t_onHide = _data.onHide;
        _data.onHide = () =>
        {
            t_onHide?.Invoke();
            t_closed.TrySetResult();
        };

        if (t_pool.AddOrUpdateUI<SimpleYNPopup>(_data) == null)
        {
            // 프리팹 미등록 등으로 못 열었다. 여기서 기다리면 다음 안내까지 통째로 막힌다.
            Debug.LogError("[AppUpdateNoticeWatcher] 버전 안내 팝업을 열지 못했습니다.");
            return;
        }

        await t_closed.Task;
    }

    static void OpenStore()
    {
        if (!AppVersionGate.HasStoreUrl) return;
        Application.OpenURL(AppVersionGate.StoreUrl);
    }
}
