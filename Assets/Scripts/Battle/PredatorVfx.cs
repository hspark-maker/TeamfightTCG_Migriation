using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>포식 흡수 연출의 **순서**만 소유한다: 피격자 자리에서 표식이 터지고 →
/// 잠깐 뒤 궤적이 피격자에서 출발해 공격자에게 도착 → 여운 뒤 호출부로 제어가 돌아간다.
///
/// 프리팹/형태 = PredatorSynergyVfxConfig(포식자 시너지 연출 에셋), 시간 = BattleTimingConfig,
/// 스폰·반납·정렬 = BattleVfx. 여기엔 어느 것도 두지 않는다(BrandVolleyVfx·HealVfx와 같은 규약).
///
/// **회복 수치는 호출부가 이미 적용한 뒤다 — 여기선 표시만 늦춘다.**
/// 상태 변경을 도착까지 미루면 두 클라의 hp 타임라인이 프레임레이트만큼 갈라져 divergence가 된다
/// (훅 계약: 첫 await 전에 상태변이 완결). RNG도 소비하지 않는다 — 궤적 부호는 고정이다.
///
/// 미배선이면 그 부분만 생략한다. 연출은 선택이고 회복은 이미 들어가 있다.</summary>
public static class PredatorVfx
{
    /// <summary>피격자 <paramref name="_victim"/>에서 공격자 <paramref name="_attacker"/>로 흡수 연출.
    /// 도착 + 여운까지 기다린 뒤 완료된다.
    ///
    /// 피격자는 이 시점에 이미 죽어 사라지는 중일 수 있다 — 자리(위치)만 쓰고 카드 상태는 보지 않는다.</summary>
    public static async UniTask PlayDrain(CardView _victim, CardView _attacker, PredatorSynergyVfxConfig _vfx)
    {
        if (_vfx == null || _attacker == null || _victim == null) return;

        // 죽는 카드가 치워지면 transform이 사라진다 — 출발점은 지금 값을 복사해 둔다.
        Vector3 t_from = _victim.transform.position;
        int t_layer = _victim.VfxSortingLayerId;

        if (_vfx.impact.prefab != null)
        {
            // 스폰이 실패해도 화면엔 "그냥 안 뜸"으로만 보인다 — 풀·프리팹 문제를 로그로 갈라 준다.
            VfxHandle t_impact = BattleVfx.Play(_vfx.impact, t_from, t_layer);
            if (!t_impact.Valid)
                Debug.LogWarning($"[PredatorVfx] impact 스폰 실패 ({_vfx.impact.prefab.name}) — 풀 등록/프리팹을 확인해라.");
        }

        if (_vfx.trail.prefab == null) return;   // 표식만 배선 = 이동 생략

        var t_ct = _attacker.GetCancellationTokenOnDestroy();
        try
        {
            // 무는 표식이 읽힌 **뒤에** 줄기가 나간다. 같이 나가면 "물고 빨아들인다"가 아니라
            // 한 덩어리로 뭉쳐 보인다. 기준은 이 값 하나다 — impact.lifetime(풀 반납 시각)에 묶으면
            // 반납을 앞당기려고 수명을 줄일 때 연출 순서까지 같이 당겨진다.
            float t_lead = GameTiming.Battle.PredatorImpactLead;
            if (t_lead > 0f)
                await UniTask.Delay((int)(t_lead * 1000), cancellationToken: t_ct);

            if (_attacker == null) return;

            // 줄기 여러 개가 시차를 두고 빨려 들어간다. 개수는 저작값이고 궤적은 인덱스에서 파생한다 —
            // 난수를 쓰면 두 클라의 화면이 갈린다(연출이라도 관전·리플레이에서 티가 난다).
            int t_count = Mathf.Max(1, _vfx.trailCount);
            var t_flights = new List<UniTask>(t_count);
            for (int t_i = 0; t_i < t_count; t_i++)
                t_flights.Add(FlyOne(t_from, t_layer, _attacker, t_i, _vfx, t_ct));

            await UniTask.WhenAll(t_flights);

            float t_hold = GameTiming.Battle.PredatorArriveHold;
            if (t_hold > 0f)
                await UniTask.Delay((int)(t_hold * 1000), cancellationToken: t_ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>줄기 한 개. 출발이 어긋나야 개수가 읽히고, 궤적 부호가 갈려야 겹치지 않는다.
    /// 도중에 공격자가 사라지면 조용히 접고 프리팹은 반납한다.</summary>
    static async UniTask FlyOne(Vector3 _from, int _layer, CardView _attacker, int _index,
                                PredatorSynergyVfxConfig _vfx, System.Threading.CancellationToken _ct)
    {
        VfxHandle t_trail = default;
        try
        {
            float t_delay = _index * GameTiming.Battle.PredatorTrailStagger;
            if (t_delay > 0f)
                await UniTask.Delay((int)(t_delay * 1000), cancellationToken: _ct);

            if (_attacker == null) return;

            t_trail = BattleVfx.Spawn(_vfx.trail, _from, _layer);
            if (!t_trail.Valid)
            {
                Debug.LogWarning($"[PredatorVfx] trail 스폰 실패 ({_vfx.trail.prefab.name}) — 풀 등록/프리팹을 확인해라.");
                return;
            }

            // 도착점은 매 프레임 다시 읽는다 — 공격자는 돌진 복귀 중이라 자리가 움직인다.
            await Travel(t_trail.Go, t_trail.Go.transform.position, _attacker, _vfx.curveHeight, _index, _ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 트윈을 쓰지 않으므로 DOKill 대상이 없다 — 끄고 반납만 한다.
            if (t_trail.Valid && t_trail.Go != null) t_trail.Go.SetActive(false);
            t_trail.Release();
        }
    }

    /// <summary>베지어 경로 비행. 트윈(DOPath) 대신 프레임 보간 — 진행 방향으로 매 프레임 눕혀야 하고,
    /// 카드 쪽 DOKill에 조용히 잘리지 않게 하려면 트윈 밖에 있는 편이 안전하다(BrandVolleyVfx와 같은 이유).</summary>
    static async UniTask Travel(GameObject _go, Vector3 _start, CardView _target,
        float _curveHeight, int _index, System.Threading.CancellationToken _ct)
    {
        float   t_dur  = Mathf.Max(0.05f, GameTiming.Battle.PredatorTravelDuration);
        float   t_time = 0f;
        Vector3 t_prev = _start;

        while (t_time < t_dur)
        {
            if (_go == null || _target == null) return;   // 풀 flush / 공격자 파괴

            t_time += Time.deltaTime;
            Vector3 t_end  = _target.transform.position;
            Vector3 t_ctrl = ControlPoint(_start, t_end, _curveHeight, _index);
            Vector3 t_pos  = Bezier(_start, t_ctrl, t_end, Mathf.Clamp01(t_time / t_dur));
            Vector3 t_dir  = t_pos - t_prev;
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

    /// <summary>직선 중점을 화면 수직 방향으로 밀어낸 제어점. 부호는 인덱스 패리티로 번갈아 주고
    /// 바깥 줄기일수록 더 벌린다 — 여러 줄기가 한 궤적에 겹치지 않고 부채꼴로 갈라진다.
    /// **RNG 미사용**(결정론): 같은 인덱스는 항상 같은 궤적이다.</summary>
    static Vector3 ControlPoint(Vector3 _start, Vector3 _end, float _height, int _index)
    {
        Vector3 t_line = _end - _start;
        Vector3 t_perp = new Vector3(-t_line.y, t_line.x, 0f);
        t_perp = t_perp.sqrMagnitude > 1e-6f ? t_perp.normalized : Vector3.up;

        float t_sign = (_index & 1) == 1 ? -1f : 1f;
        float t_lane = 1f + (_index / 2) * 0.45f;   // 0·1 = 안쪽 / 2·3 = 한 칸 바깥 …

        return ((_start + _end) * 0.5f) + (t_perp * (_height * t_sign * t_lane));
    }
}
