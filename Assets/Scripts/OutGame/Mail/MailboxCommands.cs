using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>우편 조회와 수령의 단일 창구. 지급·소유·수령 판정은 서버 응답만 따른다.</summary>
internal static class MailboxCommands
{
    static readonly List<MailboxEntry> s_mails = new List<MailboxEntry>();
    static UniTaskCompletionSource<bool> s_read;
    static MailboxCursor s_cursor;
    static string s_env, s_uid, s_receipt, s_pendingMailId;
    static bool s_pendingAll, s_readAppend;
    static int s_session, s_version;
    static long s_serverNowMs;
    static double s_adoptedAt, s_validUntil;

    internal static event Action OnChanged;
    internal static IReadOnlyList<MailboxEntry> Mails => s_mails;
    internal static bool IsReady { get; private set; }
    internal static bool IsClaiming { get; private set; }
    internal static bool IsReading => s_read != null;
    internal static bool HasMore => s_cursor != null;
    internal static bool HasClaimable { get; private set; }
    internal static string Error { get; private set; }
    internal static bool HasPendingReceipt => s_receipt != null;
    internal static string PendingMailId => s_pendingMailId;
    internal static bool PendingIsAll => HasPendingReceipt && s_pendingAll;
    internal static long NowMs => !IsReady ? 0 : s_serverNowMs
        + (long)((Time.realtimeSinceStartupAsDouble - s_adoptedAt) * 1000);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntime() { OnChanged = null; ResetSession(); }

    internal static void ResetSession()
    {
        s_session++;
        s_version++;
        var t_read = s_read;
        s_read = null;
        s_mails.Clear();
        s_cursor = null;
        s_env = s_uid = s_receipt = s_pendingMailId = null;
        s_pendingAll = s_readAppend = false;
        IsReady = IsClaiming = HasClaimable = false;
        Error = null;
        s_serverNowMs = 0;
        s_adoptedAt = s_validUntil = 0;
        t_read?.TrySetResult(false);
        OnChanged?.Invoke();
    }

    static bool EnsureSession()
    {
        string t_env = ContentProfileConfig.Active?.CloudEnvId;
        string t_uid = FirebaseAuthService.Instance.UserId;
        if (s_env != t_env || s_uid != t_uid)
        {
            ResetSession();
            s_env = t_env;
            s_uid = t_uid;
        }
        return !string.IsNullOrEmpty(s_env) && !string.IsNullOrEmpty(s_uid);
    }

    static bool IsCurrent(int _session)
        => _session == s_session && s_env == ContentProfileConfig.Active?.CloudEnvId
            && s_uid == FirebaseAuthService.Instance.UserId;

    internal static UniTask<bool> RefreshAsync(bool _force = false)
    {
        if (!EnsureSession() || IsClaiming) return UniTask.FromResult(false);
        if (s_read != null)
            return s_readAppend ? RefreshAfterPageAsync(s_read.Task, s_session) : s_read.Task;
        if (!_force && IsReady && Time.realtimeSinceStartupAsDouble < s_validUntil)
            return UniTask.FromResult(true);
        return BeginRead(false);
    }

    static async UniTask<bool> RefreshAfterPageAsync(UniTask<bool> _page, int _session)
    {
        await _page;
        return IsCurrent(_session) && await RefreshAsync(true);
    }

    internal static UniTask<bool> LoadMoreAsync()
    {
        if (!EnsureSession() || IsClaiming) return UniTask.FromResult(false);
        if (s_read != null) return s_read.Task;
        if (!IsReady) return RefreshAsync();
        return HasMore ? BeginRead(true) : UniTask.FromResult(true);
    }

    static UniTask<bool> BeginRead(bool _append)
    {
        var t_gate = new UniTaskCompletionSource<bool>();
        s_read = t_gate;
        s_readAppend = _append;
        Error = null;
        ReadAsync(t_gate, s_session, s_version, s_env, _append ? s_cursor : null).Forget();
        OnChanged?.Invoke();
        return t_gate.Task;
    }

    static async UniTaskVoid ReadAsync(UniTaskCompletionSource<bool> _gate, int _session, int _version,
        string _env, MailboxCursor _cursor)
    {
        bool t_ok = false;
        try
        {
            // Cursor는 서버가 돌려준 두 필드를 그대로 전달한다. 클라이언트에서 정렬/추정하지 않는다.
            var t_result = await ServerSaveCommands.InvokeReadOnlyAsync<MailboxResponse>("getMailbox",
                new { env = _env, limit = 20, cursor = _cursor });
            if (!IsCurrent(_session) || _version != s_version) return;
            if (t_result?.Mails == null || t_result.ServerNowMs <= 0)
                throw new InvalidOperationException("Mailbox response is incomplete.");
            if (_cursor == null) s_mails.Clear();
            foreach (var t_mail in t_result.Mails)
            {
                if (t_mail == null || string.IsNullOrEmpty(t_mail.MailId)) continue;
                int t_index = s_mails.FindIndex(_entry => _entry.MailId == t_mail.MailId);
                if (t_index >= 0) s_mails[t_index] = t_mail;
                else s_mails.Add(t_mail);
            }
            s_cursor = t_result.NextCursor;
            HasClaimable = t_result.HasClaimable;
            s_serverNowMs = t_result.ServerNowMs;
            s_adoptedAt = Time.realtimeSinceStartupAsDouble;
            s_validUntil = s_adoptedAt + 30;
            IsReady = true;
            Error = null;
            t_ok = true;
        }
        catch (Exception t_error)
        {
            if (!IsCurrent(_session) || _version != s_version) return;
            Error = "우편함을 불러오지 못했어요. 다시 시도해 주세요.";
            Debug.LogWarning($"[Mailbox] Query failed: {t_error.GetBaseException().Message}");
        }
        finally
        {
            if (ReferenceEquals(s_read, _gate)) s_read = null;
            _gate.TrySetResult(t_ok);
            if (IsCurrent(_session)) OnChanged?.Invoke();
        }
    }

