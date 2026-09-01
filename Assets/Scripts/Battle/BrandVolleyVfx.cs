using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 낙인 선피해 연출의 **순서**만 소유한다: 필드의 낙인 아군이 슬롯 순서대로 작은 투사체를
/// 하나씩 쏘고 → 대상에 착탄 → 마지막 착탄 여운 뒤 호출부(본 공격 연출)로 제어가 돌아간다.
///
/// 프리팹/형태 = BrandSynergyVfxConfig(낙인 시너지 연출 에셋), 시간 = BattleTimingConfig,
/// 스폰·반납·정렬 = BattleVfx. 여기엔 어느 것도 두지 않는다(HealVfx와 같은 규약).
///
/// **선피해 수치는 호출부가 이미 적용한 뒤다 — 여기선 표시만 늦춘다.**
/// 상태 변경을 착탄까지 미루면 두 클라의 hp 타임라인이 프레임레이트만큼 갈라져 divergence가 된다
/// (훅 계약: 첫 await 전에 상태변이 완결). RNG도 소비하지 않는다 — 궤적 부호는 인덱스 패리티로 정한다.
///
/// 투사체가 미배선이면 즉시 반환한다. 연출은 선택이고 피해는 이미 들어가 있다.
/// </summary>
public static class BrandVolleyVfx
{
    /// <summary>낙인 아군 <paramref name="_sources"/>가 <paramref name="_target"/>에게 일제 사격.
    /// 마지막 착탄 + 여운까지 기다린 뒤 완료된다 — 호출부는 이걸 await 해서 본 공격을 뒤로 미룬다.
    ///
    /// <paramref name="_damages"/> = 발당 표시할 피해량(합 = 실제 적용된 총량).
    /// <paramref name="_hpBefore"/>/<paramref name="_bonusBefore"/> = 선피해 **적용 전** 대상 체력 —
    /// hp는 이미 다 깎인 상태라, 표시만 착탄마다 단계적으로 따라오게 하려면 시작값이 필요하다.</summary>
    public static async UniTask PlayVolley(List<CardView> _sources, CardView _target,
        IReadOnlyList<int> _damages, int _hpBefore, int _bonusBefore, BrandSynergyVfxConfig _vfx)
    {
        if (_target == null || _sources == null || _sources.Count == 0) return;
        if (_vfx == null || _vfx.projectile.prefab == null) return;   // 미배선 = 연출 생략

        // 착탄 순서대로 누적 차감할 잔여 체력. 발사는 병렬이지만 도착 순서는 stagger로 정해지므로
        // 인덱스별 누적을 미리 계산해 둔다(도착 시점에 공유 변수를 갱신하면 순서가 뒤집힐 때 값이 튄다).
        var t_remain = new int[_sources.Count];
        int t_hp = _hpBefore, t_bonus = _bonusBefore;
        for (int i = 0; i < _sources.Count; i++)
        {
            int t_d = _damages != null && i < _damages.Count ? _damages[i] : 0;
            int t_fromBonus = Mathf.Min(t_bonus, t_d);   // 추가 생명력부터 소모(TakeDamage 규칙과 같은 순서)
            t_bonus -= t_fromBonus;
            t_hp     = Mathf.Max(0, t_hp - (t_d - t_fromBonus));
            t_remain[i] = t_hp;
        }

        var t_flights = new List<UniTask>(_sources.Count);
        for (int i = 0; i < _sources.Count; i++)
        {
            if (_sources[i] == null) continue;
            int t_dmg = _damages != null && i < _damages.Count ? _damages[i] : 0;
            t_flights.Add(FlyOne(_sources[i], _target, i, t_dmg, t_remain[i], _vfx));
        }
        if (t_flights.Count == 0) return;

        await UniTask.WhenAll(t_flights);

        // 마지막 착탄이 눈에 남을 짬. 없으면 본 공격 돌진이 착탄과 같은 프레임에 시작해 뭉쳐 보인다.
        float t_hold = GameTiming.Battle.BrandImpactHold;
        if (t_hold > 0f)
            await UniTask.Delay((int)(t_hold * 1000)).SuppressCancellationThrow();
    }

