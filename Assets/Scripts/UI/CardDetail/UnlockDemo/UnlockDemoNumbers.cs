/// <summary>해금 데모 대본들이 나눠 쓰는 박자와 수치. 한 대본만 쓰는 박자는 그 대본 클래스 안에 남는다.</summary>
public static class UnlockDemoNumbers
{
    /// <summary>배역이 하나씩 서는 간격.</summary>
    public const float SYNERGY_STEP = 0.3f;

    /// <summary>결과를 읽는 시간.</summary>
    public const float SYNERGY_HOLD = 0.7f;

    // 아래 여섯은 docs/SpecData/SynergyEffectDef_sheet.csv의 parameters 칸(tierIndex 0)을 베낀 사본이다 —
    // 시트를 고쳐도 이 화면은 따라가지 않는다. 값을 쓰는 계산도 대본이 다시 쓰므로(포식자 비율·낙인 장수 곱·유산 적립)
    // 전투 쪽 규칙이 바뀌면 이 화면만 옛 값을 말한다.
    public const int BULK_BONUS_HP              = 3;    // bonusHp
    public const int CARETAKER_AMOUNT           = 1;    // amount (회복 + 추가 생명력)
    public const int SCALE_DMG_REDUCTION        = 1;    // dmgReduction
    public const int BRAND_DAMAGE_PER_MEMBER    = 1;    // damagePerMember
    public const int PREDATOR_LIFESTEAL_PERCENT = 50;   // lifestealPercent
    public const int LEGACY_AMOUNT              = 1;    // amount (턴마다 쌓이는 스택)

    /// <summary>힐러 회복량. 키워드 축이라 위 시트 사본과 무관하다(HealerEffect: 힐러 하나당 아군 1 회복).</summary>
    public const int HEALER_SHOW_HEAL = 1;

    /// <summary>배지에 실리는 시너지 인원수. 무대에 아군이 둘 서는 데서 온 값이고 화면에 숫자로는 안 나온다.</summary>
    public const int SYNERGY_SHOW_COUNT = 2;
}
