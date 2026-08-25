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
    HealerImpact     = 3,   // (사용 안 함) 도착 폭발은 HealerArrival(20)로 되살아났다 — 옛 에셋에 값이
                            // 남아 있을 수 있어 3은 재사용하지 않는다.
    Hit              = 4,   // 피격 파티클(맞은 카드에 부착)
    Heal             = 5,   // 회복 파티클(회복된 카드에 부착 — 힐러/돌보미/포식자/유산 등 모든 회복 경로 공통)
    CinemaEnergyOrb  = 6,   // 시네마 공격(EnergyOrbDash): 카드가 변하는 에너지 구체. 수명은 호출부가 관리
    PeerlessSlash    = 7,   // 무쌍 연출의 베기 섬광(대상 위치에 방향 맞춰 1회). 수명은 항목 lifetime
    PeerlessSwing    = 8,   // 무쌍 연출의 휘두름(공격자 앞에서 대상 쪽으로). 베기와 짝 — 벨 때마다 같이 난다
    ExecutionSpark   = 9,   // (사용 안 함) 처형 발동의 스파크 — 마법진(ExecutionCircle)만 남겼다. 값은 재사용 금지.
    ExecutionCircle  = 10,  // 처형 발동: 처형자 카드 자리에 한 번
    CunningFog       = 11,  // 교활 퇴장: 카드가 덱으로 돌아가기 직전 자리에 깔리는 안개
    // 12·13은 (사용 안 함) 시너지 고유 연출이라 그 시너지의 SynergyVfxConfig로 옮겼다 —
    // 여기 남기면 시너지가 늘 때마다 이 enum이 같이 늘고, 값은 직렬화라 되돌릴 수도 없다.
    // 값 자체는 재사용 금지(기존 에셋에 남아 있을 수 있다).
    SwarmProjectile  = 12,  // (사용 안 함) → SwarmSynergyVfxConfig.projectile
    FlowWind         = 13,  // (사용 안 함) → FlowSynergyVfxConfig.wind
    FinishImpact     = 14,  // 승부를 가른 타격: 죽는 카드 자리에 1회. alignToDirection을 켜면 "때린 쪽 → 죽는 쪽"
                            // 방향으로 눕는다 — 반격사면 방향이 저절로 뒤집히므로 항목을 따로 만들 필요가 없다
    DeathStardust    = 15,  // (사용 안 함) 사망 별가루 — 바닥 파동(DeathNova)만 남겼다. 값은 재사용 금지.
    DeathNova        = 16,  // 사망: 카드가 사라진 자리에 남는 바닥 빛 파동. 별가루보다 늦게 1회
    RangedProjectile = 17,  // 원거리 기본 투사체. **카드가 자기 투사체를 안 가졌을 때만** 쓰인다
                            // (CardData.attackEffect.projectile이 우선). 원거리는 카드가 아니라 키워드가
                            // 만드는 연출이라, 카드마다 배선을 빠뜨리면 "발사체가 아예 안 나온다"가 된다
    TauntBlocked     = 18,  // 도발에 막힌 **공격자** 카드 위에 서는 표식
    TauntGuard       = 21,  // 도발 보유자 **본인**에게 나는 연출. 18과 짝이다 —
                            // "막는 쪽"과 "막힌 쪽"이 다른 그림이어야 누가 왜 막았는지 읽힌다.
    CardAppear       = 19,
    HealerArrival    = 20,  // 힐러 투사체가 대상에 닿는 순간의 임팩트. **힐러 경로 전용**이라
                            // 모든 회복이 공통으로 내는 Heal(5)과 겹쳐 난다 — 둘을 합치면
                            // 돌보미·포식자처럼 투사체가 없는 회복에서도 도착 임팩트가 터진다.
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

    // 프리팹 원본 크기에 곱하는 배율. **0 이하면 1로 본다** — 새로 생긴 필드라 기존 항목은 0으로
    // 역직렬화되고, 그걸 그대로 쓰면 모든 연출이 사라진다.
    // 프리팹 원본 기준으로 매번 다시 계산한다(풀 재사용분에 지난 배율이 누적되지 않게).
    [Min(0f)] public float scale;
    // true면 호출부가 준 방향으로 회전시켜 스폰(예: 피격 반대 방향으로 튀는 먼지).
    // 방향이 없으면(환경 피해 등) 평소대로 항목 회전값만 쓴다.
    //
    // 규약: **뿜는 축이 프리팹 로컬 +Z**여야 한다(정렬이 로컬 +Z를 방향에 맞춘다).
    // 파티클 Shape의 Rotation을 돌려 놓은 프리팹은 그만큼 initialRotation으로 되돌려야 한다 —
    // 예: Shape Rotation X=90(축이 -Y)이면 initialRotation X=-90. 이걸 빼먹으면 파편이
    // 화면 안팎으로 뿜어 "방향이 안 맞는" 게 아니라 아예 안 보인다.
    public bool alignToDirection;

    // true면 **반격 피격**(공격자가 되받는 경우)에는 생략한다. 먼지·파편처럼 "때린 자리에서 이는" 항목용 —
    // 공격자 발밑까지 같은 먼지가 일면 누가 맞은 건지 읽히지 않는다. 임팩트 섬광처럼 양쪽 다 나야 하는
    // 항목은 꺼둔다(기본 false = 기존 동작).
    public bool skipOnCounter;

    // ── 피해 세기 반응 (x = 세기 0일 때 배율, y = 세기 1일 때 배율) ──────────────
    // 세기는 호출부가 준 0~1 값(피격이면 HitImpact.Strength01 = 피해/최대체력).
    // **y가 0 이하면 반응 없음** = 기본값 (0,0)이라 기존 항목은 손대지 않아도 그대로다.
    // 프리팹 원본 값에 곱해지는 배율이라, 원본을 손보면 세기 반응도 같이 따라온다(두 값을 따로 관리하지 않게).
    public Vector2 countByStrength;   // 방출량(버스트 개수 + rateOverTime) 배율. 예: (0.5, 2) = 약한 타격 절반, 강타 2배
    public Vector2 speedByStrength;   // 시작 속도 배율. 세게 맞을수록 먼지가 멀리 튄다
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
