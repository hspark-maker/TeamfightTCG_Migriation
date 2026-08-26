using System;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>부팅 시 서버 문서를 1회 읽어 캐시를 채운 결과.</summary>
public enum ESaveSourcePrimeResult
{
    Ok,

    // 문서가 아직 없다 — 신규 세이브로 시작한다
    NotFound,

    Unauthenticated,

    // 서버에 도달하지 못했다(타임아웃·네트워크 단절)
    Unreachable,

    // 문서는 있으나 스키마·해시 검증을 통과하지 못했다
    Invalid,
}

/// <summary>Firestore 문서를 세이브 진실원으로 쓰는 저장소. 로컬 파일은 진단용 미러로만 쓴다.
/// 세이브 본문 키 하나만 서버로 가고 나머지 키(버전 백업·손상본·동기화 메타·conflict sidecar)는 전부 미러로 위임한다 —
/// 그 덕에 구버전 리셋 시 원격 payload가 자동으로 로컬 백업 키에 남아 DataSaveManager의 백업 사다리가 그대로 안전판이 된다.</summary>
public sealed class FirestoreSaveRepository : IAtomicRepository, ISaveJournalRepository
{
    // 종료 저널은 세이브 본문이 아니라 이 기기의 임시 기록이라 서버로 가지 않고 미러 폴더에 떨어진다.
    const string JOURNAL_KEY = "outgame_save_journal";

    readonly string m_profileId;
    readonly string m_saveKey;

    string m_payload = string.Empty;
    bool m_primed;
    ESaveSourcePrimeResult m_primeResult = ESaveSourcePrimeResult.Unreachable;

    /// <summary>서버가 마지막으로 확인해 준 revision. 다음 푸시의 선조건이 된다.</summary>
    public long CurrentRevision { get; private set; }

    /// <summary>캐시된 payload의 SHA256 풀해시.</summary>
    public string CurrentPayloadHash { get; private set; } = string.Empty;

    /// <summary>서버에 세이브 문서가 존재하는지.</summary>
    public bool DocumentExists { get; private set; }

    /// <summary>이 캐시를 만든 Firebase uid. 푸시 직전 현재 uid와 대조한다.</summary>
    public string OwnerUid { get; private set; } = string.Empty;

    /// <summary>진단용 로컬 미러. 세이브 본문 외의 모든 키가 여기로 간다.</summary>
    public JsonFileRepository Mirror { get; }

    public FirestoreSaveRepository(JsonFileRepository _mirror, string _profileId, string _saveKey)
    {
        Mirror = _mirror ?? throw new ArgumentNullException(nameof(_mirror));
        m_profileId = _profileId;
        m_saveKey = _saveKey;
    }

    /// <summary>부팅 전용. 서버 read 1회로 payload·revision·hash 캐시를 채운다.</summary>
    public async UniTask<ESaveSourcePrimeResult> PrimeAsync()
    {
        m_primeResult = await PullDocumentAsync();
        m_primed = true;
        return m_primeResult;
    }

    public UniTask<bool> HasAsync(string _key)
    {
        return _key == m_saveKey
            ? UniTask.FromResult(DocumentExists)
            : Mirror.HasAsync(_key);
    }

    public async UniTask<string> LoadAsync(string _key)
    {
        if (_key != m_saveKey) return await Mirror.LoadAsync(_key);

        // 빈 문자열을 돌려주면 기존 계정이 신규 세이브로 오인돼 그대로 덮인다 — 여기서는 반드시 터져야 한다.
        if (!m_primed)
            throw new InvalidOperationException("Cloud save was read before PrimeAsync().");
        if (m_primeResult != ESaveSourcePrimeResult.Ok && m_primeResult != ESaveSourcePrimeResult.NotFound)
            throw new InvalidOperationException($"Cloud save is unavailable ({m_primeResult}).");

        return m_primeResult == ESaveSourcePrimeResult.Ok ? m_payload : string.Empty;
    }

