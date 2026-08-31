using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

// 아웃게임 세이브 매니저. 진실원은 Firestore 문서 하나다.
public enum ESaveUploadTiming
{
    Default,
    Coalesced,
}

public static class DataSaveManager
{
    internal const int SaveSlotCount = 9;

    static readonly ESaveSlot[] s_saveSlots =
    {
        ESaveSlot.Ownership,
        ESaveSlot.Deck,
        ESaveSlot.CardGrowth,
        ESaveSlot.KeywordGrowth,
        ESaveSlot.Rank,
        ESaveSlot.AlbumReward,
        ESaveSlot.Tournament,
        ESaveSlot.Tutorial,
        ESaveSlot.Profile,
    };

    static DataSaveManager()
    {
        if (s_saveSlots.Length != SaveSlotCount)
            throw new InvalidOperationException("Save slot catalog length does not match SaveSlotCount.");

        ESaveSlot t_catalogMask = ESaveSlot.None;
        for (int i = 0; i < s_saveSlots.Length; i++)
            t_catalogMask |= s_saveSlots[i];

        ESaveSlot t_enumMask = ESaveSlot.None;
        foreach (ESaveSlot t_slot in Enum.GetValues(typeof(ESaveSlot)))
            t_enumMask |= t_slot;

        if (t_catalogMask != t_enumMask)
            throw new InvalidOperationException(
                $"Save slot catalog is incomplete. catalog=0x{(int)t_catalogMask:X}, enum=0x{(int)t_enumMask:X}");
    }

    // JsonUtility는 auto-property를 직렬화하지 못한다 — Firestore 매핑용 프로퍼티 모델과 같은 모양을 쓰려면 Newtonsoft여야 한다.
    static readonly JsonSerializerSettings s_serializerSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.None,

        // Firestore 필드명과 같은 인코딩을 쓴다 — [FirestoreProperty] 이름이 전부 프로퍼티명의 camelCase다.
        // 두 직렬화기가 같은 키를 내야 스냅샷으로 원격 문서와 대조할 수 있다.
        ContractResolver = new DefaultContractResolver
        {
            // 딕셔너리 키는 건드리지 않는다 — 재화 키가 ECurrencyType 이름이라 소문자로 바뀌면 파싱이 깨진다.
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },

