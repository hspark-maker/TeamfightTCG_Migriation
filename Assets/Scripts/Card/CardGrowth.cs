/// <summary>카드 한 장의 영구 성장 상태(강화 레벨 · 진화 단계). 세이브가 진실원이고 전투는 읽기만 한다.
///
/// 값 하나로 묶은 이유는 확장 축이다 — 성장 축이 늘어도(등급·각성 등) 이 struct에 필드만 더하면
/// <see cref="CardInstance"/> 생성자와 <see cref="BattleField"/> 공급 경로의 시그니처는 그대로다.
///
/// Battle과 OutGame 양쪽이 함께 쓰므로 어느 한쪽 폴더에 두지 않는다(경계: Battle은 OutGame을 참조하지 않는다).
/// 성장값을 만드는 쪽은 OutGame(CardGrowthManager), 소비하는 쪽은 Battle이며, 전달은 상위 부트/초기화가 한다.</summary>
public readonly struct CardGrowth
{
    /// <summary>강화 레벨. 0 = 미강화.</summary>
    public readonly int Level;

    /// <summary>강화로 얻은 최대 체력 가산분. 레벨에서 재계산하지 않고 값으로 들고 다닌다 —
    /// 곡선(CardGrowthConfig)을 아는 것은 OutGame뿐이고 전투는 결과만 받으면 되기 때문.</summary>
    public readonly int HpBonus;

    /// <summary>진화 단계. 0 = 미진화. 연출 자격(CardCinematicRules)과 진화 아트 선택의 입력.</summary>
    public readonly int EvolutionStage;

    public CardGrowth(int _level, int _hpBonus, int _evolutionStage)
    {
        this.Level          = _level;
        this.HpBonus        = _hpBonus;
        this.EvolutionStage = _evolutionStage;
    }
}
