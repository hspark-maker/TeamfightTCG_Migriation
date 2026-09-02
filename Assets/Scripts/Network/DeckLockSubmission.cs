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

    /// <summary>Flush 상한은 외부 데드라인 유무와 관계없이 잠금 대기 시간을 보존한다.
    /// 잠금 상한은 호출자가 데드라인을 주지 않는 레거시 씬 내 경로의 폴백이다.</summary>
    static readonly TimeSpan FlushFallbackTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan LockFallbackTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>취소 뒤 최종 확인 1회의 상한. 이미 데드라인이 끝난 자리라 짧아야 한다.</summary>
    static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(5);

    /// <summary>취소원 셋(상위 토큰 · Firebase 수명 · 자체 상한)을 갈라 로그 문구로 준다.
    /// 상위 토큰은 <see cref="PreBattleMatchSync"/> 에서 <b>상대 이탈·연결 실패로도</b> 취소되므로,
    /// 뭉뚱그리면 남의 이탈이 이쪽 "응답 시간 초과"로 둔갑한다.</summary>
    /// <summary>상위 취소·상한 초과 뒤의 <b>최종 확인</b> 1회. 끊긴 토큰을 일부러 무시하고
    /// Firebase 수명만 본다 — 취소 자체가 "상대가 먼저 승인을 받고 떠났다"는 신호일 수 있어서,
    /// 그 토큰으로 다시 물으면 확인이 시작도 못 한다. 응답이 approved 일 때만 성공으로 승격한다
    /// (상대가 정말 잠그지 않고 나갔으면 여전히 pending 이라 그대로 실패한다).</summary>
    static async UniTask<DeckLockResult> ConfirmAfterCancelAsync(
        HttpsCallableReference _callable, Dictionary<string, object> _payload)
    {
        if (_callable == null || FirebaseManager.Lifetime.IsCancellationRequested)
            return DeckLockResult.Unavailable;
        try
        {
            using (var t_confirm = CancellationTokenSource.CreateLinkedTokenSource(FirebaseManager.Lifetime))
            {
                t_confirm.CancelAfter(ConfirmTimeout);
                HttpsCallableResult t_response = await _callable.CallAsync(_payload)
                    .AsUniTask()
                    .AttachExternalCancellation(t_confirm.Token);
                if (t_response?.Data is IDictionary t_data
                    && t_data["status"] as string == "approved")
                    return DeckLockResult.Approved;
            }
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[LockDeck] 최종 승인 확인에 실패했습니다: {t_exception.Message}");
        }
        return DeckLockResult.Unavailable;
    }

    static string CancelCause(CancellationToken _ct, TimeSpan _ownLimit)
    {
        // 수명을 먼저 본다 — 호출부의 _ct 가 이미 Lifetime 을 링크로 물고 있어서
        // _ct 를 먼저 물으면 앱 종료까지 "상위 취소"로 둔갑한다.
        if (FirebaseManager.Lifetime.IsCancellationRequested) return "Firebase 수명 종료";
        if (_ct.IsCancellationRequested) return "상위 취소 — 상대 이탈·연결 실패·상위 데드라인";
        return $"자체 상한 {_ownLimit.TotalSeconds:0}초 초과";
    }

    /// <summary>덱 스냅샷 순서 규약: cardId 오름차순. 서버 validateDeckShape가 이 순서를 강제하고
    /// computeDeckHash가 배열 순서를 그대로 직렬화하므로, 정규화한 배열로 deckHash까지 같이 만든다.
    ///
    /// <para>잠금 제출과 결과 제출(<c>submitMatchResult.myDeckHash</c>)이 <b>같은 값</b>이어야
    /// 서버가 같은 덱으로 읽는다 — 그래서 정규화 규약은 이 자리 하나가 소유한다.
    /// 정렬을 건너뛴 배열로 해시를 따로 만들면 편성 순서가 오름차순이 아닌 덱이 전부 거절된다.</para></summary>
    internal static bool TryNormalize(int[] _cardIds, CardGrowth[] _growth,
        out int[] _sortedIds, out CardGrowth[] _sortedGrowth, out string _deckHash)
    {
        _sortedIds = null;
        _sortedGrowth = null;
        _deckHash = null;
        if (_cardIds == null || _growth == null || _cardIds.Length == 0 || _cardIds.Length != _growth.Length)
        {
            Debug.LogError("[LockDeck] 덱 성장 스냅샷이 유효하지 않습니다.");
            return false;
        }

        int[] t_ids = (int[])_cardIds.Clone();
        CardGrowth[] t_growths = (CardGrowth[])_growth.Clone();
        DeckOrder.SortInPlace(t_ids, t_growths);
        for (int i = 1; i < t_ids.Length; i++)
        {
            if (t_ids[i - 1] != t_ids[i]) continue;
            Debug.LogError($"[LockDeck] 덱에 중복 카드가 있습니다(cardId={t_ids[i]}).");
            return false;
        }

        _sortedIds = t_ids;
        _sortedGrowth = t_growths;
        _deckHash = NetworkGameController.ComputeDeckHash(t_ids, t_growths);
        return true;
    }

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

        if (!TryNormalize(_cardIds, _growth, out int[] t_ids, out CardGrowth[] t_growths, out string t_deckHash))
            return DeckLockResult.Rejected;

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

        var t_flushStopwatch = System.Diagnostics.Stopwatch.StartNew();
        DataSaveManager.SaveImmediate();
        try
        {
            using (var t_flushTimeout = CancellationTokenSource.CreateLinkedTokenSource(_ct, FirebaseManager.Lifetime))
            {
                t_flushTimeout.CancelAfter(FlushFallbackTimeout);
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
            Debug.LogError(
                $"[LockDeck] 최신 세이브 업로드가 중단되었습니다({CancelCause(_ct, FlushFallbackTimeout)}). " +
                $"matchId={_matchId} owner={_ownerIndex} flushMs={t_flushStopwatch.ElapsedMilliseconds}");
            return DeckLockResult.Unavailable;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[LockDeck] 최신 세이브 업로드 실패: {t_exception.Message}");
            return DeckLockResult.Unavailable;
        }

        // 몇 번이나 물었는지가 진단의 축이다 — "폴을 못 돌고 만료"와 "계속 pending"은 원인이 다르다.
        // catch 에서도 읽어야 하므로 try 밖에 둔다.
        long t_flushMs = t_flushStopwatch.ElapsedMilliseconds;
        var t_lockStopwatch = System.Diagnostics.Stopwatch.StartNew();
        int t_pollCount = 0;
        // 취소 뒤 최종 확인에서도 써야 하므로 try 밖에 둔다.
        HttpsCallableReference t_callable = null;
        try
        {
            t_callable = FirebaseFunctions
                .GetInstance(FirebaseApp.DefaultInstance, Region)
                .GetHttpsCallable("lockDeck");
            using (var t_lockTimeout = CancellationTokenSource.CreateLinkedTokenSource(_ct, FirebaseManager.Lifetime))
            {
                if (!_ct.CanBeCanceled) t_lockTimeout.CancelAfter(LockFallbackTimeout);
                while (true)
                {
                    t_pollCount++;
                    HttpsCallableResult t_response = await t_callable.CallAsync(t_payload)
                        .AsUniTask()
                        .AttachExternalCancellation(t_lockTimeout.Token);
                    if (t_response?.Data is IDictionary t_data)
                    {
                        string t_status = t_data["status"] as string;
                        if (t_status == "approved")
                        {
                            // 정상 판의 flushMs·lockMs 분포를 알아야 상한 15초가 넉넉한지 판정할 수 있다.
                            Debug.Log(
                                $"[LockDeck] 승인 matchId={_matchId} owner={_ownerIndex} 폴={t_pollCount} " +
                                $"flushMs={t_flushMs} lockMs={t_lockStopwatch.ElapsedMilliseconds}");
                            return DeckLockResult.Approved;
                        }
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
            // 취소가 곧 실패는 아니다. 첫 폴이 pending 을 받았다는 건 내 승인이 서버에 이미 기록됐다는
            // 뜻이고, 그 사이 상대가 정원 2를 보고 approved 를 받아 먼저 진행하면 상대 이탈로 이 토큰이
            // 죽는다 — 승인 정원이 찬 바로 그 순간에. 승인의 진실원은 서버 문서지 Photon 방의 생존이
            // 아니므로, 포기하기 전에 취소와 무관한 짧은 창으로 한 번만 더 묻는다.
            DeckLockResult t_confirmed = await ConfirmAfterCancelAsync(t_callable, t_payload);
            if (t_confirmed == DeckLockResult.Approved)
            {
                Debug.LogWarning(
                    $"[LockDeck] 대기는 끊겼지만 서버는 이미 승인 상태였습니다" +
                    $"({CancelCause(_ct, LockFallbackTimeout)}). " +
                    $"matchId={_matchId} owner={_ownerIndex} 폴={t_pollCount} " +
                    $"flushMs={t_flushMs} lockMs={t_lockStopwatch.ElapsedMilliseconds}");
                return DeckLockResult.Approved;
            }

            // 취소원이 셋이고(상위 토큰·Firebase 수명·자체 상한) 대응이 전부 다르다. 한 문구로 묶으면
            // 상대 이탈까지 "응답 시간 초과"로 읽혀 원인이 뒤바뀐다 — 실제로 그 오독으로 한 번 돌아왔다.
            Debug.LogError(
                $"[LockDeck] 덱 잠금 대기가 중단되었고 서버도 승인 전이었습니다" +
                $"({CancelCause(_ct, LockFallbackTimeout)}). " +
                $"matchId={_matchId} owner={_ownerIndex} 폴={t_pollCount} " +
                $"flushMs={t_flushMs} lockMs={t_lockStopwatch.ElapsedMilliseconds}");
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
