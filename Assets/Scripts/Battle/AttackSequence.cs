using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public static class AttackSequence
{
    public static UniTask PlaySingle(CardView _attacker, CardView _defender,
        AttackEffect _effect, Action _onEffect = null,
        CardKeyword _preEffectKw = CardKeyword.None,
        CardKeyword _atEffectKw  = CardKeyword.None,
        Func<UniTask> _afterHit = null)
        => PlayCore(_attacker, _defender, _effect, _onEffect, _preEffectKw, _atEffectKw, null, _afterHit);

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

    static async UniTask PlayCore(CardView _attacker, CardView _defender,
        AttackEffect _effect, Action _onEffect,
        CardKeyword _preEffectKw, CardKeyword _atEffectKw, CardView _splashView, Func<UniTask> _afterHit)
    {
        float t_hitDelay = _effect?.hitDelay ?? 0f;
        float t_duration = _effect?.duration ?? 0f;
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

        int t_attackerHpBefore = _attacker?.BoundCard?.hp ?? 0;
        _onEffect?.Invoke();
        bool t_attackerHit = _attacker?.BoundCard != null && _attacker.BoundCard.hp < t_attackerHpBefore;

        UniTask t_defHit = _splashView != null
            ? UniTask.WhenAll(_defender.PlayHitAnim(), _splashView.PlayHitAnim())
            : _defender.PlayHitAnim();
        if (t_attackerHit)
            await UniTask.WhenAll(t_defHit, _attacker?.PlayHitAnim() ?? UniTask.CompletedTask);
        else
            await t_defHit;

        if (_atEffectKw != CardKeyword.None)
            await (_attacker?.PlayKeywordGlow(_atEffectKw) ?? UniTask.CompletedTask);

        bool t_defenderKilled = _defender.BoundCard != null && _defender.BoundCard.hp <= 0;
        bool t_attackerKilled = _attacker?.BoundCard != null && _attacker.BoundCard.hp <= 0;
        bool t_splashKilled   = _splashView?.BoundCard != null && _splashView.BoundCard.hp <= 0;

        float t_remain = t_duration - t_hitDelay;
        if (t_remain > 0f)
            await UniTask.Delay((int)(t_remain * 1000));

        _attacker?.FocusWeapon(false);

        if (_splashView != null)
        {
            await UniTask.WhenAll(
                t_defenderKilled ? _defender.PlayDeathAnim()                           : UniTask.CompletedTask,
                t_splashKilled   ? (_splashView?.PlayDeathAnim() ?? UniTask.CompletedTask) : UniTask.CompletedTask);
            if (t_defenderKilled || t_splashKilled)
                SoundManager.Instance?.PlayKillVoice(_attacker?.BoundCard?.data?.killVoices);
        }
        else
        {
            if (t_defenderKilled)
            {
                await _defender.PlayDeathAnim();
                SoundManager.Instance?.PlayKillVoice(_attacker?.BoundCard?.data?.killVoices);
            }
        }

        if (t_attackerKilled)
            await (_attacker?.PlayDeathAnim() ?? UniTask.CompletedTask);

        // 히트/사망 연출 완료 후, 제자리 복귀 전에 공격후 효과(청소부 heal/OnAfterAttack 등) 실행
        if (_afterHit != null)
            await _afterHit();

        BattleCamera.Instance?.ExitCinema();

        await UniTask.WhenAll(
            _attacker?.RestoreAfterAttack() ?? UniTask.CompletedTask,
            _defender.MoveTo(t_defenderOrigin),
            _splashView?.MoveTo(t_splashOrigin) ?? UniTask.CompletedTask);

        CardView.RestoreAllFades();
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
