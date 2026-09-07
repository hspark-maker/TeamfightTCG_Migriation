using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using UnityEngine;

public static partial class SpecFirestoreUploader
{
    [Serializable] sealed class FirestoreBooleanValue { public bool booleanValue; }
    [Serializable] sealed class FirestoreTimestampValue { public string timestampValue; }
    [Serializable] sealed class BattleReplayConfigFields
    {
        public FirestoreBooleanValue enabled;
        public FirestoreTimestampValue updatedAt;
        public FirestoreStringValue updatedBy;
        public FirestoreStringValue note;
    }
    [Serializable] sealed class BattleReplayConfigDocument
    {
        public BattleReplayConfigFields fields;
    }
    [Serializable] sealed class ReplayDailyFields
    {
        public FirestoreStringValue day;
        public FirestoreIntegerValue settled;
        public FirestoreIntegerValue replayOk;
        public FirestoreIntegerValue replayFailed;
        public FirestoreIntegerValue unavailable;
        public FirestoreIntegerValue divergent;
        public FirestoreIntegerValue outcomeMismatch;
        public FirestoreIntegerValue hashMismatch;
    }
    [Serializable] sealed class ReplayDailyDocument
    {
        public ReplayDailyFields fields;
    }

    public sealed class BattleReplayConfigState
    {
        public bool Exists;
        public bool Enabled;
        public string UpdatedAt;
        public string UpdatedBy;
        public string Note;
    }

    public sealed class ReplayDailyState
    {
        public string Day;
        public long Settled;
        public long ReplayOk;
        public long ReplayFailed;
        public long Unavailable;
        public long Divergent;
        public long OutcomeMismatch;
        public long HashMismatch;
    }

    public static bool TryGetBattleReplayConfig(
        string _envId, out BattleReplayConfigState _state, out string _error)
    {
        _state = null;
        _error = null;
        if (!TryReadFirebaseConfig(out string t_projectId, out string t_apiKey, out _error)) return false;

        using var t_client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        string t_url = BattleReplayConfigUrl(t_projectId, t_apiKey, _envId);
        if (!TrySend(t_client, HttpMethod.Get, t_url, null,
                     out HttpStatusCode t_status, out string t_text, out _error)) return false;
        if (t_status == HttpStatusCode.NotFound)
        {
            _state = new BattleReplayConfigState { Exists = false, Enabled = false };
            return true;
        }
        if ((int)t_status < 200 || (int)t_status >= 300)
        {
            _error = $"전투 재생 설정 조회 실패 {(int)t_status}: {Shorten(t_text)}";
            return false;
        }

        BattleReplayConfigFields t_fields;
        try { t_fields = JsonUtility.FromJson<BattleReplayConfigDocument>(t_text)?.fields; }
        catch (Exception t_exception)
        {
            _error = $"전투 재생 설정 파싱 실패: {t_exception.Message}";
            return false;
        }
        _state = new BattleReplayConfigState
        {
            Exists = true,
            Enabled = t_fields?.enabled?.booleanValue == true,
            UpdatedAt = t_fields?.updatedAt?.timestampValue,
            UpdatedBy = t_fields?.updatedBy?.stringValue,
            Note = t_fields?.note?.stringValue,
        };
        return true;
    }

    public static bool SetBattleReplayEnabled(string _envId, bool _enabled, string _note, out string _error)
    {
        _error = null;
        if (!TryReadFirebaseConfig(out string t_projectId, out string t_apiKey, out _error)) return false;

        string t_resource = "projects/" + t_projectId + "/databases/" + FirebaseRootPath.DatabaseId +
                            "/documents/" + FirebaseRootPath.Environment(_envId) + "/config/battleReplay";
        var t_body = new StringBuilder(512);
        t_body.Append("{\"writes\":[{\"update\":{\"name\":");
        AppendJsonString(t_body, t_resource);
        t_body.Append(",\"fields\":{\"enabled\":{\"booleanValue\":")
              .Append(_enabled ? "true" : "false")
              .Append("},\"updatedAt\":{\"timestampValue\":");
        AppendJsonString(t_body, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        t_body.Append("},\"updatedBy\":{\"stringValue\":");
        AppendJsonString(t_body, SpecAdminAuth.SignedInEmail ?? string.Empty);
        t_body.Append("},\"note\":{\"stringValue\":");
        AppendJsonString(t_body, (_note ?? string.Empty).Trim());
        t_body.Append("}}}}]}" );

        using var t_client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        return TryCommit(t_client, t_projectId, t_apiKey, t_body.ToString(), out _error);
    }

    public static bool TryGetReplayDaily(
        string _envId, int _days, out List<ReplayDailyState> _states, out string _error)
    {
        _states = new List<ReplayDailyState>();
        _error = null;
        if (!TryReadFirebaseConfig(out string t_projectId, out string t_apiKey, out _error)) return false;

        using var t_client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        for (int i = 0; i < _days; i++)
        {
            string t_day = DateTime.UtcNow.Date.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string t_url = ReplayDailyUrl(t_projectId, t_apiKey, _envId, t_day);
            if (!TrySend(t_client, HttpMethod.Get, t_url, null,
                         out HttpStatusCode t_status, out string t_text, out _error)) return false;
            if (t_status == HttpStatusCode.NotFound)
            {
                _states.Add(new ReplayDailyState { Day = t_day });
                continue;
            }
            if ((int)t_status < 200 || (int)t_status >= 300)
            {
                _error = $"전투 재생 집계 조회 실패 {(int)t_status}: {Shorten(t_text)}";
                return false;
            }

            ReplayDailyFields t_fields;
            try { t_fields = JsonUtility.FromJson<ReplayDailyDocument>(t_text)?.fields; }
            catch (Exception t_exception)
            {
                _error = $"전투 재생 집계 파싱 실패: {t_exception.Message}";
                return false;
            }
            _states.Add(new ReplayDailyState
            {
                Day = t_fields?.day?.stringValue ?? t_day,
                Settled = Integer(t_fields?.settled),
                ReplayOk = Integer(t_fields?.replayOk),
                ReplayFailed = Integer(t_fields?.replayFailed),
                Unavailable = Integer(t_fields?.unavailable),
                Divergent = Integer(t_fields?.divergent),
                OutcomeMismatch = Integer(t_fields?.outcomeMismatch),
                HashMismatch = Integer(t_fields?.hashMismatch),
            });
        }
        return true;
    }

    public static bool TryGetCloudProjectId(out string _projectId, out string _error)
    {
        return TryReadFirebaseConfig(out _projectId, out _, out _error);
    }

    static long Integer(FirestoreIntegerValue _value) =>
        long.TryParse(_value?.integerValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t_value) ? t_value : 0;

    static string BattleReplayConfigUrl(string _projectId, string _apiKey, string _envId) =>
        ApiRoot(_projectId) + "/documents/" + EscapedEnvironmentPath(_envId) +
        "/config/battleReplay?key=" + Uri.EscapeDataString(_apiKey);

    static string ReplayDailyUrl(string _projectId, string _apiKey, string _envId, string _day) =>
        ApiRoot(_projectId) + "/documents/" + EscapedEnvironmentPath(_envId) +
        "/telemetry/replayDaily/days/" + Uri.EscapeDataString(_day) + "?key=" + Uri.EscapeDataString(_apiKey);
}
