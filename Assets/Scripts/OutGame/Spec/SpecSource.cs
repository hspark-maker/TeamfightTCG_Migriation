using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>스펙 로드 실패 사유. 로그의 <c>SPEC-#</c> 코드가 이 값이다 —
/// 화면·리포트에서 이 코드만으로 어디서 끊겼는지 갈린다.</summary>
public enum ESpecLoadError
{
    None = 0,
    /// <summary>SPEC-1: 콘텐츠 프로필이 환경을 안 준다. 스냅샷은 환경별이라 조회 자체가 불가능하다.</summary>
    NoEnvironment = 1,
    /// <summary>SPEC-2: 로컬에 서버 스냅샷이 없다. 첫 실행에서는 정상이고, 동기화가 채운다.</summary>
    NoSnapshot = 2,
    /// <summary>SPEC-3: 스냅샷은 있는데 파싱이 안 된다(손상·형식 변경).</summary>
    SnapshotParseFailed = 3,
    /// <summary>SPEC-4: 스냅샷에서 지문을 계산하지 못했다(표 누락 등).</summary>
    FingerprintFailed = 4,
    /// <summary>SPEC-5: 재계산 지문이 봉투와 다르다(부분 기록·변조).</summary>
    FingerprintMismatch = 5,
    /// <summary>SPEC-6: 전투 지문(Card 표)만 실패. 스냅샷 자체는 유효하다.</summary>
    BattleFingerprintFailed = 6,
}

// 서버 스펙 스냅샷 파싱 결과 한 벌. 시트를 읽는 축이 여럿이라 파싱을 여기서 1회만 한다.
// **진실원은 서버 스냅샷뿐이다** — 내장본(SpecData.bytes) 폴백은 없다. 못 읽으면 Manager가 null로 남고
// 사유는 LastError 코드로 드러난다.
public static class SpecSource
{
    static bool s_loaded;
    static SpecDataManager s_manager;
    static string s_fingerprint;
    static string s_battleFingerprint;
    static string s_origin;
    static ESpecLoadError s_error;
    static string s_errorDetail;

    /// <summary>이번 세션이 물고 있는 스펙 원본 — "서버스냅샷" 또는 "없음". 대조 로그의 기준점이다.</summary>
    public static string Origin
    {
        get { EnsureLoaded(); return s_origin ?? "없음"; }
    }

    /// <summary>마지막 로드 실패 사유. 성공이면 <see cref="ESpecLoadError.None"/>.</summary>
    public static ESpecLoadError LastError
    {
        get { EnsureLoaded(); return s_error; }
    }

    /// <summary>실패 코드에 붙는 사람이 읽는 설명(환경·지문 등). 성공이면 null.</summary>
    public static string LastErrorDetail
    {
        get { EnsureLoaded(); return s_errorDetail; }
    }

    /// <summary>화면·리포트에 그대로 실을 수 있는 코드 문자열. 성공이면 null.</summary>
    public static string LastErrorCode
        => LastError == ESpecLoadError.None ? null : CodeOf(s_error);

