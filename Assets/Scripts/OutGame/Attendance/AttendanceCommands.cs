using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

internal sealed class AttendanceState
{
    [JsonProperty("dailyKey")] public string DailyKey;
    [JsonProperty("nextResetAtMs")] public long NextResetAtMs;
    [JsonProperty("serverNowMs")] public long ServerNowMs;
    [JsonProperty("cycle")] public int Cycle;
    [JsonProperty("claimedDays")] public int ClaimedDays;
    [JsonProperty("claimDay")] public int ClaimDay;
    [JsonProperty("canClaim")] public bool CanClaim;
}

internal sealed class AttendanceDay
{
    [JsonProperty("day")] public int Day;
    [JsonProperty("reward")] public AttendanceReward Reward;
}

internal sealed class AttendanceReward
{
    [JsonProperty("currencies")] public List<ClaimRewardGain> Currencies;
    [JsonProperty("items")] public List<ClaimRewardItem> Items;
}

internal sealed class AttendanceResponse
{
    [JsonProperty("attendance")] public AttendanceState Attendance;
    [JsonProperty("days")] public List<AttendanceDay> Days;
}

internal sealed class ClaimAttendanceResult : ServerCommandResult
{
    [JsonProperty("attendance")] public AttendanceState Attendance;
    [JsonProperty("granted")] public List<ClaimRewardGain> Granted;
    [JsonProperty("cards")] public List<OpenPackCard> Cards;
    [JsonProperty("packs")] public List<ClaimRewardPack> Packs;
}

/// <summary>출석의 서버 상태·시계·왕복을 소유한다. 로컬 날짜나 보상표로 수령을 판정하지 않는다.</summary>
internal static class AttendanceCommands
{
    internal static event Action OnChanged;
    internal static AttendanceState State { get; private set; }
    internal static List<AttendanceDay> Days { get; private set; }
    internal static string Error { get; private set; }
    internal static string PromptedDay { get; private set; }
    internal static bool IsClaiming { get; private set; }
    internal static bool IsReading => s_read != null;
    internal static bool IsReady => State != null && Days?.Count == 7;
    internal static bool HasPendingReceipt => s_receipt != null;
    internal static long NowMs => State == null ? 0 : State.ServerNowMs
        + (long)((Time.realtimeSinceStartupAsDouble - s_adoptedAt) * 1000);
    internal static bool NeedsRefresh => s_requiresRefresh || !IsReady || NowMs >= State.NextResetAtMs;
    internal static bool CanClaim => !IsClaiming && (HasPendingReceipt || (IsReady && !NeedsRefresh && State.CanClaim));

    static UniTaskCompletionSource<bool> s_read;
    static int s_session, s_version;
    static string s_env, s_uid, s_receipt, s_pendingDay;
    static int s_pendingCycle, s_pendingIndex;
    static double s_adoptedAt, s_validUntil;
    static bool s_requiresRefresh = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntime() { OnChanged = null; ResetSession(); }

    internal static void ResetSession()
    {
        s_session++;
        s_version++;
        var t_read = s_read;
        s_read = null;
        s_env = s_uid = s_receipt = s_pendingDay = null;
        State = null;
        Days = null;
        Error = PromptedDay = null;
        IsClaiming = false;
        s_validUntil = 0;
        s_requiresRefresh = true;
        t_read?.TrySetResult(false);
        OnChanged?.Invoke();
    }

    static bool EnsureSession()
    {
        string t_env = ContentProfileConfig.Active?.CloudEnvId;
        string t_uid = FirebaseAuthService.Instance.UserId;
        if (t_env != s_env || t_uid != s_uid)
        {
            ResetSession();
            s_env = t_env;
            s_uid = t_uid;
        }
        return !string.IsNullOrEmpty(s_env) && !string.IsNullOrEmpty(s_uid);
    }

    internal static UniTask<bool> RefreshAsync(bool _force = false)
    {
        if (!EnsureSession() || IsClaiming) return UniTask.FromResult(false);
        if (s_read != null) return s_read.Task;
        if (!_force && !NeedsRefresh && Time.realtimeSinceStartupAsDouble < s_validUntil)
            return UniTask.FromResult(true);
        var t_gate = new UniTaskCompletionSource<bool>();
        s_read = t_gate;
        ReadAsync(t_gate, s_session, s_version, s_env).Forget();
        return t_gate.Task;
    }

