using System;
using System.Collections.Generic;
using Firebase.Firestore;
using UnityEngine;

// Firestore 세이브 문서의 필드명·변환 단일 창구. 문서 구조를 아는 코드는 여기뿐이다.
// 클라이언트가 문서 전체를 소유한다(쓰기는 SetOptions.Overwrite) — 서버가 소유할 필드가 생기면
// 같은 문서가 아니라 형제 문서 ".../save/server"로 분리한다.
static class PlayerSaveDocument
{
    const string DEVICE_ID_KEY = "firebase.playerSave.deviceId";

    // 메타 5 — 도메인이 아니라 클라우드 부기다. UserSaveData는 UnknownPropertyHandling.Ignore라 읽기에서 자동으로 무시된다.
    internal const string FIELD_SCHEMA_VERSION = "schemaVersion";
    internal const string FIELD_REVISION = "revision";
    internal const string FIELD_UPDATED_AT = "updatedAt";
    internal const string FIELD_DEVICE_ID = "deviceId";
    internal const string FIELD_APP_VERSION = "appVersion";

    // 슬롯 10 — UserSaveData의 [FirestoreProperty] 이름과 반드시 같다(읽기는 ConvertTo가, 쓰기는 이 표가 한다).
    internal const string FIELD_CURRENCY = "currency";
    internal const string FIELD_OWNERSHIP = "ownership";
    internal const string FIELD_DECK = "deck";
    internal const string FIELD_CARD_GROWTH = "cardGrowth";
    internal const string FIELD_KEYWORD_GROWTH = "keywordGrowth";
    internal const string FIELD_RANK = "rank";
    internal const string FIELD_ALBUM_REWARD = "albumReward";
    internal const string FIELD_TOURNAMENT = "tournament";
    internal const string FIELD_TUTORIAL = "tutorial";
    internal const string FIELD_PROFILE = "profile";

    static string s_deviceId = string.Empty;
    static string s_appVersion = string.Empty;

    /// <summary>기기 정보를 미리 읽어 둔다. PlayerPrefs·Application은 메인 스레드 전용인데
    /// 필드 맵은 트랜잭션 콜백(백그라운드일 수 있다) 안에서 만들어진다.</summary>
    internal static void CacheDeviceInfo()
    {
        s_deviceId = DeviceId();
        s_appVersion = Application.version;
    }

    /// <summary>문서 전체를 덮어쓸 필드 맵. Overwrite와 짝이라 슬롯 10개가 빠짐없이 들어가야 한다.</summary>
    internal static Dictionary<string, object> ToFieldMap(UserSaveData _data, long _revision)
    {
        if (_data == null) throw new ArgumentNullException(nameof(_data));

        return new Dictionary<string, object>
        {
            [FIELD_SCHEMA_VERSION] = (long)UserSaveData.VERSION,
            [FIELD_REVISION] = _revision,
            [FIELD_UPDATED_AT] = FieldValue.ServerTimestamp,
            [FIELD_DEVICE_ID] = s_deviceId,
            [FIELD_APP_VERSION] = s_appVersion,

            [FIELD_CURRENCY] = _data.Currency,
            [FIELD_OWNERSHIP] = _data.Ownership,
            [FIELD_DECK] = _data.Deck,
            [FIELD_CARD_GROWTH] = _data.CardGrowth,
            [FIELD_KEYWORD_GROWTH] = _data.KeywordGrowth,
            [FIELD_RANK] = _data.Rank,
            [FIELD_ALBUM_REWARD] = _data.AlbumReward,
            [FIELD_TOURNAMENT] = _data.Tournament,
            [FIELD_TUTORIAL] = _data.Tutorial,
            [FIELD_PROFILE] = _data.Profile,
        };
    }

    /// <summary>문서의 클라우드 부기 메타를 읽는다. 손편집으로 타입이 깨졌으면 false.</summary>
    internal static bool TryReadMeta(DocumentSnapshot _snapshot, out long _schemaVersion, out long _revision)
    {
        _schemaVersion = 0;
        _revision = 0;
        if (_snapshot == null || !_snapshot.Exists) return false;

        try
        {
            return _snapshot.TryGetValue(FIELD_SCHEMA_VERSION, out _schemaVersion) &&
                   _snapshot.TryGetValue(FIELD_REVISION, out _revision) &&
                   _schemaVersion > 0 &&
                   _revision >= 0;
        }
        catch (Exception)
        {
            // 콘솔에서 숫자를 number(double)로 저장하면 int64 변환이 터진다 — 깨진 문서로 취급한다.
            _schemaVersion = 0;
            _revision = 0;
            return false;
        }
    }

    /// <summary>메타를 못 읽은 문서가 실제로 무엇을 담고 있었는지 한 줄로 설명한다. 실패 로그 전용.</summary>
    internal static string DescribeMeta(DocumentSnapshot _snapshot)
    {
        if (_snapshot == null) return "snapshot=null";
        if (!_snapshot.Exists) return "document=missing";

        try
        {
            Dictionary<string, object> t_raw = _snapshot.ToDictionary();
            return DescribeField(t_raw, FIELD_SCHEMA_VERSION) + ", " + DescribeField(t_raw, FIELD_REVISION);
        }
        catch (Exception t_exception)
        {
            return $"document could not be read ({t_exception.GetBaseException().Message})";
        }
    }

    /// <summary>이번 실행의 앱 버전. 문서에 싣는 값과 서버 요청에 싣는 값이 갈리지 않게 캐시본을 쓴다.</summary>
    internal static string AppVersion() => s_appVersion;

    /// <summary>기기 식별자(PlayerPrefs에 심는 GUID). 어느 기기가 마지막으로 썼는지 콘솔에서 읽기 위한 것이다.</summary>
    internal static string DeviceId()
    {
        if (!string.IsNullOrEmpty(s_deviceId)) return s_deviceId;

        string t_deviceId = PlayerPrefs.GetString(DEVICE_ID_KEY, string.Empty);
        if (string.IsNullOrEmpty(t_deviceId))
        {
            t_deviceId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(DEVICE_ID_KEY, t_deviceId);
            PlayerPrefs.Save();
        }

        s_deviceId = t_deviceId;
        return t_deviceId;
    }

    static string DescribeField(Dictionary<string, object> _raw, string _field)
    {
        if (!_raw.TryGetValue(_field, out object t_value)) return $"{_field}=absent";
        if (t_value == null) return $"{_field}=null";
        return $"{_field}={t_value} ({t_value.GetType().Name})";
    }
}
