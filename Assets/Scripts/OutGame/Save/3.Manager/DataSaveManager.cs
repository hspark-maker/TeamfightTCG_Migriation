using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

// 아웃게임 세이브 매니저. 진실원은 Firestore 문서 하나다.
public static class DataSaveManager
{
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

    public static event Action OnSaved;

    public static UserSaveData Data { get; private set; } = new UserSaveData();

    // 클라우드 계층이 꽂는다. 3계층이 4계층을 직접 참조하지 않게 하는 배선(OnSaved와 대칭).
    public static void SetImmediateUploadHandler(Action _handler)
    {
        s_immediateUploadHandler = _handler;
    }

    /// <summary>메모리 세이브가 바뀌었음을 통지한다(업로드는 클라우드 계층이 디바운스해서 한다).</summary>
    public static void Save()
    {
        OnSaved?.Invoke();
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

    /// <summary>부트에서 채택한 세이브를 메모리에 세운다. 채택은 부트당 1회다.</summary>
    internal static void AdoptRemote(UserSaveData _data)
    {
        Data = Normalize(_data);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        OnSaved = null;
        s_immediateUploadHandler = null;
        Data = new UserSaveData();
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
