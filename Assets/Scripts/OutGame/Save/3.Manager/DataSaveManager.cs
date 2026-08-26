using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

// 아웃게임 세이브 매니저. 저장 매체는 IRepository로 교체한다.
public static class DataSaveManager
{
    const string SAVE_KEY = "outgame_save";
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
    static bool s_saveBlocked;

    public static event Action<string> OnSaved;

    public static UserSaveData Data { get; private set; } = new UserSaveData();
    public static bool CloudUploadAllowed { get; private set; } = true;
    public static bool IsSaveBlocked => s_saveBlocked;
    internal static bool HasLocalSave => s_repository.Has(SAVE_KEY);

    // 저장 매체 교체. Load 이전에 호출한다.
    public static void SetRepository(IRepository _repository)
    {
        if (_repository != null) s_repository = _repository;
    }

    // 부팅 시 한 번 호출한다. 캐시가 깨져 있으면 원본을 남기고 기본값으로 시작한다.
    public static void Load()
    {
        s_saveBlocked = false;
        CloudUploadAllowed = true;

        string t_json = s_repository.Load(SAVE_KEY);
        if (string.IsNullOrEmpty(t_json))
        {
            Data = new UserSaveData();
            return;
        }

        try
        {
            Data = Parse(t_json);
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[DataSaveManager] Load failed. Backing up source and starting with defaults: {t_exception}");
            s_repository.Save(CORRUPT_KEY, t_json);
            Data = new UserSaveData();
            CloudUploadAllowed = false;
        }
    }

    public static void Save()
    {
        if (s_saveBlocked)
        {
            Debug.LogWarning("[DataSaveManager] Save blocked because the loaded save is newer than this client.");
            return;
        }

        string t_json = Serialize(Data);
        s_repository.Save(SAVE_KEY, t_json);
        OnSaved?.Invoke(t_json);
    }

    public static string CreateSnapshot()
    {
        return Serialize(Data);
    }

    internal static UserSaveData Deserialize(string _json)
    {
        return JsonConvert.DeserializeObject<UserSaveData>(_json, s_serializerSettings);
    }

    internal static string LoadSyncMetadata(string _key)
    {
        return s_repository.Load(_key);
    }

    internal static void SaveSyncMetadata(string _key, string _json)
    {
        s_repository.Save(_key, _json);
    }

    internal static bool TryApplyRemote(
        string _payload,
        string _backupKey,
        out string _error)
    {
        _error = string.Empty;
        if (s_repository is not IAtomicRepository t_atomicRepository)
        {
            _error = "Active repository does not support atomic replacement.";
            return false;
        }

        string t_original = s_repository.Load(SAVE_KEY);
        try
        {
            string t_expected = Serialize(Parse(_payload));

            t_atomicRepository.ReplaceWithBackup(SAVE_KEY, _payload, _backupKey);
            Load();
            if (s_saveBlocked || CreateSnapshot() != t_expected)
                throw new InvalidOperationException("Reloaded save does not match the remote payload.");

            return true;
        }
        catch (Exception t_exception)
        {
            _error = t_exception.Message;
            try
            {
                if (string.IsNullOrEmpty(t_original))
                    s_repository.Delete(SAVE_KEY);
                else
                    t_atomicRepository.ReplaceWithBackup(SAVE_KEY, t_original, _backupKey + "_failed_apply");
                Load();

                string t_restored = s_repository.Load(SAVE_KEY);
                if (t_restored != t_original || s_saveBlocked)
                    throw new InvalidOperationException(
                        $"Original save restore verification failed. Recovery backup: '{_backupKey}'.");
            }
            catch (Exception t_restoreException)
            {
                _error += $" Restore failed: {t_restoreException.Message}";
            }

            return false;
        }
    }

    internal static bool TrySaveRemoteConflict(string _key, string _json, out string _error)
    {
        _error = string.Empty;
        try
        {
            s_repository.Save(_key, _json);
            if (s_repository.Load(_key) != _json)
                throw new InvalidOperationException("Conflict backup verification failed.");
            return true;
        }
        catch (Exception t_exception)
        {
            _error = t_exception.Message;
            return false;
        }
    }

    // 손으로 편집한 문서가 슬롯을 통째로 비워 두면 소비자가 NullReference로 죽는다 — 빠진 슬롯만 기본값으로 세운다.
    static UserSaveData Parse(string _json)
    {
        var t_data = Deserialize(_json) ?? new UserSaveData();

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

    static string Serialize(UserSaveData _data)
    {
        return JsonConvert.SerializeObject(_data, s_serializerSettings);
    }
}
