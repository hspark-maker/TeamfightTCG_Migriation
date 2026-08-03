using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>엠블럼 한 줄 = **언제 · 누구에게 · 어떤 몸짓으로**.
///
/// 같은 시너지라도 타이밍마다 어울리는 몸짓이 다르다 — 비늘은 놓일 때 떨어져 반짝이고,
/// 피해를 깎는 순간엔 짧게 pop 한다. 그래서 몸짓을 시너지당 하나로 묶지 않고 이 줄을 여러 개 둔다.
///
/// timing은 Flags라 한 줄이 여러 타이밍을 겸할 수 있다(무리처럼 배치·발동에 같은 몸짓을 쓰는 경우).</summary>
[Serializable]
public class SynergyEmblemEntry
{
    public SynergyEmblemTiming timing = SynergyEmblemTiming.Placed;
    [Tooltip("Triggered일 때만 의미 있다. Placed는 '그 카드가 놓인 사건'이라 항상 1장.")]
    public SynergyEmblemScope scope = SynergyEmblemScope.Self;

    // 몸짓 타입을 인스펙터에서 골라 꽂는다 — 고른 타입의 칸만 뜬다(안 쓰는 몸짓 값이 에셋에 안 남는다).
    [SerializeReference] public SynergyEmblemSpec spec = new RiseAndShakeEmblem();

    /// <summary>이 줄이 그 타이밍에 실제로 뜨는가. 몸짓/그림 미배정이면 false(무동작 안전).</summary>
    public bool Covers(SynergyEmblemTiming _timing)
        => (this.timing & _timing) != 0 && this.spec != null && this.spec.sprite != null;
}

/// <summary>
/// **시너지 하나의 연출 전부**를 담는 에셋. SynergyData.vfx 한 방향으로만 연결된다
/// (이 에셋은 자기가 어느 시너지 것인지 모른다 — 양방향이면 진실원이 흐려진다).
///
/// 상속으로 나누는 이유: 모든 시너지가 똑같이 갖는 것(엠블럼)은 여기 베이스에,
/// **그 시너지에서만 쓰는 연출**(무리 투사체, 흐름 바람)은 자식에 둔다. 한 타입에 다 몰아넣으면
/// 시너지가 늘수록 슬롯 종류만 늘고 각 에셋은 안 쓰는 빈 슬롯으로 채워진다.
///
/// 축 구분: 카드 고유 연출 = CardData.attackEffect / **시너지 고유 연출 = 이 에셋** /
/// 키워드·전투 이벤트 공용 연출 = BattleVfxLibrary(Hit·Heal·처형·무쌍·교활 등).
/// </summary>
public abstract class SynergyVfxConfig : ScriptableObject
{
    [Header("엠블럼 (타이밍마다 한 줄)")]
    // 비우면 이 시너지는 엠블럼 없이 규칙만 돈다. 같은 타이밍을 두 줄이 겹쳐 덮으면 앞줄이 이긴다
    // (겹쳐 재생하면 상징 두 개가 한 자리에 포개져 읽히지 않는다).
    public List<SynergyEmblemEntry> emblems = new List<SynergyEmblemEntry>();

    /// <summary>그 타이밍을 맡은 줄. 없으면 null — 호출부는 조용히 건너뛴다.</summary>
    public SynergyEmblemEntry EntryFor(SynergyEmblemTiming _timing)
    {
        if (this.emblems == null) return null;
        foreach (SynergyEmblemEntry t_e in this.emblems)
            if (t_e != null && t_e.Covers(_timing)) return t_e;
        return null;
    }

    /// <summary>이 타이밍에 엠블럼을 띄우는가.</summary>
    public bool PlaysEmblemAt(SynergyEmblemTiming _timing) => EntryFor(_timing) != null;
}
