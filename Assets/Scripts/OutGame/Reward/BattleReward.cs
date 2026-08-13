using UnityEngine;
using UnityEngine.Serialization;

// 전투 결과 → 보상 환산 튜닝 파라미터 SO
[CreateAssetMenu(fileName = "RewardConfig", menuName = "TCG/Reward Config")]
public class BattleReward : ScriptableObject
{
    [Tooltip("전투 보상으로 지급할 재화 종류.")]
    public ECurrencyType rewardType = ECurrencyType.Gold;

    [Tooltip("승리 시 남은 카드 1장당 지급 골드. 승리에만 적용된다 — 패배는 남은 카드와 무관하게 loseGold 고정이라 " +
             "카드를 남긴 채 항복해도 이 값이 붙지 않는다.")]
    public long goldPerCard = 10;

    [Tooltip("승리 지급액의 하한. 카드가 배치되기 전에 끝난 부전승(남은 카드 0장)을 받치는 바닥이다. " +
             "goldPerCard보다 크게 두면 그 구간의 카드 비례가 평평해지므로 장당 골드 이하로 둔다.")]
    public long winFloor = 10;

    [Tooltip("패배 시 지급 골드(고정). 남은 카드 수를 보지 않는다 — 항복·정상 패배가 같은 액수다.")]
    [FormerlySerializedAs("minGold")]
    public long loseGold = 5;
}
