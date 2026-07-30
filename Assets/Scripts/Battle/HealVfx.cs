using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 힐러 회복 연출의 **순서**만 소유한다: 힐러 카드 아래 발동 이펙트 → 대상 수만큼 투사체 →
/// 2차 베지어로 휘어 비행 → 도착 시 임팩트 + 카드 회복 연출("+N").
///
/// 프리팹/배치 = BattleVfxLibrary, 시간 = BattleTimingConfig, 스폰·반납·정렬 = BattleVfx.
/// 여기엔 어느 것도 두지 않는다(연출 하나가 자기 설정을 따로 들면 배선 지점이 다시 갈라진다).
///
/// **회복 수치 적용은 호출부가 이미 끝낸 뒤다 — 여기선 표시만 늦춘다.**
/// 상태 변경을 투사체 도착까지 미루면 두 클라의 hp 타임라인이 프레임레이트만큼 갈라져
/// 멀티 divergence가 된다(연출은 비동기, 규칙은 동기 — 기존 규약 그대로).
/// </summary>
public static class HealVfx
{
    const float DEFAULT_CURVE_HEIGHT = 0.8f;   // 라이브러리 미배선 시 형태값 폴백

    /// <summary>_targets = (대상 뷰, 이번에 실제 회복된 양). 투사체가 배선돼 있지 않으면
    /// 즉시 회복 연출만 재생한다 — 연출은 선택, "+N"과 HP 표기는 필수.</summary>
    public static void PlayHealBurst(CardView _source, List<(CardView view, int amount)> _targets)
    {
        if (_targets == null || _targets.Count == 0) return;

        if (_source == null || !BattleVfx.TryGetEntry(BattleVfxId.HealerProjectile, out _))
        {
            foreach ((CardView t_view, int t_amount) in _targets)
                t_view?.PlayHealEffect(t_amount);
            return;
        }

        // 순서: 힐러 카드 아래에서 발동 이펙트가 먼저 터지고 → HealLaunchLead 뒤부터 투사체가 나간다.
        BattleVfx.Play(BattleVfxId.HealerLaunch, _source.BottomCenter, _source.VfxSortingLayerId);

        for (int i = 0; i < _targets.Count; i++)
            FlyOne(_source, _targets[i].view, _targets[i].amount, i).Forget();
    }

    /// <summary>대상 1명분 비행. 대상이 파괴되면(사망/씬 전환) 취소되고 투사체는 반납된다.</summary>
    static async UniTaskVoid FlyOne(CardView _source, CardView _target, int _amount, int _index)
    {
        if (_target == null) return;

        VfxHandle t_proj = default;
        try
        {
            var t_ct = _target.GetCancellationTokenOnDestroy();

            // 발동 이펙트가 먼저 보이도록 lead만큼 늦추고, 그 뒤 대상별로 stagger를 준다
            // (여러 발이 한 프레임에 겹쳐 나가면 한 덩어리로 보인다). 두 값 모두 배속이 적용된 상태.
            float t_delay = GameTiming.Battle.HealLaunchLead + (_index * GameTiming.Battle.HealLaunchStagger);
            if (t_delay > 0f)
                await UniTask.Delay((int)(t_delay * 1000), cancellationToken: t_ct);

            if (_source == null || _target == null) return;

            Vector3 t_start = _source.BottomCenter;
            Vector3 t_end   = _target.transform.position;

            t_proj = BattleVfx.Spawn(BattleVfxId.HealerProjectile, t_start, _source.VfxSortingLayerId);
            if (!t_proj.Valid) return;

            // 실제 시작점은 항목 오프셋이 반영된 스폰 위치 — 커브도 그 점을 기준으로 잡아야 궤적이 안 튄다.
            await Travel(t_proj.Go, t_proj.Go.transform.position, t_end, _index, t_ct);

            // 도착 후 마무리(축소 → 소멸 → 회복 표기)는 FinishAndRelease가 이어받는다.
            // 도착 전용 폭발을 따로 두지 않는다 — 회복이면 어느 경로든 같은 연출이어야 한다(CardView.PlayHealEffect 단일 지점).
        }
        catch (OperationCanceledException) { }
        finally
        {
            FinishAndRelease(t_proj, _target, _amount).Forget();
        }
    }