    public async UniTask<ESaveWriteResult> SaveAsync(string _key, string _value)
    {
        if (_key != m_saveKey) return await Mirror.SaveAsync(_key, _value);

        return await PushAsync(_value);
    }

    public async UniTask<ESaveWriteResult> ReplaceWithBackupAsync(string _key, string _value, string _backupKey)
    {
        if (_key != m_saveKey) return await Mirror.ReplaceWithBackupAsync(_key, _value, _backupKey);

        if (DocumentExists && !string.IsNullOrEmpty(m_payload) &&
            await Mirror.SaveAsync(_backupKey, m_payload) != ESaveWriteResult.Success)
        {
            Debug.LogError($"[FirestoreSaveRepository] 교체 전 원격 payload 백업에 실패해 교체를 중단한다. key={_backupKey}");
            return ESaveWriteResult.IoFailed;
        }

        return await PushAsync(_value);
    }

    public UniTask DeleteAsync(string _key)
    {
        if (_key != m_saveKey) return Mirror.DeleteAsync(_key);

        // firestore.rules 가 allow delete: if false 라 요청해봐야 거절된다.
        Debug.LogWarning("[FirestoreSaveRepository] 클라우드 세이브 문서는 삭제할 수 없다 — 요청을 무시한다.");
        return UniTask.CompletedTask;
    }

    /// <summary>종료 콜백 전용. 지금 스냅샷을 미러에 동기 기록한다 —
    /// 채택할지 폐기할지는 다음 부팅이 서버 revision과 대조해 싸게 판정한다.</summary>
    public ESaveWriteResult WriteJournalBlocking(string _payload)
    {
        try
        {
            var t_entry = new SaveJournalEntry
            {
                payload = _payload,
                payloadHash = PlayerSaveDocument.HashOf(_payload),
                baseRevision = CurrentRevision,
                schemaVersion = UserSaveData.VERSION,
                profileId = m_profileId,
                uid = OwnerUid,
                writtenAtUtcTicks = DateTime.UtcNow.Ticks,
            };

            return Mirror.SaveBlocking(JOURNAL_KEY, JsonUtility.ToJson(t_entry));
        }
        catch (Exception t_exception)
        {
            // 종료 콜백에서 예외가 새면 이후 flush 대상까지 함께 죽는다.
            Debug.LogError($"[FirestoreSaveRepository] 종료 저널 기록 실패: {t_exception.GetBaseException().Message}");
            return ESaveWriteResult.IoFailed;
        }
    }

