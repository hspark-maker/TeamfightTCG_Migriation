using UnityEngine;

/// <summary>엠블럼 연출을 띄울 타이밍(복수 선택). **시너지마다 "그 시너지의 순간"이 다르다** —
/// 낙인은 공격 전 선피해가, 수호자는 배치가, 덩치는 배치가 그 순간이라 코드가 한 지점을 못 고른다.
/// 그래서 발동 지점 자체는 각 효과가 <see cref="SynergyTriggers.Fire"/>로 이미 알리고,
/// **그중 어느 것을 엠블럼으로 보여줄지만** 이 플래그가 고른다(효과 로직과 연출 선택의 분리).</summary>
[System.Flags]
public enum SynergyEmblemTiming
{
    None      = 0,
    Placed    = 1 << 0,   // 필드에 놓이는 순간 — 오프닝 배치(Placed) + 런타임 등장(Entered). 그 카드 1장.
    Triggered = 1 << 1,   // 효과가 실제로 일한 순간(각 효과의 Fire 지점). 범위는 emblemTriggerScope.
}

/// <summary>Triggered 타이밍의 대상 범위. Placed는 "그 카드가 놓인 사건"이라 항상 1장이다.</summary>
public enum SynergyEmblemScope
{
    Self       = 0,   // 발동 주체 1장
    AllMembers = 1,   // 그 필드의 라이브 소속 아군 전원(낙인 선피해처럼 전원이 함께 일하는 효과)
}

// 엠블럼의 움직임 스타일은 enum이 아니라 **타입**이다 — SynergyEmblemSpec의 자식 하나가 몸짓 하나다.
// (enum이면 몸짓을 늘릴 때 값 추가 + 재생부 switch + 안 쓰는 형태값 칸이 같이 늘어난다.)

// 저작하지 않는다 — SynergySpecSource가 시트 행에서 만들어 꽂는다(인스펙터에 뜨지 않으므로 직렬화 속성도 없다).
public class SynergyTier
{
    public int requiredCount;

    // 이 단계의 별칭(SynergyTierDef.label). 설명문에 '2장 — <라벨>'로 붙는다.
    // 비면 요구 장수만 나오고, 시너지 이름과 같으면 표시하지 않는다.
    public string label;

    public SynergyEffect[] effects;
}

[CreateAssetMenu(fileName = "NewSynergy", menuName = "Card Battle/Synergy Data")]
public class SynergyData : ScriptableObject
{
    public Color  color;
    public Sprite activeIcon;   // 배지에 표시할 시너지 아이콘 스프라이트(SynergyBadgeView가 사용)
    public Sprite inactiveIcon;

    // 이 시너지의 연출 전부(엠블럼 + 고유 연출). **시너지당 에셋 하나**, 타입은 시너지 성격에 따라
    // SynergyVfxConfig의 자식 중 하나. 비워 두면 이 시너지는 연출 없이 규칙만 돈다(무동작 안전).
    // 값이 계속 늘어나는 축이라 여기 인라인으로 두면 SynergyData가 연출 값으로 부푼다.
    public SynergyVfxConfig vfx;

    /// <summary>이 타이밍에 엠블럼을 띄우는가. 연출 에셋/그림 미배정이면 어느 타이밍이든 false(무동작 안전).</summary>
    public bool PlaysEmblemAt(SynergyEmblemTiming _timing)
        => this.vfx != null && this.vfx.PlaysEmblemAt(_timing);

    /// <summary>UI 틴트용 색. **color 미배정이면 기본값이 (0,0,0,0) = 완전 투명**이라
    /// 그대로 곱하면 아이콘이 사라진다. 알파가 0이면 틴트 없음(흰색)으로 폴백한다.
    /// 색을 실제로 지정한 시너지만 틴트가 먹는다.</summary>
    public Color TintOrWhite => this.color.a > 0f ? this.color : Color.white;

    [Tooltip("시트(SynergyDef.synergyId)와 맞물리는 논리 ID. 비우면 에셋 이름의 Data_Synergy_ 접두를 뗀 값을 쓴다.")]
    public string synergyId;

    /// <summary>시트 조인 키. 에셋 이름에 기대는 폴백을 남겨 둔 건 에셋 8개를 손대지 않고 넘어오기 위해서다.</summary>
    public string SynergyId
    {
        get
        {
            if (!string.IsNullOrEmpty(this.synergyId)) return this.synergyId;
            const string PREFIX = "Data_Synergy_";
            return this.name.StartsWith(PREFIX, System.StringComparison.Ordinal)
                 ? this.name.Substring(PREFIX.Length)
                 : this.name;
        }
    }

    // 화면 표시 이름(SynergyDef.displayName). 비면 SynergyText가 에셋 이름으로 폴백한다.
    // **저작하지 않는다** — 아래 tiers와 같이 SynergySpecSource가 시트에서 꽂는다.
    [System.NonSerialized] public string displayName;

    // 효과 설명문(SynergyDef.effectDescription). 저작하지 않는다.
    [System.NonSerialized] public string effectDescription;

    // 다중 티어 정의. requiredCount 오름차순, Resolver가 만족하는 최고 티어 하나를 고른다.
    //
    // **저작하지 않는다 — SynergySpecSource가 스펙시트(SynergyTierDef/EffectDef/EffectParamDef)에서 만들어 꽂는다.**
    // 직렬화하지 않으므로 에셋에는 값이 남지 않고, 주입 전에 조회하면 비어 있다(= 시너지 비활성).
    [System.NonSerialized] public SynergyTier[] tiers;
}
