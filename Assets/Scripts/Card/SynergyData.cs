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

    // 다중 티어 정의. requiredCount 오름차순 권장, Resolver가 만족하는 최고 티어 선택
    public SynergyTier[] tiers;
}