    /// <summary>부팅 전용. PrimeAsync 직후에 부른다 — 남아 있는 종료 저널을 서버에 올리거나 폐기한다.</summary>
    public async UniTask<ESaveWriteResult> ConsumeJournalAsync()
    {
        string t_json;
        try
        {
            t_json = await Mirror.LoadAsync(JOURNAL_KEY);
        }
        catch (Exception t_exception)
        {
            // 여기서 예외가 새면 부트 게이트를 열 주체가 사라져 로딩이 끝나지 않는다.
            Debug.LogError($"[FirestoreSaveRepository] 종료 저널을 읽지 못했다: {t_exception.GetBaseException().Message}");
            return ESaveWriteResult.IoFailed;
        }

        if (string.IsNullOrEmpty(t_json)) return ESaveWriteResult.Success;

        SaveJournalEntry t_entry = null;
        try
        {
            t_entry = JsonUtility.FromJson<SaveJournalEntry>(t_json);
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning(
                $"[FirestoreSaveRepository] 종료 저널을 해석하지 못했다: {t_exception.GetBaseException().Message}");
        }

        if (t_entry == null || string.IsNullOrEmpty(t_entry.payload))
        {
            Debug.LogWarning("[FirestoreSaveRepository] 종료 저널이 손상돼 폐기한다.");
            await DiscardJournalAsync();
            return ESaveWriteResult.Success;
        }

        string t_rejectReason = RejectReasonOf(t_entry);
        if (t_rejectReason != null)
        {
            Debug.Log($"[FirestoreSaveRepository] 종료 저널을 폐기한다 — {t_rejectReason}");
            await DiscardJournalAsync();
            return ESaveWriteResult.Success;
        }

        try
        {
            long t_newRevision = await PlayerSaveDocument.PushTransactionAsync(
                t_entry.uid,
                m_profileId,
                t_entry.payload,
                t_entry.payloadHash,
                t_entry.baseRevision,
                CurrentPayloadHash);

            DocumentExists = true;
            CurrentRevision = t_newRevision;
            CurrentPayloadHash = t_entry.payloadHash;
            m_payload = t_entry.payload;
            await WriteMirrorAsync(t_entry.payload);
            await DiscardJournalAsync();

            Debug.Log(
                $"[FirestoreSaveRepository] 종료 저널을 반영했다. profile={m_profileId}, revision={t_newRevision}");
            return ESaveWriteResult.Success;
        }
        catch (Exception t_exception)
        {
            ESaveWriteResult t_result = ClassifyPushFailure(t_exception);
            string t_message = t_exception.GetBaseException().Message;

            // 그 사이 다른 기기가 이미 썼다는 뜻이라 이 저널은 무효다 — 남겨두면 매 부팅 실패하며 영원히 못 올린다.
            if (t_result == ESaveWriteResult.Conflict)
            {
                Debug.LogWarning($"[FirestoreSaveRepository] 종료 저널이 원격 변경과 충돌해 폐기한다: {t_message}");
                await DiscardJournalAsync();
                return ESaveWriteResult.Success;
            }

            Debug.LogWarning(
                $"[FirestoreSaveRepository] 종료 저널 업로드 실패({t_result}) — 저널을 남긴다: {t_message}");
            return t_result;
        }
    }

    async UniTask<ESaveSourcePrimeResult> PullDocumentAsync()
    {
        FirebaseAuthService t_auth = FirebaseAuthService.Instance;
        if (t_auth.State != EFirebaseAuthState.SignedIn || !t_auth.IsCurrentUserActive)
        {
            Debug.LogWarning($"[FirestoreSaveRepository] 인증이 준비되지 않았다. state={t_auth.State}");
            return ESaveSourcePrimeResult.Unauthenticated;
        }

        string t_userId = t_auth.UserId;
        DocumentSnapshot t_snapshot;
        try
        {
            Task<DocumentSnapshot> t_readTask =
                PlayerSaveDocument.Document(t_userId, m_profileId).GetSnapshotAsync(Source.Server);
            Task t_completedTask = await Task.WhenAny(t_readTask, Task.Delay(PlayerSaveDocument.PULL_TIMEOUT_MS));
            if (t_completedTask != t_readTask)
            {
                PlayerSaveDocument.ObserveLateTaskAsync(t_readTask).Forget();
                Debug.LogWarning($"[FirestoreSaveRepository] 세이브 문서 read 타임아웃. profile={m_profileId}");
                return ESaveSourcePrimeResult.Unreachable;
            }

            t_snapshot = await t_readTask;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning(
                $"[FirestoreSaveRepository] 세이브 문서 read 실패: {t_exception.GetBaseException().Message}");
            return ESaveSourcePrimeResult.Unreachable;
        }

        if (!t_snapshot.Exists)
        {
            DocumentExists = false;
            CurrentRevision = 0;
            CurrentPayloadHash = string.Empty;
            m_payload = string.Empty;
            // 첫 푸시가 소유 대조에 걸리지 않도록 여기서도 uid를 고정한다 — 서버를 이 uid로 읽은 사실은 같다.
            OwnerUid = t_userId;

            // 진실원 전환에서 가장 중대한 분기다 — 여기가 무음이면 "신규로 시작됐다"를 사후에 알 길이 없다.
            Debug.Log(
                $"[FirestoreSaveRepository] 서버에 세이브 문서가 없다 — 신규로 시작한다. " +
                $"profile={m_profileId}, uid={t_userId}");
            return ESaveSourcePrimeResult.NotFound;
        }

        if (!PlayerSaveDocument.TryReadRemote(t_snapshot, out long t_schemaVersion, out long t_revision,
                out string t_payload, out string t_fullHash, out string t_error))
        {
            Debug.LogError(
                $"[FirestoreSaveRepository] 원격 세이브가 유효하지 않다: {t_error}. profile={m_profileId}");
            return ESaveSourcePrimeResult.Invalid;
        }

        DocumentExists = true;
        CurrentRevision = t_revision;
        CurrentPayloadHash = t_fullHash;
        m_payload = t_payload;
        OwnerUid = t_userId;

        // 로컬 파일에는 없던 제약이라 상한에 다가가는 것이 눈에 띄어야 한다.
        int t_payloadBytes = Encoding.UTF8.GetByteCount(t_payload);
        if (t_payloadBytes > PlayerSaveDocument.PAYLOAD_WARNING_BYTES)
            Debug.LogWarning(
                $"[FirestoreSaveRepository] payload가 {t_payloadBytes} bytes다. " +
                $"문서 상한은 {PlayerSaveDocument.PAYLOAD_MAX_BYTES} bytes.");

        Debug.Log(
            $"[FirestoreSaveRepository] 클라우드 세이브 로드. profile={m_profileId}, uid={t_userId}, " +
            $"revision={t_revision}, schema=v{t_schemaVersion}, bytes={t_payloadBytes}");

        await WriteMirrorAsync(t_payload);
        return ESaveSourcePrimeResult.Ok;
    }

