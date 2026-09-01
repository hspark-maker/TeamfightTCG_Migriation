using System;
using System.Collections.Generic;
using UnityEngine;

// 스펙시트(SpecData.bytes) 파싱 결과 한 벌. 시트를 읽는 축이 여럿이라 복호화·파싱을 여기서 1회만 한다.
// 못 읽으면 Manager가 null로 남고, 각 조회 창구가 SO 인스펙터 값으로 폴백한다.
public static class SpecSource
{
    static bool s_loaded;
    static SpecDataManager s_manager;
    static string s_fingerprint;
    static string s_battleFingerprint;
    static string s_origin;

    /// <summary>이번 세션이 물고 있는 스펙 원본 — "서버캐시" 또는 "내장본". 대조 로그의 기준점이다.</summary>
    public static string Origin
    {
        get { EnsureLoaded(); return s_origin ?? "없음"; }
    }

    public static string Fingerprint
    {
        get { EnsureLoaded(); return s_fingerprint ?? "nospec"; }
    }

    public static string BattleFingerprint
    {
        get { EnsureLoaded(); return s_battleFingerprint ?? "nospec"; }
    }

    /// <summary>시트를 못 읽었으면 null — 호출부는 폴백으로 떨어져야 한다.</summary>
    public static SpecDataManager Manager
    {
        get
        {
            EnsureLoaded();
            return s_manager;
        }
    }

    // 초기화에서 1회. 지연 로드도 되지만 첫 조회 프레임에 복호화·파싱이 걸리지 않게 미리 당긴다.
    public static void Init() => EnsureLoaded();

    /// <summary>현재 콘텐츠 모드의 표 데이터를 순수 <see cref="CardSpec"/> 값으로 변환한다.</summary>
    public static Dictionary<int, CardSpec> LoadCards(EContentRunMode _mode)
    {
        SpecDataManager t_manager = Manager;
        if (t_manager == null)
            throw new InvalidOperationException("[SpecSource] SpecData를 읽지 못해 카드 정의를 만들 수 없다.");

        var t_specs = new Dictionary<int, CardSpec>();
        if (_mode == EContentRunMode.Test)
        {
            IReadOnlyList<Card_Test> t_rows = t_manager.Card_Test?.All;
            if (t_rows == null || t_rows.Count == 0)
                throw new InvalidOperationException("[SpecSource] Card_Test 표가 비었다.");
            foreach (Card_Test t_row in t_rows) AddCard(t_specs, From(t_row));
        }
        else
        {
            IReadOnlyList<Card> t_rows = t_manager.Card?.All;
            if (t_rows == null || t_rows.Count == 0)
                throw new InvalidOperationException("[SpecSource] Card 표가 비었다.");
            foreach (Card t_row in t_rows) AddCard(t_specs, From(t_row));
        }
        return t_specs;
    }

    static CardSpec From(Card _row)
    {
        if (_row == null) throw new InvalidOperationException("Card 표에 null 행이 있다.");
        return CreateCard(_row.id, _row.name, _row.displayName, _row.channel, _row.maxHp, _row.keywords,
            _row.keywordUnlockLevel, _row.defaultEvolutionStage, _row.hp2, _row.hp3, _row.hp4,
            _row.cardExplain, _row.grade, _row.synergies);
    }

    static CardSpec From(Card_Test _row)
    {
        if (_row == null) throw new InvalidOperationException("Card_Test 표에 null 행이 있다.");
        return CreateCard(_row.id, _row.name, _row.displayName, _row.channel, _row.maxHp, _row.keywords,
            _row.keywordUnlockLevel, _row.defaultEvolutionStage, _row.hp2, _row.hp3, _row.hp4,
            _row.cardExplain, _row.grade, _row.synergies);
    }

    static CardSpec CreateCard(
        int _id, string _assetName, string _displayName, string _channel, int _maxHp,
        string _keywords, int _keywordUnlockLevel, int _defaultEvolutionStage,
        int _hp2, int _hp3, int _hp4, string _cardExplain, string _grade, string _synergies)
        => new CardSpec(_id, _assetName, _displayName,
            ParseEnum<ECardChannel>(_channel, _id, _assetName, "channel"), _maxHp,
            ParseKeywords(_keywords, _id, _assetName), _keywordUnlockLevel, _defaultEvolutionStage,
            _hp2, _hp3, _hp4, _cardExplain,
            ParseEnum<ECardGrade>(_grade, _id, _assetName, "grade"),
            ParseSynergies(_synergies, _id, _assetName));

