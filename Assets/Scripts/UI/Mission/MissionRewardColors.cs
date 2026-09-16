using UnityEngine;

// 미션 행과 누름 유지 툴팁이 공유하는 보상별 배경색.
[CreateAssetMenu(fileName = "MissionRewardColors", menuName = "Card Battle/UI/Mission Reward Colors")]
public sealed class MissionRewardColors : ScriptableObject
{
    [Header("재화 배경색")]
    [InspectorName("골드")] [SerializeField] Color gold = new Color32(255, 240, 194, 255);
    [InspectorName("다이아")] [SerializeField] Color diamond = new Color32(225, 230, 255, 255);
    [InspectorName("에너지")] [SerializeField] Color energy = new Color32(222, 242, 202, 255);
    [InspectorName("샤드 (강화 조각)")] [SerializeField] Color shard = new Color32(218, 240, 255, 255);
    [InspectorName("룰렛 티켓")] [SerializeField] Color rouletteTicket = new Color32(255, 222, 218, 255);

    [Header("경험치 배경색")]
    [InspectorName("계정 경험치")] [SerializeField] Color accountExp = new Color32(255, 229, 199, 255);
    [InspectorName("패스 경험치")] [SerializeField] Color passExp = new Color32(237, 225, 255, 255);

    [Header("아이템 배경색")]
    [InspectorName("카드")] [SerializeField] Color card = new Color32(223, 239, 230, 255);
    [InspectorName("카드팩")] [SerializeField] Color pack = new Color32(246, 222, 233, 255);
    [InspectorName("선택 카드팩")] [SerializeField] Color packChoice = new Color32(240, 223, 255, 255);
    [InspectorName("기타 보상")] [SerializeField] Color fallback = new Color32(255, 247, 226, 255);

    public Color AccountExp => this.accountExp;
    public Color PassExp => this.passExp;
    public Color Fallback => this.fallback;

    public Color Currency(ECurrencyType _type) => _type switch
    {
        ECurrencyType.Gold => this.gold,
        ECurrencyType.Diamond => this.diamond,
        ECurrencyType.Energy => this.energy,
        ECurrencyType.Shard => this.shard,
        ECurrencyType.RouletteTicket => this.rouletteTicket,
        _ => this.fallback,
    };

    public Color Item(string _type) => _type switch
    {
        "Card" => this.card,
        "Pack" => this.pack,
        "PackChoice" => this.packChoice,
        _ => this.fallback,
    };
}