    /// <summary>도착 마무리. 예전엔 투사체가 도착 지점에 **그대로 멈춘 채** linger만큼 떠 있다가
    /// 한 프레임에 사라져서 툭 끊겼다(이 프리팹엔 PooledParticle이 없어 수명 반납이 즉시 소멸이다).
    ///
    /// 순서: 방출 정지(살아 있는 파티클은 각자 수명대로 페이드아웃) → 카드에 흡수되듯 축소 →
    /// **완전히 사라진 뒤** 카드 회복 연출("+N"). 투사체와 숫자가 겹쳐 두 번 터지는 것처럼 보이지 않게.
    /// 순수 연출 — 회복 수치는 이미 적용된 뒤다.</summary>
    static async UniTaskVoid FinishAndRelease(VfxHandle _proj, CardView _target, int _amount)
    {
        // 투사체가 없거나 이미 죽었으면 표기만이라도 즉시 — "+N"과 HP 갱신은 필수다.
        if (!_proj.Valid)
        {
            if (_target != null) _target.PlayHealEffect(_amount);
            _proj.Release();
            return;
        }

        GameObject t_go    = _proj.Go;
        Vector3    t_scale = t_go.transform.localScale;   // 풀 재사용분이 줄어든 스케일로 나오지 않게 원복용

        foreach (ParticleSystem t_ps in t_go.GetComponentsInChildren<ParticleSystem>(true))
            t_ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);

        float t_linger = Mathf.Max(0.05f, GameTiming.Battle.HealTrailLinger);
        await t_go.transform.DOScale(Vector3.zero, t_linger)
                            .SetEase(Ease.InQuad)
                            .SetLink(t_go)
                            .ToUniTask()
                            .SuppressCancellationThrow();

        // **비활성 → 스케일 원복 → 반납** 순서가 중요하다. Release는 내부적으로 잠깐(최소 0.05s) 뒤에
        // 풀로 돌려보내므로, 원복을 먼저 하면 원래 크기로 돌아온 투사체가 그 사이 한 프레임 번쩍인다.
        if (t_go != null)
        {
            t_go.transform.DOKill();
            t_go.SetActive(false);
            t_go.transform.localScale = t_scale;
        }
        _proj.Release();

        if (_target != null) _target.PlayHealEffect(_amount);
    }

    /// <summary>베지어 경로 비행. 트윈(DOPath) 대신 프레임 보간 — 진행 방향으로 매 프레임 눕혀야 하고,
    /// 카드 쪽 DOKill에 조용히 잘리지 않게 하려면 트윈 밖에 있는 편이 안전하다.</summary>
    static async UniTask Travel(GameObject _go, Vector3 _start, Vector3 _end, int _index,
        System.Threading.CancellationToken _ct)
    {
        Vector3 t_ctrl = ControlPoint(_start, _end, _index);
        float   t_dur  = Mathf.Max(0.05f, GameTiming.Battle.HealTravelDuration);
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

    /// <summary>직선 중점을 화면 수직 방향으로 밀어낸 제어점. 대상마다 부호를 번갈아 주면
    /// 여러 발이 같은 궤적으로 겹치지 않고 부채꼴로 갈라진다.</summary>
    static Vector3 ControlPoint(Vector3 _start, Vector3 _end, int _index)
    {
        BattleVfxLibrary t_lib = BattleVfx.Library;
        float t_height    = t_lib != null ? t_lib.healCurveHeight : DEFAULT_CURVE_HEIGHT;
        bool  t_alternate = t_lib == null || t_lib.healAlternateCurve;

        Vector3 t_line = _end - _start;
        Vector3 t_perp = new Vector3(-t_line.y, t_line.x, 0f);
        t_perp = t_perp.sqrMagnitude > 1e-6f ? t_perp.normalized : Vector3.up;

        float t_sign = (t_alternate && (_index & 1) == 1) ? -1f : 1f;
        return ((_start + _end) * 0.5f) + (t_perp * (t_height * t_sign));
    }
}
