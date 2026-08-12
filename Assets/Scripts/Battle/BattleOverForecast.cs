using System.Collections.Generic;

/// <summary>"이 공격 한 방으로 전투가 끝나는가"를 <b>공격이 나가기 전에</b> 답하는 판정기.
///
/// <para>왜 따로 있나 — 연출(접근 줌·슬로우)은 공격 모션이 시작될 때 켜야 하는데, 그 시점엔
/// 피해가 아직 적용되지 않았다(적용 지점은 AttackSequence.ResolveHits 하나). 그렇다고 보드에
/// 몇 장 남았는지로 대신하면 "마지막 한 장인데 안 죽는" 공격마다 연출이 헛나간다.</para>
///
/// <para><b>규칙을 복제하지 않는다.</b> 피해·치사 판정은 전부 <see cref="CardInstance"/>의 기존
/// 단일 진실원 메서드(<c>AttackDamage</c>/<c>SplashDamage</c>/<c>WouldDieFrom</c>/<c>TakesCounterFrom</c>)를
/// 그대로 부르고, 효과가 개입하는 부분은 효과 자신에게 물어본다
/// (<see cref="BattleEffect.ThornDamage"/>, <see cref="BattleEffect.CanAlterLethalOutcome"/>).
/// 상태는 아무것도 바꾸지 않고 RNG도 소비하지 않는다 — 양 클라가 같은 보드에서 같은 답을 낸다.</para>
///
/// <para><b>확신이 없으면 "안 끝난다"로 답한다.</b> 부활·사망 시 회복처럼 결과를 뒤집을 수 있는 효과가
/// 걸려 있으면 계산을 포기한다. 놓친 결정타는 연출이 한 번 안 나올 뿐이지만, 헛짚은 예측은
/// 매번 눈에 띄는 거짓 연출이 된다.</para></summary>
public static class BattleOverForecast
{
    /// <summary>본 판정을 돌려볼 가치가 있는 보드인가. <b>싸다</b>(리스트 할당 없음) —
    /// 대부분의 공격은 여기서 끝난다.
    ///
    /// <para>대기 카드가 하나라도 있으면 그 편은 이번 공격으로 비지 않는다
    /// (<see cref="BattleField.IsEmpty"/>가 대기열까지 보므로, 전투 종료 기준과 같은 판단이다).
    /// 슬롯 수는 한 번에 죽을 수 있는 최대치로 자른다 — 방어 측은 무쌍 광역까지 2장,
    /// 공격 측은 반격으로 공격자 1장뿐이다.</para></summary>
    public static bool CouldEnd(BattleField _attackerField, BattleField _defenderField, bool _peerless)
    {
        if (_attackerField == null || _defenderField == null) return false;

        int t_maxDefenderKills = _peerless ? 2 : 1;   // 주 대상 + 광역 1

        bool t_defenderCandidate = _defenderField.WaitingCount == 0
                                && _defenderField.ActiveCount <= t_maxDefenderKills;
        bool t_attackerCandidate = _attackerField.WaitingCount == 0
                                && _attackerField.ActiveCount == 1;

        return t_defenderCandidate || t_attackerCandidate;
    }

    /// <summary>이 공격으로 어느 한 편이 전멸하는가. true면 <paramref name="_loserOwner"/>에 지는 편.
    /// <paramref name="_preSelectedSplash"/>는 무쌍 광역 대상 — <b>반드시 이미 선정된 것을 넘긴다</b>
    /// (여기서 다시 뽑으면 MatchRandom 스트림이 어긋나 멀티가 갈라진다).</summary>
    public static bool WillEnd(CardInstance _attacker, CardInstance _defender,
                               BattleField _attackerField, BattleField _defenderField,
                               CardInstance _preSelectedSplash, out int _loserOwner)
    {
        _loserOwner = -1;
        if (_attacker == null || _defender == null || _attackerField == null || _defenderField == null)
            return false;

        // ── 피해 스냅샷: AttackProcessor.Execute의 '피해 적용 전' 스냅샷과 같은 순서·같은 함수 ──
        bool t_peerless     = _attacker.HasKeyword(CardKeyword.Peerless);
        int  t_atkDmg       = _attacker.AttackDamage();
        int  t_splashDmg    = t_peerless ? _attacker.SplashDamage() : 0;
        // 일반 강화(Lv10): 기본타 뒤 추가타. 주 대상에게만 들어가고 반격·[Attacked]는 없다(AttackProcessor와 같은 계약).
        bool t_takesCounter = _attacker.TakesCounterFrom(_defender);
        int  t_ctrDmg       = t_takesCounter ? _defender.AttackDamage() : 0;
        int  t_thorn        = _defender.data?.passive?.ThornDamage ?? 0;
        (_, bool t_attackerDiesBeforeEnhance) = _attacker.PreviewDamageChain(t_ctrDmg, t_thorn, false);
        int  t_enhanceDmg   = _attacker.HasVanillaEnhance && !t_attackerDiesBeforeEnhance
            ? _attacker.VanillaEnhanceDamage()
            : 0;

        CardInstance t_splash = t_peerless && _preSelectedSplash != null && t_splashDmg > 0
            ? _preSelectedSplash : null;

        // 공격자가 받는 것은 반격과 가시 둘 다 '감소 없음' 소스라 합산해도 실제와 같다 —
        // **단 무적은 예외**다. 실제로는 첫 피해가 무적을 태우고 두 번째가 들어가는데,
        // 합산 한 번으로는 그 순서를 재현할 수 없다. 그 경우는 아래 불확실 게이트가 잡는다.
        // ── 지는 편 판정 ──
        // 교활(공격자가 대기열로 빠짐)은 공격 측 후보가 WaitingCount==0 전제라 자연히 배제된다
        // (스왑할 대기 카드가 없으면 스왑도 없다).
        bool t_defenderWiped = WipesField(_defenderField, _defender, t_atkDmg, true, t_splash, t_splashDmg, t_enhanceDmg);
        bool t_attackerWiped = (t_ctrDmg > 0 || t_thorn > 0)
                            && WipesField(_attackerField, _attacker, t_ctrDmg, false, null, 0, t_thorn);

        if (!t_defenderWiped && !t_attackerWiped) return false;

        // 동시 전멸은 **로컬 기준 상대 편을 패자로** 잡는다 — TurnRunner.CheckGameOver가 적 필드를 먼저 보고
        // 로컬 승리로 판정하므로, 여기서 다르게 고르면 예측과 실제 결과가 엇갈린다
        // (BattleFinisher.Arm도 같은 편향을 따라간다. 동시 전멸 정책이 정해지면 세 곳을 같이 고칠 것).
        if (t_defenderWiped && t_attackerWiped)
        {
            _loserOwner = _attackerField.OwnerIndex == TurnState.LocalOwnerIndex
                ? _defenderField.OwnerIndex
                : _attackerField.OwnerIndex;
            return true;
        }

        _loserOwner = t_defenderWiped ? _defenderField.OwnerIndex : _attackerField.OwnerIndex;
        return true;
    }

