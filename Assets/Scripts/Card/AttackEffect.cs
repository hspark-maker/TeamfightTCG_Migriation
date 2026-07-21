using System;
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

    public void SpawnParticles(Transform _attacker, Transform _defender, bool _flipOffset = false)
    {
        foreach (var t_entry in this.particles)
            SpawnDelayed(t_entry, _attacker, _defender, _flipOffset).Forget();
    }

    async UniTask SpawnDelayed(ParticleEntry _entry, Transform _attacker, Transform _defender, bool _flipOffset)
    {
        if (_entry.spawnDelay > 0f)
            await UniTask.Delay((int)(_entry.spawnDelay * 1000));
        if (_entry.prefab == null) return;

        Transform t_anchor = _entry.spawnTarget == ParticleSpawnTarget.Defender ? _defender : _attacker;
        if (t_anchor == null) return;

        Vector3    t_offset = _flipOffset ? -_entry.localOffset : _entry.localOffset;
        Quaternion t_base   = Quaternion.Euler(_entry.initialRotation);
        Quaternion t_rot    = _flipOffset
            ? Quaternion.Euler(180f, 0f, 0f) * t_base
            : t_base;
        string t_id = _entry.prefab.GetInstanceID().ToString();
        ParticlePooler.Register(t_id, _entry.prefab);
        ParticlePooler.Spawn(t_id, t_anchor.TransformPoint(t_offset), t_rot);
    }
}

public enum ParticleSpawnTarget { Attacker, Defender }

[Serializable]
public struct ParticleEntry
{
    public GameObject prefab;
    [Min(0f)] public float spawnDelay;
    public Vector3 localOffset;
    public Vector3 initialRotation;
    public ParticleSpawnTarget spawnTarget;
}

[Serializable]
public struct ProjectileData
{
    public GameObject prefab;
    public GameObject impactPrefab;
    public Vector3 localOffset;
    [Min(0f)] public float spawnDelay;
}
