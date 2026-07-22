using UnityEngine;

[System.Serializable]
public class SynergyTier
{
    public int             requiredCount;
    public string          label;
    public SynergyEffect[] effects;
}

[CreateAssetMenu(fileName = "NewSynergy", menuName = "Card Battle/Synergy Data")]
public class SynergyData : ScriptableObject
{
    public string displayName;
    [TextArea] public string effectDescription;
    public Color  color;
    public Sprite icon;   // 배지에 표시할 시너지 아이콘 스프라이트(SynergyBadgeView가 사용)

    // 다중 티어 정의. requiredCount 오름차순 권장, Resolver가 만족하는 최고 티어 선택
    public SynergyTier[] tiers;
}