    /// <summary>한 발. 대상/발사자가 파괴되면 조용히 접고 투사체는 반납한다.</summary>
    static async UniTask FlyOne(CardView _source, CardView _target, int _index, int _damage, int _hpAfter,
        BrandSynergyVfxConfig _vfx)
    {
        VfxHandle t_proj = default;
        try
        {
            var t_ct = _target.GetCancellationTokenOnDestroy();

            // 발사 간격 — 한 프레임에 다 나가면 여러 장이 쏜 게 아니라 한 덩어리로 보인다.
            float t_delay = _index * GameTiming.Battle.BrandLaunchStagger;
            if (t_delay > 0f)
                await UniTask.Delay((int)(t_delay * 1000), cancellationToken: t_ct);

            if (_source == null || _target == null) return;

            // 쏘는 동작 = 원거리 공격과 같은 제자리 반동(뒤로 살짝 밀렸다 복귀). 투사체와 **동시에** 시작한다 —
            // 반동이 끝나길 기다리면 발사가 늦어 "밀린 뒤에 쏜다"로 보인다.
            // await 하지 않는 이유: 비행 시간이 반동보다 길어 어차피 볼리가 더 늦게 끝난다(연출 길이 기준은 비행).
            AttackSequence.RecoilInPlace(_source, _target).Forget();

            t_proj = BattleVfx.Spawn(_vfx.projectile, _source.transform.position, _source.VfxSortingLayerId);
            if (!t_proj.Valid) return;

            // 실제 시작점은 항목 오프셋이 반영된 스폰 위치 — 커브도 그 점 기준이어야 궤적이 안 튄다.
            await Travel(t_proj.Go, t_proj.Go.transform.position, _target.transform.position, _index,
                         _vfx.curveHeight, t_ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Impact(t_proj, _target, _source, _damage, _hpAfter);
        }
    }

    /// <summary>착탄: 일반 피격과 같은 연출(피격 파티클 + "-N")을 그 발의 몫만큼 재생하고 투사체는 반납.
    ///
    /// HP 표기는 <b>덮어쓴다</b>. hp는 선피해 때 이미 전량 깎여 있어서 PlayHitAnim이 실제 값을 쓰면
    /// 첫 착탄에 총량만큼 한 번에 떨어진다 — 발마다 숫자가 뜨는데 게이지는 안 움직이는 그림이 된다.
    /// PlayHitAnim이 표기를 먼저 세팅하므로 **그 뒤에** 덮어써야 한다.</summary>
    static void Impact(VfxHandle _proj, CardView _target, CardView _source, int _damage, int _hpAfter)
    {
        if (_target != null)
        {
            _target.PlayHitAnim(_damage: _damage, _hitFrom: _source).Forget();
            _target.OverrideHpDisplay(_hpAfter, 0);
        }

        if (_proj.Valid && _proj.Go != null)
        {
            _proj.Go.transform.DOKill();
            _proj.Go.SetActive(false);
        }
        _proj.Release();
    }

    /// <summary>베지어 경로 비행. 트윈(DOPath) 대신 프레임 보간 — 진행 방향으로 매 프레임 눕혀야 하고,
    /// 카드 쪽 DOKill에 조용히 잘리지 않게 하려면 트윈 밖에 있는 편이 안전하다(HealVfx와 같은 이유).</summary>
    static async UniTask Travel(GameObject _go, Vector3 _start, Vector3 _end, int _index,
        float _curveHeight, System.Threading.CancellationToken _ct)
    {
        Vector3 t_ctrl = ControlPoint(_start, _end, _index, _curveHeight);
        float   t_dur  = Mathf.Max(0.05f, GameTiming.Battle.BrandTravelDuration);
        float   t_time = 0f;
        Vector3 t_prev = _start;

        while (t_time < t_dur)
        {
            if (_go == null) return;   // 풀 flush/씬 언로드

            t_time += Time.deltaTime;
            Vector3 t_pos = Bezier(_start, t_ctrl, _end, Mathf.Clamp01(t_time / t_dur));
            Vector3 t_dir = t_pos - t_prev;
            if (t_dir.sqrMagnitude > 1e-6f) _go.transform.right = t_dir.normalized;
            _go.transform.position = t_pos;
            t_prev = t_pos;

            await UniTask.Yield(PlayerLoopTiming.Update, _ct);
        }
    }

    static Vector3 Bezier(Vector3 _a, Vector3 _ctrl, Vector3 _b, float _t)
    {
        float t_inv = 1f - _t;
        return (t_inv * t_inv * _a) + (2f * t_inv * _t * _ctrl) + (_t * _t * _b);
    }

    /// <summary>직선 중점을 화면 수직 방향으로 밀어낸 제어점. 인덱스 패리티로 부호를 번갈아 주면
    /// 여러 발이 같은 궤적으로 겹치지 않고 부채꼴로 갈라진다. **RNG 미사용**(결정론).</summary>
    static Vector3 ControlPoint(Vector3 _start, Vector3 _end, int _index, float _height)
    {
        Vector3 t_line = _end - _start;
        Vector3 t_perp = new Vector3(-t_line.y, t_line.x, 0f);
        t_perp = t_perp.sqrMagnitude > 1e-6f ? t_perp.normalized : Vector3.up;

        float t_sign = (_index & 1) == 1 ? -1f : 1f;
        return ((_start + _end) * 0.5f) + (t_perp * (_height * t_sign));
    }
}