    /// <summary>스펙을 실제로 들고 있는가. false면 스펙을 읽는 축은 진행하면 안 된다.</summary>
    public static bool IsReady
    {
        get { EnsureLoaded(); return s_manager != null; }
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

    /// <summary>카드 표 데이터를 순수 <see cref="CardSpec"/> 값으로 변환한다.
    /// 표는 Card 하나다 — 테스트 프로필도 같은 표를 읽고, 테스트 전용 카드는
    /// 표 안의 <see cref="ECardChannel"/> 열로 갈린다(<c>CardCatalog.SetSource</c>의 includeTestCards).</summary>
    public static Dictionary<int, CardSpec> LoadCards()
    {
        SpecDataManager t_manager = Manager;
        if (t_manager == null)
            throw new InvalidOperationException("[SpecSource] SpecData를 읽지 못해 카드 정의를 만들 수 없다.");

        var t_specs = new Dictionary<int, CardSpec>();
        IReadOnlyList<Card> t_rows = t_manager.Card?.All;
        if (t_rows == null || t_rows.Count == 0)
            throw new InvalidOperationException("[SpecSource] Card 표가 비었다.");
        foreach (Card t_row in t_rows) AddCard(t_specs, From(t_row));
        return t_specs;
    }

    static CardSpec From(Card _row)
    {
        if (_row == null) throw new InvalidOperationException("Card 표에 null 행이 있다.");
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

#if UNITY_EDITOR
    /// <summary>에디터 도구 전용 — <b>저작본</b>(Assets/Resources/SpecData.bytes)을 스냅샷 자리에 올린다.
    ///
    /// <para>런타임의 진실원은 서버 스냅샷 하나이고 폴백이 없다. 그런데 업로드 전 검증은 성격이 반대다 —
    /// 지금 올리려는 <b>로컬 표</b>가 규칙에 맞는지 봐야 하고, 그건 서버에 아직 없는 값이다.
    /// 그래서 자동 폴백이 아니라 도구가 이름을 불러 쓰는 문으로 둔다(플레이 모드는 이 문을 쓰지 않는다).</para>
    /// </summary>
    /// <param name="_error">실패 사유(성공이면 null)</param>
    /// <returns>저작본을 올렸으면 true</returns>
    public static bool TryLoadLocalAuthoring(out string _error)
    {
        Clear();
        s_loaded = true;   // 아래에서 세운 것을 EnsureLoaded 가 덮지 않게 잠근다.
        _error = null;

        string t_json = SpecDataResourceLoader.LoadSpecData();
        if (string.IsNullOrEmpty(t_json))
        {
            _error = "SpecData 리소스를 못 읽었다(시트 적용 & CS 생성 후 임포터를 돌릴 것).";
            Fail(ESpecLoadError.SnapshotParseFailed, _error);
            return false;
        }
        if (!TryLoadManager(t_json, out SpecDataManager t_manager))
        {
            _error = "SpecData 파싱 실패(생성 리소스가 손상됐을 수 있다).";
            Fail(ESpecLoadError.SnapshotParseFailed, _error);
            return false;
        }

        s_manager = t_manager;
        s_origin = "저작본";
        s_error = ESpecLoadError.None;
        s_errorDetail = null;

        string t_envId = null;
        try { t_envId = ContentProfileConfig.Active.CloudEnvId; }
        catch (System.Exception) { }

        // 지문은 있으면 좋고 없어도 검증을 막지 않는다 — 검증 대상은 표의 내용이지 지문이 아니다.
        if (!string.IsNullOrEmpty(t_envId) &&
            TryCombinedFingerprint(t_manager, t_envId, out string t_fingerprint, out _))
            s_fingerprint = t_fingerprint;

        return true;
    }
#endif

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
        s_error = ESpecLoadError.None;
        s_errorDetail = null;
    }

    static string CodeOf(ESpecLoadError _error) => "SPEC-" + (int)_error;

    /// <summary>실패 코드를 남긴다. 코드는 로그 첫 토큰이라 검색·리포트가 이 한 줄만 보면 된다.</summary>
    /// <param name="_error">사유 코드</param>
    /// <param name="_detail">환경·지문 같은 판별 근거</param>
    /// <param name="_asError">true면 LogError. 동기화 전 스냅샷 부재처럼 정상 경로는 false.</param>
    /// <param name="_clearSnapshot">false면 이미 세운 스냅샷을 유지한다(전투 지문만 실패한 경우).</param>
    static void Fail(ESpecLoadError _error, string _detail, bool _asError = true, bool _clearSnapshot = true)
    {
        s_error = _error;
        s_errorDetail = _detail;
        if (_clearSnapshot)
        {
            s_manager = null;
            s_fingerprint = "nospec";
            s_battleFingerprint = "nospec";
            s_origin = "없음";
        }

        string t_line = $"[SpecSource] {CodeOf(_error)} {_error} — {_detail}";
        if (_asError) Debug.LogError(t_line);
        else Debug.Log(t_line);
    }

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(폴백으로 계속 돈다).

        string t_envId = null;
        try { t_envId = ContentProfileConfig.Active.CloudEnvId; }
        catch (System.Exception) { }

        if (string.IsNullOrEmpty(t_envId))
        {
            Fail(ESpecLoadError.NoEnvironment, "콘텐츠 프로필의 CloudEnvId 가 비었다.");
            return;
        }

        // 스펙의 진실원은 서버 스냅샷 하나다 — 내장본 폴백은 없다.
        // 폴백이 있으면 서버와 다른 표로 조용히 돌다가, 어긋남이 대전이나 정산에서야 드러난다.
        if (!SpecSnapshotCache.TryLoad(t_envId, out string t_cachedJson, out string t_cachedFingerprint))
        {
            // 첫 실행이나 캐시 삭제 뒤의 정상 상태다 — 초기화의 SpecSyncStep 이 받아 채운다.
            // 그 뒤에도 이 상태로 남아 있으면 스펙을 읽는 축이 각자 예외를 던진다.
            Fail(ESpecLoadError.NoSnapshot, $"env={t_envId} 로컬 스냅샷 없음 — 서버 동기화 전이다.", _asError: false);
            return;
        }

        // 봉투에 적힌 지문을 그대로 믿으면 payload와 지문이 어긋난 캐시(부분 기록·손상·payload만 고친 파일)가
        // 그대로 통과해, **내가 들고 있는 데이터와 내가 주장하는 지문이 다른 상태**로 대전에 들어간다.
        // 그래서 payload에서 지문을 다시 계산해 봉투 값과 대조하고, 어긋나면 캐시를 버린다.
        //
        // 이건 자기정합성 검사지 인증이 아니다 — 지문까지 같이 고쳐 쓰면 여기는 통과한다.
        // 조작 스펙을 실제로 막는 것은 서버 대조(BattleContentSync)와 상대와의 지문 대조(InitialDeck)다.
        if (!TryLoadManager(t_cachedJson, out SpecDataManager t_manager))
        {
            Fail(ESpecLoadError.SnapshotParseFailed, $"env={t_envId} 봉투={t_cachedFingerprint} 캐시 파싱 실패.");
            return;
        }

        if (!TryCombinedFingerprint(t_manager, t_envId, out string t_recomputed, out string t_cacheError))
        {
            Fail(ESpecLoadError.FingerprintFailed, $"env={t_envId} 봉투={t_cachedFingerprint} {t_cacheError}");
            return;
        }

        if (!string.Equals(t_recomputed, t_cachedFingerprint, System.StringComparison.Ordinal))
        {
            Fail(ESpecLoadError.FingerprintMismatch,
                 $"env={t_envId} 봉투={t_cachedFingerprint} 재계산={t_recomputed}");
            return;
        }

        s_manager = t_manager;
        s_fingerprint = t_recomputed;
        s_origin = "서버스냅샷";
        s_error = ESpecLoadError.None;
        s_errorDetail = null;

        {
            const string t_battleTable = "Card";
            if (SpecPayloadCodec.TryBuildLocalTable(t_manager, t_battleTable, out SpecTablePayload t_battlePayload, out string t_battleError))
                s_battleFingerprint = SpecPayloadCodec.CombinedHash(t_envId, new[] { t_battlePayload });
            else
                // 전투 지문만 못 세운 것이라 스냅샷 자체는 유효하다 — 코드만 남기고 진행한다.
                Fail(ESpecLoadError.BattleFingerprintFailed, $"table={t_battleTable}: {t_battleError}", _clearSnapshot: false);

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
