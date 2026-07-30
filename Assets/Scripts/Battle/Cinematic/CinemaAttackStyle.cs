/// <summary>시네마 공격(3단계 첫 공격) 연출 종류. **카드마다 다른 연출**을 주기 위한 축이며,
/// 값은 CardData에 직렬화되므로 재사용/재정렬 금지(추가만).
///
/// 연출 구현은 AttackSequence가 소유하고, 어떤 카드가 어떤 연출인지는 CardData가 소유한다 —
/// 판정 분기를 뷰/시퀀스 양쪽에 흩지 않기 위한 구분이다.</summary>
public enum CinemaAttackStyle
{
    /// <summary>기존 시네마: 둘만 앞으로 떠서 카메라 확대 + 박치기.</summary>
    Default = 0,

    /// <summary>카드가 에너지 구체로 변해(알파로 사라짐) 상대에게 돌진·충돌 후 제자리로 돌아와 다시 나타난다.
    /// **슬롯 등장 연출도 같이 바뀐다** — 같은 구체가 화면 중앙에서 커브를 그리며 날아온다(CardAppearVfx).</summary>
    EnergyOrbDash = 1,
}
