/// <summary>해금 데모 대본들이 나눠 쓰는 박자와 수치.
///
/// 대본 하나만 쓰는 박자는 여기 오지 않고 그 대본 클래스 안에 남는다 — 인스펙터에 내면 안 되는 것과
/// 같은 이유로, 한 대본의 내부 호흡을 공용 자리에 두면 다른 대본에도 있는 값처럼 읽힌다.</summary>
public static class UnlockDemoNumbers
{
    // ── 공용 박자 ────────────────────────────────────────────────────────
    //
    // 저작 축으로 뺄 값이 아니다. 이 무대에만 있는 대본들의 호흡이라, 인스펙터에 내면
    // 기획이 만질 수 있는 값처럼 보이지만 실제로는 대본 안무와 한 몸이다.

    /// <summary>배역이 하나씩 서는 간격.</summary>
    public const float SYNERGY_STEP = 0.3f;

    /// <summary>결과를 읽는 시간.</summary>
    public const float SYNERGY_HOLD = 0.7f;

    // ── 화면에 나오는 수치 ───────────────────────────────────────────────
    //
    // **진실원은 docs/SpecData/SynergyEffectDef_sheet.csv의 parameters 칸이고
    // 아래는 그 1단계(tierIndex 0) 값을 베낀 사본이다** — 시트를 고쳐도 이 화면은 따라가지 않는다.
    // 화면 숫자가 전투와 어긋나 보이면 그 시트에서 해당 synergyId의 tierIndex 0 행을 펴고,
    // 옆에 적어 둔 키 이름의 칸과 아래 값을 맞춰라.
    //
    // 사본인 것은 값만이 아니다 — **그 값을 쓰는 계산도 대본에서 다시 쓴다**. 전투 쪽 짝은 이렇다:
    //   포식자 비율 적용 floor(가한 피해 × %)  = PredatorSynergyEffect.OnAfterAttack
    //   낙인 장수 곱 + 하한 Max(1, …)          = BrandSynergyEffect.OnBeforeAttack
    //   유산 턴당 적립                          = LegacySynergyEffect.OnTurnBegan
    // 그쪽 규칙이 바뀌면 이 화면만 옛 값을 말한다. 값과 규칙이 대본 안에 닫혀 있는 것이 이 배치의 대가다.
    // 값만이라도 한자리에 모아 두는 것은, 대본이 파일로 갈린 뒤에도 이 장부가 남게 하기 위해서다.

    public const int BULK_BONUS_HP              = 3;    // bonusHp
    public const int CARETAKER_AMOUNT           = 1;    // amount (회복 + 추가 생명력)
    public const int SCALE_DMG_REDUCTION        = 1;    // dmgReduction
    public const int BRAND_DAMAGE_PER_MEMBER    = 1;    // damagePerMember
    public const int PREDATOR_LIFESTEAL_PERCENT = 50;   // lifestealPercent
    public const int LEGACY_AMOUNT              = 1;    // amount (턴마다 쌓이는 스택)

    /// <summary>힐러는 시너지가 아니라 키워드 축이라 위 시트 사본과 무관하다
    /// (HealerEffect: 힐러 하나당 아군 1 회복).</summary>
    public const int HEALER_SHOW_HEAL = 1;

    /// <summary>배지가 켜졌다는 사실만 나르므로 수치 자체는 화면에 안 나온다 —
    /// 무대에 아군이 둘 서는 데서 온 값이다.</summary>
    public const int SYNERGY_SHOW_COUNT = 2;
}
