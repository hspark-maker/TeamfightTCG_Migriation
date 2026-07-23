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

    /// <summary>UI 틴트용 색. **color 미배정이면 기본값이 (0,0,0,0) = 완전 투명**이라
    /// 그대로 곱하면 아이콘이 사라진다. 알파가 0이면 틴트 없음(흰색)으로 폴백한다.
    /// 색을 실제로 지정한 시너지만 틴트가 먹는다.</summary>
    public Color TintOrWhite => this.color.a > 0f ? this.color : Color.white;

    // 다중 티어 정의. requiredCount 오름차순 권장, Resolver가 만족하는 최고 티어 선택
    public SynergyTier[] tiers;
}
