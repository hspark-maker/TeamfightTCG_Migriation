using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "NewAttackEffect", menuName = "Card Battle/Attack Effect")]
public class AttackEffect : ScriptableObject
{
    public string animTrigger;
    [Min(0f)] public float hitDelay  = 0.3f;
    [Min(0f)] public float duration  = 0.6f;
    public AudioClip[] attackClips;
    public ProjectileData projectile;
    public ParticleEntry[] particles;

    /// <summary>공격 시작 시점에 터뜨릴 파티클들. 무장 지속형(timing=Armed)은 CardView가 따로 관리하므로 건너뛴다.</summary>
    public void SpawnParticles(Transform _attacker, Transform _defender, bool _flipOffset = false)
    {
        if (this.particles == null) return;
        foreach (var t_entry in this.particles)
        {
            if (t_entry.timing != ParticleTiming.AttackStart) continue;
            SpawnDelayed(t_entry, _attacker, _defender, _flipOffset).Forget();
        }
    }

    /// <summary>무장(포커스) 동안 카드에 붙여둘 항목들. 없으면 빈 열거.
    /// 스폰/해제 시점은 CardView가 쥔다 — 무장은 공격 발사 전 입력 상태라 AttackSequence 바깥이다.</summary>
    public IEnumerable<ParticleEntry> ArmedEntries()
    {
        if (this.particles == null) yield break;
        foreach (var t_entry in this.particles)
            if (t_entry.timing == ParticleTiming.Armed && t_entry.prefab != null)
                yield return t_entry;
    }

    async UniTask SpawnDelayed(ParticleEntry _entry, Transform _attacker, Transform _defender, bool _flipOffset)
    {
        if (_entry.spawnDelay > 0f)
            await UniTask.Delay((int)(_entry.spawnDelay * 1000));
        if (_entry.prefab == null) return;

        Transform t_anchor = _entry.spawnTarget == ParticleSpawnTarget.Defender ? _defender : _attacker;
        if (t_anchor == null) return;

        // 부착·flip·풀 대여 규약은 BattleVfx 하나가 소유한다(무장 이펙트·테스터와 같은 규칙).
        GameObject t_go = BattleVfx.SpawnAttached(_entry.prefab, t_anchor,
            _entry.localOffset, _entry.initialRotation, _flipOffset, out string t_id);
        if (t_go == null) return;

        // PooledParticle이 있으면 스스로 반납한다. 없으면 영영 안 돌아와 카드 자식으로 계속 쌓인다.
        if (!BattleVfx.SelfReleasing(t_go))
            BattleVfx.ReleaseAfter(t_id, t_go, _entry.lifetime > 0f ? _entry.lifetime : this.duration).Forget();
    }
}

public enum ParticleSpawnTarget { Attacker, Defender }

/// <summary>파티클이 언제 붙는가. 기존 에셋은 값이 없어 0(AttackStart)으로 읽히므로 동작이 그대로다.</summary>
public enum ParticleTiming
{
    AttackStart,   // 공격 모션 시작 시 1회. spawnDelay/lifetime을 따른다.
    Armed,         // 무장(포커스)~접촉까지 유지. CardView가 켜고 AttackSequence 접촉 시점이 끈다.
}

[Serializable]
public struct ParticleEntry
{
    public GameObject prefab;
    public ParticleTiming timing;
    [Min(0f)] public float spawnDelay;
    public Vector3 localOffset;
    public Vector3 initialRotation;
    public ParticleSpawnTarget spawnTarget;
    // 풀 반납까지의 시간. 0이면 AttackEffect.duration을 쓴다. timing=Armed면 접촉 시점에 꺼지므로 안 쓰인다.
    // 프리팹에 PooledParticle이 붙어 있으면 그쪽 releaseTime이 우선이라 이 값은 무시된다.
    [Min(0f)] public float lifetime;
}

[Serializable]
public struct ProjectileData
{
    public GameObject prefab;
    public GameObject impactPrefab;
    public Vector3 localOffset;
    [Min(0f)] public float spawnDelay;
}
