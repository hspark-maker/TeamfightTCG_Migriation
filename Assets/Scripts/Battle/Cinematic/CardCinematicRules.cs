/// <summary>카드 시네마 공격의 일회성 판정 규칙.</summary>
public static class CardCinematicRules
{
    public const int CINEMA_ATTACK_STAGE = 3;

    /// <summary>3단계 카드의 첫 공격 한 번만 시네마 공격으로 소비한다.</summary>
    public static bool TryConsumeCinemaAttack(CardInstance _attacker)
    {
        if (_attacker == null) return false;
        if (_attacker.evolutionStage < CINEMA_ATTACK_STAGE) return false;
        if (_attacker.cinemaAttackUsed) return false;

        _attacker.cinemaAttackUsed = true;
        return true;
    }
}
