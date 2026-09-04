using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameInitialization
{
    static readonly List<ReadySubscription> s_readySubscriptions = new();

    internal static EGameInitState State { get; private set; } = EGameInitState.Initializing;

    /// <summary>UpdateRequired 로 끊긴 사유. 다른 상태에서는 의미가 없다.</summary>
    internal static EUpdateRequiredReason UpdateReason { get; private set; } = EUpdateRequiredReason.Content;

    public static bool IsReady => State == EGameInitState.Ready;
    public static bool IsTerminated =>
        State == EGameInitState.UpdateRequired || State == EGameInitState.RecoveryRequired;

    /// <summary>다시 태우면 답이 달라질 수 있는 실패인가(업데이트 필요는 아니다).</summary>
    public static bool CanRetry => State == EGameInitState.RecoveryRequired;

    public static float Progress
    {
        get
        {
            float t_dataProgress = UiPrefabCache.IsComplete ? 1f : Mathf.Min(UiPrefabCache.LoadProgress, 0.99f);
            float t_syncProgress = SaveDependentManagersStep.IsInstalled ? 1f : 0.5f;
            float t_artProgress = CardArtCache.IsComplete ? 1f : Mathf.Min(CardArtCache.LoadProgress, 0.99f);
            float t_packArtProgress = PackArtCache.IsComplete ? 1f : Mathf.Min(PackArtCache.LoadProgress, 0.99f);
            return (t_dataProgress + t_syncProgress + t_artProgress + t_packArtProgress) / 4f;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        State = EGameInitState.Initializing;
        UpdateReason = EUpdateRequiredReason.Content;
        s_readySubscriptions.Clear();
    }

    public static IDisposable WhenReady(Action _callback)
    {
        if (_callback == null) throw new ArgumentNullException(nameof(_callback));

        if (IsReady)
        {
            try
            {
                _callback.Invoke();
            }
            catch (Exception t_exception)
            {
                Debug.LogException(t_exception);
            }
            return ReadySubscription.Empty;
        }

        if (IsTerminated) return ReadySubscription.Empty;

        var t_subscription = new ReadySubscription(_callback);
        s_readySubscriptions.Add(t_subscription);
        return t_subscription;
    }

    internal static void SetState(EGameInitState _next)
    {
        if (State == _next) return;

        State = _next;
        if (IsTerminated)
        {
            foreach (ReadySubscription t_subscription in s_readySubscriptions)
                t_subscription.Dispose();
            s_readySubscriptions.Clear();
            return;
        }

        if (_next != EGameInitState.Ready) return;

        ReadySubscription[] t_subscriptions = s_readySubscriptions.ToArray();
        s_readySubscriptions.Clear();
        foreach (ReadySubscription t_subscription in t_subscriptions)
        {
            try
            {
                t_subscription.Invoke();
            }
            catch (Exception t_exception)
            {
                Debug.LogException(t_exception);
            }
        }
    }

    internal static void MarkReady()
    {
        if (State == EGameInitState.InstallingManagers)
            SetState(EGameInitState.Ready);
    }

    internal static void MarkRecoveryRequired()
    {
        SetState(EGameInitState.RecoveryRequired);
    }

    /// <param name="_reason">업데이트 화면이 문구와 스토어 버튼을 가르는 축. 기존 호출부는 전부
    /// 콘텐츠 표 세대 사고라 기본값이 Content다.</param>
    internal static void MarkUpdateRequired(EUpdateRequiredReason _reason = EUpdateRequiredReason.Content)
    {
        // 사유는 상태보다 먼저 세운다 — SetState가 구독을 끊으며 화면 전이를 유발할 수 있는데,
        // 그 화면이 아직 갱신 안 된 사유를 읽으면 첫 표시가 틀린다.
        UpdateReason = _reason;
        SetState(EGameInitState.UpdateRequired);
    }

    /// <summary>재시도가 종료 상태만 되돌린다. 종료 때 지워진 WhenReady 구독은 복원되지 않는다.</summary>
    internal static void ResetForRetry()
    {
        if (!IsTerminated) return;

        SetState(EGameInitState.SyncingSave);
    }

    sealed class ReadySubscription : IDisposable
    {
        internal static readonly ReadySubscription Empty = new(null);

        Action m_callback;

        internal ReadySubscription(Action _callback)
        {
            m_callback = _callback;
        }

        public void Dispose()
        {
            m_callback = null;
        }

        internal void Invoke()
        {
            Action t_callback = m_callback;
            m_callback = null;
            t_callback?.Invoke();
        }
    }
}
