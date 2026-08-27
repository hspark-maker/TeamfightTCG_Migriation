using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Functions;
using UnityEngine;

internal enum DeckLockResult
{
    Approved,
    Rejected,
    Unavailable,
}

internal static class DeckLockSubmission
{
    const string Region = "asia-northeast3";
    static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    internal static async UniTask<DeckLockResult> TryLockAsync(
        string _env,
        string _matchId,
        byte[] _myNonce,
        byte[] _opponentNonce,
        string _contentFingerprint,
        string _deckHash,
        int[] _cardIds,
        CardGrowth[] _growth)
    {
        if (_cardIds == null || _growth == null || _cardIds.Length == 0
            || _cardIds.Length != _growth.Length)
        {
            Debug.LogError("[LockDeck] 덱 성장 스냅샷이 유효하지 않습니다.");
            return DeckLockResult.Rejected;
        }

        var t_cards = new List<object>(_cardIds.Length);
        for (int i = 0; i < _cardIds.Length; i++)
        {
            CardGrowth t_growth = _growth[i];
            t_cards.Add(new Dictionary<string, object>
            {
                ["cardId"] = _cardIds[i],
                ["level"] = t_growth.Level,
                ["hpBonus"] = t_growth.HpBonus,
                ["evolutionStage"] = t_growth.EvolutionStage,
                ["unlockedKeywords"] = (int)t_growth.UnlockedKeywords,
                ["synergyUnlocked"] = t_growth.SynergyUnlocked,
            });
        }

        var t_payload = new Dictionary<string, object>
        {
            ["env"] = _env,
            ["matchId"] = _matchId,
            ["myNonce"] = MatchResultSubmission.Hex(_myNonce),
            ["opponentNonce"] = MatchResultSubmission.Hex(_opponentNonce),
            ["contentFingerprint"] = _contentFingerprint,
            ["deckHash"] = _deckHash,
            ["cardSnapshots"] = t_cards,
        };

        if (!await MatchResultSubmission.EnsureSignedIn())
        {
            Debug.LogError("[LockDeck] Firebase 로그인이 완료되지 않아 덱 검증을 시작할 수 없습니다.");
            return DeckLockResult.Unavailable;
        }
        if (!PlayerSaveCloud.IsGateComplete
            || PlayerSaveCloud.State == EPlayerSaveCloudState.Disabled
            || PlayerSaveCloud.State == EPlayerSaveCloudState.Blocked)
        {
            Debug.LogError(
                $"[LockDeck] 클라우드 저장이 비활성 상태입니다(state={PlayerSaveCloud.State}). " +
                "멀티플레이 테스트는 각 UID의 클라우드 저장을 활성화해야 합니다.");
            return DeckLockResult.Unavailable;
        }

        DataSaveManager.SaveImmediate();
        try
        {
            using (var t_flushTimeout = new CancellationTokenSource(FlushTimeout))
            {
                await FirebaseManager.FlushPendingAsync()
                    .AttachExternalCancellation(t_flushTimeout.Token);
            }
            if (PlayerSaveCloud.State != EPlayerSaveCloudState.Ready)
            {
                Debug.LogError(
                    $"[LockDeck] 최신 세이브 업로드를 확인할 수 없습니다(state={PlayerSaveCloud.State}).");
                return DeckLockResult.Unavailable;
            }
        }
        catch (OperationCanceledException)
        {
            Debug.LogError("[LockDeck] 최신 세이브 업로드 시간이 초과되었습니다.");
            return DeckLockResult.Unavailable;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[LockDeck] 최신 세이브 업로드 실패: {t_exception.Message}");
            return DeckLockResult.Unavailable;
        }

        try
        {
            HttpsCallableReference t_callable = FirebaseFunctions
                .GetInstance(FirebaseApp.DefaultInstance, Region)
                .GetHttpsCallable("lockDeck");
            using (var t_lockTimeout = new CancellationTokenSource(LockTimeout))
            {
                while (true)
                {
                    HttpsCallableResult t_response = await t_callable.CallAsync(t_payload)
                        .AsUniTask()
                        .AttachExternalCancellation(t_lockTimeout.Token);
                    if (t_response?.Data is IDictionary t_data)
                    {
                        string t_status = t_data["status"] as string;
                        if (t_status == "approved") return DeckLockResult.Approved;
                        if (t_status == "rejected")
                        {
                            Debug.LogError($"[LockDeck] 서버 검증 거절: {t_data["reason"]}");
                            return DeckLockResult.Rejected;
                        }
                        if (t_status == "pending")
                        {
                            await UniTask.Delay(PollInterval, cancellationToken: t_lockTimeout.Token);
                            continue;
                        }
                    }

                    Debug.LogError("[LockDeck] 서버가 알 수 없는 응답을 반환했습니다.");
                    return DeckLockResult.Unavailable;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Debug.LogError("[LockDeck] 서버 검증 응답 시간이 초과되었습니다.");
            return DeckLockResult.Unavailable;
        }
        catch (Exception t_exception)
        {
            if (MatchResultSubmission.IsPermanentRejection(
                    t_exception,
                    out FunctionsErrorCode t_errorCode))
            {
                Debug.LogError($"[LockDeck] 서버가 덱 잠금을 영구 거절했습니다(code={t_errorCode}).");
                return DeckLockResult.Rejected;
            }
            Debug.LogError($"[LockDeck] 서버 호출 실패: {t_exception.Message}");
            return DeckLockResult.Unavailable;
        }
    }
}