    static async UniTaskVoid ReadAsync(UniTaskCompletionSource<bool> _gate, int _session, int _version, string _env)
    {
        bool t_ok = false;
        try
        {
            var t_result = await ServerSaveCommands.InvokeReadOnlyAsync<AttendanceResponse>("getAttendance", new { env = _env });
            if (_session != s_session || _version != s_version) return;
            if (t_result?.Attendance == null || t_result.Days?.Count != 7)
                throw new InvalidOperationException("Attendance response is incomplete.");
            Days = t_result.Days;
            Adopt(t_result.Attendance);
            t_ok = true;
        }
        catch (Exception t_error)
        {
            if (_session != s_session || _version != s_version) return;
            Error = "출석 정보를 불러오지 못했어요. 다시 시도해 주세요.";
            Debug.LogWarning($"[Attendance] Query failed: {t_error.GetBaseException().Message}");
        }
        finally
        {
            if (ReferenceEquals(s_read, _gate)) s_read = null;
            _gate.TrySetResult(t_ok);
            if (_session == s_session) OnChanged?.Invoke();
        }
    }

    static void Adopt(AttendanceState _state)
    {
        State = _state;
        s_requiresRefresh = false;
        s_adoptedAt = Time.realtimeSinceStartupAsDouble;
        s_validUntil = s_adoptedAt + 30;
        Error = null;
    }

    internal static void MarkPrompted()
    {
        if (IsReady) PromptedDay = State.DailyKey;
    }

    internal static async UniTask<ClaimAttendanceResult> ClaimAsync()
    {
        if (!EnsureSession() || !CanClaim) return null;
        if (s_receipt == null)
        {
            s_receipt = $"claimAttendance:{Guid.NewGuid():N}";
            s_pendingDay = State.DailyKey;
            s_pendingCycle = State.Cycle;
            s_pendingIndex = State.ClaimDay;
        }
        int t_session = s_session;
        IsClaiming = true;
        s_version++;
        s_validUntil = 0;
        Error = null;
        OnChanged?.Invoke();
        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<ClaimAttendanceResult>("claimAttendance",
                new { env = s_env, dailyKey = s_pendingDay, cycle = s_pendingCycle, day = s_pendingIndex },
                _receiptId: s_receipt);
            if (t_session != s_session) return null;
            if (t_result?.Attendance == null) throw new InvalidOperationException("Attendance claim has no state.");
            s_receipt = null;
            Adopt(t_result.Attendance);
            return t_result;
        }
        catch (ServerCommandRejectedException t_error)
        {
            if (t_session == s_session)
            {
                s_receipt = null;
                s_requiresRefresh = true;
                Error = t_error.Reason == "AlreadyClaimed" ? "오늘 보상은 이미 받았어요."
                    : t_error.Reason == "AttendanceStale" ? "출석 날짜가 바뀌었어요. 다시 확인해 주세요."
                    : "보상을 받을 수 없어요. 잠시 후 다시 시도해 주세요.";
            }
            return null;
        }
        catch (Exception t_error)
        {
            if (t_session == s_session) Error = "수령 결과를 확인하지 못했어요. 다시 눌러 확인해 주세요.";
            Debug.LogWarning($"[Attendance] Claim failed: {t_error.GetBaseException().Message}");
            return null;
        }
        finally
        {
            if (t_session == s_session)
            {
                IsClaiming = false;
                OnChanged?.Invoke();
            }
        }
    }

    internal static List<RewardLine> RewardLines(int _day)
    {
        var t_lines = new List<RewardLine>();
        AttendanceReward t_reward = Days?.Find(_entry => _entry.Day == _day)?.Reward;
        if (t_reward?.Currencies != null)
            foreach (var t_gain in t_reward.Currencies)
                if (CurrencyCode.TryParse(t_gain.Currency, out var t_type))
                    t_lines.Add(new RewardLine(new CurrencyGain(t_type, t_gain.Amount)));
        if (t_reward?.Items != null)
            foreach (var t_item in t_reward.Items)
                if (Enum.TryParse(t_item.RewardType, out ERewardType t_type))
                    t_lines.Add(new RewardLine(new AlbumRewardDef { rewardType = t_type,
                        rewardId = t_item.RewardId, amount = t_item.Amount }));
        return t_lines;
    }
}
