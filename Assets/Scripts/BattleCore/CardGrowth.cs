/// <summary>카드 한 장의 영구 성장 상태(강화 레벨). 세이브가 진실원이고 전투는 읽기만 한다.
///
/// 값 하나로 묶은 이유는 확장 축이다 — 성장 축이 늘어도(등급·각성 등) 이 struct에 필드만 더하면
/// <see cref="CardInstance"/> 생성자와 <see cref="BattleField"/> 공급 경로의 시그니처는 그대로다.
///
/// Battle과 OutGame 양쪽이 함께 쓰므로 어느 한쪽 폴더에 두지 않는다(경계: Battle은 OutGame을 참조하지 않는다).
/// 성장값을 만드는 쪽은 OutGame(CardGrowthManager), 소비하는 쪽은 Battle이며, 전달은 상위 초기화가 한다.</summary>
public static class GrowthStar
{
    public const int MinStar = 0;

    public static int FromLevel(int _level)
        => _level <= CardGrowth.BaseLevel ? MinStar : _level - CardGrowth.BaseLevel;

    public static string Label(int _level) => $"{FromLevel(_level)}성";

    public static string ProgressLabel(int _level, int _maxLevel)
        => $"{Label(_level)} / {Label(_maxLevel)}";

    public static string TransitionLabel(int _fromLevel, int _toLevel)
        => $"{Label(_fromLevel)} → {Label(_toLevel)}";
}

public readonly struct CardGrowth
{
    /// <summary>미강화 카드의 레벨. 레벨은 1부터 세고 강화가 여기서부터 올린다 — 강화 횟수는 (Level - BaseLevel)이다.</summary>
    public const int BaseLevel = CardSpec.BaseGrowthLevel;

    /// <summary>강화 레벨. BaseLevel = 미강화.</summary>
    public readonly int Level;

    /// <summary>강화로 얻은 최대 체력 가산분. 레벨에서 재계산하지 않고 값으로 들고 다닌다 —
    /// 곡선(GrowthRules·카드 스펙)을 아는 것은 OutGame뿐이고 전투는 결과만 받으면 되기 때문.</summary>
    public readonly int HpBonus;

    /// <summary>강화로 도달한 진화 단계(0 = 미진화). 관문 레벨은 GrowthRules가 소유하고 여기엔 결과만 담긴다 —
    /// 전투가 곡선을 알 필요가 없다는 <see cref="HpBonus"/>와 같은 규약.</summary>
    public readonly int EvolutionStage;

    /// <summary>이 레벨에서 **실제로 켜져 있는** 카드 키워드. 기본 키워드에 더하는 값이 아니라 **대체하는** 값이다 —
    /// 키워드는 해금 레벨 전까지 아예 없는 것으로 친다. 소비측은 <see cref="Applied"/>가 true일 때만 이 값을 쓰고,
    /// 미주입(default)이면 카드 스펙 keywords를 그대로 써야 한다(AI·원격 미러의 기존 동작 보존).</summary>
    public readonly CardKeyword UnlockedKeywords;

    /// <summary>성장원이 실제로 주입됐는가. default(Level 0)는 "성장 미적용"이고 <see cref="Fresh"/>(Level 1)는
    /// "성장을 아는데 아직 미강화"다 — 이 둘을 가르는 유일한 기준이라 해금 게이트가 전부 여기에 매달린다.</summary>
    public bool Applied => this.Level >= BaseLevel;

    /// <summary>1차 진화로 시너지 기능이 열렸는가. **성장원 미주입(default)일 때도 false라는 점에 주의** —
    /// 소비측은 "false = 시너지 차단"이 아니라 성장이 주입된 경로에서만 게이트로 써야 한다.</summary>
    public readonly bool SynergyUnlocked;

    /// <summary>아직 한 번도 강화하지 않은 카드. 세이브에 기록이 없는 카드가 이 값이다
    /// (default는 성장원 미주입 — 레벨을 읽는 쪽이 없어 보너스 0으로만 쓰인다).</summary>
    public static CardGrowth Fresh => new CardGrowth(BaseLevel, 0);

    public CardGrowth(int _level, int _hpBonus)
        : this(_level, _hpBonus, 0, CardKeyword.None, false) { }

    public CardGrowth(int _level, int _hpBonus, int _evolutionStage,
                      CardKeyword _unlockedKeywords, bool _synergyUnlocked)
    {
        this.Level            = _level;
        this.HpBonus          = _hpBonus;
        this.EvolutionStage   = _evolutionStage;
        this.UnlockedKeywords = _unlockedKeywords;
        this.SynergyUnlocked  = _synergyUnlocked;
    }
}
