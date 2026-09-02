using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Firebase 인증 뒤 최신 콘텐츠 스냅샷을 확인하고, 후속 스펙 소비 단계 전에 메모리에 채택한다.
/// 씬의 requiredIds에는 프로필·Firebase 인증 단계를, 소비 단계에는 이 스텝 id를 지정해야 한다.
/// </summary>
public sealed class SpecSyncStep : MainInitializer
{
    public override async UniTask Initialize(InitializationContext _context)
    {
        try
        {
            await BattleContentSync.SyncForInitializationAsync(this.GetCancellationTokenOnDestroy());
        }
        catch (ContentUpdateRequiredException)
        {
            GameInitialization.MarkUpdateRequired();
            Destroy(_context.Root);
            _context.Abort();
        }
        catch (OperationCanceledException) when (this == null || _context.IsAborted)
        {
            // 초기화 루트가 정리되는 정상 취소다.
        }
        catch (Exception t_exception)
        {
            FailToRecovery(_context, t_exception);
        }
    }
}
