using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>서버 버전 정책을 대조해 "이 빌드로 계속 갈 수 있는가"를 정한다.
///
/// <para>저작 위치는 Firebase 가 선 뒤, <c>SpecSyncStep</c> <b>앞</b>이다 — 지원이 끝난 빌드에
/// 표 스냅샷을 통째로 내려보내고 나서 막으면 그 트래픽이 통째로 낭비된다.</para>
///
/// <para><c>required</c> 는 <b>false</b>로 저작한다. 통지를 못 읽은 것은 초기화 실패가 아니다.
/// <c>retryEntry</c> 는 켜지 않는다 — 러너가 "정확히 하나"를 요구하고 그 자리는
/// <c>WaitCloudSaveGateStep</c> 이 이미 갖고 있다.</para></summary>
public sealed class AppVersionGateStep : MainInitializer
{
    [Tooltip("Test 런모드에서는 버전 대조를 건너뛴다. 끄면 로컬 개발 빌드가 라이브 정책에 막힌다.")]
    [SerializeField] bool bypassInTestMode = true;

    public override async UniTask Initialize(InitializationContext _context)
    {
        if (bypassInTestMode && _context.Profile != null && _context.Profile.RunMode == EContentRunMode.Test)
        {
            Debug.Log("[AppVersionGateStep] Test 런모드라 버전 대조를 건너뜁니다.");
            return;
        }

        try
        {
            await AppVersionGate.EvaluateAsync(this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) when (this == null || _context.IsAborted)
        {
            // 초기화 루트가 정리되는 정상 취소다.
            return;
        }
        catch (Exception t_exception)
        {
            // EvaluateAsync 는 스스로 삼키지만, 그래도 여기서 초기화를 끊지 않는다는 것을 명시해 둔다.
            Debug.LogWarning($"[AppVersionGateStep] 버전 대조에 실패해 통지 없이 진행합니다: {t_exception.Message}");
            return;
        }

        if (AppVersionGate.Action != EAppVersionAction.Blocked) return;

        // 차단은 SpecSyncStep 의 콘텐츠 업데이트와 같은 출구를 쓴다 — 화면은 하나고 사유만 갈린다.
        GameInitialization.MarkUpdateRequired(EUpdateRequiredReason.AppVersion);
        Destroy(_context.Root);
        _context.Abort();
    }
}