    /// <summary>이 필드가 이번 공격으로 통째로 비는가. 슬롯의 <b>모든</b> 카드가 죽어야 하고,
    /// 죽는 방식이 확실해야 한다.</summary>
    static bool WipesField(BattleField _field, CardInstance _primary, int _primaryDmg, bool _primaryIsAttackHit,
                           CardInstance _splash, int _splashDmg, int _primaryExtraDmg)
    {
        if (_field.WaitingCount > 0) return false;   // 대기 카드가 채우면 전멸이 아니다

        // 리스트는 한 번만 만든다 — GetActiveCards()는 부를 때마다 새로 할당한다.
        List<CardInstance> t_cards = _field.GetActiveCards();
        if (t_cards.Count == 0) return false;        // 이미 빈 필드 = 이 공격이 끝낸 게 아니다
        if (HasLethalAlteringEffect(_field, t_cards)) return false;

        foreach (CardInstance t_c in t_cards)
        {
            if (t_c == null) continue;

            // 무리 선피해([BeforeAttack])로 이미 hp 0인데 시체 정리 전인 카드가 슬롯에 남아 있을 수 있다.
            if (!t_c.IsAlive) continue;

            if (t_c == _primary)
            {
                // 무적은 이번 피해를 소멸시키고 살아남는다. WouldDieFrom이 이미 false를 내지만,
                // 합산 판정(반격+가시)에서는 순서를 재현할 수 없으므로 명시적으로 포기한다.
                if (_primaryExtraDmg <= 0 && t_c.HasKeyword(CardKeyword.Invincible)) return false;
                // 강화 추가타가 있으면 두 직격을 폴딩해 본다(순서·감소·덩치 소진이 실제와 같아야 한다).
                // 추가타가 없으면(0) 종전과 같은 한 번짜리 판정으로 환원된다.
                bool t_primaryDies = _primaryExtraDmg > 0
                    ? t_c.PreviewDamageChain(_primaryDmg, _primaryExtraDmg, _primaryIsAttackHit).dies
                    : t_c.WouldDieFrom(_primaryDmg, _primaryIsAttackHit);
                if (!t_primaryDies) return false;
                continue;
            }

            if (t_c == _splash)
            {
                if (!t_c.WouldDieFrom(_splashDmg, true)) return false;
                continue;
            }

            return false;   // 이번 공격이 건드리지 않는 카드가 남아 있다
        }
        return true;
    }

    /// <summary>이 필드에 사망 결과를 뒤집을 수 있는 효과(부활·사망 시 회복)가 걸려 있는가.
    /// 카드 패시브와 이 덱의 활성 시너지를 모두 본다.</summary>
    static bool HasLethalAlteringEffect(BattleField _field, List<CardInstance> _active)
    {
        foreach (CardInstance t_c in _active)
            if (t_c?.data?.passive != null && t_c.data.passive.CanAlterLethalOutcome) return true;

        SynergyState t_state = _field.Synergy;
        if (t_state == null) return false;

        foreach (ActiveSynergy t_active in t_state.Active)
        {
            SynergyEffect[] t_effects = t_active?.Tier?.effects;
            if (t_effects == null) continue;

            foreach (SynergyEffect t_e in t_effects)
                if (t_e != null && t_e.CanAlterLethalOutcome) return true;
        }
        return false;
    }
}
