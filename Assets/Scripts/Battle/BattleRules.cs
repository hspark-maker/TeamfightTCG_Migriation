/// <summary>전투 규칙 술어의 단일 진실원. 규칙(BattleField/AttackProcessor)과 표현(CardView)이
/// **같은 함수**를 부르게 해서 "규칙은 되는데 UI는 막는" 종류의 이중 진실원을 원천 차단한다.
///
/// 여기 있는 판정은 전부 <see cref="CardInstance"/> 기준이다 — data.keywords만 보면
/// 시너지(synergyKeywords)·패시브(runtimeKeywords)가 런타임에 부여한 키워드를 놓친다.</summary>
public static class BattleRules
{
    /// <summary>이 카드가 도발인가. 마스터 데이터 + 시너지 + 런타임 부여를 모두 포함한다.
    /// 타겟 필터(BattleField.GetValidTargets)와 UI 강조/차단 안내(CardView)가 공유하는 유일한 정의.</summary>
    public static bool IsTaunt(CardInstance _card)
        => _card != null && _card.HasKeyword(CardKeyword.Taunt);
}
