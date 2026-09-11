using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>배틀패스 조회·수령 callable 의 클라이언트 단일 창구.</summary>
internal static class PassCommands
{
    static bool s_missionRefreshPending;

    internal static void ApplyMissionProgress(PassProgress _progress)
    {
        bool t_reuseDefinitions = CanReuseDefinitions && s_inFlightClaims.Count == 0;
        Invalidate();
        if (!TryAdoptProgress(_progress, t_reuseDefinitions) && !s_missionRefreshPending)
            RefreshAfterMissionAsync().Forget();
    }

    // 초기 조회 중이거나 시즌이 달라졌으면 정의까지 다시 받는다. 일괄 수령 중의 요청은 합친다.
    static async UniTaskVoid RefreshAfterMissionAsync()
    {
        s_missionRefreshPending = true;
        try
        {
            while (true)
            {
                await UniTask.WaitUntil(() => !s_refreshInFlight && s_inFlightClaims.Count == 0,
                    cancellationToken: FirebaseManager.Lifetime);
                int t_generation = s_stateGeneration;
                await RefreshAsync();
                // 왕복 중 다른 미션 수령이 옛 조회를 무효화한 경우만 다시 조회한다.
                if (t_generation == s_stateGeneration) break;
            }
        }
        finally { s_missionRefreshPending = false; }
    }

    const string GET_COMMAND = "getPass";
    const string CLAIM_COMMAND = "claimPassReward";

    static readonly HashSet<int> s_inFlightClaims = new HashSet<int>();
    static bool s_refreshInFlight;
    static int s_stateGeneration;
    static int s_loadedGeneration = -1;
    static long s_loadedRankPoints;

    internal static bool NeedsRefresh => !PassManager.IsReady ||
        s_loadedGeneration != s_stateGeneration || s_loadedRankPoints != RankManager.Points;

    // 진행도만 받는 응답은 기존 시즌·곡선·팩 선택 목록이 최신일 때만 조회를 대신한다.
    static bool CanReuseDefinitions => !NeedsRefresh && PassManager.HasSeason &&
        PassManager.Levels.Count > 0 &&
        (PassManager.Season.EndAtMs <= 0L ||
         PassManager.Season.EndAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    static bool TryAdoptProgress(PassProgress _progress, bool _markFresh)
    {
        if (_progress == null || !PassManager.HasSeason || PassManager.Levels.Count == 0 ||
            string.IsNullOrEmpty(_progress.SeasonId) || PassManager.Season.SeasonId != _progress.SeasonId)
            return false;

        // 랭크 기준은 마지막 getPass의 값으로 둔다. 진행 응답에는 새 팩 선택 목록이 없다.
        if (_markFresh && (PassManager.Season.EndAtMs <= 0L ||
            PassManager.Season.EndAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            s_loadedGeneration = s_stateGeneration;
        PassManager.Adopt(_progress);
        return true;
    }

    // 경험치·수령 상태가 바뀌면 진행 중인 옛 조회도 캐시로 채택하지 않는다.
    internal static void Invalidate()
    {
        unchecked { s_stateGeneration++; }
    }

    internal static bool IsInFlight(int _level) => s_inFlightClaims.Contains(_level);

    /// <summary>시즌·곡선·진행도를 한 번에 받는다. 실패는 부가 기능 실패라 세션을 막지 않는다.</summary>
    internal static async UniTask<bool> RefreshAsync()
    {
        // 수령 중 읽은 봉투가 수령 완료 뒤 도착하면 낙인을 옛 값으로 되돌린다(미션과 같은 함정).
        if (s_refreshInFlight || s_inFlightClaims.Count > 0) return false;
        s_refreshInFlight = true;
        int t_generation = s_stateGeneration;
        long t_rankPoints = RankManager.Points;

        try
        {
            PassGetResponse t_result = await ServerSaveCommands.InvokeReadOnlyAsync<PassGetResponse>(
                GET_COMMAND, new { env = ContentProfileConfig.Active.CloudEnvId });
            if (t_result == null)
            {
                Debug.LogWarning("[PassCommands] getPass returned nothing.");
                return false;
            }
            if (s_inFlightClaims.Count > 0 || t_generation != s_stateGeneration) return false;

            s_loadedGeneration = t_generation;
            s_loadedRankPoints = t_rankPoints;
            PassManager.Adopt(t_result);
            return true;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[PassCommands] Pass query failed — {t_exception.GetBaseException().Message}");
            return false;
        }
        finally
        {
            s_refreshInFlight = false;
        }
    }

    /// <summary>레벨 하나의 무료 트랙 보상을 수령한다. 진행 상태는 서버 응답 채택만 따른다.</summary>
    internal static async UniTask<ClaimPassRewardResult> ClaimAsync(int _level, string _selectedPackId = null)
    {
        if (_level <= 0) return null;
        bool t_reuseDefinitions = CanReuseDefinitions && s_inFlightClaims.Count == 0;
        if (!s_inFlightClaims.Add(_level)) return null;
        Invalidate();
        int t_generation = s_stateGeneration;
        PassManager.NotifyCommandStateChanged();

        try
        {
            ClaimPassRewardResult t_result = await ServerSaveCommands.InvokeAsync<ClaimPassRewardResult>(
                CLAIM_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, level = _level, selectedPackId = _selectedPackId });
            TryAdoptProgress(t_result?.Progress,
                t_reuseDefinitions && t_generation == s_stateGeneration && s_inFlightClaims.Count == 1);
            Debug.Log($"[PassCommands] Level {_level} claimed — {t_result?.Granted?.Count ?? 0} currency line(s)");
            return t_result;
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            Debug.LogWarning($"[PassCommands] Level {_level} claim rejected ({t_rejected.Reason}) — {t_rejected.Message}");
            return null;
        }
        catch (ServerAdoptionException t_adoption)
        {
            Debug.LogWarning($"[PassCommands] Failed to adopt the claim response — {t_adoption.Message}");
            return null;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[PassCommands] claimPassReward failed — {t_exception.GetBaseException().Message}");
            return null;
        }
        finally
        {
            s_inFlightClaims.Remove(_level);
            PassManager.NotifyCommandStateChanged();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_missionRefreshPending = false;
        s_inFlightClaims.Clear();
        s_refreshInFlight = false;
        s_stateGeneration = 0;
        s_loadedGeneration = -1;
        s_loadedRankPoints = 0L;
    }
}
