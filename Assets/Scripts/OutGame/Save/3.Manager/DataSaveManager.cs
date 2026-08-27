using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

// 아웃게임 세이브 매니저. 진실원은 클라우드 문서이고 로컬은 오프라인 폴백용 캐시 봉투다.
public static class DataSaveManager
{
    const string SAVE_KEY = "outgame_save";
    const string BACKUP_KEY = "outgame_save_prev";
    const string CORRUPT_KEY = "outgame_save_corrupt";

    // JsonUtility는 auto-property를 직렬화하지 못한다 — Firestore 매핑용 프로퍼티 모델과 같은 모양을 쓰려면 Newtonsoft여야 한다.
    static readonly JsonSerializerSettings s_serializerSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.None,

        // Firestore 필드명과 같은 인코딩을 쓴다 — [FirestoreProperty] 이름이 전부 프로퍼티명의 camelCase다.
        // 두 직렬화기가 같은 키를 내야 로컬 캐시와 원격 문서를 나란히 대조할 수 있다.
        ContractResolver = new DefaultContractResolver
        {
            // 딕셔너리 키는 건드리지 않는다 — 재화 키가 ECurrencyType 이름이라 소문자로 바뀌면 파싱이 깨진다.
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },

        // 기본값(Auto)은 프로퍼티 이니셜라이저로 만든 컬렉션에 이어붙인다 — 값이 조용히 중복되는 것을 막는다.
        ObjectCreationHandling = ObjectCreationHandling.Replace,
    };

    static IRepository s_repository = new JsonFileRepository();
    static Action s_immediateUploadHandler;
    static long s_cachedRevision;

    public static event Action OnSaved;

    public static UserSaveData Data { get; private set; } = new UserSaveData();

    internal static bool HasLocalSave => s_repository.Has(SAVE_KEY);

    // 저장 매체 교체. 클라우드 채택 이전에 호출한다.
    public static void SetRepository(IRepository _repository)
    {
        if (_repository != null) s_repository = _repository;
    }

    /// <summary>로컬 캐시를 통째로 버리고 메모리 세이브도 빈 값으로 되돌린다.
    /// 계정을 갈아탈 때 남의 진행도를 물고 가지 않게 하는 유일한 통로다 —
    /// 캐시 소유자가 어긋난 채로 부팅하면 클라우드가 세션을 차단한다.</summary>
    internal static void ClearLocalCache()
    {
        s_repository.Delete(SAVE_KEY);
        s_repository.Delete(BACKUP_KEY);
        s_cachedRevision = 0;
        Data = new UserSaveData();
    }

    // 클라우드 계층이 꽂는다. 3계층이 4계층을 직접 참조하지 않게 하는 배선(OnSaved와 대칭).
    public static void SetImmediateUploadHandler(Action _handler)
    {
        s_immediateUploadHandler = _handler;
    }

    /// <summary>메모리 세이브를 캐시 봉투로 굳히고 변경을 통지한다(업로드는 클라우드 계층이 디바운스해서 한다).</summary>
    public static void Save()
    {
        WriteCache();
        OnSaved?.Invoke();
    }

    /// <summary>디바운스를 기다리지 않고 업로드까지 요청하는 저장. 유실되면 안 되는 지점에서만 쓴다.</summary>
    public static void SaveImmediate()
    {
        Save();
        s_immediateUploadHandler?.Invoke();
    }

    /// <summary>도메인만 담은 직렬화 결과. 원격 문서 대조·변경 감지의 기준값이다(봉투 메타는 빠진다).</summary>
    public static string CreateSnapshot()
    {
        return SnapshotOf(Data);
    }

    /// <summary>메모리에 세우지 않은 세이브의 스냅샷. 원격과 캐시를 같은 잣대로 대조할 때 쓴다.</summary>
    internal static string SnapshotOf(UserSaveData _data)
    {
        // 정규화 후에 찍는다 — 한쪽만 슬롯이 비어 있으면 내용이 같아도 다른 스냅샷이 나온다.
        return JsonConvert.SerializeObject(Normalize(_data), s_serializerSettings);
    }

    /// <summary>초기화에서 채택한 세이브를 메모리에 세우고 캐시 봉투를 갱신한다. 채택은 초기화당 1회다.</summary>
    internal static void AdoptRemote(UserSaveData _data, long _revision)
    {
        Data = Normalize(_data);
        s_cachedRevision = _revision < 0 ? 0 : _revision;
        WriteCache();
    }

    /// <summary>업로드에 성공한 revision을 캐시 봉투에 새긴다 — 다음 오프라인 세션의 기대값이 된다.</summary>
    internal static void MarkUploadedRevision(long _revision)
    {
        if (_revision <= s_cachedRevision) return;

        s_cachedRevision = _revision;
        WriteCache();
    }

    /// <summary>오프라인 폴백용 캐시 읽기. 없거나 스키마가 어긋나면 false, 깨져 있으면 원본을 남기고 false.</summary>
    internal static bool TryLoadCache(out UserSaveData _data, out long _revision)
    {
        _data = null;
        _revision = 0;

        string t_json = s_repository.Load(SAVE_KEY);
        if (string.IsNullOrEmpty(t_json)) return false;

        PlayerSaveCacheEnvelope t_envelope;
        try
        {
            t_envelope = JsonConvert.DeserializeObject<PlayerSaveCacheEnvelope>(t_json, s_serializerSettings);
        }
        catch (Exception t_exception)
        {
            // 원본을 백업하고 캐시를 걷는다 — 남겨 두면 매 초기화 같은 LogError가 반복된다.
            Debug.LogError($"[DataSaveManager] Local cache is corrupt. Backing up the source: {t_exception}");
            s_repository.Save(CORRUPT_KEY, t_json);
            s_repository.Delete(SAVE_KEY);
            return false;
        }

        if (t_envelope?.Data == null)
        {
            Debug.LogError("[DataSaveManager] Local cache has no save data. Backing up the source.");
            s_repository.Save(CORRUPT_KEY, t_json);
            s_repository.Delete(SAVE_KEY);
            return false;
        }

        if (t_envelope.SchemaVersion != UserSaveData.VERSION)
        {
            // 변환 코드가 없어 쓸 수 없는 캐시다. 손상 경로와 같이 원본을 남기고 걷는다.
            Debug.LogWarning(
                $"[DataSaveManager] Local cache schema v{t_envelope.SchemaVersion} does not match client v{UserSaveData.VERSION}. Backing up and discarding.");
            s_repository.Save(CORRUPT_KEY, t_json);
            s_repository.Delete(SAVE_KEY);
            return false;
        }

        _data = Normalize(t_envelope.Data);
        _revision = t_envelope.Revision < 0 ? 0 : t_envelope.Revision;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        OnSaved = null;
        s_immediateUploadHandler = null;
        s_cachedRevision = 0;
        Data = new UserSaveData();
    }

    // 부분 기록으로 캐시가 깨지는 것을 막는다 — 봉투는 통째로만 유효하다.
    static void WriteCache()
    {
        string t_json = JsonConvert.SerializeObject(
            new PlayerSaveCacheEnvelope
            {
                SchemaVersion = UserSaveData.VERSION,
                Revision = s_cachedRevision,
                Data = Data,
            },
            s_serializerSettings);

        if (s_repository is IAtomicRepository t_atomicRepository)
            t_atomicRepository.ReplaceWithBackup(SAVE_KEY, t_json, BACKUP_KEY);
        else
            s_repository.Save(SAVE_KEY, t_json);
    }

    // 손으로 편집한 문서가 슬롯을 통째로 비워 두면 소비자가 NullReference로 죽는다 — 빠진 슬롯만 기본값으로 세운다.
    static UserSaveData Normalize(UserSaveData _data)
    {
        UserSaveData t_data = _data ?? new UserSaveData();

        if (t_data.Currency == null) t_data.Currency = new CurrencySaveData();
        if (t_data.Ownership == null) t_data.Ownership = new OwnershipSaveData();
        if (t_data.Deck == null) t_data.Deck = new DeckSaveData();
        if (t_data.CardGrowth == null) t_data.CardGrowth = new CardGrowthSaveData();
        if (t_data.KeywordGrowth == null) t_data.KeywordGrowth = new KeywordGrowthSaveData();
        if (t_data.Rank == null) t_data.Rank = new RankSaveData();
        if (t_data.AlbumReward == null) t_data.AlbumReward = new AlbumRewardSaveData();
        if (t_data.Tournament == null) t_data.Tournament = new TournamentSaveData();
        if (t_data.Tutorial == null) t_data.Tutorial = new TutorialSaveData();
        if (t_data.Profile == null) t_data.Profile = new ProfileSaveData();

        return t_data;
    }
}
