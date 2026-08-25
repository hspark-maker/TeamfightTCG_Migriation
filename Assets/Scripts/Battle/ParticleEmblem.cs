using System;
using UnityEngine;

/// <summary>그림 한 장이 아니라 **파티클 프리팹 한 벌**을 띄우는 몸짓(유산 왕관·비늘 비늘막·덩치 몸통).
///
/// <see cref="PrefabEmblem"/>과 갈라지는 지점은 딱 하나다 — 저건 SpriteRenderer 리그(+Animator)를 띄우고
/// 여기는 ParticleSystem 프리팹을 띄운다. 스프라이트가 하나도 없는 프리팹은 PrefabEmblem이
/// 크기 기준(렌더러 bounds)을 못 잡아 조용히 아무것도 안 뜬다 — 그래서 타입을 나눈다.
///
/// 스폰·정렬·풀 반납은 직접 하지 않고 <see cref="BattleVfx.SpawnPrefab"/>에 넘긴다.
/// 전투 파티클의 부착·정렬·반납 규약은 그쪽이 단일 진실원이라, 여기서 또 Instantiate하면
/// 시너지 파티클만 풀을 안 타는 예외가 생긴다.
///
/// ⚠ 크기는 카드 높이에 자동으로 안 맞춘다(베이스의 heightRatio·alpha·sprite 칸은 여기서 안 쓴다).
///   파티클은 스폰 시점에 렌더러 bounds가 비어 있어 "지금 얼마나 큰가"를 물어볼 대상이 없다 —
///   대신 프리팹 저작 크기 × <see cref="scale"/>로 못 박는다(BattleVfx.ApplyScale과 같은 규칙).
///
/// 순수 연출 — 상태/RNG 무접촉.</summary>
[Serializable]
public class ParticleEmblem : SynergyEmblemSpec
{
    [Header("파티클")]
    [Tooltip("띄울 파티클 프리팹. 비면 이 몸짓은 무동작")]
    public GameObject prefab;

    [Tooltip("슬롯 기준 위치 보정(월드 단위).")]
    public Vector2 offset;

    [Tooltip("프리팹 저작 크기 대비 배율. 카드 높이에 맞추는 자동 보정은 없다(파티클은 잴 수 없다).")]
    [Min(0.01f)] public float scale = 1f;

    /// <summary>스프라이트가 아니라 프리팹이 그림이다 — 베이스의 sprite 칸은 쓰지 않는다.</summary>
    public override bool HasArt => this.prefab != null;

    public override void Play(CardView _view, SynergyData _synergy)
    {
        if (this.prefab == null || _view == null) return;

        // 카드가 아니라 **슬롯** 기준이다(다른 몸짓과 동일) — 공격 연출로 카드가 나가 있어도 상징은 자리에 남는다.
        Vector3 t_pos = new Vector3(_view.SlotPosition.x + this.offset.x,
                                    _view.SlotPosition.y + this.offset.y,
                                    _view.SlotPosition.z);

        VfxHandle t_handle = BattleVfx.SpawnPrefab(this.prefab, t_pos, _view.VfxSortingLayerId, this.sortingOrder);
        if (!t_handle.Valid) return;

        // 풀에서 빌려온 오브젝트라 지난번에 먹인 배율이 남아 있다 — 매번 저작값 기준으로 다시 찍는다.
        t_handle.Go.transform.localScale = this.prefab.transform.localScale * Mathf.Max(0.01f, this.scale);

        // 반납은 이 몸짓의 길이(raw 초 × 배속)를 기준으로. 자기반납형(PooledParticle) 프리팹이면 핸들이 알아서 비켜난다.
        t_handle.Release(Duration);
    }
}
