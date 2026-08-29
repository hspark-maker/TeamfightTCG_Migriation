using System;
using System.Collections.Generic;

/// <summary>전투 리졸버가 소비하는 카드 정적 데이터. 로딩과 문자열 파싱은 소유하지 않는다.</summary>
public sealed class CardSpec
{
    public const int BaseGrowthLevel = 1;
    public const int MinHpCurveLevel = BaseGrowthLevel + 1;
    public const int MaxHpCurveLevel = 4;
    public const int MaxEvolutionStage = 3;

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

    readonly int[] hpGainByLevel;
    readonly bool hasAuthoredCurve;

    public CardSpec(
        int _id, string _assetName, string _displayName, ECardChannel _channel, int _maxHp,
        CardKeyword _keywords, int _keywordUnlockLevel, int _defaultEvolutionStage,
        int _hp2, int _hp3, int _hp4, string _cardExplain, ECardGrade _grade,
        IReadOnlyList<string> _synergyNames)
    {
        if (_id <= 0) throw new InvalidOperationException($"카드 ID가 올바르지 않다: {_id}");
        if (string.IsNullOrWhiteSpace(_assetName)) throw new InvalidOperationException($"카드 {_id}의 name이 비었다.");
        if (_maxHp <= 0) throw new InvalidOperationException($"카드 {_id}({_assetName})의 maxHp가 {_maxHp}다.");
        if (_keywordUnlockLevel < 0) throw new InvalidOperationException($"카드 {_id}({_assetName})의 keywordUnlockLevel이 음수다.");
        if (_defaultEvolutionStage < 0 || _defaultEvolutionStage > MaxEvolutionStage)
            throw new InvalidOperationException($"카드 {_id}({_assetName})의 defaultEvolutionStage가 범위를 벗어났다.");
        if (_hp2 < 0 || _hp3 < 0 || _hp4 < 0)
            throw new InvalidOperationException($"카드 {_id}({_assetName})의 hp2~hp4에 음수가 있다.");

        Id = _id;
        AssetName = _assetName;
        DisplayName = string.IsNullOrWhiteSpace(_displayName) ? _assetName : _displayName;
        Channel = _channel;
        Grade = _grade;
        Keywords = _keywords;
        MaxHp = _maxHp;
        KeywordUnlockLevel = _keywordUnlockLevel;
        DefaultEvolutionStage = _defaultEvolutionStage;
        CardExplain = _cardExplain ?? string.Empty;
        SynergyNames = _synergyNames ?? Array.Empty<string>();
        hpGainByLevel = new[] { 0, 0, _hp2, _hp3, _hp4 };
        hasAuthoredCurve = _hp2 != 0 || _hp3 != 0 || _hp4 != 0;
    }

    public bool HasKeyword(CardKeyword _keyword) => (Keywords & _keyword) != 0;

    public bool TryGetHpGain(int _level, out int _hpGain)
    {
        _hpGain = 0;
        if (!hasAuthoredCurve || _level < MinHpCurveLevel || _level > MaxHpCurveLevel) return false;
        _hpGain = hpGainByLevel[_level];
        return true;
    }
}
