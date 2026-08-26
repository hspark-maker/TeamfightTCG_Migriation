using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>푸시 선조건(revision·payloadHash)이 서버 상태와 어긋났을 때 던진다.
/// 호출부가 "재시도로 풀릴 실패"와 "재조회가 필요한 충돌"을 문자열 대조 없이 가르기 위한 타입이다.</summary>
class PlayerSavePreconditionException : Exception
{
    public PlayerSavePreconditionException(string _message) : base(_message) { }
}

/// <summary>클라우드 세이브 문서(users/{uid}/save/{profileId})의 경로·필드·트랜잭션을 소유하는 단일 창구.</summary>
static class PlayerSaveDocument
{
    internal const string FIELD_SCHEMA_VERSION = "schemaVersion";
    internal const string FIELD_REVISION = "revision";
    internal const string FIELD_PAYLOAD = "payload";
    internal const string FIELD_PAYLOAD_HASH = "payloadHash";
    internal const string FIELD_UPDATED_AT = "updatedAt";
    internal const string FIELD_DEVICE_ID = "deviceId";
    internal const string FIELD_APP_VERSION = "appVersion";

    internal const int PAYLOAD_WARNING_BYTES = 256 * 1024;
    // firestore.rules 의 payload.size() <= 300000 과 맞물린 계약이다.
    internal const int PAYLOAD_MAX_BYTES = 300000;
    // firestore.rules 의 payloadHash.size() == 16 과 맞물린 계약이다.
    internal const int WIRE_HASH_LENGTH = 16;
    internal const int PULL_TIMEOUT_MS = 5000;
    internal const int TRANSACTION_TIMEOUT_MS = 10000;

    const string DEVICE_ID_KEY = "firebase.playerSave.deviceId";
    const string USERS_COLLECTION = "users";
    const string SAVE_COLLECTION = "save";

    static FirebaseFirestore s_firestore;

    /// <summary>해당 유저·프로필의 세이브 문서 참조.</summary>
    internal static DocumentReference Document(string _userId, string _profileId)
    {
        return Firestore().Collection(USERS_COLLECTION)
            .Document(_userId)
            .Collection(SAVE_COLLECTION)
            .Document(_profileId);
    }