        // 기본값(Auto)은 프로퍼티 이니셜라이저로 만든 컬렉션에 이어붙인다 — 값이 조용히 중복되는 것을 막는다.
        ObjectCreationHandling = ObjectCreationHandling.Replace,
    };

    static Action s_immediateUploadHandler;

    public static event Action<ESaveUploadTiming> OnSaved;

    /// <summary>서버가 슬롯을 갈아끼웠다 — 슬롯을 캐싱한 매니저는 여기서 재수화한다.</summary>
    public static event Action<ESaveSlot> OnServerSlotsAdopted;

    public static UserSaveData Data { get; private set; } = new UserSaveData();

    /// <summary>스냅샷 대조와 callable 역직렬화가 함께 쓴다 — 여기 손대면 둘이 동시에 바뀐다.</summary>
    internal static JsonSerializerSettings SaveSerializerSettings => s_serializerSettings;

    // 클라우드 계층이 꽂는다. 3계층이 4계층을 직접 참조하지 않게 하는 배선(OnSaved와 대칭).
    public static void SetImmediateUploadHandler(Action _handler)
    {
        s_immediateUploadHandler = _handler;
    }

    /// <summary>메모리 세이브가 바뀌었음을 통지한다(업로드는 클라우드 계층이 디바운스해서 한다).</summary>
    public static void Save()
    {
        OnSaved?.Invoke(ESaveUploadTiming.Default);
    }

    /// <summary>Requests a longer debounce for repeatable UI changes.</summary>
    public static void SaveCoalesced()
    {
        OnSaved?.Invoke(ESaveUploadTiming.Coalesced);
    }

    /// <summary>디바운스를 기다리지 않고 업로드까지 요청하는 저장. 유실되면 안 되는 지점에서만 쓴다.</summary>
    public static void SaveImmediate()
    {
        Save();
        s_immediateUploadHandler?.Invoke();
    }

    /// <summary>도메인만 담은 직렬화 결과. 원격 문서 대조·변경 감지의 기준값이다.</summary>
    public static string CreateSnapshot()
    {
        // 정규화 후에 찍는다 — 슬롯이 비어 있으면 내용이 같아도 다른 스냅샷이 나온다.
        return JsonConvert.SerializeObject(Normalize(Data), s_serializerSettings);
    }

    internal static void WriteSlotSnapshots(string[] _destination)
    {
        if (_destination == null) throw new ArgumentNullException(nameof(_destination));
        if (_destination.Length != SaveSlotCount)
            throw new ArgumentException($"Expected {SaveSlotCount} save slot snapshots.", nameof(_destination));

        UserSaveData t_data = Normalize(Data);
        for (int i = 0; i < SaveSlotCount; i++)
        {
            _destination[i] = JsonConvert.SerializeObject(
                GetSlotValue(t_data, s_saveSlots[i]),
                s_serializerSettings);
        }
    }

    internal static ESaveSlot SaveSlotAt(int _index)
    {
        if (_index < 0 || _index >= SaveSlotCount)
            throw new ArgumentOutOfRangeException(nameof(_index));
        return s_saveSlots[_index];
    }

    internal static object GetSlotValue(UserSaveData _data, ESaveSlot _slot)
    {
        if (_data == null) throw new ArgumentNullException(nameof(_data));

        switch (_slot)
        {
            case ESaveSlot.Ownership: return _data.Ownership;
            case ESaveSlot.Deck: return _data.Deck;
            case ESaveSlot.CardGrowth: return _data.CardGrowth;
            case ESaveSlot.KeywordGrowth: return _data.KeywordGrowth;
            case ESaveSlot.Rank: return _data.Rank;
            case ESaveSlot.AlbumReward: return _data.AlbumReward;
            case ESaveSlot.Tournament: return _data.Tournament;
            case ESaveSlot.Tutorial: return _data.Tutorial;
            case ESaveSlot.Profile: return _data.Profile;
            default: throw new ArgumentOutOfRangeException(nameof(_slot), _slot, "Unknown save slot.");
        }
    }

    /// <summary>초기화에서 채택한 세이브를 메모리에 세운다. 채택은 초기화당 1회다.</summary>
    internal static void AdoptRemote(UserSaveData _data)
    {
        Data = Normalize(_data);
    }

    /// <summary>서버가 쓴 슬롯만 메모리 세이브에 갈아끼운다. null 슬롯은 서버가 건드리지 않은 것이다.</summary>
    internal static ESaveSlot AdoptServerSlots(ServerSlotPatch _slots)
    {
        if (_slots == null) return ESaveSlot.None;

        ESaveSlot t_touched = ESaveSlot.None;

        if (_slots.Ownership != null) { Data.Ownership = _slots.Ownership; t_touched |= ESaveSlot.Ownership; }
        if (_slots.Deck != null) { Data.Deck = _slots.Deck; t_touched |= ESaveSlot.Deck; }
        if (_slots.CardGrowth != null) { Data.CardGrowth = _slots.CardGrowth; t_touched |= ESaveSlot.CardGrowth; }
        if (_slots.KeywordGrowth != null) { Data.KeywordGrowth = _slots.KeywordGrowth; t_touched |= ESaveSlot.KeywordGrowth; }
        if (_slots.Rank != null) { Data.Rank = _slots.Rank; t_touched |= ESaveSlot.Rank; }
        if (_slots.AlbumReward != null) { Data.AlbumReward = _slots.AlbumReward; t_touched |= ESaveSlot.AlbumReward; }
        if (_slots.Tournament != null) { Data.Tournament = _slots.Tournament; t_touched |= ESaveSlot.Tournament; }
        if (_slots.Tutorial != null) { Data.Tutorial = _slots.Tutorial; t_touched |= ESaveSlot.Tutorial; }
        if (_slots.Profile != null) { Data.Profile = _slots.Profile; t_touched |= ESaveSlot.Profile; }

        Data = Normalize(Data);

        // Save()·OnSaved를 발화하지 않는다 — PlayerSaveCloud.MarkDirty가 dirty를 하나 올려
        // 방금 세운 업로드 기준선을 그 자리에서 깨뜨린다. 채택 결과의 기준선 정렬은 클라우드 계층이 직접 한다.
        if (t_touched != ESaveSlot.None) OnServerSlotsAdopted?.Invoke(t_touched);

        return t_touched;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        OnSaved = null;
        OnServerSlotsAdopted = null;
        s_immediateUploadHandler = null;
        Data = new UserSaveData();
    }

    // 손으로 편집한 문서가 슬롯을 통째로 비워 두면 소비자가 NullReference로 죽는다 — 빠진 슬롯만 기본값으로 세운다.
    static UserSaveData Normalize(UserSaveData _data)
    {
        UserSaveData t_data = _data ?? new UserSaveData();

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
