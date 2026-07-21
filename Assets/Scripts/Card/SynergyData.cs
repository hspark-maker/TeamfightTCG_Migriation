using UnityEngine;

[CreateAssetMenu(fileName = "NewSynergy", menuName = "Card Battle/Synergy Data")]
public class SynergyData : ScriptableObject
{
    public string displayName;
    public int    requiredCount;
    [TextArea] public string effectDescription;
    public Color  color;
}
