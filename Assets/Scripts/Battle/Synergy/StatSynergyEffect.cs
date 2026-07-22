using UnityEngine;

// 배선 데모용 선언형 효과: 공격력 가산 + 키워드 부여.
// "효과=데이터로 추가"의 증명 에셋. 규칙은 CardInstance.ApplySynergy에 위임.
[CreateAssetMenu(fileName = "NewStatSynergyEffect", menuName = "Card Battle/Synergy Effect/Stat")]
public class StatSynergyEffect : SynergyEffect
{
    [SerializeField] private int         bonusAtk;
    [SerializeField] private CardKeyword grantedKeywords;

    public override void Apply(CardInstance card, SynergyState state)
    {
        if (card == null) return;
        card.ApplySynergy(bonusAtk, grantedKeywords);
    }
}