    async UniTask<ESaveWriteResult> PushAsync(string _value)
    {
        FirebaseAuthService t_auth = FirebaseAuthService.Instance;
        if (t_auth.State != EFirebaseAuthState.SignedIn || !t_auth.IsCurrentUserActive)
        {
            Debug.LogWarning($"[FirestoreSaveRepository] 인증이 끊겨 세이브를 올리지 못했다. state={t_auth.State}");
            return ESaveWriteResult.Offline;
        }

        string t_userId = t_auth.UserId;
        // Cloud에서는 PlayerSaveSync의 계정 전환 감시가 꺼진다 — 남의 문서를 덮는 것을 이 대조가 대신 막는다.
        if (OwnerUid != t_userId)
        {
            Debug.LogError(
                $"[FirestoreSaveRepository] 세이브 소유 uid가 바뀌었다. owner={OwnerUid}, current={t_userId}");
            GameManager.MarkRecoveryRequired();
            return ESaveWriteResult.IoFailed;
        }

        string t_fullHash = PlayerSaveDocument.HashOf(_value);
        long t_previousRevision = CurrentRevision;
        try
        {
            long t_newRevision = await PlayerSaveDocument.PushTransactionAsync(
                t_userId,
                m_profileId,
                _value,
                t_fullHash,
                CurrentRevision,
                CurrentPayloadHash);

            DocumentExists = true;
            CurrentRevision = t_newRevision;
            CurrentPayloadHash = t_fullHash;
            m_payload = _value;
            await WriteMirrorAsync(_value);

            // 성공이 무음이면 "안 써진 것"과 "쓸 것이 없던 것"을 콘솔로 구분할 수 없다.
            if (t_newRevision == t_previousRevision)
                Debug.Log(
                    $"[FirestoreSaveRepository] 서버와 내용이 같아 쓰기를 생략했다. revision={t_newRevision}");
            else
                Debug.Log(
                    $"[FirestoreSaveRepository] 세이브 푸시. revision={t_previousRevision}→{t_newRevision}, " +
                    $"bytes={Encoding.UTF8.GetByteCount(_value)}");

            return ESaveWriteResult.Success;
        }
        catch (Exception t_exception)
        {
            ESaveWriteResult t_result = ClassifyPushFailure(t_exception);
            Debug.LogWarning(
                $"[FirestoreSaveRepository] 세이브 푸시 실패({t_result}): {t_exception.GetBaseException().Message}");
            return t_result;
        }
    }

