using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public static class AttackSequence
{
    // ── 박치기(일반) 연출 튜닝 ──
    // 값의 진실원은 BattleTimingConfig(SO) → 인게임 설정 지점이 여기 하나다.
    // 테스트 씬(AttackAnimTester)만 런타임에 덮어써 슬라이더로 굴린다.
    public struct NormalTuning
    {
        public float windDur;     // 윈드업(뒤로 살짝) 시간.
        public float windDist;    // 윈드업 거리(적 반대 방향).
        public float inDur;       // 돌진(접촉까지) 시간.
        public float recoilDur;   // 접촉 후 뒤로 반동 시간.
        public float recoilDist;  // 반동 거리(슬롯 뒤).
        public float outDur;      // 반동 → 슬롯 복귀 시간.
        public float lungeT;      // 방어자까지 이동 비율(1=완전겹침).
        public float maxLean;     // 적 방향 최대 lean 각(도).

    }

    static NormalTuning? s_normalOverride;   // 테스터 런타임 조정분. null이면 SO 값을 쓴다.

    /// <summary>이번 공격에 쓸 튜닝. 기본은 BattleTimingConfig(배속 반영된 값),
    /// 테스터가 대입하면 그 값이 우선한다. ClearNormalOverride로 SO 값으로 되돌린다.</summary>
    public static NormalTuning Normal
    {
        get => s_normalOverride ?? GameTiming.Battle.NormalAttack;
        set => s_normalOverride = value;
    }

    public static void ClearNormalOverride() => s_normalOverride = null;

    public static UniTask PlaySingle(CardView _attacker, CardView _defender,
        AttackEffect _effect, Action _onEffect = null,
        CardKeyword _preEffectKw = CardKeyword.None,
        CardKeyword _atEffectKw  = CardKeyword.None,
        Func<UniTask> _afterHit = null,
        bool? _forceSpecial = null)
        => PlayCore(_attacker, _defender, _effect, _onEffect, _preEffectKw, _atEffectKw, null, _afterHit, _forceSpecial);

    public static UniTask PlaySplash(CardView _attacker, CardView _defender,
        AttackEffect _effect, Action _onEffect = null, CardView _splashView = null,
        CardKeyword _preEffectKw = CardKeyword.None,
        CardKeyword _atEffectKw  = CardKeyword.None,
        Func<UniTask> _afterHit = null)
        => PlayCore(_attacker, _defender, _effect, _onEffect, _preEffectKw, _atEffectKw, _splashView, _afterHit);

    /// <summary>splashView 유무로 splash/single 자동 선택. 호출부의 if/else 제거용.
    /// _afterHit: 히트/사망 연출 완료 후·제자리 복귀 직전에 실행되는 공격후 효과 콜백.</summary>
    public static UniTask Play(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect,
        CardKeyword _preEffectKw, CardKeyword _atEffectKw,
        Func<UniTask> _afterHit = null)
        => PlayCore(_attacker, _defender, _effect, _onEffect, _preEffectKw, _atEffectKw, _splashView, _afterHit);

    /// <summary>공격력이 이 값 이상이면 특별(시네마) 연출. 표시 공격력과 동일한 CardInstance.AttackDamage() 기준.</summary>
    public const int CINEMA_ATTACK_THRESHOLD = 6;

    /// <summary>시네마 연출 대상인가. 공격자의 실제 공격력(도발 반감·시너지·흐름 포함) ≥ 임계값.
    /// 히트 해결 전에 평가하므로 카드 UI에 표시 중인 값과 일치. 순수 판정 — RNG 미소비, 양 클라 동일 입력 → 동일 결과.</summary>
    static bool IsCinemaAttack(CardView _attacker)
        => (_attacker?.BoundCard?.AttackDamage() ?? 0) >= CINEMA_ATTACK_THRESHOLD;

    /// <summary>연출 디스패치. 두 프레젠테이션 공유 히트해결(ResolveHits)로 데미지/사망 타이밍 일치.
    /// - 일반(PlayNormal): 자기 위치에서 적 방향으로 각도 틀고 박치기.
    /// - 특별(PlayCinema): 둘만 앞으로 떠서 카메라 시네마 1vs1. 공격력 CINEMA_ATTACK_THRESHOLD 이상일 때.</summary>
    static UniTask PlayCore(CardView _attacker, CardView _defender,
        AttackEffect _effect, Action _onEffect,
        CardKeyword _preEffectKw, CardKeyword _atEffectKw, CardView _splashView, Func<UniTask> _afterHit,
        bool? _forceSpecial = null)
    {
        // 테스트/특수 호출이 강제하면 그 값, 아니면 공격력 기준 판정.
        bool t_special = _forceSpecial ?? IsCinemaAttack(_attacker);
        if (t_special)
            return PlayCinema(_attacker, _defender, _effect, _onEffect, _preEffectKw, _atEffectKw, _splashView, _afterHit);

        // 원거리(Ranged)는 붙지 않는다 — 제자리에서 투사체를 쏘고, 투사체가 닿는 시점에 히트.
        // 판정은 런타임 키워드 포함(HasKeyword) — 시너지/패시브가 준 원거리도 같은 연출이어야 한다.
        return IsRangedAttack(_attacker)
            ? PlayRanged(_attacker, _defender, _splashView, _effect, _onEffect, _preEffectKw, _atEffectKw, _afterHit)
            : PlayNormal(_attacker, _defender, _splashView, _effect, _onEffect, _preEffectKw, _atEffectKw, _afterHit);
    }

    /// <summary>이 공격이 원거리 발사 연출인가. 공격자 없음(환경 피해)이면 false → 기존 경로.</summary>
    static bool IsRangedAttack(CardView _attacker)
        => _attacker?.BoundCard != null && _attacker.BoundCard.HasKeyword(CardKeyword.Ranged);

    // ── 일반 연출: 박치기 ─────────────────────────────────────────────────
    // 나머지 암전, 공격자가 제자리에서 적 방향으로 기울며 돌진 → 접촉(히트) → 튕겨 복귀.
    static async UniTask PlayNormal(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect, CardKeyword _preEffectKw, CardKeyword _atEffectKw, Func<UniTask> _afterHit)
    {
        float t_hitDelay = _effect?.hitDelay ?? 0f;

        CardView.FadeAll(0.3f);
        if (_splashView != null) CardView.FadeCards(1f, _attacker, _defender, _splashView);
        else                     CardView.FadeCards(1f, _attacker, _defender);

        bool t_flip = _attacker?.BoundCard?.ownerIndex != TurnState.LocalOwnerIndex;
        _attacker?.PlayAttackAnim();
        SoundManager.Instance?.PlayRandom(_effect?.attackClips);
        SoundManager.Instance?.PlayAttackVoice(_attacker?.BoundCard?.data?.attackVoices);
        _effect?.SpawnParticles(_attacker?.transform, _defender.transform, t_flip);

        if (_preEffectKw != CardKeyword.None)
            await (_attacker?.PlayKeywordGlow(_preEffectKw) ?? UniTask.CompletedTask);

        // 공격자 없음(환경 피해 등): 이동/회전 없이 히트만.
        if (_attacker == null)
        {
            await ResolveHits(null, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit, _skipRemain: true);
            CardView.RestoreAllFades();
            return;
        }

        await Headbutt(_attacker, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit,
            _home: _attacker.SlotPosition);

        CardView.RestoreAllFades();
    }

    // ── 원거리 연출: 제자리 발사 ─────────────────────────────────────────
    /// <summary>원거리 공격. 공격자는 슬롯에 남아 반동(뒤로 살짝 → 복귀)만 하고, 투사체가 대신 날아간다.
    /// 히트 시점은 박치기와 같은 기준인 `hitDelay` — 그래야 투사체 도착과 데미지·피격 연출이 맞는다
    /// (LaunchProjectile의 비행 시간도 hitDelay - spawnDelay로 잡혀 있다).
    ///
    /// 데미지 적용 지점은 ResolveHits 하나로 박치기와 공유한다 — 연출이 갈라져도 규칙 타임라인은 같다.
    /// 투사체 프리팹이 미배선이면 발사만 없고 나머지는 동일하게 흐른다(무동작 안전).</summary>
    static async UniTask PlayRanged(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect, CardKeyword _preEffectKw, CardKeyword _atEffectKw, Func<UniTask> _afterHit)
    {
        float t_hitDelay = _effect?.hitDelay ?? 0f;

        CardView.FadeAll(0.3f);
        if (_splashView != null) CardView.FadeCards(1f, _attacker, _defender, _splashView);
        else                     CardView.FadeCards(1f, _attacker, _defender);

        bool t_flip = _attacker?.BoundCard?.ownerIndex != TurnState.LocalOwnerIndex;
        _attacker?.PlayAttackAnim();
        SoundManager.Instance?.PlayRandom(_effect?.attackClips);
        SoundManager.Instance?.PlayAttackVoice(_attacker?.BoundCard?.data?.attackVoices);
        _effect?.SpawnParticles(_attacker?.transform, _defender.transform, t_flip);
        LaunchProjectile(_effect?.projectile ?? default, _attacker?.transform, _defender.transform, t_hitDelay, t_flip).Forget();

        if (_preEffectKw != CardKeyword.None)
            await (_attacker?.PlayKeywordGlow(_preEffectKw) ?? UniTask.CompletedTask);

        // 발사 반동: 적 반대 방향으로 살짝 밀렸다 제자리로. 거리·시간은 박치기와 같은 SO 값을 재사용한다
        // (원거리 전용 값을 새로 만들면 배속·튜닝 지점이 또 갈라진다).
        UniTask t_kick = RecoilInPlace(_attacker, _defender);

        if (t_hitDelay > 0f)
            await UniTask.Delay((int)(t_hitDelay * 1000));   // 투사체 비행 시간

        await UniTask.WhenAll(
            ResolveHits(_attacker, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit, _skipRemain: true),
            t_kick);

        _attacker?.SetArmedVfx(false);   // 박치기의 반동 지점과 같은 의미 — 발사가 끝나면 무장 해제
        CardView.RestoreAllFades();
    }

    /// <summary>제자리 발사 반동. 이동하지 않고 슬롯에서 살짝 밀렸다 돌아온다(각도 변화 없음).</summary>
    static async UniTask RecoilInPlace(CardView _attacker, CardView _defender)
    {
        if (_attacker == null) return;

        NormalTuning t_cfg = Normal;
        Transform t_atk  = _attacker.transform;
        Vector3   t_home = t_atk.position;

        Vector3 t_dir  = _defender.transform.position - t_home;
        Vector3 t_dirN = t_dir.sqrMagnitude > 0.0001f ? t_dir.normalized : Vector3.up;

        Vector3 t_back = t_home - t_dirN * t_cfg.windDist;
        t_back.z = t_home.z;

        t_atk.DOKill();
        await DOTween.Sequence().SetLink(_attacker.gameObject)
            .Append(t_atk.DOMove(t_back, t_cfg.windDur).SetEase(Ease.OutQuad))
            .Append(t_atk.DOMove(t_home, t_cfg.outDur).SetEase(Ease.OutBack))
            .ToUniTask();
    }

    // ── 공유: 박치기 모션 ────────────────────────────────────────────────
    /// <summary>윈드업(뒤로 살짝) → 돌진(각도 틀며 접촉=히트) → 반동 → _home 복귀.
    /// 히트/사망 해결(ResolveHits)과 반동/복귀는 병렬 — 데미지는 접촉 시점에 적용.
    /// 일반 연출은 _home=원래 슬롯, 시네마 연출은 _home=시네마 위치(이후 호출부가 슬롯으로 복귀시킴).</summary>
    static async UniTask Headbutt(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect, CardKeyword _atEffectKw, Func<UniTask> _afterHit, Vector3 _home)
    {
        NormalTuning t_cfg = Normal;   // 이 공격 동안 쓸 튜닝 스냅샷.

        Transform  t_atk     = _attacker.transform;
        Vector3    t_origin  = t_atk.position;            // 공격 시작점 = 현재 위치(드래그-백이면 띄운 자리).
        Quaternion t_baseRot = t_atk.localRotation;

        // 적 방향으로 기우는 각도(Z lean). 세로 부호 무시(적/아군 위아래 뒤집힘 방지), 좌우 성분으로만 기울임.
        Vector3 t_dir  = _defender.transform.position - t_origin;
        Vector3 t_dirN = t_dir.sqrMagnitude > 0.0001f ? t_dir.normalized : Vector3.up;
        float   t_lean = Mathf.Clamp(
            -Mathf.Atan2(t_dir.x, Mathf.Max(0.0001f, Mathf.Abs(t_dir.y))) * Mathf.Rad2Deg,
            -t_cfg.maxLean, t_cfg.maxLean);
        Quaternion t_leanRot = t_baseRot * Quaternion.Euler(0f, 0f, t_lean);

        Vector3 t_windback = t_origin - t_dirN * t_cfg.windDist;   // 현재 위치서 뒤로 살짝(적 반대 방향).
        t_windback.z = t_origin.z;
        Vector3 t_impact = Vector3.Lerp(t_origin, _defender.transform.position, t_cfg.lungeT);
        t_impact.z = t_origin.z;   // 평면 유지(뒤로 파고들지 않게).

        // 윈드업(뒤로 살짝) → 박치기 돌진(각도 틀며 접촉). 끊김 없이 연속.
        t_atk.DOKill();
        await DOTween.Sequence().SetLink(_attacker.gameObject)
            .Append(t_atk.DOMove(t_windback, t_cfg.windDur).SetEase(Ease.OutQuad))
            .Append(t_atk.DOMove(t_impact, t_cfg.inDur).SetEase(Ease.InQuad))
            .Join(t_atk.DOLocalRotateQuaternion(t_leanRot, t_cfg.inDur).SetEase(Ease.OutQuad))
            .ToUniTask();

        // 접촉: 히트/사망 해결과 공격자 반동/복귀를 동시 진행 → 중간 대기 없이 시퀀스 계속.
        // 데미지(_onEffect)는 ResolveHits 진입 즉시(=접촉 시점) 적용되고, 방어자 히트/사망 연출이
        // 공격자의 반동→복귀 모션과 병렬로 흐른다.
        UniTask t_resolve = ResolveHits(_attacker, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit, _skipRemain: true);

        Vector3 t_recoil = t_impact - t_dirN * t_cfg.recoilDist;   // 충격 지점 기준 뒤로 반동(적 반대 방향).
        t_recoil.z = _home.z;

        // 반동(각도 유지) → 끝난 뒤 복귀(이동+각도 원복). 순차. 방어자 히트/사망(ResolveHits)과는 병렬.
        async UniTask RecoilThenReturn()
        {
            await t_atk.DOMove(t_recoil, t_cfg.recoilDur).SetEase(Ease.OutQuad).SetLink(_attacker.gameObject).ToUniTask();

            // 무장 이펙트는 반동이 끝나는 지점에서 꺼진다. 접촉 프레임(돌진 트윈 직후)에 끄면
            // 충돌 연출이 보이기 전에 사라져 "닿기 전에 꺼진" 것처럼 읽힌다.
            _attacker.SetArmedVfx(false);

            await UniTask.WhenAll(
                t_atk.DOMove(_home, t_cfg.outDur).SetEase(Ease.OutBack).SetLink(_attacker.gameObject).ToUniTask(),
                t_atk.DOLocalRotateQuaternion(t_baseRot, t_cfg.outDur).SetEase(Ease.OutQuad).SetLink(_attacker.gameObject).ToUniTask());
        }

        await UniTask.WhenAll(t_resolve, RecoilThenReturn());
    }

    // ── 특별 연출: 카메라 시네마 1vs1 ────────────────────────────────────
    // 둘만(스플래시 포함) 앞으로 떠서 카메라가 확대, 무기 애니/파티클/발사체 후 히트.
    static async UniTask PlayCinema(CardView _attacker, CardView _defender,
        AttackEffect _effect, Action _onEffect,
        CardKeyword _preEffectKw, CardKeyword _atEffectKw, CardView _splashView, Func<UniTask> _afterHit)
    {
        float t_hitDelay = _effect?.hitDelay ?? 0f;
        float t_cinema   = GameTiming.Battle.CinemaDuration;

        Vector3 t_defenderOrigin = _defender.SlotPosition;
        Vector3 t_splashOrigin   = _splashView?.SlotPosition ?? Vector3.zero;

        CardView.FadeAll(0.3f);
        if (_splashView != null)
            CardView.FadeCards(1f, _attacker, _defender, _splashView);
        else
            CardView.FadeCards(1f, _attacker, _defender);

        if (_splashView != null)
            await UniTask.WhenAll(
                _attacker?.MoveToCenter() ?? UniTask.CompletedTask,
                _defender.MoveToCinemaPosition(0, 2),
                _splashView.MoveToCinemaPosition(1, 2));
        else if (_attacker != null)
            await UniTask.WhenAll(_attacker.MoveToCenter(), _defender.MoveToCenter());
        else
            await _defender.MoveToCenter();

        float t_origAttackerZ = _attacker?.SlotPosition.z ?? 0f;
        float t_origDefenderZ = _defender.SlotPosition.z;
        float t_origSplashZ   = _splashView?.SlotPosition.z ?? 0f;

        _ = _attacker?.transform.DOMoveZ(t_origAttackerZ - 5f, t_cinema);
        _ = _defender.transform.DOMoveZ(t_origDefenderZ - 5f, t_cinema);
        _ = _splashView?.transform.DOMoveZ(t_origSplashZ - 5f, t_cinema);

        SoundManager.Instance?.PlayCinemaEnter();
        if (BattleCamera.Instance != null)
            await BattleCamera.Instance.EnterCinema();
        else
            await UniTask.Delay((int)(t_cinema * 1000));

        bool t_flip = _attacker?.BoundCard?.ownerIndex != TurnState.LocalOwnerIndex;
        _attacker?.PlayAttackAnim();
        SoundManager.Instance?.PlayRandom(_effect?.attackClips);
        SoundManager.Instance?.PlayAttackVoice(_attacker?.BoundCard?.data?.attackVoices);
        _effect?.SpawnParticles(_attacker?.transform, _defender.transform, t_flip);
        LaunchProjectile(_effect?.projectile ?? default, _attacker?.transform, _defender.transform, t_hitDelay, t_flip).Forget();

        if (_preEffectKw != CardKeyword.None)
            await (_attacker?.PlayKeywordGlow(_preEffectKw) ?? UniTask.CompletedTask);

        if (t_hitDelay > 0f)
            await UniTask.Delay((int)(t_hitDelay * 1000));

        // 시네마 자리에서 일반 연출과 동일한 박치기(윈드업→돌진→반동). 복귀점=시네마 위치,
        // 슬롯 복귀는 아래 RestoreAfterAttack가 담당. 공격자 없음(환경 피해)이면 히트만.
        if (_attacker != null)
            await Headbutt(_attacker, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit,
                _home: _attacker.transform.position);
        else
            await ResolveHits(null, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit, _skipRemain: true);

        BattleCamera.Instance?.ExitCinema();

        await UniTask.WhenAll(
            _attacker?.RestoreAfterAttack() ?? UniTask.CompletedTask,
            _defender.MoveTo(t_defenderOrigin),
            _splashView?.MoveTo(t_splashOrigin) ?? UniTask.CompletedTask);

        CardView.RestoreAllFades();
    }

    // ── 공유: 히트/반격/사망/공격후 해결 ────────────────────────────────
    // 데미지 적용(_onEffect)부터 사망 연출·afterHit까지. 두 연출이 동일 순서/타이밍을 쓰도록 단일화.
    // 이동/카메라 같은 프레젠테이션은 호출부가 담당, 여기선 상태변화 반영 연출만.
    // 카드 총 체력(hp+bonusHp). 뷰/카드 없으면 0.
    static int HpTotal(CardView _v) => _v?.BoundCard != null ? _v.BoundCard.hp + _v.BoundCard.bonusHp : 0;

    static async UniTask ResolveHits(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect, CardKeyword _atEffectKw, Func<UniTask> _afterHit,
        bool _skipRemain = false)
    {
        float t_hitDelay = _effect?.hitDelay ?? 0f;
        float t_duration = _effect?.duration ?? 0f;

        // 데미지 숫자 = onEffect 전후 총 체력(hp+bonusHp) 감소분. 각 피격 뷰에 전달.
        int t_defBefore = HpTotal(_defender);
        int t_splBefore = HpTotal(_splashView);
        int t_atkBefore = HpTotal(_attacker);
        _onEffect?.Invoke();
        int t_defDmg = t_defBefore - HpTotal(_defender);
        int t_splDmg = t_splBefore - HpTotal(_splashView);
        int t_atkDmg = t_atkBefore - HpTotal(_attacker);
        bool t_attackerHit = t_atkDmg > 0;

        // 피격 방향: 맞은 쪽은 "때린 쪽"을 넘긴다(먼지 등이 반대로 튀게). 반격은 방향이 뒤집힌다.
        UniTask t_defHit = _splashView != null
            ? UniTask.WhenAll(_defender.PlayHitAnim(_damage: t_defDmg, _hitFrom: _attacker),
                              _splashView.PlayHitAnim(_damage: t_splDmg, _hitFrom: _attacker))
            : _defender.PlayHitAnim(_damage: t_defDmg, _hitFrom: _attacker);
        if (t_attackerHit)
            await UniTask.WhenAll(t_defHit,
                _attacker?.PlayHitAnim(_damage: t_atkDmg, _hitFrom: _defender) ?? UniTask.CompletedTask);
        else
            await t_defHit;

        if (_atEffectKw != CardKeyword.None)
            await (_attacker?.PlayKeywordGlow(_atEffectKw) ?? UniTask.CompletedTask);

        bool t_defenderKilled = _defender.BoundCard != null && _defender.BoundCard.hp <= 0;
        bool t_attackerKilled = _attacker?.BoundCard != null && _attacker.BoundCard.hp <= 0;
        bool t_splashKilled   = _splashView?.BoundCard != null && _splashView.BoundCard.hp <= 0;

        float t_remain = t_duration - t_hitDelay;
        if (!_skipRemain && t_remain > 0f)
            await UniTask.Delay((int)(t_remain * 1000));

        _attacker?.FocusWeapon(false);

        if (_splashView != null)
        {
            // 무쌍 등 다중 파괴: 동시 재생하면 "따닥"으로 뭉쳐 보인다 → 대상→스플래시 순차 재생.
            if (t_defenderKilled) await _defender.PlayDeathAnim();
            if (t_splashKilled)   await (_splashView?.PlayDeathAnim() ?? UniTask.CompletedTask);
            if (t_defenderKilled || t_splashKilled)
                SoundManager.Instance?.PlayKillVoice(_attacker?.BoundCard?.data?.killVoices);
        }
        else if (t_defenderKilled)
        {
            await _defender.PlayDeathAnim();
            SoundManager.Instance?.PlayKillVoice(_attacker?.BoundCard?.data?.killVoices);
        }

        if (t_attackerKilled)
            await (_attacker?.PlayDeathAnim() ?? UniTask.CompletedTask);

        // 히트/사망 연출 완료 후, 제자리 복귀 전에 공격후 효과(청소부 heal/OnAfterAttack 등) 실행
        if (_afterHit != null)
            await _afterHit();
    }

    static async UniTask LaunchProjectile(ProjectileData _proj, Transform _attacker, Transform _defender, float _duration, bool _flipOffset = false)
    {
        if (_proj.prefab == null || _attacker == null || _defender == null) return;

        if (_proj.spawnDelay > 0f)
            await UniTask.Delay((int)(_proj.spawnDelay * 1000));

        Vector3 t_offset = _flipOffset ? -_proj.localOffset : _proj.localOffset;
        Vector3 t_start  = _attacker.TransformPoint(t_offset);
        Vector3 t_end    = _defender.position;

        GameObject t_proj = UnityEngine.Object.Instantiate(_proj.prefab, t_start, Quaternion.identity);
        Vector3 t_dir = t_end - t_start;
        if (t_dir != Vector3.zero)
            t_proj.transform.right = t_dir.normalized;

        float t_travel = Mathf.Max(0f, _duration - _proj.spawnDelay);
        if (t_travel > 0f)
            await t_proj.transform.DOMove(t_end, t_travel).SetEase(Ease.Linear).ToUniTask();

        UnityEngine.Object.Destroy(t_proj);

        if (_proj.impactPrefab != null)
        {
            string t_id = _proj.impactPrefab.GetInstanceID().ToString();
            ParticlePooler.Register(t_id, _proj.impactPrefab);
            ParticlePooler.Spawn(t_id, t_end, Quaternion.identity);
        }
    }
}
