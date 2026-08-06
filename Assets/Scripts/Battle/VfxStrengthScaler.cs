using UnityEngine;

/// <summary>
/// 파티클의 **방출량·시작 속도**를 프리팹 원본 대비 배율로 덮어쓴다(피해 세기 반응용).
///
/// 풀 재사용 때문에 "곱해서 누적"은 쓸 수 없다 — 같은 인스턴스가 열 번 쓰이면 배율이 열 번 곱해진다.
/// 그래서 **첫 사용 때 원본 값을 캡처**해 두고, 매번 원본×배율을 **절대값으로** 다시 쓴다.
/// 캡처는 인스턴스마다 1회. 풀에서 갓 나온 첫 사용 시점의 값이 곧 프리팹 원본이다.
///
/// 순수 표시 계층 — RNG/게임상태 무관. 배선은 BattleVfxLibrary의 항목(countByStrength/speedByStrength)이 쥔다.
/// </summary>
[DisallowMultipleComponent]
public class VfxStrengthScaler : MonoBehaviour
{
    ParticleSystem[]         systems;
    float[]                  baseSpeed;
    float[]                  baseRate;
    ParticleSystem.Burst[][] baseBursts;
    ParticleSystem.Burst[][] scratchBursts;   // 매 적용마다 새로 할당하지 않으려는 재사용 버퍼

    /// <summary>_go에 붙여(없으면 추가해) 배율을 적용한다. 배율 1,1이면 호출부가 부르지 않는 게 정상이다.</summary>
    public static void Apply(GameObject _go, float _countMul, float _speedMul)
    {
        if (_go == null) return;
        if (!_go.TryGetComponent(out VfxStrengthScaler t_scaler))
            t_scaler = _go.AddComponent<VfxStrengthScaler>();
        t_scaler.ApplyInternal(Mathf.Max(0f, _countMul), Mathf.Max(0f, _speedMul));
    }

    void Capture()
    {
        if (this.systems != null) return;

        this.systems       = GetComponentsInChildren<ParticleSystem>(true);
        this.baseSpeed     = new float[this.systems.Length];
        this.baseRate      = new float[this.systems.Length];
        this.baseBursts    = new ParticleSystem.Burst[this.systems.Length][];
        this.scratchBursts = new ParticleSystem.Burst[this.systems.Length][];

        for (int i = 0; i < this.systems.Length; i++)
        {
            ParticleSystem t_ps = this.systems[i];
            if (t_ps == null) { this.baseBursts[i] = new ParticleSystem.Burst[0]; continue; }

            this.baseSpeed[i] = t_ps.main.startSpeedMultiplier;
            this.baseRate[i]  = t_ps.emission.rateOverTimeMultiplier;

            var t_bursts = new ParticleSystem.Burst[t_ps.emission.burstCount];
            if (t_bursts.Length > 0) t_ps.emission.GetBursts(t_bursts);
            this.baseBursts[i]    = t_bursts;
            this.scratchBursts[i] = new ParticleSystem.Burst[t_bursts.Length];
        }
    }

    void ApplyInternal(float _countMul, float _speedMul)
    {
        Capture();

        for (int i = 0; i < this.systems.Length; i++)
        {
            ParticleSystem t_ps = this.systems[i];
            if (t_ps == null) continue;

            ParticleSystem.MainModule t_main = t_ps.main;
            t_main.startSpeedMultiplier = this.baseSpeed[i] * _speedMul;

            ParticleSystem.EmissionModule t_em = t_ps.emission;
            t_em.rateOverTimeMultiplier = this.baseRate[i] * _countMul;

            ParticleSystem.Burst[] t_src = this.baseBursts[i];
            if (t_src.Length == 0) continue;

            ParticleSystem.Burst[] t_dst = this.scratchBursts[i];
            for (int b = 0; b < t_src.Length; b++)
            {
                t_dst[b] = t_src[b];
                t_dst[b].count = ScaleCount(t_src[b].count, _countMul);
            }
            t_em.SetBursts(t_dst);
        }

        // 스폰 직후 이미 재생이 시작된 상태다(풀이 SetActive로 깨운다) — 첫 버스트는 이미 뿌려졌으므로
        // 바뀐 개수를 반영하려면 처음부터 다시 뿌려야 한다. 루트 하나만 건드리면 자식은 withChildren이 끌고 간다.
        if (this.systems.Length > 0 && this.systems[0] != null)
        {
            this.systems[0].Clear(withChildren: true);
            this.systems[0].Play(withChildren: true);
        }
    }

    /// <summary>버스트 개수 곡선에 배율. 상수 모드는 상수를, 곡선 모드는 곡선 배율을 곱한다 —
    /// 모드를 안 보고 한쪽만 건드리면 그 모드로 저작된 파티클이 조용히 무반응이 된다.</summary>
    static ParticleSystem.MinMaxCurve ScaleCount(ParticleSystem.MinMaxCurve _count, float _mul)
    {
        switch (_count.mode)
        {
            case ParticleSystemCurveMode.Constant:
                _count.constant = _count.constant * _mul;
                break;
            case ParticleSystemCurveMode.TwoConstants:
                _count.constantMin = _count.constantMin * _mul;
                _count.constantMax = _count.constantMax * _mul;
                break;
            default:
                _count.curveMultiplier = _count.curveMultiplier * _mul;
                break;
        }
        return _count;
    }
}