    static void AddCard(Dictionary<int, CardSpec> _specs, CardSpec _spec)
    {
        if (_specs.ContainsKey(_spec.Id))
            throw new InvalidOperationException($"카드 ID {_spec.Id}가 중복이다.");
        _specs.Add(_spec.Id, _spec);
    }

    static T ParseEnum<T>(string _value, int _id, string _name, string _field) where T : struct
    {
        if (string.IsNullOrWhiteSpace(_value) || char.IsDigit(_value.Trim()[0]) ||
            !Enum.TryParse(_value.Trim(), true, out T t_value) || !Enum.IsDefined(typeof(T), t_value))
            throw new InvalidOperationException($"카드 {_id}({_name}).{_field} 값 '{_value}'을 해석할 수 없다.");
        return t_value;
    }

    static CardKeyword ParseKeywords(string _value, int _id, string _name)
    {
        CardKeyword t_result = CardKeyword.None;
        if (string.IsNullOrWhiteSpace(_value)) return t_result;

        foreach (string t_raw in _value.Split(new[] { '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string t_token = t_raw.Trim();
            if (t_token.Length == 0) continue;
            if (char.IsDigit(t_token[0]) || !Enum.TryParse(t_token, true, out CardKeyword t_keyword) ||
                !Enum.IsDefined(typeof(CardKeyword), t_keyword) || t_keyword == CardKeyword.None)
                throw new InvalidOperationException($"카드 {_id}({_name}).keywords 값 '{t_token}'을 해석할 수 없다.");
            t_result |= t_keyword;
        }
        return t_result;
    }

    static IReadOnlyList<string> ParseSynergies(string _value, int _id, string _name)
    {
        var t_result = new List<string>();
        var t_seen = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(_value)) return t_result.AsReadOnly();

        foreach (string t_raw in _value.Split(new[] { '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string t_token = SynergyRegistry.NormalizeName(t_raw);
            if (t_token.Length == 0) continue;
            if (!t_seen.Add(t_token))
                throw new InvalidOperationException($"카드 {_id}({_name}).synergies에 '{t_token}'이 중복이다.");
            t_result.Add(t_token);
        }
        return t_result.AsReadOnly();
    }

    /// <summary>파싱 스냅샷을 버리고 그 자리에서 다시 읽는다.
    /// 에디터 도구가 SpecData.bytes를 새로 만든 뒤 낡은 스냅샷으로 판정하지 않게 여는 문이다 —
    /// 플레이 진입 전까지는 <see cref="ResetRuntimeState"/>가 안 돌아 스냅샷이 세션 내내 남는다.</summary>
    public static void Reload()
    {
        Clear();
        EnsureLoaded();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => Clear();

    static void Clear()
    {
        s_loaded = false;
        s_manager = null;
        s_fingerprint = null;
        s_battleFingerprint = null;
        s_origin = null;
    }

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(폴백으로 계속 돈다).

        string t_envId = null;
        try { t_envId = ContentProfileConfig.Active.CloudEnvId; }
        catch (System.Exception) { }

        SpecDataManager t_manager = null;

        // 봉투에 적힌 지문을 그대로 믿으면 payload와 지문이 어긋난 캐시(부분 기록·손상·payload만 고친 파일)가
        // 그대로 통과해, **내가 들고 있는 데이터와 내가 주장하는 지문이 다른 상태**로 대전에 들어간다.
        // 그래서 payload에서 지문을 다시 계산해 봉투 값과 대조하고, 어긋나면 캐시를 버린다.
        //
        // 이건 자기정합성 검사지 인증이 아니다 — 지문까지 같이 고쳐 쓰면 여기는 통과한다.
        // 조작 스펙을 실제로 막는 것은 서버 대조(BattleContentSync)와 상대와의 지문 대조(InitialDeck)다.
        if (!string.IsNullOrEmpty(t_envId) &&
            SpecSnapshotCache.TryLoad(t_envId, out string t_cachedJson, out string t_cachedFingerprint))
        {
            string t_recomputed = null;
            string t_cacheError = "캐시 파싱 실패";
            if (TryLoadManager(t_cachedJson, out SpecDataManager t_cachedManager)
                && !TryCombinedFingerprint(t_cachedManager, t_envId, out t_recomputed, out t_cacheError))
                t_recomputed = null;

            if (t_recomputed != null && string.Equals(t_recomputed, t_cachedFingerprint, System.StringComparison.Ordinal))
            {
                t_manager = t_cachedManager;
                s_fingerprint = t_recomputed;
                s_origin = "서버캐시";
            }
            else
            {
                Debug.LogError("[SpecSource] 서버캐시를 신뢰할 수 없다 — 버리고 내장본으로 돈다. " +
                               $"봉투={t_cachedFingerprint} 재계산={t_recomputed ?? "(실패: " + t_cacheError + ")"}");
            }
        }

        if (t_manager == null)
        {
            string t_json = SpecDataResourceLoader.LoadSpecData();
            if (string.IsNullOrEmpty(t_json))
            {
                Debug.LogWarning("[SpecSource] SpecData 리소스를 못 읽었다. 시트를 쓰는 축은 전부 SO 값으로 돈다.");
                return;
            }
            if (!TryLoadManager(t_json, out t_manager))
            {
                Debug.LogWarning("[SpecSource] SpecData 파싱 실패. 시트를 쓰는 축은 전부 SO 값으로 돈다.");
                return;
            }
            s_fingerprint = null;   // 캐시 경로에서 채웠더라도 원본이 바뀌었으니 다시 계산한다
            s_origin = "내장본";
        }

        s_manager = t_manager;
        string t_error = null;
        if (string.IsNullOrEmpty(s_fingerprint) && !string.IsNullOrEmpty(t_envId)
            && !TryCombinedFingerprint(t_manager, t_envId, out s_fingerprint, out t_error))
        {
            s_fingerprint = "nospec";
            // 지문이 nospec이면 멀티 InitialDeck 송신이 차단된다 — 조용히 넘기면 원인을 못 찾는다.
            Debug.LogError($"[SpecSource] 지문 계산 실패: {t_error} — 멀티플레이가 차단된다.");
            return;
        }
        if (!string.IsNullOrEmpty(t_envId))
        {
            string t_battleTable = ContentProfileConfig.Active.RunMode == EContentRunMode.Test ? "Card_Test" : "Card";
            if (SpecPayloadCodec.TryBuildLocalTable(t_manager, t_battleTable, out SpecTablePayload t_battlePayload, out string t_battleError))
                s_battleFingerprint = SpecPayloadCodec.CombinedHash(t_envId, new[] { t_battlePayload });
            else
                Debug.LogError($"[SpecSource] 전투 지문 계산 실패 table={t_battleTable}: {t_battleError} — 멀티플레이가 차단된다.");

            // 에디터에서는 안 찍는다. 이 스냅샷은 static이라 도메인 리로드마다(=컴파일마다) 다시 서고,
            // 인스펙터 드로어(CardIdDrawer)가 첫 리페인트에 로드를 깨워 컴파일할 때마다 같은 줄이 쌓였다.
            // 이 로그의 값어치는 멀티가 "스펙 스냅샷 다름"으로 끊겼을 때 어느 원본을 물었는지 보는 것뿐이라
            // 플레이 중에만 필요하다. 실패 로그(LogError)는 에디터에서도 그대로 나간다.
            if (Application.isPlaying)
                Debug.Log($"[SpecSource] 스펙 로드 완료 원본={s_origin} env={t_envId} 전투표={t_battleTable} " +
                          $"지문={s_fingerprint ?? "(없음)"} 전투지문={s_battleFingerprint ?? "(없음)"}");
        }
    }

    static bool TryLoadManager(string _json, out SpecDataManager _manager)
    {
        _manager = null;
        if (string.IsNullOrEmpty(_json)) return false;
        var t_manager = new SpecDataManager();
        if (!t_manager.Load(_json)) return false;
        _manager = t_manager;
        return true;
    }

    /// <summary>6표 전체를 접은 콘텐츠 지문. 로비 게이트(BattleContentSync)가 서버와 대조하는 값과
    /// **같은 함수**로 만든다 — 여기서 따로 접으면 캐시 검증과 서버 대조가 서로 다른 값을 보게 된다.</summary>
    static bool TryCombinedFingerprint(SpecDataManager _manager, string _envId, out string _fingerprint, out string _error)
    {
        _fingerprint = null;
        _error = null;
        var t_tables = new System.Collections.Generic.List<SpecTablePayload>();
        foreach (string t_tableName in SpecPayloadCodec.TableNames)
        {
            if (!SpecPayloadCodec.TryBuildLocalTable(_manager, t_tableName, out SpecTablePayload t_table, out string t_tableError))
            {
                _error = $"table={t_tableName}: {t_tableError}";
                return false;
            }
            t_tables.Add(t_table);
        }
        _fingerprint = SpecPayloadCodec.CombinedHash(_envId, t_tables);
        return true;
    }
}