    /// <summary>payload 의 SHA256 풀해시(소문자 hex).</summary>
    internal static string HashOf(string _payload)
    {
        using SHA256 t_sha = SHA256.Create();
        byte[] t_hash = t_sha.ComputeHash(Encoding.UTF8.GetBytes(_payload));
        return BitConverter.ToString(t_hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>문서에 적히는 와이어 해시 — 풀해시의 앞 16자.</summary>
    internal static string WireHashOf(string _fullHash)
    {
        return string.IsNullOrEmpty(_fullHash)
            ? string.Empty
            : _fullHash.Substring(0, WIRE_HASH_LENGTH);
    }

    /// <summary>이 기기의 고정 식별자. 없으면 생성해 PlayerPrefs 에 남긴다.</summary>
    internal static string DeviceId()
    {
        string t_deviceId = PlayerPrefs.GetString(DEVICE_ID_KEY, string.Empty);
        if (!string.IsNullOrEmpty(t_deviceId)) return t_deviceId;

        t_deviceId = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(DEVICE_ID_KEY, t_deviceId);
        PlayerPrefs.Save();
        return t_deviceId;
    }

    /// <summary>원격 문서를 검증해 스키마·revision·payload·풀해시를 꺼낸다. 실패 사유는 _error 에 담긴다.</summary>
    internal static bool TryReadRemote(
        DocumentSnapshot _snapshot,
        out long _schemaVersion,
        out long _revision,
        out string _payload,
        out string _fullHash,
        out string _error)
    {
        _schemaVersion = 0;
        _revision = 0;
        _payload = string.Empty;
        _fullHash = string.Empty;
        _error = string.Empty;

        if (!_snapshot.TryGetValue(FIELD_SCHEMA_VERSION, out _schemaVersion) ||
            !_snapshot.TryGetValue(FIELD_REVISION, out _revision) ||
            !_snapshot.TryGetValue(FIELD_PAYLOAD, out _payload) ||
            !_snapshot.TryGetValue(FIELD_PAYLOAD_HASH, out string t_wireHash))
        {
            _error = "missing or invalid fields";
            return false;
        }

        if (_schemaVersion <= 0 || _revision < 1 || string.IsNullOrEmpty(_payload))
        {
            _error = "invalid schema, revision, or payload";
            return false;
        }

        int t_payloadBytes = Encoding.UTF8.GetByteCount(_payload);
        if (t_payloadBytes > PAYLOAD_MAX_BYTES)
        {
            _error = $"payload too large ({t_payloadBytes} bytes)";
            return false;
        }

        _fullHash = HashOf(_payload);
        if (string.IsNullOrEmpty(t_wireHash) ||
            t_wireHash.Length != WIRE_HASH_LENGTH ||
            !_fullHash.StartsWith(t_wireHash, StringComparison.OrdinalIgnoreCase))
        {
            _error = "payload hash mismatch";
            return false;
        }

        UserSaveData t_remoteData;
        try
        {
            t_remoteData = JsonUtility.FromJson<UserSaveData>(_payload);
        }
        catch (Exception)
        {
            _error = "payload JSON is invalid";
            return false;
        }

        if (!IsComplete(t_remoteData) || t_remoteData.version != _schemaVersion)
        {
            _error = "payload schema does not match document";
            return false;
        }

        return true;
    }

    /// <summary>선조건(revision·payloadHash)을 대조하고 revision 을 1 올려 payload 를 커밋한다. 새 revision 을 돌려준다.</summary>
    internal static async Task<long> PushTransactionAsync(
        string _userId,
        string _profileId,
        string _payload,
        string _fullHash,
        long _expectedRevision,
        string _expectedRemoteHash)
    {
        DocumentReference t_document = Document(_userId, _profileId);
        string t_wireHash = WireHashOf(_fullHash);
        string t_deviceId = DeviceId();
        string t_appVersion = Application.version;
        int t_payloadBytes = Encoding.UTF8.GetByteCount(_payload);
        if (t_payloadBytes > PAYLOAD_MAX_BYTES)
            throw new InvalidOperationException($"Payload too large: {t_payloadBytes} bytes.");

        Task<long> t_transactionTask = Firestore().RunTransactionAsync<long>(async t_transaction =>
        {
            DocumentSnapshot t_snapshot = await t_transaction.GetSnapshotAsync(t_document);
            long t_currentRevision = 0;
            string t_currentWireHash = string.Empty;
            if (t_snapshot.Exists)
            {
                if (!t_snapshot.TryGetValue(FIELD_SCHEMA_VERSION, out long t_currentSchemaVersion) ||
                    t_currentSchemaVersion != UserSaveData.VERSION ||
                    !t_snapshot.TryGetValue(FIELD_REVISION, out t_currentRevision) ||
                    t_currentRevision < 1 ||
                    !t_snapshot.TryGetValue(FIELD_PAYLOAD_HASH, out t_currentWireHash))
                    throw new InvalidOperationException("Remote transaction precondition fields are invalid.");

                // ack 유실로 같은 payload 를 다시 올리는 경우 — 중복 커밋 대신 현재 revision 을 그대로 돌려준다.
                if (string.Equals(t_currentWireHash, t_wireHash, StringComparison.OrdinalIgnoreCase) &&
                    t_snapshot.TryGetValue(FIELD_PAYLOAD, out string t_currentPayload) &&
                    HashOf(t_currentPayload) == _fullHash)
                    return t_currentRevision;
            }

            string t_expectedWireHash = WireHashOf(_expectedRemoteHash);
            if (t_currentRevision != _expectedRevision ||
                !string.Equals(t_currentWireHash, t_expectedWireHash, StringComparison.OrdinalIgnoreCase))
                throw new PlayerSavePreconditionException("Remote save changed after inspection.");

            long t_newRevision = t_currentRevision + 1;
            var t_data = new Dictionary<string, object>
            {
                [FIELD_SCHEMA_VERSION] = UserSaveData.VERSION,
                [FIELD_REVISION] = t_newRevision,
                [FIELD_PAYLOAD] = _payload,
                [FIELD_PAYLOAD_HASH] = t_wireHash,
                [FIELD_UPDATED_AT] = FieldValue.ServerTimestamp,
                [FIELD_DEVICE_ID] = t_deviceId,
                [FIELD_APP_VERSION] = t_appVersion
            };
            t_transaction.Set(t_document, t_data, SetOptions.MergeAll);
            return t_newRevision;
        });


        Task t_completedTask = await Task.WhenAny(
            t_transactionTask,
            Task.Delay(TRANSACTION_TIMEOUT_MS));
        if (t_completedTask != t_transactionTask)
        {
            ObserveLateTaskAsync(t_transactionTask).Forget();
            throw new TimeoutException("Firestore save transaction timed out.");
        }

        return await t_transactionTask;
    }

    /// <summary>타임아웃으로 버려진 Task 를 관측해 UnobservedTaskException 을 막는다.</summary>
    internal static async UniTaskVoid ObserveLateTaskAsync(Task _task)
    {
        try
        {
            await _task;
        }
        catch
        {
            // The visible timeout already owns the failure.
        }
    }

    // 도메인 리로드가 꺼진 에디터 재생에서 이전 세션 인스턴스가 남지 않도록 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_firestore = null;
    }

    static FirebaseFirestore Firestore()
    {
        if (s_firestore != null) return s_firestore;

        s_firestore = FirebaseFirestore.DefaultInstance;
        s_firestore.Settings.PersistenceEnabled = false;
        return s_firestore;
    }

    static bool IsComplete(UserSaveData _data)
    {
        return _data != null &&
               _data.currency != null &&
               _data.ownership != null &&
               _data.deck != null &&
               _data.tutorial != null &&
               _data.rank != null &&
               _data.cardGrowth != null &&
               _data.keywordGrowth != null &&
               _data.albumReward != null &&
               _data.tournament != null &&
               _data.profile != null;
    }
}