    internal static UniTask<ClaimMailResult> ClaimAsync(string _mailId)
        => string.IsNullOrEmpty(_mailId) ? UniTask.FromResult<ClaimMailResult>(null) : ClaimCoreAsync(false, _mailId);

    /// <summary>서버가 만료 임박순 최대 20통을 선택한다. HasMore면 다음 클릭에서 새 영수증으로 계속 받는다.</summary>
    internal static UniTask<ClaimMailResult> ClaimAllAsync() => ClaimCoreAsync(true, null);

    internal static UniTask<ClaimMailResult> RetryPendingAsync()
    {
        // 다른 계정/환경으로 바뀌었다면 이전 계정의 미확인 수령을 새 요청으로 보내지 않는다.
        if (!EnsureSession() || !HasPendingReceipt) return UniTask.FromResult<ClaimMailResult>(null);
        return ClaimCoreAsync(s_pendingAll, s_pendingMailId);
    }

    static async UniTask<ClaimMailResult> ClaimCoreAsync(bool _all, string _mailId)
    {
        if (!EnsureSession() || IsClaiming) return null;
        if (HasPendingReceipt && (s_pendingAll != _all || s_pendingMailId != _mailId))
        {
            Error = "먼저 이전 우편의 수령 결과를 확인해 주세요.";
            OnChanged?.Invoke();
            return null;
        }
        string t_command = _all ? "claimAllMail" : "claimMail";
        if (!HasPendingReceipt)
        {
            s_receipt = $"{t_command}:{Guid.NewGuid():N}";
            s_pendingMailId = _mailId;
            s_pendingAll = _all;
        }
        int t_session = s_session;
        IsClaiming = true;
        s_version++;
        s_validUntil = 0;
        Error = null;
        OnChanged?.Invoke();
        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<ClaimMailResult>(t_command,
                new { env = s_env, mailId = _mailId }, _receiptId: s_receipt);
            if (!IsCurrent(t_session)) return null;
            // 이 시점에는 공통 배관이 지갑·세이브·미션 응답을 이미 채택했다.
            s_receipt = s_pendingMailId = null;
            s_pendingAll = false;
            if (t_result.ClaimedMailIds != null)
                foreach (string t_id in t_result.ClaimedMailIds)
                {
                    var t_mail = s_mails.Find(_entry => _entry.MailId == t_id);
                    if (t_mail != null) t_mail.State = "Claimed";
                }
            // 개별 hasMore=false는 전체 수령 가능 여부가 아니다. 정확한 뱃지는 다음 조회에서 채택한다.
            if (_all) HasClaimable = t_result.HasMore;
            return t_result;
        }
        catch (ServerCommandRejectedException t_error)
        {
            if (IsCurrent(t_session))
            {
                s_receipt = s_pendingMailId = null;
                s_pendingAll = false;
                var t_mail = s_mails.Find(_entry => _entry.MailId == _mailId);
                if (t_mail != null && t_error.Reason == "MailAlreadyClaimed") t_mail.State = "Claimed";
                if (t_mail != null && t_error.Reason == "MailExpired") t_mail.State = "Expired";
                if (t_error.Reason == "MailNothingToClaim") HasClaimable = false;
                Error = t_error.Reason == "MailAlreadyClaimed" ? "이미 받은 우편이에요."
                    : t_error.Reason == "MailExpired" ? "보관 기간이 지난 우편이에요."
                    : t_error.Reason == "MailNotFound" ? "우편을 찾을 수 없어요."
                    : t_error.Reason == "MailNothingToClaim" ? "받을 수 있는 우편이 없어요."
                    : "우편을 받을 수 없어요. 잠시 후 다시 시도해 주세요.";
            }
            return null;
        }
        catch (Exception t_error)
        {
            // 응답 유실 때 새 txId로 다시 요청하면 수령 낙인은 맞아도 보상 응답을 복구할 수 없다.
            // 같은 작업의 영수증을 보존한다. revision 충돌/채택 실패는 공통 세션 복구 표면이 처리한다.
            if (IsCurrent(t_session)) Error = "수령 결과를 확인하지 못했어요. 수령 확인을 눌러 주세요.";
            Debug.LogWarning($"[Mailbox] Claim failed: {t_error.GetBaseException().Message}");
            return null;
        }
        finally
        {
            if (IsCurrent(t_session))
            {
                IsClaiming = false;
                OnChanged?.Invoke();
            }
        }
    }

    internal static List<RewardLine> RewardLines(MailboxEntry _mail)
    {
        var t_lines = new List<RewardLine>();
        if (_mail?.Rewards?.Currencies != null)
            foreach (var t_gain in _mail.Rewards.Currencies)
                if (t_gain != null && t_gain.Amount > 0 && CurrencyCode.TryParse(t_gain.Currency, out var t_type))
                    t_lines.Add(new RewardLine(new CurrencyGain(t_type, t_gain.Amount)));
        if (_mail?.Rewards?.Items != null)
            foreach (var t_item in _mail.Rewards.Items)
                if (t_item != null && t_item.Amount > 0 && Enum.TryParse(t_item.RewardType, out ERewardType t_type))
                    t_lines.Add(new RewardLine(new AlbumRewardDef { rewardType = t_type,
                        rewardId = t_item.RewardId, amount = t_item.Amount }));
        return t_lines;
    }

}
