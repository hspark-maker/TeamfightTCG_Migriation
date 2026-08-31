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

    /// <summary>호출자가 데드라인을 안 준 경우에만 쓰는 <b>폴백</b> 상한(레거시 씬 내 경로).
    /// 씬 전 경로는 <see cref="NetTimeouts.PreBattleSyncSec"/> 하나로 잘린다 — 두 상한이 겹치면
    /// 합이 단일 데드라인을 넘어(20+15+30 &gt; 45) 여기 값이 완주 불가능한 죽은 값이 된다.</summary>
    static readonly TimeSpan FlushFallbackTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan LockFallbackTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    internal static async UniTask<DeckLockResult> TryLockAsync(
        string _env,
        string _matchId,
        string _seedSource,
        string _seedHex,
        int _rulesetVersion,
        int _ownerIndex,
        string _contentFingerprint,
        int[] _cardIds,
        CardGrowth[] _growth,
        CancellationToken _ct = default)
    {
        if (_cardIds == null || _growth == null || _cardIds.Length == 0
            || _cardIds.Length != _growth.Length || _ownerIndex < 0 || _ownerIndex > 1)
        {
            Debug.LogError("[LockDeck] 덱 성장 스냅샷이 유효하지 않습니다.");
            return DeckLockResult.Rejected;
        }

        // 덱 스냅샷 순서 규약: cardId 오름차순. 서버 validateDeckShape가 이 순서를 강제하고
        // computeDeckHash가 배열 순서를 그대로 직렬화하므로, 여기서 정규화한 배열로
        // deckHash까지 같이 계산해야 한다(전송 순서가 다른 레거시 경로 포함).
        int[] t_ids = (int[])_cardIds.Clone();
        CardGrowth[] t_growths = (CardGrowth[])_growth.Clone();
        Array.Sort(t_ids, t_growths);
        for (int i = 1; i < t_ids.Length; i++)
        {
            if (t_ids[i - 1] != t_ids[i]) continue;
            Debug.LogError($"[LockDeck] 덱에 중복 카드가 있습니다(cardId={t_ids[i]}).");
            return DeckLockResult.Rejected;
        }
        string t_deckHash = NetworkGameController.ComputeDeckHash(t_ids, t_growths);

        var t_cards = new List<object>(t_ids.Length);
        for (int i = 0; i < t_ids.Length; i++)
        {
            CardGrowth t_growth = t_growths[i];
            t_cards.Add(new Dictionary<string, object>
            {
                ["cardId"] = t_ids[i],
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
            ["seedSource"] = _seedSource,
            ["contentFingerprint"] = _contentFingerprint,
            ["cardDataVersion"] = _contentFingerprint,
            ["deckHash"] = t_deckHash,
            ["cardSnapshots"] = t_cards,
            ["ownerIndex"] = _ownerIndex,
        };
        // 서버 시드만 권위다 — seedHex·rulesetVersion 은 lockDeck 의 필수 항목이다.
        t_payload["seedHex"] = _seedHex;
        t_payload["rulesetVersion"] = _rulesetVersion;

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
            using (var t_flushTimeout = CancellationTokenSource.CreateLinkedTokenSource(_ct, FirebaseManager.Lifetime))
            {
                if (!_ct.CanBeCanceled) t_flushTimeout.CancelAfter(FlushFallbackTimeout);
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
            using (var t_lockTimeout = CancellationTokenSource.CreateLinkedTokenSource(_ct, FirebaseManager.Lifetime))
            {
                if (!_ct.CanBeCanceled) t_lockTimeout.CancelAfter(LockFallbackTimeout);
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
                // 거절 원인은 서버 신원 대조 5개 항목 중 하나다. 클라가 무엇을 보냈는지 같이 찍어야
                // Functions 로그의 expected 와 눈으로 맞출 수 있다.
                Debug.LogError($"[LockDeck] 서버가 덱 잠금을 영구 거절했습니다(code={t_errorCode}). " +
                    $"matchId={_matchId} owner={_ownerIndex} seedSource={_seedSource} " +
                    $"seedHex={_seedHex} ruleset={_rulesetVersion} fingerprint={_contentFingerprint}");
                return DeckLockResult.Rejected;
            }
            Debug.LogError($"[LockDeck] 서버 호출 실패: {t_exception.Message}");
            return DeckLockResult.Unavailable;
        }
    }
}
