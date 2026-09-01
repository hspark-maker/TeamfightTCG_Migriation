using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>유산(Legacy) 왕관 연출의 **개수와 순서**만 소유한다.
/// 턴이 시작될 때 지금까지 쌓인 스택 수만큼 왕관을 띄웠다 거두고(<see cref="Show"/>),
/// 그 카드가 죽는 순간 같은 수의 왕관을 회복받을 아군에게 날려 보낸다(<see cref="Fly"/>).
///
/// 프리팹/형태 = <see cref="LegacySynergyVfxConfig"/>, 스폰·정렬·풀 반납 = <see cref="BattleVfx"/>.
/// 여기엔 어느 것도 두지 않는다(BrandVolleyVfx·HealVfx와 같은 규약).
///
/// <b>순수 연출이다 — 상태/RNG 무접촉.</b> 개수의 진실원은 <c>CardInstance.legacyStack</c> 하나이고
/// 여기서는 그 값을 매번 읽어 그린다. 그림 쪽에 스택을 따로 들고 있지 않기 때문에
/// (표시 상한을 넘든, 연출이 중간에 잘리든) 둘이 어긋날 수가 없다.
///
/// ⚠ 왕관은 카드 자식이 아니라 월드 오브젝트다(엠블럼과 같은 이유 — 카드 쪽 DOKill/FadeView가
///   연출을 잘라먹지 않게). 대신 스스로 사라지지 않으므로 수명은 전부 여기서 건다:
///   등장분은 showDuration 뒤, 비행분은 도착 뒤, 그리고 전투 종료엔 <see cref="Clear"/>가 남은 것을 쓸어담는다.</summary>
public static class LegacyCrownVfx
{
    // 지금 화면에 떠 있는 왕관 전부(등장분 + 비행분). 각자 제 수명에 반납하지만,
    // 전투가 도중에 끝나면(항복·씬 전환) 그 타이머는 영영 안 온다 — 그때 쓸어담을 목록이다.
    static readonly List<VfxHandle> s_live = new List<VfxHandle>();

    // 파티클 재생 판정용 재사용 버퍼(할당 0). 연출 스폰·정리는 전부 메인 스레드 한 프레임 안이라 하나로 충분하다.
    static readonly List<ParticleSystem> s_psBuffer = new List<ParticleSystem>();
    static readonly HashSet<ParticleSystem> s_subEmitters = new HashSet<ParticleSystem>();

    // Clear가 순회용으로 쓰는 임시 버퍼(할당 0). 연출 스폰·정리는 전부 메인 스레드라 하나로 충분하다.
    static readonly List<VfxHandle> s_retiring = new List<VfxHandle>();

    /// <summary>턴 시작: 지금 스택 수만큼 왕관을 띄웠다 거둔다. 스택 0이거나 배선이 없으면 무동작.
    /// 기다리지 않는다 — 턴 진행을 붙잡으면 스택이 쌓일수록 턴 시작이 느려진다.</summary>
    public static void Show(CardInstance _card, SynergyData _synergy)
        => Show(_card, _synergy, -1);

    /// <summary>게임 상태를 바꾸지 않고 명시한 개수의 왕관을 재생하는 미리보기 진입점.</summary>
    public static void Show(CardInstance _card, SynergyData _synergy, int _visibleCount)
    {
        LegacySynergyVfxConfig t_cfg = ConfigOf(_synergy);
        if (t_cfg == null || t_cfg.crown.prefab == null || _card == null) return;

        int t_count = _visibleCount >= 0 ? VisibleCount(_visibleCount, t_cfg) : VisibleCount(_card, t_cfg);
        if (t_count <= 0) return;

        CardView t_view = CardView.GetView(_card);
        if (t_view == null) return;

        float t_scale = CountScale(t_count, t_cfg);
        for (int t_i = 0; t_i < t_count; t_i++)
            ShowOne(t_view, t_i, t_count, t_scale, t_cfg).Forget();
    }

