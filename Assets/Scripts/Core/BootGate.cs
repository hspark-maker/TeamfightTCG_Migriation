using Cysharp.Threading.Tasks;
using UnityEngine;

// 부트 게이트 신호의 단일 창구. 세이브 동기화 부품이 무엇이든(로컬·클라우드) 부트를 여는 신호는 여기 하나다 —
// 신호를 특정 부품 안에 두면 그 부품이 꺼지는 구성에서 부트가 영영 열리지 않는다.
// 차단·재시도 신호도 같은 이유로 여기 모은다(차단 화면은 이 값만 보고 그린다).
internal static class BootGate
{
    static bool s_complete;
    static ESaveBootBlockReason s_blockReason;
    static UniTaskCompletionSource s_retrySource = new UniTaskCompletionSource();

    // 대기가 걸리기 전에 눌린 재시도를 잃지 않기 위한 걸쇠.
    static bool s_retryRequested;

    /// <summary>부트 게이트가 열렸는지. 소비자는 이 값만 보고 세이브 의존 설치를 시작한다.</summary>
    internal static bool IsComplete => s_complete;

    /// <summary>지금 부트를 막고 있는 사유(없으면 None). 표시 문구는 화면이 고른다.</summary>
    internal static ESaveBootBlockReason BlockReason => s_blockReason;

    /// <summary>게이트를 연다. 동기화 성공·실패·타임아웃 모든 종료 경로가 여기로 모인다.</summary>
    internal static void MarkComplete()
    {
        s_complete = true;
    }

    /// <summary>차단 사유를 남긴다(풀렸으면 None).</summary>
    internal static void SetBlockReason(ESaveBootBlockReason _reason)
    {
        s_blockReason = _reason;
    }

    /// <summary>재시도를 요청한다. 게이트를 열지는 않는다 — 기다리던 부트 단계를 깨울 뿐이다.</summary>
    internal static void RequestRetry()
    {
        s_retryRequested = true;
        s_retrySource.TrySetResult();
    }

    /// <summary>재시도 요청이 올 때까지 기다린다. 대기 전에 이미 눌렸으면 그 자리에서 돌아온다.</summary>
    internal static async UniTask WaitForRetryAsync()
    {
        if (!s_retryRequested)
            await s_retrySource.Task;

        s_retryRequested = false;
        s_retrySource = new UniTaskCompletionSource();
    }

    /// <summary>게이트를 닫힌 상태로 되돌린다(부트 재시작·도메인 리로드 비활성 대응).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Reset()
    {
        s_complete = false;
        s_blockReason = ESaveBootBlockReason.None;
        s_retryRequested = false;

        // 소스를 그냥 갈아치우면 대기 중이던 부트 단계가 영영 돌아오지 못한다 — 먼저 깨워서 내보낸다.
        s_retrySource.TrySetResult();
        s_retrySource = new UniTaskCompletionSource();
    }
}
