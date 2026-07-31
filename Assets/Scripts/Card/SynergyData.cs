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
    public Sprite activeIcon;   // 배지에 표시할 시너지 아이콘 스프라이트(SynergyBadgeView가 사용)
    public Sprite inactiveIcon;

    // 시너지 발동/등장 시 카드 **뒤쪽**에 크게 떠오르는 상징 그림. 배지 아이콘(activeIcon)과 축이 다르다 —
    // 저건 상시 표시용 작은 UI 아이콘이고 이건 1회성 대형 연출이라, 같은 그림을 쓰면 둘 중 하나가 반드시 어색해진다.
    // 비워 두면 그 시너지는 엠블럼 연출 자체를 건너뛴다(무동작 안전).
    public Sprite emblem;

    /// <summary>UI 틴트용 색. **color 미배정이면 기본값이 (0,0,0,0) = 완전 투명**이라
    /// 그대로 곱하면 아이콘이 사라진다. 알파가 0이면 틴트 없음(흰색)으로 폴백한다.
    /// 색을 실제로 지정한 시너지만 틴트가 먹는다.</summary>
    public Color TintOrWhite => this.color.a > 0f ? this.color : Color.white;

    // 다중 티어 정의. requiredCount 오름차순 권장, Resolver가 만족하는 최고 티어 선택
    public SynergyTier[] tiers;
}