    /// <summary>파괴: 쌓인 수만큼 왕관을 띄워 회복받은 아군에게 날려 보낸다.
    /// 대상이 왕관보다 적으면 돌려 쓴다 — "몇 명이 회복됐나"가 아니라 "스택이 몇이었나"를 보여주는 연출이다.
    ///
    /// <paramref name="_healAmount"/>는 이미 적용된 회복량이다(수치는 규칙이 확정했고 여기선 표기만 낸다).
    /// 대상마다 **한 번씩만** 낸다 — 왕관이 대상보다 많으면 첫 도착분이 표기를 맡고 나머지는 그림만 얹는다.
    /// 반대로 왕관이 모자라 왕관을 못 받는 대상은 같은 시각에 따로 표기를 내준다:
    /// 유예된 숫자(DeferHpDisplay)를 아무도 안 풀면 그 카드의 체력 표기가 영영 안 오른다.</summary>
    public static void Fly(CardInstance _from, IReadOnlyList<CardInstance> _targets, int _healAmount,
                           SynergyData _synergy)
        => Fly(_from, _targets, _healAmount, _synergy, -1);

    /// <summary>게임 상태를 바꾸지 않고 명시한 개수의 왕관 비행을 재생하는 미리보기 진입점.</summary>
    public static void Fly(CardInstance _from, IReadOnlyList<CardInstance> _targets, int _healAmount,
                           SynergyData _synergy, int _visibleCount)
    {
        LegacySynergyVfxConfig t_cfg = ConfigOf(_synergy);
        if (t_cfg == null || t_cfg.crown.prefab == null || _from == null) return;
        if (_targets == null || _targets.Count == 0) return;

        int t_count = _visibleCount >= 0 ? VisibleCount(_visibleCount, t_cfg) : VisibleCount(_from, t_cfg);
        if (t_count <= 0) return;

        CardView t_view = CardView.GetView(_from);
        if (t_view == null) return;

        // 죽는 카드의 뷰는 곧 사망 연출로 사라진다 — 출발 자리를 지금 값으로 굳혀 두지 않으면
        // 비행 시작 시점에 뷰가 없어 왕관이 원점(0,0)에서 출발한다.
        float   t_scale = CountScale(t_count, t_cfg);
        Vector3 t_from  = SlotAnchor(t_view, t_cfg);
        int     t_layer = t_view.VfxSortingLayerId;

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            // 대상 목록을 한 바퀴 도는 동안(첫 t_targets.Count개)만 표기를 맡는다.
            bool t_carriesHeal = t_i < _targets.Count;
            FlyOne(t_from, t_layer, _targets[t_i % _targets.Count], t_i, t_count, t_scale,
                   t_carriesHeal ? _healAmount : 0, t_cfg).Forget();
        }

