using System;
using System.Collections.Generic;

/// <summary>SpecData Card/Card_Test 행에서 만든 카드 정적 정의. Unity 에셋 참조는 CardData가 계속 소유한다.</summary>
public sealed class CardSpec
{
    public int Id { get; }
    public string AssetName { get; }
    public string DisplayName { get; }
    public ECardChannel Channel { get; }
    public ECardGrade Grade { get; }
    public CardKeyword Keywords { get; }
    public int MaxHp { get; }
    public int KeywordUnlockLevel { get; }
    public int DefaultEvolutionStage { get; }
    public string CardExplain { get; }
    public IReadOnlyList<string> SynergyNames { get; }
    public CinemaAttackStyle CinemaAttackStyle { get; }
    public string AttackEffectKey { get; }

    readonly int[] hpGainByLevel;

    /// hp2~hp4가 전부 0이면 "미저작"으로 본다 — 구 SO의 빈 배열과 같은 뜻이고, 성장식은
    /// CardGrowthConfig 전역값으로 떨어진다. 표는 빈 칸과 0을 구분해 주지 못하므로(둘 다 int 0)
    /// 이 규약이 없으면 열을 비운 카드가 조용히 "증가량 0"으로 굳는다.
    readonly bool hasAuthoredCurve;

    CardSpec(
        int _id, string _assetName, string _displayName, string _channel, int _maxHp,
        string _keywords, int _keywordUnlockLevel, int _defaultEvolutionStage,
        int _hp2, int _hp3, int _hp4, string _cardExplain, string _grade, string _synergies,
        string _cinemaAttackStyle, string _attackEffectKey)
    {
        if (_id <= 0) throw new InvalidOperationException($"카드 표 ID가 올바르지 않다: {_id}");
        if (string.IsNullOrWhiteSpace(_assetName)) throw new InvalidOperationException($"카드 {_id}의 name이 비었다.");
        if (_maxHp <= 0) throw new InvalidOperationException($"카드 {_id}({_assetName})의 maxHp가 {_maxHp}다.");
        if (_keywordUnlockLevel < 0) throw new InvalidOperationException($"카드 {_id}({_assetName})의 keywordUnlockLevel이 음수다.");
        if (_defaultEvolutionStage < 0 || _defaultEvolutionStage > CardData.MaxEvolutionStage)
            throw new InvalidOperationException($"카드 {_id}({_assetName})의 defaultEvolutionStage가 범위를 벗어났다.");
        if (_hp2 < 0 || _hp3 < 0 || _hp4 < 0)
            throw new InvalidOperationException($"카드 {_id}({_assetName})의 hp2~hp4에 음수가 있다.");

        Id = _id;
        AssetName = _assetName;
        DisplayName = string.IsNullOrWhiteSpace(_displayName) ? _assetName : _displayName;
        Channel = ParseEnum<ECardChannel>(_channel, _id, _assetName, "channel");
        Grade = ParseEnum<ECardGrade>(_grade, _id, _assetName, "grade");
        Keywords = ParseKeywords(_keywords, _id, _assetName);
        MaxHp = _maxHp;
        KeywordUnlockLevel = _keywordUnlockLevel;
        DefaultEvolutionStage = _defaultEvolutionStage;
        CardExplain = _cardExplain ?? string.Empty;
        SynergyNames = ParseSynergies(_synergies, _id, _assetName);
        CinemaAttackStyle = ParseEnumOrDefault(_cinemaAttackStyle, CinemaAttackStyle.Default, _id, _assetName, "cinemaAttackStyle");
        AttackEffectKey = _attackEffectKey?.Trim() ?? string.Empty;
        hpGainByLevel = new[] { 0, 0, _hp2, _hp3, _hp4 };
        hasAuthoredCurve = _hp2 != 0 || _hp3 != 0 || _hp4 != 0;
    }

    public bool HasKeyword(CardKeyword _keyword) => (Keywords & _keyword) != 0;

    public bool TryGetHpGain(int _level, out int _hpGain)
    {
        _hpGain = 0;
        if (!hasAuthoredCurve) return false;
        if (_level < CardData.MinHpCurveLevel || _level > CardData.MaxHpCurveLevel) return false;
        _hpGain = hpGainByLevel[_level];
        return true;
    }

    public static Dictionary<int, CardSpec> Load(EContentRunMode _mode)
    {
        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null)
            throw new InvalidOperationException("[CardSpec] SpecData를 읽지 못해 카드 정의를 만들 수 없다.");

        var t_specs = new Dictionary<int, CardSpec>();
        if (_mode == EContentRunMode.Test)
        {
            IReadOnlyList<Card_Test> t_rows = t_manager.Card_Test?.All;
            if (t_rows == null || t_rows.Count == 0) throw new InvalidOperationException("[CardSpec] Card_Test 표가 비었다.");
            foreach (Card_Test t_row in t_rows) Add(t_specs, From(t_row));
        }
        else
        {
            IReadOnlyList<Card> t_rows = t_manager.Card?.All;
            if (t_rows == null || t_rows.Count == 0) throw new InvalidOperationException("[CardSpec] Card 표가 비었다.");
            foreach (Card t_row in t_rows) Add(t_specs, From(t_row));
        }
        return t_specs;
    }

    static CardSpec From(Card _row)
    {
        if (_row == null) throw new InvalidOperationException("Card 표에 null 행이 있다.");
        return new CardSpec(_row.id, _row.name, _row.displayName, _row.channel, _row.maxHp, _row.keywords,
            _row.keywordUnlockLevel, _row.defaultEvolutionStage, _row.hp2, _row.hp3, _row.hp4,
            _row.cardExplain, _row.grade, _row.synergies, _row.cinemaAttackStyle, _row.attackEffectKey);
    }

    static CardSpec From(Card_Test _row)
    {
        if (_row == null) throw new InvalidOperationException("Card_Test 표에 null 행이 있다.");
        return new CardSpec(_row.id, _row.name, _row.displayName, _row.channel, _row.maxHp, _row.keywords,
            _row.keywordUnlockLevel, _row.defaultEvolutionStage, _row.hp2, _row.hp3, _row.hp4,
            _row.cardExplain, _row.grade, _row.synergies, _row.cinemaAttackStyle, _row.attackEffectKey);
    }

    static void Add(Dictionary<int, CardSpec> _specs, CardSpec _spec)
    {
        if (_specs.ContainsKey(_spec.Id)) throw new InvalidOperationException($"카드 표 ID {_spec.Id}가 중복이다.");
        _specs.Add(_spec.Id, _spec);
    }

    static T ParseEnum<T>(string _value, int _id, string _name, string _field) where T : struct
    {
        if (string.IsNullOrWhiteSpace(_value) || char.IsDigit(_value.Trim()[0]) ||
            !Enum.TryParse(_value.Trim(), true, out T t_value) || !Enum.IsDefined(typeof(T), t_value))
            throw new InvalidOperationException($"카드 {_id}({_name}).{_field} 값 '{_value}'을 해석할 수 없다.");
        return t_value;
    }

    static T ParseEnumOrDefault<T>(string _value, T _default, int _id, string _name, string _field) where T : struct
        => string.IsNullOrWhiteSpace(_value) ? _default : ParseEnum<T>(_value, _id, _name, _field);

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
                throw new InvalidOperationException($"카드 {_id}({_name}).synergies에 '{t_token}'이 중복됐다.");
            t_result.Add(t_token);
        }
        return t_result.AsReadOnly();
    }
}
