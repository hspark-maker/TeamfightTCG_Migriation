using UnityEngine;

// 전투 결과 → 보상 환산 튜닝 파라미터 SO
[CreateAssetMenu(fileName = "RewardConfig", menuName = "TCG/Reward Config")]
public class BattleReward : ScriptableObject
{
    [Tooltip("전투 보상으로 지급할 재화 종류.")]
    public ECurrencyType rewardType = ECurrencyType.Gold;

    [Tooltip("전투 종료 시 남은 카드 1장당 지급 골드.")]
    public long goldPerCard = 10;

    [Tooltip("한 판에서 지급되는 골드 하한(클램프)")]
    public long minGold = 5;

}
