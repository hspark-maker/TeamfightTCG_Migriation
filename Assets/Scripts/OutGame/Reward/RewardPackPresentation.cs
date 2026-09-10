using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>서버가 이미 지급한 팩과 직접 지급 카드를 응답의 순서대로 보여준다.</summary>
public static class RewardPackPresentation
{
    static Queue<(string PackId, List<DrawnCard> Cards, bool IsPack)> s_pending = new();
    static List<(CurrencyGain Refund, List<int> Cards)> s_lobbyRewards = new();
    static CancellationToken s_lifetime;
    static bool s_running;

    public static void Show(RewardClaimOutcome outcome)
    {
        if (!outcome.Succeeded) return;

        // 이전 계정/플레이의 큐가 새 Firebase 세션으로 넘어가지 않게 한다.
        var t_token = FirebaseManager.Lifetime;
        if (s_lifetime != t_token)
        {
            s_pending = new();
            s_lobbyRewards = new();
            s_lifetime = t_token;
            s_running = false;
        }

        // 호출자가 응답 목록을 재사용해도 대기 중인 결과와 순서는 바뀌지 않는다.
        if (outcome.PresentationBatches != null)
        {
            foreach (var t_batch in outcome.PresentationBatches)
                if (t_batch.Cards != null && t_batch.Cards.Count > 0)
                    s_pending.Enqueue((t_batch.PackId, new List<DrawnCard>(t_batch.Cards), t_batch.IsPack));
        }
        else
        {
            if (outcome.Packs != null)
                foreach (var t_pack in outcome.Packs)
                    if (t_pack?.Cards != null && t_pack.Cards.Count > 0)
                        s_pending.Enqueue((t_pack.PackId, new List<DrawnCard>(t_pack.Cards), true));

            if (outcome.Cards != null && outcome.Cards.Count > 0)
                s_pending.Enqueue((null, new List<DrawnCard>(outcome.Cards), false));
        }

        if (s_running || s_pending.Count == 0) return;
        s_running = true;
        RunAsync(s_pending, s_lobbyRewards, t_token).Forget();
    }

    // 보상 개봉의 [획득]은 로비 인계만 미룬다. 중간 OnClosed가 빈 캐리어를 지나가므로
    // 팩마다 도감 삽입을 요구하지 않고, 전체 결과를 닫은 뒤 한 번에 비행한다.
    internal static bool TryDeferLobbyReward(CurrencyGain _refund, IReadOnlyList<int> _cards)
    {
        if (!s_running || s_lifetime.IsCancellationRequested) return false;
        s_lobbyRewards.Add((_refund, new List<int>(_cards)));
        return true;
    }

    static async UniTask RunAsync(Queue<(string PackId, List<DrawnCard> Cards, bool IsPack)> _queue,
                                 List<(CurrencyGain Refund, List<int> Cards)> _lobbyRewards,
                                 CancellationToken _token)
    {
        bool t_waitingForLobby = CardPackRewardHandoff.HasPending && LobbyGainEffectDirector.Exists;
        void OnPackClosed() => t_waitingForLobby = LobbyGainEffectDirector.Exists;
        void OnGainFinished()
        {
            if (!PackOpenOverlay.IsOpen) t_waitingForLobby = false;
        }

        // 기존 개봉이 닫힐 때도 기다린다. OnClosed 안에서 바로 다음 팩을 열면
        // 뒤쪽 구독자와 한 프레임 늦게 시작하는 로비 연출을 앞질러 버린다.
        PackOpenOverlay.OnClosed += OnPackClosed;
        LobbyGainEffectDirector.OnAnyFinished += OnGainFinished;
        try
        {
            while (true)
            {
                await UniTask.NextFrame(cancellationToken: _token);
                await UniTask.WaitUntil(() =>
                    !PackOpenOverlay.IsOpen && !PackHandoff.HasPending && !PackPurchaseFlow.IsPurchasing
                    && !CardSetRewardOverlay.IsOpen && !RewardClaimPopup.IsOpen
                    && (!LobbyGainEffectDirector.Exists ||
                        (!t_waitingForLobby && !CardPackRewardHandoff.HasPending))
                    && !AlbumInsertSession.IsRunning
                    && (!LobbyGainEffectDirector.Exists || !AlbumInsertQueue.HasPending),
                    cancellationToken: _token);

                if (_queue.Count == 0)
                {
                    if (_lobbyRewards.Count == 0) return;
                    foreach (var t_reward in _lobbyRewards)
                        CardPackRewardHandoff.Set(t_reward.Refund, t_reward.Cards);
                    _lobbyRewards.Clear();
                    t_waitingForLobby = LobbyGainEffectDirector.PlayNow();
                    if (!t_waitingForLobby) return;
                    continue;
                }
                var t_next = _queue.Peek();
                if (t_next.IsPack && TryReveal(t_next.PackId, t_next.Cards))
                {
                    _queue.Dequeue();
                    continue;
                }

                // 개봉 프리팹이 없거나 배선이 끊겼어도 받은 카드 전량을 결과판으로 보여준다.
                // 팩 카드도 여기서만 대체 표시한다. 성공한 개봉을 다시 나열하지 않는다.
                if (!CardSetRewardOverlay.TryGet(out var t_overlay))
                {
                    Debug.LogWarning("[RewardPackPresentation] No card result overlay — keeping granted cards queued until the next Show call.");
                    return;
                }

                t_overlay.ShowGranted(t_next.Cards);
                _queue.Dequeue();
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
            _queue.Clear();
            _lobbyRewards.Clear();
        }
        finally
        {
            PackOpenOverlay.OnClosed -= OnPackClosed;
            LobbyGainEffectDirector.OnAnyFinished -= OnGainFinished;
            if (ReferenceEquals(s_pending, _queue)) s_running = false;
        }
    }

    static bool TryReveal(string _packId, List<DrawnCard> _cards)
    {
        if (PackOpenOverlay.Instance == null) return false;

        // 환급 종류도 서버 카드 스냅샷을 따른다. 추첨·구매·지급 요청은 하지 않는다.
        var t_refundType = _cards[0].RefundType;
        foreach (var t_card in _cards)
            if (t_card.Refund > 0) { t_refundType = t_card.RefundType; break; }

        var t_opened = OpenedPack.CreateSuccess(_cards, t_refundType);
        PackHandoff.Set(t_opened, _packId, null, false, _isReward: true);
        if (PackOpenOverlay.TryOpen()) return true;

        // BeginSession이 소비하기 전에 실패했을 때만 자신이 실은 결과를 걷는다.
        if (ReferenceEquals(PackHandoff.Opened, t_opened)) PackHandoff.Consume();
        return false;
    }
}