        // 왕관보다 대상이 많으면(스택 < 아군 수) 남은 대상은 왕관 없이 같은 박자에 표기만 낸다.
        for (int t_i = t_count; t_i < _targets.Count; t_i++)
            HealLater(_targets[t_i], _healAmount, _cfg: t_cfg, _index: t_i).Forget();
    }

    /// <summary>전투 종료 정리. 풀 자체는 BattleCleanup이 flush하지만, 여기서 참조를 놓지 않으면
    /// 다음 판 첫 프레임에 지난 판의 왕관이 반납 타이머와 함께 남아 있다.</summary>
    public static void Clear()
    {
        // 목록을 먼저 떼어 낸다 — Retire가 s_live에서 자기를 빼므로 그대로 순회하면 한 칸씩 건너뛴다.
        s_retiring.Clear();
        s_retiring.AddRange(s_live);
        s_live.Clear();

        for (int t_i = 0; t_i < s_retiring.Count; t_i++) ForceRetire(s_retiring[t_i]);
        s_retiring.Clear();
    }

    // ── 내부 ──────────────────────────────────────────────────────────────

    static LegacySynergyVfxConfig ConfigOf(SynergyData _synergy) => _synergy?.vfx as LegacySynergyVfxConfig;

    /// <summary>이번에 띄울 개수. 진실원은 legacyStack이고 표시 상한만 여기서 건다 —
    /// 상한을 넘겨도 규칙(회복량)은 스택 그대로다.</summary>
    static int VisibleCount(CardInstance _card, LegacySynergyVfxConfig _cfg)
    {
        return VisibleCount(Mathf.Max(0, _card.legacyStack), _cfg);
    }

    static int VisibleCount(int _count, LegacySynergyVfxConfig _cfg)
        => _cfg.maxVisible > 0 ? Mathf.Min(Mathf.Max(0, _count), _cfg.maxVisible) : Mathf.Max(0, _count);

    /// <summary>개수에 따른 축소 배율. 한 개 늘 때마다 falloff를 곱하고 하한에서 멈춘다 —
    /// 크기가 개수를 대신 말해 주므로 왕관이 늘수록 줄 전체가 조밀해진다.</summary>
    static float CountScale(int _count, LegacySynergyVfxConfig _cfg)
        => Mathf.Max(_cfg.minCountScale, Mathf.Pow(_cfg.countScaleFalloff, Mathf.Max(0, _count - 1)));

    /// <summary>줄의 기준점(슬롯 중심 + firstOffset).</summary>
    static Vector3 SlotAnchor(CardView _view, LegacySynergyVfxConfig _cfg)
        => new Vector3(_view.SlotPosition.x + _cfg.firstOffset.x,
                       _view.SlotPosition.y + _cfg.firstOffset.y,
                       _view.SlotPosition.z);

    /// <summary>가운데 정렬된 i번째 자리.
    ///
    /// 간격이 축소 배율을 **얼마나** 따라갈지는 배선값(spacingFollowsScale)이 정한다.
    /// 완전히 따라가면(1) 왕관이 작아질수록 줄이 빽빽해져 개수가 안 읽히고,
    /// 전혀 안 따라가면(0) 작은 왕관들이 넓은 자리에 띄엄띄엄 뜬다 — 그 사이를 여기서 섞는다.</summary>
    static Vector3 SlotFor(Vector3 _anchor, int _index, int _count, float _scale, LegacySynergyVfxConfig _cfg)
    {
        float t_mid     = (_count - 1) * 0.5f;
        float t_spacing = Mathf.Lerp(1f, _scale, _cfg.spacingFollowsScale);
        float t_off     = (_index - t_mid) * t_spacing;

        // 호(弧): 줄 안에서의 위치를 -1~1로 정규화해 포물선으로 올린다(가운데가 가장 높다).
        // 왕관이 하나면 줄이 없으므로 호도 없다 — 그러지 않으면 1개일 때만 혼자 위로 떠오른다.
        float t_arc = 0f;
        if (_count > 1 && !Mathf.Approximately(_cfg.arcHeight, 0f))
        {
            float t_norm = (_index - t_mid) / t_mid;   // 양 끝 ±1, 가운데 0
            t_arc = _cfg.arcHeight * (1f - t_norm * t_norm) * t_spacing;
        }

        return _anchor + new Vector3(_cfg.step.x * t_off,
                                     _cfg.step.y * t_off + t_arc, 0f);
    }

    /// <summary>왕관 하나를 띄웠다 거둔다. 등장은 stagger로 하나씩 톡톡 — 동시에 뜨면 개수가 안 세진다.</summary>
    static async UniTaskVoid ShowOne(CardView _view, int _index, int _count, float _scale,
                                     LegacySynergyVfxConfig _cfg)
    {
        float t_delay = _index * _cfg.showStagger;
        if (t_delay > 0f) await UniTask.Delay((int)(GameTiming.Battle.Scaled(t_delay) * 1000));

        // 기다리는 사이 카드가 죽거나 슬롯을 비웠으면 그냥 접는다.
        if (_view == null) return;

        VfxHandle t_handle = Spawn(SlotFor(SlotAnchor(_view, _cfg), _index, _count, _scale, _cfg),
                                   _view.VfxSortingLayerId, _scale, _cfg);
        if (!t_handle.Valid) return;

        Pop(t_handle.Go, _cfg);

        await UniTask.Delay((int)(GameTiming.Battle.Scaled(_cfg.showDuration) * 1000));
        await FadeOut(t_handle, _cfg);
    }

    /// <summary>왕관 하나가 대상에게 날아간다. 궤적 프리팹이 배선돼 있으면 왕관에 붙여 따라 보낸다.
    /// 어디서 끊기든 왕관은 반드시 반납한다(finally).</summary>
    static async UniTaskVoid FlyOne(Vector3 _anchor, int _sortingLayerId, CardInstance _target,
                                    int _index, int _count, float _scale, int _healAmount,
                                    LegacySynergyVfxConfig _cfg)
    {
        VfxHandle t_crown = default;
        VfxHandle t_trail = default;
        try
        {
            float t_delay = _index * _cfg.flyStagger;
            if (t_delay > 0f) await UniTask.Delay((int)(GameTiming.Battle.Scaled(t_delay) * 1000));

            CardView t_view = CardView.GetView(_target);
            if (t_view == null) return;

            Vector3 t_start = SlotFor(_anchor, _index, _count, _scale, _cfg);
            t_crown = Spawn(t_start, _sortingLayerId, _scale, _cfg);
            if (!t_crown.Valid) return;

            // 궤적은 왕관 자식으로 붙여 같이 움직인다 — 따로 날리면 둘의 속도가 갈라져 꼬리가 떨어진다.
            if (_cfg.trail.prefab != null)
            {
                t_trail = BattleVfx.Spawn(_cfg.trail, t_start, _sortingLayerId);
                if (t_trail.Valid && t_trail.Go != null)
                {
                    s_live.Add(t_trail);
                    PlayAll(t_trail.Go);
                    t_trail.Go.transform.SetParent(t_crown.Go.transform, worldPositionStays: true);
                    t_trail.Go.transform.localPosition = Vector3.zero;
                }
            }

            await Travel(t_crown.Go, t_start, t_view.SlotPosition, _index, _cfg);

            // 도착 = 회복 표기의 발화점. 유예해 둔 숫자를 여기서 푼다(힐러 투사체와 같은 규약).
            if (_healAmount > 0) t_view.PlayHealEffect(_healAmount, _consumeDeferred: true);

            if (_cfg.arriveHold > 0f)
                await UniTask.Delay((int)(GameTiming.Battle.Scaled(_cfg.arriveHold) * 1000));
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 부모(왕관)가 먼저 반납되면 궤적이 풀 밖에서 미아가 된다 — 떼어 낸 뒤 반납한다.
            if (t_trail.Valid && t_trail.Go != null)
                t_trail.Go.transform.SetParent(null, worldPositionStays: true);
            Retire(t_trail);
            FadeOut(t_crown, _cfg).Forget();
        }
    }

    /// <summary>왕관을 못 받은 대상의 회복 표기. 비행분과 같은 시각에 풀어야 "같이 회복됐다"로 읽힌다.</summary>
    static async UniTaskVoid HealLater(CardInstance _target, int _healAmount,
                                       LegacySynergyVfxConfig _cfg, int _index)
    {
        if (_healAmount <= 0) return;

        float t_wait = _index * _cfg.flyStagger + _cfg.flyDuration;
        await UniTask.Delay((int)(GameTiming.Battle.Scaled(t_wait) * 1000));

        CardView.GetView(_target)?.PlayHealEffect(_healAmount, _consumeDeferred: true);
    }

    /// <summary>왕관 한 개 스폰. 배율은 항목(crown.scale)에 개수 축소를 곱해 **프리팹 저작 크기 기준**으로
    /// 매번 다시 찍는다 — 풀 재사용분에 지난 배율이 누적되지 않게(BattleVfx.ApplyScale과 같은 규칙).</summary>
    static VfxHandle Spawn(Vector3 _pos, int _sortingLayerId, float _scale, LegacySynergyVfxConfig _cfg)
    {
        VfxHandle t_handle = BattleVfx.Spawn(_cfg.crown, _pos, _sortingLayerId);
        if (!t_handle.Valid || t_handle.Go == null) return t_handle;

        float t_entryScale = _cfg.crown.scale > 0f ? _cfg.crown.scale : 1f;
        t_handle.Go.transform.localScale = _cfg.crown.prefab.transform.localScale * (t_entryScale * _scale);

        PlayAll(t_handle.Go);

        s_live.Add(t_handle);
        return t_handle;
    }

    /// <summary>빌려온 인스턴스의 파티클을 **직접 재생**한다.
    ///
    /// ⚠ SetActive(true)만으로 도는 것은 playOnAwake가 켜진 시스템뿐이다. 왕관 프리팹은 5개 중 1개만,
    ///   궤적은 하나도 켜져 있지 않아서 그냥 스폰하면 아무것도 안 보인다
    ///   (덩치·비늘 프리팹은 전부 켜져 있어 이 문제가 안 드러난다).
    ///   풀에서 재사용된 인스턴스는 지난번 입자가 남아 있을 수 있어 Clear를 먼저 건다.
    ///
    /// 서브이미터는 부모가 뿜는 것이라 직접 재생하면 안 된다 — 미리 걷어 내고 나머지만 돌린다.</summary>
    static void PlayAll(GameObject _go)
    {
        if (_go == null) return;

        _go.GetComponentsInChildren(true, s_psBuffer);
        if (s_psBuffer.Count == 0) return;

        s_subEmitters.Clear();
        for (int t_i = 0; t_i < s_psBuffer.Count; t_i++)
        {
            ParticleSystem.SubEmittersModule t_sub = s_psBuffer[t_i].subEmitters;
            if (!t_sub.enabled) continue;
            for (int t_j = 0; t_j < t_sub.subEmittersCount; t_j++)
            {
                ParticleSystem t_child = t_sub.GetSubEmitterSystem(t_j);
                if (t_child != null) s_subEmitters.Add(t_child);
            }
        }

        for (int t_i = 0; t_i < s_psBuffer.Count; t_i++)
        {
            ParticleSystem t_ps = s_psBuffer[t_i];
            if (s_subEmitters.Contains(t_ps)) continue;

            t_ps.Clear(withChildren: false);
            t_ps.Play(withChildren: false);
        }
    }

    /// <summary>방출만 멈추고 **이미 떠 있는 입자가 다 꺼질 때까지** 기다렸다 반납한다.
    ///
    /// 이게 없으면 SetActive(false)가 살아 있는 입자를 통째로 지운다 — 왕관 파티클은 뿌려진 알갱이가
    /// 모여 형태를 이루는 구성이라, 유지시간이 끝나는 순간 "사라지는" 게 아니라 "없던 일이 된다".
    /// (증상: 왕관이 다 나오기도 전에 툭 사라진다.)
    ///
    /// 대기 상한을 두는 이유 — 파티클 하나가 수명을 길게 잡아 두면 그 왕관만 영영 안 돌아온다.
    /// 상한을 넘기면 그냥 접는다(그때는 이미 거의 안 보인다).</summary>
    static async UniTask FadeOut(VfxHandle _handle, LegacySynergyVfxConfig _cfg)
    {
        if (!_handle.Valid || _handle.Go == null) { Retire(_handle); return; }
        if (!s_live.Contains(_handle)) return;   // 전투 종료 정리가 이미 걷어갔다

        foreach (ParticleSystem t_ps in _handle.Go.GetComponentsInChildren<ParticleSystem>(true))
            t_ps.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmitting);

        float t_max     = GameTiming.Battle.Scaled(Mathf.Max(0f, _cfg.fadeOutMaxWait));
        float t_waited  = 0f;
        while (t_waited < t_max)
        {
            if (_handle.Go == null) return;   // 풀 flush / 씬 언로드
            if (!_handle.Go.activeInHierarchy) break;

            bool t_alive = false;
            foreach (ParticleSystem t_ps in _handle.Go.GetComponentsInChildren<ParticleSystem>(true))
                if (t_ps.IsAlive(withChildren: false)) { t_alive = true; break; }
            if (!t_alive) break;

            t_waited += Time.deltaTime;
            await UniTask.Yield();
        }

        Retire(_handle);
    }

    /// <summary>왕관 한 개 반납. **살아 있는 목록에서 실제로 뺀 쪽만** 풀에 돌린다 —
    /// 수명 타이머와 전투 종료 정리(Clear)가 같은 오브젝트를 두고 경합하므로, 이 소유권 검사가 없으면
    /// 같은 오브젝트가 풀에 두 번 들어가 다음 스폰 두 곳이 같은 것을 잡는다.</summary>
    static void Retire(VfxHandle _handle)
    {
        if (!_handle.Valid || !s_live.Remove(_handle)) return;
        ForceRetire(_handle);
    }

    /// <summary>소유권 검사 없이 즉시 반납(Clear 전용 — 목록은 이미 비운 뒤다).</summary>
    static void ForceRetire(VfxHandle _handle)
    {
        if (!_handle.Valid) return;

        if (_handle.Go != null)
        {
            _handle.Go.transform.DOKill();
            _handle.Go.SetActive(false);
        }
        _handle.Release();
    }

    static void Pop(GameObject _go, LegacySynergyVfxConfig _cfg)
    {
        if (_go == null || _cfg.popScale <= 1f) return;

        Transform t_tr = _go.transform;
        t_tr.DOPunchScale(t_tr.localScale * (_cfg.popScale - 1f),
                          GameTiming.Battle.Scaled(_cfg.popDuration), vibrato: 1, elasticity: 0.6f)
            .SetLink(_go);
    }

    /// <summary>베지어 경로 비행. 트윈이 아니라 프레임 보간인 이유는 BrandVolleyVfx.Travel과 같다 —
    /// 카드 쪽 DOKill에 조용히 잘리지 않게 트윈 밖에 둔다.</summary>
    static async UniTask Travel(GameObject _go, Vector3 _start, Vector3 _end, int _index,
                                LegacySynergyVfxConfig _cfg)
    {
        // 제어점을 직선에서 밀어내는 방향은 인덱스 패리티로 가른다 — 난수를 쓰면 같은 판이 매번 달라진다.
        Vector3 t_mid  = (_start + _end) * 0.5f;
        Vector3 t_dir  = (_end - _start).normalized;
        Vector3 t_side = new Vector3(-t_dir.y, t_dir.x, 0f) * (_index % 2 == 0 ? 1f : -1f);
        Vector3 t_ctrl = t_mid + t_side * _cfg.curveHeight;

        float t_dur  = Mathf.Max(0.05f, GameTiming.Battle.Scaled(_cfg.flyDuration));
        float t_time = 0f;

        while (t_time < t_dur)
        {
            if (_go == null) return;   // 풀 flush / 씬 언로드

            t_time += Time.deltaTime;
            _go.transform.position = Bezier(_start, t_ctrl, _end, Mathf.Clamp01(t_time / t_dur));
            await UniTask.Yield();
        }
    }

    static Vector3 Bezier(Vector3 _a, Vector3 _c, Vector3 _b, float _t)
    {
        float t_inv = 1f - _t;
        return t_inv * t_inv * _a + 2f * t_inv * _t * _c + _t * _t * _b;
    }
}
