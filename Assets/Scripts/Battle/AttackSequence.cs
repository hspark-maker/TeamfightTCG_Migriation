using System;
using System.Collections.Generic;
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

    // ── 무쌍 연출 튜닝 ──
    // 박치기와 같은 규약: 값의 진실원은 BattleTimingConfig(SO), 시간 항목만 배속이 적용된 채로 들어온다.
    public struct PeerlessTuning
    {
        public float approachT;    // 주 대상 쪽으로 파고드는 비율(1=완전겹침).
        public float approachDur;  // 대상 앞까지 가는 시간.
        public float turnDur;      // 광역 대상 쪽으로 도는 시간.
        public float returnDur;    // 슬롯 복귀 시간.
        public float hitStop;      // 벨 때마다 멈칫하는 시간(데미지 표시 직전).
        public float afterHitHold; // 한 대 때린 뒤 다음 동작(회전/복귀)까지 머무는 시간.
        public float maxTurn;      // 최대 회전각(도). 넘기면 카드가 드러누워 보인다.
        public float swingFront;   // 휘두름 이펙트가 공격자 앞으로 나가는 거리(월드).
        public float turnSideStep; // 마무리로 광역 대상 쪽으로 더 미끄러지는 거리(월드).
        public float windupAngle;  // 베기 전, 광역 대상 **반대쪽**으로 더 트는 각(도).
        public float slashMaxTurn; // 베기 자국이 수평에서 기울 수 있는 최대 각(도). 0이면 항상 수평.
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

    /// <summary>매치포인트 접근 연출의 시간 배율 적용 지점.
    /// 후퇴·복귀는 원속을 유지하고 타격으로 이어지는 전진 구간에만 무게를 싣는다.
    ///
    /// <para>결정타는 <b>도달 거리도</b> 바꾼다 — 평소 lungeT는 방어자 앞에서 멈추는 값이라
    /// 느려진 접근에서는 닿지도 않은 채 피격 연출이 터진다. 접근 중일 때만 실제로 부딪히는 지점까지
    /// 파고들게 해서 "피격 연출 프레임 = 부딪히는 프레임"이 되게 한다.</para></summary>
    static void ApplyApproach(ref NormalTuning _cfg)
    {
        _cfg.inDur *= BattleFinisher.ApproachDurationFactor;
        if (BattleFinisher.ApproachActive) _cfg.lungeT = GameTiming.Battle.ApproachLungeT;
    }

    static void ApplyApproach(ref PeerlessTuning _cfg)
        => _cfg.approachDur *= BattleFinisher.ApproachDurationFactor;

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
        Func<UniTask> _afterHit = null,
        bool? _forceSpecial = null)
        => PlayCore(_attacker, _defender, _effect, _onEffect, _preEffectKw, _atEffectKw, _splashView, _afterHit, _forceSpecial);

    /// <summary>splashView 유무로 splash/single 자동 선택. 호출부의 if/else 제거용.
    /// _afterHit: 히트/사망 연출 완료 후·제자리 복귀 직전에 실행되는 공격후 효과 콜백.</summary>
    public static UniTask Play(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect,
        CardKeyword _preEffectKw, CardKeyword _atEffectKw,
        Func<UniTask> _afterHit = null)
        => PlayCore(_attacker, _defender, _effect, _onEffect, _preEffectKw, _atEffectKw, _splashView, _afterHit);

    /// <summary>시네마 연출 대상인가. **3단계 진화 카드의 첫 공격 1회만** — 등장 컷씬으로 들어온 카드가
    /// 처음 치는 순간을 클로즈업으로 보여주고, 그 뒤로는 일반 연출(박치기)로 돌아간다.
    /// 판정·래치는 CardCinematicRules 단독 소유(여기서 stage를 직접 비교하지 않는다).
    ///
    /// 구 규칙(공격력 ≥ 6이면 시네마)은 폐기했다 — 고체력 방어형이 시네마를 남발했고,
    /// "진화 카드의 등장 후 첫 일격"이라는 연출 의도와 무관한 기준이었다.
    ///
    /// **래치를 소비하므로 한 공격에 한 번만 호출**해야 한다(PlayCore 한 곳).</summary>
    static bool IsCinemaAttack(CardView _attacker)
        => CardCinematicRules.TryConsumeCinemaAttack(_attacker?.BoundCard);

    /// <summary>연출 디스패치. 두 프레젠테이션 공유 히트해결(ResolveHits)로 데미지/사망 타이밍 일치.
    /// - 일반(PlayNormal): 자기 위치에서 적 방향으로 각도 틀고 박치기.
    /// - 특별(PlayCinema): 둘만 앞으로 떠서 카메라 시네마 1vs1. 3단계 진화 카드의 첫 공격 1회.</summary>
    static async UniTask PlayCore(CardView _attacker, CardView _defender,
        AttackEffect _effect, Action _onEffect,
        CardKeyword _preEffectKw, CardKeyword _atEffectKw, CardView _splashView, Func<UniTask> _afterHit,
        bool? _forceSpecial = null)
    {
        // 테스트/특수 호출이 강제하면 그 값, 아니면 공격력 기준 판정.
        bool t_special = _forceSpecial ?? IsCinemaAttack(_attacker);
        if (t_special) BattleFinisher.CancelApproachArm();
        bool t_approach = !t_special && BattleFinisher.TryBeginApproach(_attacker, _defender);

        try
        {
            if (t_special)
            {
                await PlayCinema(_attacker, _defender, _effect, _onEffect, _preEffectKw, _atEffectKw, _splashView, _afterHit);
                return;
            }

            // 원거리(Ranged)는 붙지 않는다 — 제자리에서 투사체를 쏘고, 투사체가 닿는 시점에 히트.
            if (IsRangedAttack(_attacker))
            {
                await PlayRanged(_attacker, _defender, _splashView, _effect, _onEffect, _preEffectKw, _atEffectKw, _afterHit);
                return;
            }

            // 무쌍은 광역 대상이 실제로 있을 때만 전용 연출로 간다.
            if (IsPeerlessAttack(_attacker) && _splashView != null)
            {
                await PlayPeerless(_attacker, _defender, _splashView, _effect, _onEffect, _preEffectKw, _atEffectKw, _afterHit);
                return;
            }

            await PlayNormal(_attacker, _defender, _splashView, _effect, _onEffect, _preEffectKw, _atEffectKw, _afterHit);
        }
        finally
        {
            if (t_approach) BattleFinisher.EndApproach();
        }
    }

    /// <summary>이 공격이 원거리 발사 연출인가. 공격자 없음(환경 피해)이면 false → 기존 경로.</summary>
    static bool IsRangedAttack(CardView _attacker)
        => _attacker?.BoundCard != null && _attacker.BoundCard.HasKeyword(CardKeyword.Ranged);

    /// <summary>이 공격이 무쌍 광역 연출인가. 원거리와 마찬가지로 런타임 키워드 포함 판정.</summary>
    static bool IsPeerlessAttack(CardView _attacker)
        => _attacker?.BoundCard != null && _attacker.BoundCard.HasKeyword(CardKeyword.Peerless);

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
        _effect?.SpawnParticles(_attacker?.transform, _defender.transform, t_flip,
                                BattleFinisher.ApproachDurationFactor);

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
        float t_approachFactor = BattleFinisher.ApproachDurationFactor;
        float t_hitDelay = (_effect?.hitDelay ?? 0f) * t_approachFactor;

        CardView.FadeAll(0.3f);
        if (_splashView != null) CardView.FadeCards(1f, _attacker, _defender, _splashView);
        else                     CardView.FadeCards(1f, _attacker, _defender);

        bool t_flip = _attacker?.BoundCard?.ownerIndex != TurnState.LocalOwnerIndex;
        _attacker?.PlayAttackAnim();
        SoundManager.Instance?.PlayRandom(_effect?.attackClips);
        SoundManager.Instance?.PlayAttackVoice(_attacker?.BoundCard?.data?.attackVoices);
        _effect?.SpawnParticles(_attacker?.transform, _defender.transform, t_flip,
                                BattleFinisher.ApproachDurationFactor);
        LaunchProjectile(_effect?.projectile ?? default, _attacker?.transform, _defender.transform,
                         t_hitDelay, t_flip, t_approachFactor).Forget();

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

    /// <summary>제자리 발사 반동. 이동하지 않고 슬롯에서 살짝 밀렸다 돌아온다(각도 변화 없음).
    ///
    /// 원거리 공격 외에 <b>무리 선피해 일제사격</b>(SwarmVfx)도 이걸 쓴다 — "쏘는 동작"은 한 가지여야
    /// 같은 카드가 경로에 따라 다르게 움직이지 않는다. 거리·시간은 박치기와 같은 SO 값(NormalTuning)이다.</summary>
    public static async UniTask RecoilInPlace(CardView _attacker, CardView _defender)
    {
        if (_attacker == null) return;

        NormalTuning t_cfg = Normal;
        Transform t_atk  = _attacker.transform;

        // 복귀 목표는 **슬롯**이다(현재 위치가 아니라). 현재 위치를 목표로 잡으면 어떤 이유로든 어긋난
        // 좌표가 그대로 새 기준이 되어 발사할 때마다 누적된다 — 박치기(PlayNormal)는 _home(슬롯)으로
        // 복귀해 매 공격 리셋되지만 이 경로엔 리셋 지점이 없어, 한 번 뜨면 카드가 뜬 채로 영구히 남는다
        // (원거리 카드만 공격 후 위에 떠 있던 원인).
        // z는 현재 평면을 따른다 — 발사하는 동안 방어자보다 앞에 그려져야 한다.
        Vector3 t_home = _attacker.SlotPosition;
        t_home.z = t_atk.position.z;

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

    // ── 무쌍 연출: 앞으로 파고들어 두 번 베기 ────────────────────────────
    // 공격자가 주 대상 앞까지 파고들어 벤 뒤, 광역 대상 쪽으로 몸을 틀어 한 번 더 벤다.
    // 데미지 표시는 대상별로 **순차**이고 벨 때마다 잠깐 멈칫(hit stop)한다 — 한 번에 두 장이 같이
    // 깎이면 무쌍이 "광역 한 방"으로 읽히고, 어느 쪽이 얼마 맞았는지 숫자가 겹쳐 안 보인다.
    //
    // **규칙은 여전히 한 번에 적용된다**(_onEffect 1회 = AttackProcessor의 고정 시퀀스).
    // 순차로 만든 건 표시뿐이다 — 피해 적용을 연출에 쪼개 붙이면 두 클라의 상태 타임라인이 갈라지고,
    // 그 사이에 낀 패시브/시너지 훅이 다른 hp를 보게 된다(AttackProcessor 주석의 seam 규약).
    //
    // 베기 프리팹은 BattleVfxLibrary(BattleVfxId.PeerlessSlash) 소유 — 미배선이면 베기 없이 동작한다.

    static async UniTask PlayPeerless(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect, CardKeyword _preEffectKw, CardKeyword _atEffectKw, Func<UniTask> _afterHit)
    {
        CardView.FadeAll(0.3f);
        CardView.FadeCards(1f, _attacker, _defender, _splashView);

        bool t_flip = _attacker.BoundCard?.ownerIndex != TurnState.LocalOwnerIndex;
        _attacker.PlayAttackAnim();
        SoundManager.Instance?.PlayRandom(_effect?.attackClips);
        SoundManager.Instance?.PlayAttackVoice(_attacker.BoundCard?.data?.attackVoices);
        _effect?.SpawnParticles(_attacker.transform, _defender.transform, t_flip,
                                BattleFinisher.ApproachDurationFactor);

        if (_preEffectKw != CardKeyword.None)
            await _attacker.PlayKeywordGlow(_preEffectKw);

        Transform  t_atk     = _attacker.transform;
        Vector3    t_home    = _attacker.SlotPosition;
        Quaternion t_baseRot = t_atk.localRotation;
        int        t_layer   = _attacker.VfxSortingLayerId;

        // 이 공격 동안 쓸 튜닝 스냅샷(박치기와 같은 규약). 시간 항목은 이미 배속이 적용돼 들어온다.
        PeerlessTuning t_cfg = GameTiming.Battle.PeerlessAttack;
        ApplyApproach(ref t_cfg);

        // 이번 연출이 띄운 이펙트들. 멈칫(hit stop) 때 **같이 얼어야** 해서 들고 있는다 —
        // 카드만 멈추고 베기가 계속 흐르면 "멈춘 순간"이 아니라 "카드가 굳은 것"으로 보인다.
        var t_live = new List<GameObject>();

        // 광역 대상이 주 대상의 왼쪽인가 오른쪽인가. 윈드업(반대쪽으로 튼다)과 마무리 미끄러짐(그쪽으로 간다)이
        // 이 부호 하나를 공유한다 — 따로 계산하면 둘이 어긋나 반대로 휘두르는 그림이 나온다.
        float t_sideSign = Mathf.Sign(_splashView.transform.position.x - _defender.transform.position.x);

        // 베기 프리팹은 왼쪽 → 오른쪽으로 긋는 그림이다. 광역 대상이 왼쪽이면 칼도 오른쪽 → 왼쪽으로
        // 쓸어내리므로 좌우를 뒤집어야 그림과 궤적이 맞는다.
        bool t_mirror = t_sideSign < 0f;

        // 휘두름(칼 궤적)이 지나가는 자리 = 두 대상의 가운데. 한 번 그은 칼이 둘을 함께 쓸고 가는 그림이라
        // 어느 한쪽에 붙이면 나머지 하나는 안 맞은 것처럼 보인다.
        Vector3 t_mid = Vector3.Lerp(_defender.transform.position, _splashView.transform.position, 0.5f);
        t_mid.z = _defender.transform.position.z;

        // 베기 **자국**은 반대로 맞은 놈마다 하나씩 — 누가 몇 대 맞았는지는 자국이 알려줘야 한다.
        // 방향은 수평 기준으로 각을 제한한다(대상이 위/아래로 멀면 자국이 세로로 서서 "찌른 것"이 된다).
        void SlashTarget(Vector3 _targetPos)
            => Track(t_live, Slash(BattleVfxId.PeerlessSlash, _targetPos,
                                   ClampSlashDir(_targetPos - t_atk.position, t_cfg.slashMaxTurn),
                                   t_mirror, t_layer));

        // 멈칫: 띄운 이펙트 + 이번 연출에 낀 카드들의 트윈(이동·회전·떨림)을 함께 세웠다 되살린다.
        // 파티클만 세우면 카드가 계속 돌아 "멈춘 순간"이 아니라 "이펙트만 끊긴 것"으로 보인다.
        // ResolveHits가 각 대상 표시 직전에 부른다.
        var t_frozen = new List<ParticleSystem>();
        var t_speeds = new List<float>();

        async UniTask FreezeBeat()
        {
            if (t_cfg.hitStop <= 0f) return;

            PauseVfx(t_live, t_frozen, t_speeds);
            SetCardTweensPaused(true, _attacker, _defender, _splashView);

            await UniTask.Delay((int)(t_cfg.hitStop * 1000));

            SetCardTweensPaused(false, _attacker, _defender, _splashView);
            ResumeVfx(t_frozen, t_speeds);
        }

        // 1) 주 대상 앞으로 파고들며 동시에 윈드업한다. 도착 프레임이 곧 첫 베기 프레임이다.
        Vector3 t_front = Vector3.Lerp(t_atk.position, _defender.transform.position, t_cfg.approachT);
        t_front.z = t_atk.position.z;   // 평면 유지(뒤로 파고들지 않게) — 박치기와 같은 규약

        // 주 대상을 보는 각에서 **광역 대상 반대쪽으로 더** 튼다.
        // 접근과 함께 젖혀놔야 이어지는 회전(주 대상 → 광역 대상)이 한 번에 쓸어내리는 궤적이 된다.
        Quaternion t_aimDef = FaceRot(t_baseRot, _defender.transform.position - t_front, t_cfg.maxTurn);
        t_atk.DOKill();
        await DOTween.Sequence().SetLink(_attacker.gameObject)
            .Append(t_atk.DOMove(t_front, t_cfg.approachDur).SetEase(Ease.InQuad))
            .Join(t_atk.DOLocalRotateQuaternion(
                t_aimDef * Quaternion.Euler(0f, 0f, t_sideSign * t_cfg.windupAngle), t_cfg.turnDur)
                .SetEase(Ease.OutQuad))
            .ToUniTask();

        // 3) 공격 시작. 파고드는 동안 미리 띄우면 닿기도 전에 휘두른 것으로 보인다.
        //
        // 휘두름은 **한 번만** 난다. 대상마다 새로 띄우지 않는 이유: 이건 벤 자국이 아니라 공격자가 든
        // 무기 궤적이라 두 번 태어나면 칼이 두 자루로 보인다.
        // 카드에 붙이지 않고 월드에 놓는다 — 붙이면 이후 회전(광역 대상 쪽으로 틀기)을 따라 궤적까지
        // 같이 돌아서 이미 그어진 자국이 움직이는 것처럼 보인다.
        // 자리는 두 대상의 가운데(t_mid), swingFront는 거기서 공격자 반대쪽으로 더 밀어내는 여유값이다.
        Vector3 t_swingDir = ClampSlashDir(t_mid - t_atk.position, t_cfg.slashMaxTurn);
        VfxHandle t_swing  = SpawnSwing(t_mid + t_swingDir.normalized * t_cfg.swingFront,
                                        t_swingDir, t_mirror, t_layer);
        Track(t_live, t_swing);

        // 벤 방향으로 **베는 내내 계속** 미끄러진다(칼 휘두른 관성). 끝나고 한 번에 밀면 두 타격은 제자리에서
        // 나고 마지막에만 툭 밀리는 그림이 된다. 기다리지 않고 흘려보내며, 멈칫 때는 카드 트윈 정지가
        // 이 미끄러짐도 같이 세운다 — 그래서 트윈 길이엔 멈칫 시간을 넣지 않는다(멈춘 만큼 실제로 늘어난다).
        Vector3 t_slideEnd = t_atk.position + new Vector3(t_sideSign * t_cfg.turnSideStep, 0f, 0f);
        float   t_slideDur = Mathf.Max(0.05f, t_cfg.afterHitHold + t_cfg.turnDur * 2f);
        t_atk.DOMove(t_slideEnd, t_slideDur).SetEase(Ease.Linear).SetLink(_attacker.gameObject);

        // 이 직후 ResolveHits가 데미지를 적용하고, 멈칫 뒤 숫자가 뜬다.
        SlashTarget(_defender.transform.position);

        // 5) 첫 타격·첫 멈칫 뒤, 광역 대상 쪽으로 **더** 돌아 두 번째로 벤다.
        // 윈드업에서 반대쪽으로 젖혀놨으므로 여기서 도는 각이 그만큼 커져 한 번에 쓸어내리는 궤적이 된다.
        // 이동은 없다 — 미끄러짐은 다 베고 난 마무리(7)에서 한 번만.
        async UniTask TurnAndSlashSplash()
        {
            if (_attacker == null || _splashView == null) return;

            // 때린 여운. 맞자마자 다음 대상으로 돌면 두 타격이 한 동작으로 뭉쳐 보인다.
            if (t_cfg.afterHitHold > 0f)
                await UniTask.Delay((int)(t_cfg.afterHitHold * 1000));

            if (_attacker == null || _splashView == null) return;

            await t_atk.DOLocalRotateQuaternion(
                           FaceRot(t_baseRot, _splashView.transform.position - t_atk.position, t_cfg.maxTurn),
                           t_cfg.turnDur)
                       .SetEase(Ease.OutQuad).SetLink(_attacker.gameObject).ToUniTask();

            SoundManager.Instance?.PlayRandom(_effect?.attackClips);
            SlashTarget(_splashView.transform.position);
        }

        await ResolveHits(_attacker, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit,
            _skipRemain: true, _beforeSplashHit: TurnAndSlashSplash, _hitStop: FreezeBeat);

        if (_attacker == null) { t_swing.Release(); CardView.RestoreAllFades(); return; }

        // 7) 마무리. 아직 미끄러지는 중이면 그 자리에서 끊고 복귀로 넘어간다 —
        // 안 끊으면 남은 미끄러짐 트윈이 복귀 DOMove와 같은 Transform을 두고 다툰다.
        t_atk.DOKill();

        // 휘두름은 여기서 반납한다 — 수명을 항목 lifetime에 맡기면 멈칫만큼 늘어난
        // 연출 도중에 먼저 사라진다(자기반납형 프리팹이면 Release가 무동작).
        t_swing.Release();
        _attacker.SetArmedVfx(false);

        await UniTask.WhenAll(
            t_atk.DOMove(t_home, t_cfg.returnDur).SetEase(Ease.OutBack).SetLink(_attacker.gameObject).ToUniTask(),
            t_atk.DOLocalRotateQuaternion(t_baseRot, t_cfg.returnDur).SetEase(Ease.OutQuad).SetLink(_attacker.gameObject).ToUniTask());

        CardView.RestoreAllFades();
    }

    /// <summary>_dir 쪽을 보도록 기준 자세에서 Z로 튼 회전. 세로 성분은 절대값으로 써서
    /// 아군/적 어느 진영이든 카드가 뒤집히지 않는다(박치기 lean과 같은 공식, 각도 한계만 크다).</summary>
    static Quaternion FaceRot(Quaternion _baseRot, Vector3 _dir, float _maxDeg)
    {
        float t_ang = Mathf.Clamp(
            -Mathf.Atan2(_dir.x, Mathf.Max(0.0001f, Mathf.Abs(_dir.y))) * Mathf.Rad2Deg,
            -_maxDeg, _maxDeg);
        return _baseRot * Quaternion.Euler(0f, 0f, t_ang);
    }

    /// <summary>베기 계열 이펙트 1회. _pos에 _dir 쪽으로 눕혀 스폰한다(회전 규약은 BattleVfx.PlayAttached와 동일).
    /// 카드에 붙이지 않는 이유: 붙이면 피격으로 흔들리는 카드를 따라 이펙트까지 같이 떨린다.
    /// 미배선이면 Valid=false를 돌려준다 — 호출부에 널 분기가 늘지 않는다.</summary>
    static VfxHandle Slash(BattleVfxId _id, Vector3 _pos, Vector3 _dir, bool _mirror, int _sortingLayerId)
    {
        VfxHandle t_h = BattleVfx.Spawn(_id, _pos, _sortingLayerId);
        if (!t_h.Valid) return t_h;

        if (BattleVfx.TryGetEntry(_id, out VfxEntry t_e))
            t_h.Go.transform.rotation = SlashRot(t_e, _dir, _mirror);

        t_h.ReleaseAfterLifetime();
        return t_h;
    }

    /// <summary>베기 계열의 스폰 회전. **대상 방향으로 눕히는 건 항목의 alignToDirection이 켜졌을 때뿐**이고,
    /// 꺼져 있으면 항목에 적힌 initialRotation 그대로 나간다(기본 0,0,0 = 프리팹 원래 자세).
    /// 방향 조준을 코드가 항상 강제하면 프리팹이 이미 원하는 각으로 그려져 있어도 비틀린다.
    /// 좌우 미러는 어느 쪽이든 적용된다 — 그건 각도가 아니라 "어느 방향으로 긋는 그림인가"의 문제다.</summary>
    static Quaternion SlashRot(VfxEntry _entry, Vector3 _dir, bool _mirror)
    {
        Quaternion t_rot = Quaternion.Euler(_entry.initialRotation);

        if (_entry.alignToDirection && _dir.sqrMagnitude > 1e-6f)
            t_rot = Quaternion.LookRotation(_dir.normalized, Vector3.back) * t_rot;

        return _mirror ? t_rot * MirrorRot : t_rot;
    }

    static void Track(List<GameObject> _live, VfxHandle _h)
    {
        if (_h.Valid) _live.Add(_h.Go);
    }

    /// <summary>멈칫 동안 카드 트윈을 세운다. Transform만이 아니라 **컴포넌트 전부**를 훑는다:
    /// 이동·회전·떨림은 Transform이 target이지만 피격 오버레이 페이드는 SpriteRenderer가,
    /// 데미지 숫자는 TMP_Text가 target이라 Transform만 세우면 카드는 굳고 숫자만 계속 움직인다.
    /// DOTween.PauseAll은 쓰지 않는다 — 이번 공격과 무관한 UI·다른 카드 연출까지 같이 멈춘다.</summary>
    static void SetCardTweensPaused(bool _paused, params CardView[] _cards)
    {
        foreach (CardView t_cv in _cards)
        {
            if (t_cv == null) continue;

            foreach (Component t_c in t_cv.GetComponentsInChildren<Component>(true))
            {
                if (t_c == null) continue;   // 스크립트 누락 슬롯
                if (_paused) t_c.DOPause();
                else         t_c.DOPlay();
            }
        }
    }

    /// <summary>휘두름 궤적을 공격자 **자식으로** 붙여 앞쪽에 띄운다 — 파고들기·회전을 따라다녀야 하므로
    /// 대상 위치에 놓는 베기(Slash)와 달리 부착형이다. 앞 방향은 카드 로컬 +Y이고, 적 진영은 배치가
    /// 위아래로 뒤집혀 있어 SpawnAttached의 flip 규약에 맡긴다(오프셋 부호 + X축 180도).
    /// 수명은 호출부가 쥔다 — 연출 길이가 멈칫만큼 늘어나므로 항목 lifetime으로는 짧다.</summary>
    static VfxHandle SpawnSwing(Vector3 _pos, Vector3 _dir, bool _mirror, int _sortingLayerId)
    {
        VfxHandle t_h = BattleVfx.Spawn(BattleVfxId.PeerlessSwing, _pos, _sortingLayerId);
        if (!t_h.Valid) return t_h;

        if (BattleVfx.TryGetEntry(BattleVfxId.PeerlessSwing, out VfxEntry t_e))
            t_h.Go.transform.rotation = SlashRot(t_e, _dir, _mirror);

        return t_h;   // 반납은 호출부(연출이 멈칫만큼 길어지므로 항목 lifetime으론 짧다)
    }

    /// <summary>베기 방향을 **수평 기준 ±_maxDeg** 안으로 접는다. 기준축은 원래 방향의 좌우(+x/-x)라
    /// 어느 쪽으로 베는지는 유지된 채 기울기만 제한된다. _maxDeg가 0이면 완전 수평.</summary>
    static Vector3 ClampSlashDir(Vector3 _dir, float _maxDeg)
    {
        if (_dir.sqrMagnitude < 1e-6f) return Vector3.right;

        float t_base = _dir.x >= 0f ? 0f : 180f;                        // 좌우 어느 쪽으로 긋는가
        float t_ang  = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        t_ang = t_base + Mathf.Clamp(Mathf.DeltaAngle(t_base, t_ang), -_maxDeg, _maxDeg);

        float t_rad = t_ang * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(t_rad), Mathf.Sin(t_rad), 0f);
    }

    /// <summary>좌우 뒤집기. 베기 프리팹은 **왼쪽 → 오른쪽으로 긋는 그림 하나뿐**이라, 반대로 벨 때
    /// 그냥 쓰면 칼이 가는 방향과 자국이 어긋난다. Y축 180도 = 평면 그림의 좌우 거울(적 진영의 X축 180도와 같은 규약).</summary>
    static readonly Quaternion MirrorRot = Quaternion.Euler(0f, 180f, 0f);

    /// <summary>추적 중인 이펙트를 세운다. 멈춘 것과 원래 시뮬레이션 속도를 _frozen/_speeds에 담아
    /// ResumeVfx가 그대로 되돌린다 — 되살릴 때 1로 일괄 복구하면 느리게/빠르게 도는 프리팹이 어긋난다.
    ///
    /// Pause()만으로는 부족해서 simulationSpeed까지 0으로 눌러둔다. 구매 에셋 프리팹은 자식마다
    /// 재생 상태가 제각각이라 부모 하나에 Pause(withChildren)만 걸면 안 멈추는 자식이 남는다.
    /// 이미 반납/파괴된 것은 목록에서 지운다 — 풀 재사용분을 들고 있으면 **다음 연출이 쓰는 오브젝트를 멈춘다**.
    /// Time.timeScale을 안 쓰는 이유는 HitStop 주석과 같다(전역을 흔들지 않는다).</summary>
    static void PauseVfx(List<GameObject> _live, List<ParticleSystem> _frozen, List<float> _speeds)
    {
        _frozen.Clear();
        _speeds.Clear();

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            GameObject t_go = _live[i];
            if (t_go == null || !t_go.activeInHierarchy) { _live.RemoveAt(i); continue; }

            foreach (ParticleSystem t_ps in t_go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule t_main = t_ps.main;

                _frozen.Add(t_ps);
                _speeds.Add(t_main.simulationSpeed);

                t_main.simulationSpeed = 0f;
                t_ps.Pause(withChildren: false);   // 자식은 이 반복문이 각자 처리한다(이중 처리 방지)
            }
        }
    }

    /// <summary>PauseVfx가 세운 것만 되살린다. 멈추기 전부터 끝나 있던 것은 isPaused가 아니므로
    /// Play를 부르지 않는다 — 부르면 다 타버린 이펙트가 처음부터 다시 터진다.</summary>
    static void ResumeVfx(List<ParticleSystem> _frozen, List<float> _speeds)
    {
        for (int i = 0; i < _frozen.Count; i++)
        {
            ParticleSystem t_ps = _frozen[i];
            if (t_ps == null) continue;

            ParticleSystem.MainModule t_main = t_ps.main;
            t_main.simulationSpeed = _speeds[i];

            if (t_ps.isPaused) t_ps.Play(withChildren: false);
        }

        _frozen.Clear();
        _speeds.Clear();
    }

    // ── 공유: 박치기 모션 ────────────────────────────────────────────────
    /// <summary>윈드업(뒤로 살짝) → 돌진(각도 틀며 접촉=히트) → 반동 → _home 복귀.
    /// 히트/사망 해결(ResolveHits)과 반동/복귀는 병렬 — 데미지는 접촉 시점에 적용.
    /// 일반 연출은 _home=원래 슬롯, 시네마 연출은 _home=시네마 위치(이후 호출부가 슬롯으로 복귀시킴).</summary>
    static async UniTask Headbutt(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect, CardKeyword _atEffectKw, Func<UniTask> _afterHit, Vector3 _home)
    {
        NormalTuning t_cfg = Normal;   // 이 공격 동안 쓸 튜닝 스냅샷.
        ApplyApproach(ref t_cfg);

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
        float t_lungeStart = t_cfg.windDur * 0.85f;
        await DOTween.Sequence().SetLink(_attacker.gameObject)
            .Append(t_atk.DOMove(t_windback, t_cfg.windDur).SetEase(Ease.OutSine))
            .Insert(t_lungeStart, t_atk.DOMove(t_impact, t_cfg.inDur).SetEase(Ease.InQuad))
            .Insert(t_lungeStart, t_atk.DOLocalRotateQuaternion(t_leanRot, t_cfg.inDur).SetEase(Ease.OutQuad))
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
        _effect?.SpawnParticles(_attacker?.transform, _defender.transform, t_flip,
                                BattleFinisher.ApproachDurationFactor);
        LaunchProjectile(_effect?.projectile ?? default, _attacker?.transform, _defender.transform, t_hitDelay, t_flip).Forget();

        if (_preEffectKw != CardKeyword.None)
            await (_attacker?.PlayKeywordGlow(_preEffectKw) ?? UniTask.CompletedTask);

        if (t_hitDelay > 0f)
            await UniTask.Delay((int)(t_hitDelay * 1000));

        // 시네마 자리에서의 타격 모션. 카드마다 다른 연출을 주는 분기점 — 어떤 카드가 어떤 연출인지는
        // CardData.cinemaAttackStyle이 소유하고, 연출 구현은 여기 있다. 데미지 해결은 어느 쪽이든 ResolveHits 공용.
        CinemaAttackStyle t_style = _attacker?.BoundCard?.data != null
            ? _attacker.BoundCard.data.cinemaAttackStyle : CinemaAttackStyle.Default;

        if (_attacker != null && t_style == CinemaAttackStyle.EnergyOrbDash)
            await EnergyOrbDash(_attacker, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit);
        else if (_attacker != null)
            await Headbutt(_attacker, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit,
                _home: _attacker.transform.position);
        else
            await ResolveHits(null, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit, _skipRemain: true);

        // 결정타가 열렸으면 시네마가 카메라 소유권을 돌려놓지 않는다.
        // FinishFocus가 현재 위치에서 이어받았는데 여기서 ExitCinema를 호출하면 강한 줌이 즉시 풀린다.
        if (!BattleResultBeat.FinishPlayed)
            BattleCamera.Instance?.ExitCinema();

        await UniTask.WhenAll(
            _attacker?.RestoreAfterAttack() ?? UniTask.CompletedTask,
            _defender.MoveTo(t_defenderOrigin),
            _splashView?.MoveTo(t_splashOrigin) ?? UniTask.CompletedTask);

        CardView.RestoreAllFades();
    }

    // ── 시네마 연출 변형: 에너지 구체 돌진 ───────────────────────────────
    // 카드가 알파로 사라지며 에너지 구체로 변해 상대에게 돌진 → 충돌(히트) → 구체가 제자리로 돌아오며 카드가 다시 나타난다.
    // 프리팹은 BattleVfxLibrary(BattleVfxId.CinemaEnergyOrb) 소유 — 미배선이면 구체 없이 카드가 그대로 돌진한다(무동작 안전).

    const float ORB_MORPH_DUR  = 0.14f;   // 카드 → 구체 (알파 아웃)
    const float ORB_DASH_DUR   = 0.16f;   // 구체가 상대까지
    const float ORB_RETURN_DUR = 0.22f;   // 구체가 제자리로
    const float ORB_LUNGE_T    = 0.82f;   // 상대 쪽으로 얼마나 파고드는가(1=완전겹침)
    const float ORB_DASH_SCALE = 1.8f;    // 돌진 중 구체가 커지는 배율(제자리 크기 대비) — 이동 궤적이 굵게 보이도록

    static async UniTask EnergyOrbDash(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect, CardKeyword _atEffectKw, Func<UniTask> _afterHit)
    {
        Transform t_atk  = _attacker.transform;
        Vector3   t_home = t_atk.position;

        Vector3 t_impact = Vector3.Lerp(t_home, _defender.transform.position, ORB_LUNGE_T);
        t_impact.z = t_home.z;   // 평면 유지(뒤로 파고들지 않게) — 박치기와 같은 규약

        float t_morph  = ORB_MORPH_DUR  * GameTiming.Factor;
        float t_dash   = ORB_DASH_DUR   * GameTiming.Factor;
        float t_return = ORB_RETURN_DUR * GameTiming.Factor;

        // 1) 카드가 사라지며 구체 등장. 구체는 카드 자리에서 태어난다.
        _attacker.FadeView(0f, t_morph);
        // 구체는 카드마다 테마가 다르다(전기/물/안개…). 카드에 지정된 게 있으면 그걸 쓰고,
        // 없으면 라이브러리 기본 구체로 떨어진다 — 배선을 안 해도 연출이 비지는 않게.
        GameObject t_orbPrefab = _attacker.BoundCard?.data?.cinemaOrbPrefab;
        VfxHandle  t_orb       = t_orbPrefab != null
            ? BattleVfx.SpawnPrefab(t_orbPrefab, t_home, _attacker.VfxSortingLayerId)
            : BattleVfx.Spawn(BattleVfxId.CinemaEnergyOrb, t_home, _attacker.VfxSortingLayerId);
        await UniTask.Delay((int)(t_morph * 1000));

        Transform t_orbTr = t_orb.Valid ? t_orb.Go.transform : null;

        // 2) 돌진. 구체가 없으면(미배선) 카드 본체를 그대로 옮긴다 — 연출이 비어 보이지 않게.
        Transform t_mover = t_orbTr != null ? t_orbTr : t_atk;
        if (t_orbTr == null) _attacker.FadeView(1f, 0f);

        t_mover.DOKill();

        // 돌진하는 동안만 구체를 키운다(멈춰 있을 땐 원래 크기) — 이동 궤적이 굵고 세게 보이도록.
        // **풀 반납 전 반드시 원복**한다(HealVfx와 같은 규약). 안 하면 다음 스폰이 커진 채로 나온다.
        Vector3 t_orbScale = t_orbTr != null ? t_orbTr.localScale : Vector3.one;
        if (t_orbTr != null)
            t_orbTr.DOScale(t_orbScale * ORB_DASH_SCALE, t_dash).SetEase(Ease.OutQuad).SetLink(t_orbTr.gameObject);

        await t_mover.DOMove(t_impact, t_dash).SetEase(Ease.InQuad).ToUniTask();

        // 3) 충돌 = 데미지·피격·사망 해결(박치기와 동일 지점). 복귀 모션과 병렬로 흘린다.
        UniTask t_resolve = ResolveHits(_attacker, _defender, _splashView, _effect, _onEffect, _atEffectKw, _afterHit,
            _skipRemain: true);

        async UniTask ReturnHome()
        {
            // 복귀도 "이동"이므로 커진 채로 돌아오고, 제자리에 닿으면서 원래 크기로 줄어든다.
            if (t_orbTr != null)
                t_orbTr.DOScale(t_orbScale, t_return).SetEase(Ease.InQuad).SetLink(t_orbTr.gameObject);

            await t_mover.DOMove(t_home, t_return).SetEase(Ease.OutQuad).ToUniTask();

            _attacker.SetArmedVfx(false);   // 박치기의 반동 지점과 같은 의미 — 복귀가 끝나면 무장 해제
            _attacker.FadeView(1f, t_morph);   // 구체가 카드 자리로 돌아오며 카드가 다시 나타난다
            await UniTask.Delay((int)(t_morph * 1000));

            // 반납 전 확정 원복(트윈이 중간에 끊겼을 수 있다) — 풀에서 다시 나올 때 커진 채로 나오지 않게.
            if (t_orbTr != null)
            {
                t_orbTr.DOKill();
                t_orbTr.localScale = t_orbScale;
            }

            t_orb.Release();   // 자기반납형 프리팹이면 무동작
        }

        await UniTask.WhenAll(t_resolve, ReturnHome());

        // 페이드가 중간에 끊겼을 경우를 대비한 확정 복원(다음 연출이 반투명 카드로 시작하지 않게).
        _attacker.FadeView(1f, 0f);
        t_atk.position = t_home;
    }

    // ── 공유: 히트/반격/사망/공격후 해결 ────────────────────────────────
    // 데미지 적용(_onEffect)부터 사망 연출·afterHit까지. 두 연출이 동일 순서/타이밍을 쓰도록 단일화.
    // 이동/카메라 같은 프레젠테이션은 호출부가 담당, 여기선 상태변화 반영 연출만.
    // 카드 총 체력(hp+bonusHp). 뷰/카드 없으면 0.
    static int HpTotal(CardView _v) => _v?.BoundCard != null ? _v.BoundCard.hp + _v.BoundCard.bonusHp : 0;

    // 무쌍 광역 타격의 화면 흔들림 세기(주 대상 대비). 광역은 피해도 절반이라 같은 세기로 흔들면
    // 두 번째 타격이 더 세게 읽힌다(감쇠가 덜 끝난 상태에서 최대값으로 재시작되므로).
    const float SPLASH_SHAKE_SCALE = 0.7f;

    /// <summary>_beforeSplashHit를 주면 주 대상 → (그 콜백) → 광역 대상 순으로 **표시가 갈라진다**(무쌍 연출).
    /// _hitStop은 각 대상의 피격 표시 직전에 끼우는 멈칫이다. **얼마나 멈추는지가 아니라 무엇을 멈추는지까지
    /// 호출부가 정한다** — 카드만 멈추고 자기가 띄운 이펙트는 계속 흐르면 "멈춘 순간"으로 안 읽힌다.
    /// 둘 다 기본값이면 기존과 완전히 같은 동시 재생이다.
    /// **데미지 적용(_onEffect)은 어느 경우에도 여기 한 번뿐** — 갈라지는 건 표시 순서지 규칙이 아니다.</summary>
    static async UniTask ResolveHits(CardView _attacker, CardView _defender, CardView _splashView,
        AttackEffect _effect, Action _onEffect, CardKeyword _atEffectKw, Func<UniTask> _afterHit,
        bool _skipRemain = false, Func<UniTask> _beforeSplashHit = null, Func<UniTask> _hitStop = null)
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

        // 이 한 방이 판을 끝내는가 — **소비하지 않는 조회**다(_onEffect 안의 Arm 이후라 유효).
        // 아래 표시들을 기다릴지 말지가 여기서 갈린다: 결정타는 부딪힌 프레임을 그대로 얼려야 하므로
        // 표시를 흘려보내고 곧장 TryBegin으로 간다.
        bool t_finishing = BattleFinisher.WillFinish;

        // 피격 방향: 맞은 쪽은 "때린 쪽"을 넘긴다(먼지 등이 반대로 튀게). 반격은 방향이 뒤집힌다.
        if (_beforeSplashHit != null && _splashView != null)
        {
            // 순차 표시(무쌍). 반격은 주 대상 히트와 함께 둔다 — 반격은 주 대상이 되받는 것이라
            // 광역 쪽으로 미루면 누가 때렸는지가 어긋난다.
            // 순서가 중요하다: **타격을 먼저 터뜨리고 그 순간을 얼린다.**
            // 멈칫을 앞에 두면 그 시점엔 트윈이 다 끝나 있어 멈출 게 없고, 그냥 대기만 하다 타격이 뜬다.
            //
            // 피격은 기다리지 않는다(.Forget). PlayHitAnim은 오버레이가 들어왔다 빠질 때까지
            // (hitDuration × 2) 잡고 있어서, 기다리면 다음 대상으로 넘어가는 간격이 그만큼 고정돼
            // 호출부가 준 대기값이 무의미해진다.
            // 화면 흔들림은 피격 표시와 같은 프레임. 순차 타격이라 벨 때마다 한 번씩 흔든다.
            // 반격은 주 대상 타격과 한 묶음이므로 여기서 같이 처리된다(따로 흔들지 않는다).
            // 세기 = 피해/최대체력 비율(HitImpact 단일 진실원). 주 대상과 반격 중 **비율이 큰 쪽** 기준 —
            // 한 순간에 겹치는 타격이라 둘을 더하면 연타에서 화면이 폭주한다.
            if (t_defDmg > 0 || t_attackerHit)
                BattleCamera.Shake(HitImpact.ShakeScale01(Mathf.Max(
                    HitImpact.Strength01(t_defDmg, _defender?.BoundCard),
                    HitImpact.Strength01(t_atkDmg, _attacker?.BoundCard))));
            _defender.PlayHitAnim(_damage: t_defDmg, _hitFrom: _attacker).Forget();
            // 공격자가 맞는 건 **반격**이다 — 먼지는 주 타격 쪽에서만 인다(_isCounter).
            if (t_attackerHit) _attacker?.PlayHitAnim(_damage: t_atkDmg, _hitFrom: _defender, _isCounter: true).Forget();
            await HitStop(_hitStop);

            await _beforeSplashHit();

            if (_splashView != null)
            {
                // 광역은 주 대상보다 약하게(고정 감쇠) × 그 대상이 받은 비율 세기.
                if (t_splDmg > 0)
                    BattleCamera.Shake(HitImpact.ShakeScale(t_splDmg, _splashView?.BoundCard) * SPLASH_SHAKE_SCALE);
                _splashView.PlayHitAnim(_damage: t_splDmg, _hitFrom: _attacker).Forget();
            }
            await HitStop(_hitStop);
        }
        else
        {
            // 동시 타격(일반·원거리·시네마·일반 스플래시): 한 순간에 다 맞으므로 흔들림도 한 번,
            // 세기는 그 순간 맞은 대상들의 **비율 중 가장 큰 것** 기준(합산하면 다대상 공격만 과하게 흔들린다).
            if (t_defDmg > 0 || t_splDmg > 0 || t_attackerHit)
                BattleCamera.Shake(HitImpact.ShakeScale01(Mathf.Max(
                    HitImpact.Strength01(t_defDmg, _defender?.BoundCard),
                    Mathf.Max(HitImpact.Strength01(t_splDmg, _splashView?.BoundCard),
                              HitImpact.Strength01(t_atkDmg, _attacker?.BoundCard)))));

            UniTask t_defHit = _splashView != null
                ? UniTask.WhenAll(_defender.PlayHitAnim(_damage: t_defDmg, _hitFrom: _attacker),
                                  _splashView.PlayHitAnim(_damage: t_splDmg, _hitFrom: _attacker))
                : _defender.PlayHitAnim(_damage: t_defDmg, _hitFrom: _attacker);
            UniTask t_atkHit = t_attackerHit
                ? _attacker?.PlayHitAnim(_damage: t_atkDmg, _hitFrom: _defender, _isCounter: true)
                  ?? UniTask.CompletedTask
                : UniTask.CompletedTask;

            // 결정타면 **기다리지 않는다**. 무쌍 경로와 같은 규약이다(위 주석) — 타격을 먼저 터뜨리고
            // 그 프레임을 얼려야 한다. 기다리면 피격 연출(hitDuration×2)이 다 끝난 뒤에 얼어붙어,
            // 부딪힌 순간이 아니라 체력이 다 깎인 뒤에 슬로우가 걸린다.
            if (t_finishing) { t_defHit.Forget(); t_atkHit.Forget(); }
            else             await UniTask.WhenAll(t_defHit, t_atkHit);
        }

        // 결정타에서는 글로우도 기다리지 않는다 — KeywordGlowHold(현재 1.25초)만큼 얼어붙기가 밀린다.
        if (_atEffectKw != CardKeyword.None)
        {
            UniTask t_glow = _attacker?.PlayKeywordGlow(_atEffectKw) ?? UniTask.CompletedTask;
            if (t_finishing) t_glow.Forget();
            else             await t_glow;
        }

        bool t_defenderKilled = _defender.BoundCard != null && _defender.BoundCard.hp <= 0;
        bool t_attackerKilled = _attacker?.BoundCard != null && _attacker.BoundCard.hp <= 0;
        bool t_splashKilled   = _splashView?.BoundCard != null && _splashView.BoundCard.hp <= 0;

        // 이 한 방이 판을 끝냈다면 여기서 강조를 연다 — **죽는 카드의 View와 좌표가 살아 있는 마지막 지점**이다.
        // 아래 사망 연출부터는 카드가 페이드되고, 그 뒤엔 슬롯이 충원 카드에 재바인딩된다.
        // true면 화면이 느려진 채로 돌아오므로 **사망 연출이 그 슬로우 안에서 재생된다**(스케일드 트윈).
        // 끝낸 게 아니면 즉시 false로 빠져 기존 흐름과 완전히 같다.
        try
        {
            bool t_finished = await BattleFinisher.TryBegin(_attacker, _defender, _splashView,
                                                            t_attackerKilled, t_defenderKilled, t_splashKilled);

            // 피니시가 이미 한 박자를 썼으므로 잔여 대기는 건너뛴다(끝난 판에서 빈 시간이 겹치지 않게).
            float t_remain = t_duration - t_hitDelay;
            if (!_skipRemain && !t_finished && t_remain > 0f)
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
        }
        finally
        {
            // 어떤 경로로 빠져나가도 배속은 반드시 정상으로 돌아온다 — 여기를 건너뛰면
            // 전투가 느린 채로 계속된다. 피니시가 안 열렸으면 무동작.
            await BattleFinisher.End();
        }

        // 히트/사망 연출 완료 후, 제자리 복귀 전에 공격후 효과(청소부 heal/OnAfterAttack 등) 실행
        if (_afterHit != null)
            await _afterHit();
    }

    /// <summary>타격 순간의 멈칫. 무엇을 멈출지는 호출부가 준 콜백이 정한다(미지정이면 멈칫 없음).
    /// Time.timeScale을 건드리는 구현은 쓰지 않는다 — 전역 배속을 흔들면 다른 카드의 트윈·대기까지
    /// 같이 늘어나고, 그게 그대로 두 클라의 연출 길이 차이가 된다.</summary>
    static UniTask HitStop(Func<UniTask> _beat)
        => _beat?.Invoke() ?? UniTask.CompletedTask;

    static async UniTask LaunchProjectile(ProjectileData _proj, Transform _attacker, Transform _defender,
                                          float _duration, bool _flipOffset = false, float _timingFactor = 1f)
    {
        if (_proj.prefab == null || _attacker == null || _defender == null) return;

        float t_spawnDelay = _proj.spawnDelay * Mathf.Max(0f, _timingFactor);
        if (t_spawnDelay > 0f)
            await UniTask.Delay((int)(t_spawnDelay * 1000));

        Vector3 t_offset = _flipOffset ? -_proj.localOffset : _proj.localOffset;
        Vector3 t_start  = _attacker.TransformPoint(t_offset);
        Vector3 t_end    = _defender.position;

        GameObject t_proj = UnityEngine.Object.Instantiate(_proj.prefab, t_start, Quaternion.identity);
        Vector3 t_dir = t_end - t_start;
        if (t_dir != Vector3.zero)
            t_proj.transform.right = t_dir.normalized;

        float t_travel = Mathf.Max(0f, _duration - t_spawnDelay);
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
