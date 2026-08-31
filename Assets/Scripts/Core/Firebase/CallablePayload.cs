using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Callable 요청·응답을 Functions SDK가 다루는 원시 타입과 세이브 모델 사이에서 옮긴다.</summary>
internal static class CallablePayload
{
    /// <summary>요청 객체를 SDK 인코더가 받는 원시 맵으로 낮춘다.</summary>
    internal static Dictionary<string, object> ToPrimitiveMap(object _request)
    {
        if (_request == null) return new Dictionary<string, object>();

        object t_lowered = LowerValue(_request);
        if (t_lowered is Dictionary<string, object> t_map) return t_map;

        throw new ArgumentException("Callable request must be a map at the root.", nameof(_request));
    }

    /// <summary>Callable 응답 데이터를 세이브 모델과 같은 직렬화 규약으로 역직렬화한다.</summary>
    internal static TResponse ToResponse<TResponse>(object _data) where TResponse : class
    {
        if (_data == null) return null;
        return JToken.FromObject(_data).ToObject<TResponse>(CreateSerializer());
    }

    // 세이브와 같은 설정을 공유해야 [FirestoreProperty] 이름과 camelCase 키가 계속 일치한다 — 사본을 만들면 스냅샷 대조가 깨진다.
    static JsonSerializer CreateSerializer() => JsonSerializer.Create(DataSaveManager.SaveSerializerSettings);

    // FunctionsSerializer.Encode는 원시 타입·byte[]·IList·IDictionary만 받고 나머지는 던진다.
    // 컨테이너는 직접 훑어 내려간다 — JObject를 통째로 태우면 byte[]가 base64 문자열로 바뀌어 전송 모양이 달라진다.
    static object LowerValue(object _value)
    {
        switch (_value)
        {
            case null:
                return null;
            case string t_string:
                return t_string;
            case bool t_bool:
                return t_bool;

            // byte[]는 IList이기도 하다 — 컨테이너 판정보다 먼저 걸러야 바이트 하나씩 뜯기지 않는다.
            case byte[] t_bytes:
                return t_bytes;

            case sbyte _:
            case byte _:
            case short _:
            case ushort _:
            case int _:
            case uint _:
            case long _:
            case ulong _:
                return Convert.ToInt64(_value, CultureInfo.InvariantCulture);
            case float _:
            case double _:
            case decimal _:
                return Convert.ToDouble(_value, CultureInfo.InvariantCulture);
            case char t_char:
                return t_char.ToString(CultureInfo.InvariantCulture);
            case DateTime t_dateTime:
                return t_dateTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            case DateTimeOffset t_dateTimeOffset:
                return t_dateTimeOffset.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            case JToken t_token:
                return LowerToken(t_token);
            case IDictionary t_dictionary:
            {
                var t_map = new Dictionary<string, object>(t_dictionary.Count);
                foreach (DictionaryEntry t_entry in t_dictionary)
                    t_map[Convert.ToString(t_entry.Key, CultureInfo.InvariantCulture)] = LowerValue(t_entry.Value);
                return t_map;
            }
            case IList t_list:
            {
                var t_items = new List<object>(t_list.Count);
                foreach (object t_item in t_list)
                    t_items.Add(LowerValue(t_item));
                return t_items;
            }
        }

        // 남은 것은 enum·POCO·익명 타입이다 — 세이브와 같은 직렬화기를 태워 토큰으로 만든 뒤 다시 낮춘다.
        return LowerToken(JToken.FromObject(_value, CreateSerializer()));
    }

    static object LowerToken(JToken _token)
    {
        if (_token is JObject t_object)
        {
            var t_map = new Dictionary<string, object>(t_object.Count);
            foreach (KeyValuePair<string, JToken> t_pair in t_object)
                t_map[t_pair.Key] = LowerToken(t_pair.Value);
            return t_map;
        }

        if (_token is JArray t_array)
        {
            var t_items = new List<object>(t_array.Count);
            foreach (JToken t_item in t_array)
                t_items.Add(LowerToken(t_item));
            return t_items;
        }

        if (!(_token is JValue t_value) || t_value.Value == null) return null;

        switch (t_value.Type)
        {
            case JTokenType.Integer:
                return Convert.ToInt64(t_value.Value, CultureInfo.InvariantCulture);
            case JTokenType.Float:
                return Convert.ToDouble(t_value.Value, CultureInfo.InvariantCulture);
            case JTokenType.Boolean:
                return Convert.ToBoolean(t_value.Value, CultureInfo.InvariantCulture);
            case JTokenType.Bytes:
                return t_value.Value as byte[];
            case JTokenType.Date:
                return LowerValue(t_value.Value);
            default:
                return t_value.Value as string ?? Convert.ToString(t_value.Value, CultureInfo.InvariantCulture);
        }
    }
}
