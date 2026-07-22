using UnityEngine;

// 배선 데모용 선언형 효과: 공격력 가산 + 키워드 부여.
// "효과=데이터로 추가"의 증명 에셋. 규칙은 CardInstance.ApplySynergy에 위임.
[CreateAssetMenu(fileName = "NewStatSynergyEffect", menuName = "Card Battle/Synergy Effect/Stat")]
public class StatSynergyEffect : SynergyEffect
{
    [SerializeField] private int         bonusAtk;
    [SerializeField] private int         bonusHp;   // 덩치: 생애 1회 bonusHp 가산 (ApplyDeckSynergy 1회 경로 전용, stateful)
    [SerializeField] private CardKeyword grantedKeywords;
    [SerializeField] private int         dmgReduction;   // 비늘: 받는 피해 상시 -N (정적, 멱등)

    public override void Apply(CardInstance card, SynergyState state)
    {
        if (card == null) return;
        card.ApplySynergy(bonusAtk, bonusHp, grantedKeywords, dmgReduction);
    }
}