    // 채택 조건 중 어긋난 첫 항목을 돌려준다. 전부 맞으면 null.
    string RejectReasonOf(SaveJournalEntry _entry)
    {
        if (string.IsNullOrEmpty(OwnerUid) || _entry.uid != OwnerUid)
            return $"계정이 다르다(journal={_entry.uid}, owner={OwnerUid})";

        if (_entry.profileId != m_profileId)
            return $"프로필이 다르다(journal={_entry.profileId}, current={m_profileId})";

        if (_entry.schemaVersion != UserSaveData.VERSION)
            return $"스키마가 다르다(journal=v{_entry.schemaVersion}, client=v{UserSaveData.VERSION})";

        // 저널은 원자적으로 쓰이지 않는다 — 종료 도중 잘린 payload를 서버에 올리지 않기 위한 대조다.
        if (PlayerSaveDocument.HashOf(_entry.payload) != _entry.payloadHash)
            return "payload가 자기 해시와 맞지 않는다";

        // 두 방향은 뜻이 다르다 — 뒤쪽은 저널이 딛고 선 서버 상태가 사라졌다는 신호라 조용히 넘기면 안 된다.
        if (_entry.baseRevision < CurrentRevision)
            return $"서버가 앞서 있다(journal base={_entry.baseRevision}, server={CurrentRevision})";

        if (_entry.baseRevision > CurrentRevision)
            return $"저널이 딛고 선 서버 상태가 없다(journal base={_entry.baseRevision}, server={CurrentRevision})";

        // 종료 직전 푸시가 실은 성공했다는 뜻이라 다시 올릴 것이 없다.
        if (_entry.payloadHash == CurrentPayloadHash)
            return "이미 서버에 반영된 내용이다";

        return null;
    }

    // 삭제 실패는 진행을 막지 않는다 — 남은 저널은 다음 부팅에서 baseRevision 대조에 걸려 폐기된다.
    async UniTask DiscardJournalAsync()
    {
        try
        {
            await Mirror.DeleteAsync(JOURNAL_KEY);
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[FirestoreSaveRepository] 종료 저널 삭제 실패: {t_exception.GetBaseException().Message}");
        }
    }

    async UniTask WriteMirrorAsync(string _payload)
    {
        // 미러는 진단용이라 실패해도 진실원(서버)에는 영향이 없다.
        if (await Mirror.SaveAsync(m_saveKey, _payload) != ESaveWriteResult.Success)
            Debug.LogWarning("[FirestoreSaveRepository] 진단용 미러 기록에 실패했다 — 진행에는 영향이 없다.");
    }

    // Firestore SDK가 콜백 예외를 여러 겹으로 감싸므로 타입을 사슬 전체에서 찾는다.
    static ESaveWriteResult ClassifyPushFailure(Exception _exception)
    {
        for (Exception t_cursor = _exception; t_cursor != null; t_cursor = t_cursor.InnerException)
        {
            if (t_cursor is PlayerSavePreconditionException) return ESaveWriteResult.Conflict;
        }

        for (Exception t_cursor = _exception; t_cursor != null; t_cursor = t_cursor.InnerException)
        {
            if (t_cursor is TimeoutException || t_cursor is OperationCanceledException)
                return ESaveWriteResult.Offline;
            if (t_cursor is FirestoreException t_firestore)
                return ClassifyFirestoreError(t_firestore.ErrorCode);
        }

        return ESaveWriteResult.IoFailed;
    }

    static ESaveWriteResult ClassifyFirestoreError(FirestoreError _error)
    {
        switch (_error)
        {
            case FirestoreError.Unavailable:
            case FirestoreError.DeadlineExceeded:
            case FirestoreError.Cancelled:
            case FirestoreError.ResourceExhausted:
                return ESaveWriteResult.Offline;

            // 다른 쓰기가 먼저 커밋돼 트랜잭션이 밀린 상태 — 재조회가 필요하다.
            case FirestoreError.Aborted:
            case FirestoreError.FailedPrecondition:
                return ESaveWriteResult.Conflict;

            default:
                return ESaveWriteResult.IoFailed;
        }
    }
}
