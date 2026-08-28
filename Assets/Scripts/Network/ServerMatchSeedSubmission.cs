using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Functions;
using UnityEngine;

internal enum ServerMatchSeedStatus
{
    Paired,
    Rejected,
    Unavailable,
}

internal sealed class ServerMatchSeed
{
    internal string MatchId;
    internal string SeedHex;
    internal ulong Seed;
    internal int RulesetVersion;
    internal int Slot;
}

internal static class ServerMatchSeedSubmission
{
    const string Region = "asia-northeast3";

    /// <summary>호출자가 데드라인을 안 준 경우에만 쓰는 <b>폴백</b> 상한.
    /// 씬 전 경로는 <see cref="NetTimeouts.PreBattleSyncSec"/> 하나로 잘리므로 여기서 또 자르면
    /// 어느 값이 진짜 상한인지 코드로 판별되지 않는다 — 취소 가능한 토큰이 오면 그쪽만 신뢰한다.</summary>
    static readonly TimeSpan PairingFallbackTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    internal static async UniTask<(ServerMatchSeedStatus status, ServerMatchSeed match)> TryAcquireAsync(
        string _env,
        string _pairingKey,
        string _contentFingerprint,
        int _ownerIndex,
        CancellationToken _ct = default)
    {
        if (string.IsNullOrWhiteSpace(_pairingKey) || string.IsNullOrWhiteSpace(_contentFingerprint)
            || _ownerIndex < 0 || _ownerIndex > 1)
            return (ServerMatchSeedStatus.Unavailable, null);
        if (!await MatchResultSubmission.EnsureSignedIn())
            return (ServerMatchSeedStatus.Unavailable, null);

        var t_payload = new Dictionary<string, object>
        {
            ["env"] = _env,
            ["pairingKey"] = _pairingKey,
            ["contentFingerprint"] = _contentFingerprint,
            ["ownerIndex"] = _ownerIndex,
        };

        try
        {
            HttpsCallableReference t_callable = FirebaseFunctions
                .GetInstance(FirebaseApp.DefaultInstance, Region)
                .GetHttpsCallable("createMatch");
            using (var t_timeout = CancellationTokenSource.CreateLinkedTokenSource(_ct, FirebaseManager.Lifetime))
            {
                if (!_ct.CanBeCanceled) t_timeout.CancelAfter(PairingFallbackTimeout);
                while (true)
                {
                    HttpsCallableResult t_response = await t_callable.CallAsync(t_payload)
                        .AsUniTask()
                        .AttachExternalCancellation(t_timeout.Token);
                    if (!(t_response?.Data is IDictionary t_data))
                        return (ServerMatchSeedStatus.Unavailable, null);

                    string t_status = t_data["status"] as string;
                    if (t_status == "waiting")
                    {
                        await UniTask.Delay(PollInterval, cancellationToken: t_timeout.Token);
                        continue;
                    }
                    if (t_status != "paired")
                        return (ServerMatchSeedStatus.Unavailable, null);

                    string t_matchId = t_data["matchId"] as string;
                    string t_seedHex = t_data["seedHex"] as string;
                    if (string.IsNullOrEmpty(t_matchId) ||
                        !ulong.TryParse(t_seedHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong t_seed))
                        return (ServerMatchSeedStatus.Unavailable, null);

                    return (ServerMatchSeedStatus.Paired, new ServerMatchSeed
                    {
                        MatchId = t_matchId,
                        SeedHex = t_seedHex,
                        Seed = t_seed,
                        RulesetVersion = Convert.ToInt32(t_data["rulesetVersion"], CultureInfo.InvariantCulture),
                        Slot = Convert.ToInt32(t_data["slot"], CultureInfo.InvariantCulture),
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            Debug.LogError("[MatchSeed] 서버 페어링 제한 시간 초과. 매치를 중단합니다.");
            return (ServerMatchSeedStatus.Unavailable, null);
        }
        catch (Exception t_exception)
        {
            if (MatchResultSubmission.IsPermanentRejection(
                    t_exception,
                    out FunctionsErrorCode t_errorCode))
            {
                Debug.LogError($"[MatchSeed] 서버 매치 발급 거절(code={t_errorCode}).");
                return (ServerMatchSeedStatus.Rejected, null);
            }
            Debug.LogError($"[MatchSeed] 서버 매치 발급 실패. 매치를 중단합니다: {t_exception.Message}");
            return (ServerMatchSeedStatus.Unavailable, null);
        }
    }
}
