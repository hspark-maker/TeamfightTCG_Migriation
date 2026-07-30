using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>전역(규칙 기반) 연출 식별자. **카드 고유 연출은 여기 넣지 않는다** — 그쪽 축은 CardData.attackEffect.
/// 값은 직렬화되므로 재사용/재정렬 금지(추가만).</summary>
public enum BattleVfxId
{
    None             = 0,
    HealerLaunch     = 1,   // 힐러 카드 아래에서 먼저 터지는 발동 이펙트
    HealerProjectile = 2,   // 힐러 → 아군으로 날아가는 투사체(수명은 호출부가 관리)
    HealerImpact     = 3,   // (사용 안 함) 투사체 도착 폭발 → 카드 회복 연출(Heal)로 통합됨. 값은 재사용 금지.
    Hit              = 4,   // 피격 파티클(맞은 카드에 부착 — 숫자는 CardView 프리팹의 HitEffectView 담당)
    Heal             = 5,   // 회복 파티클(회복된 카드에 부착 — 힐러/돌보미/청소부/유산 등 모든 회복 경로 공통)
    CinemaEnergyOrb  = 6,   // 시네마 공격(EnergyOrbDash): 카드가 변하는 에너지 구체. 수명은 호출부가 관리
    PeerlessSlash    = 7,   // 무쌍 연출의 베기 섬광(대상 위치에 방향 맞춰 1회). 수명은 항목 lifetime
    PeerlessSwing    = 8,   // 무쌍 연출의 휘두름(공격자 앞에서 대상 쪽으로). 베기와 짝 — 벨 때마다 같이 난다
    ExecutionSpark   = 9,   // 처형 발동: 처형자 무기(도끼) **양 끝**에 하나씩
    ExecutionCircle  = 10,  // 처형 발동: 처형자 카드 자리에 한 번
    CunningFog       = 11,  // 교활 퇴장: 카드가 덱으로 돌아가기 직전 자리에 깔리는 안개
}

/// <summary>연출 1건의 배치 스펙. AttackEffect의 ParticleEntry와 필드가 겹치지만 재사용하지 않는다 —
/// 거긴 timing(Armed/AttackStart)·spawnTarget(Attacker/Defender)이 공격 문맥 전용이라 전역 연출엔 의미가 없다.
/// 공통이어야 할 것은 타입이 아니라 **스폰/반납/정렬 규약**이고, 그건 BattleVfx 하나가 이미 소유한다.</summary>
[Serializable]
public struct VfxEntry
{
    public BattleVfxId id;
    public GameObject  prefab;
    public Vector3     localOffset;       // 스폰 지점 기준 추가 오프셋
    public Vector3     initialRotation;
    [Min(0f)] public float lifetime;      // 풀 반납까지의 시간(PooledParticle 보유 프리팹이면 무시)
    public int sortingOrder;              // 카드와 같은 정렬 레이어에서의 order(구매 에셋이 카드 뒤로 깔리는 것 방지)
    // true면 호출부가 준 방향으로 회전시켜 스폰(예: 피격 반대 방향으로 튀는 먼지).
    // 방향이 없으면(환경 피해 등) 평소대로 항목 회전값만 쓴다.
    public bool alignToDirection;
}

/// <summary>
/// 키워드·시너지·전투 이벤트처럼 **규칙이 발동시키는** 연출의 프리팹 배선 단일 지점.
/// 연출이 하나 늘어도 씬/프리팹 수정 없이 이 에셋의 목록만 늘어난다(씬 배선 증식 차단).
///
/// 축 구분: 카드 고유 연출 = CardData.attackEffect / 규칙 기반 연출 = 이 라이브러리 /
/// **시간 값 = BattleTimingConfig**(여기엔 시간을 두지 않는다 — 배속 배율 우회 방지).
/// </summary>
[CreateAssetMenu(fileName = "BattleVfxLibrary", menuName = "Card Battle/Battle Vfx Library")]
public class BattleVfxLibrary : ScriptableObject
{
    [Header("연출 목록 (id → 프리팹/배치)")]
    public VfxEntry[] entries;

    [Header("힐러 비행 형태값 (시간은 BattleTimingConfig)")]
    public float healCurveHeight    = 0.8f;    // 베지어 제어점을 직선에서 밀어내는 거리(0이면 직선)
    public bool  healAlternateCurve = true;    // 대상마다 커브 방향 교차 → 여러 발이 부채꼴로 갈라진다

    [Header("처형 형태값")]
    // 스파크 두 점의 위치 = 카드 중심에서 (±x, +y). 카드 로컬 기준이라 연출 중 카드가 기울면 같이 기운다.
    public Vector2 executionSparkOffset = new Vector2(0.45f, 0.6f);

    /// <summary>같은 id의 항목을 **전부** 모은다(0개 가능). 한 연출을 여러 프리팹으로 겹쳐 쓰기 위한 것 —
    /// 예: 피격 = 임팩트 섬광 + 반대 방향 먼지. 재생 순서는 목록 순서 그대로.</summary>
    public int Collect(BattleVfxId _id, List<VfxEntry> _into)
    {
        _into.Clear();
        if (this.entries == null) return 0;

        foreach (VfxEntry t_e in this.entries)
            if (t_e.id == _id && t_e.prefab != null) _into.Add(t_e);
        return _into.Count;
    }

    /// <summary>id로 항목 하나 조회(같은 id가 여럿이면 첫 번째). 프리팹이 비어 있으면 "없음"으로 취급한다
    /// — 반쯤 배선된 항목이 스폰 실패로 새지 않게.</summary>
    public bool TryGet(BattleVfxId _id, out VfxEntry _entry)
    {
        _entry = default;
        if (this.entries == null) return false;

        foreach (VfxEntry t_e in this.entries)
        {
            if (t_e.id != _id || t_e.prefab == null) continue;
            _entry = t_e;
            return true;
        }
        return false;
    }
}
