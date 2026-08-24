using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적(AI) 공격 선택의 단일 진실원. 공격자(아군) 선택과 타깃(적군) 선택 규칙을 여기에만 둔다 —
/// <see cref="EnemyTurn"/>은 이 함수만 부른다(튜토리얼 자유공격 스텝 포함).
///
/// 규칙
///  · 공격자: **3계층 사다리**로 후보군(pool)을 먼저 정하고, 그 안에서 체력 가중 룰렛으로 뽑는다.
///      1계층 — 체력 30% 이상인 **키워드 카드**(<see cref="AttackKeywords"/>)
///      2계층 — 1계층이 비면, 체력 30% 이상인 나머지 카드
///      3계층 — 전원 빈사(30% 미만)면 전체. 가중치를 뒤집어 **체력이 낮을수록** 먼저 나간다
///              (죽어 슬롯을 비우면 새 카드가 보충돼 보드가 회전한다).
///  · 타깃  : 유효 타깃 중 **실효 체력이 가장 낮은** 카드(= 가장 잘 죽는 카드) 우선.
///
/// 이 게임에서 공격력은 곧 현재 체력이다(<see cref="CardInstance.AttackDamage"/>) — 체력 가중치는
/// 생존력이자 화력 가중치다. 유일한 예외인 도발(체력의 절반)은 그래서 키워드 계층에서 뺐다.
///
/// 결정론: 랜덤은 <see cref="MatchRandom"/>만 쓰고, 공격자 1회 선택당 **정확히 1회** 소비한다
/// (계층이 어디로 갈리든 소비 횟수는 같다). 룰렛은 후보 리스트 순서(슬롯 오름차순)를 그대로 훑는다.
/// 타깃 선택은 랜덤을 아예 소비하지 않는다(동점은 슬롯 오름차순으로 깬다).
/// </summary>
public static class EnemyAi
{
    /// <summary>빈사 기준. 실효 체력 비율이 이 값 <b>미만</b>이면 빈사다 — 정확히 30%는 정상 후보로 남는다.</summary>
    public const float LowHpRatio = 0.30f;

    /// <summary>체력 가중치 하한. 0을 주지 않는 이유: 가중치 0인 카드는 룰렛에서 영원히 안 뽑혀,
    /// 빈사만 남은 판에서 후보 전체가 0이 되면 턴이 통째로 비어 버린다.</summary>
    public const int HpWeightMin = 1;

    /// <summary>체력 가중치 상한.</summary>
    public const int HpWeightMax = 10;

    /// <summary>"키워드 카드"(1계층) 판정 대상.
    ///
    /// 도발은 뺐다 — 공격력이 체력의 절반이라 같은 체력이면 실제 화력이 낮다(과대평가 방지).
    /// 힐러도 아니다: 공격에 내보내는 것보다 살아서 회복시키는 쪽이 이득이다.
    /// 표식·무적·추가생명력은 방어 성능이라 공격자 우선순위와 무관하다.</summary>
    public static readonly CardKeyword[] AttackKeywords =
    {
        CardKeyword.Ranged,
        CardKeyword.Execution,
        CardKeyword.Peerless,
        CardKeyword.Cunning,
    };

    /// <summary>공격 계층에 올릴 키워드를 하나라도 가졌는가.
    /// 판정은 <see cref="CardInstance.HasKeyword"/> — 시너지·패시브가 런타임에 얹어 준 키워드도 그대로 반영된다.
    /// (여러 비트를 한 번에 넘기지 않는다. HasKeyword는 HasFlag 기반이라 마스크를 주면 "전부 보유"가 된다.)</summary>
    public static bool HasAttackKeyword(CardInstance _card)
    {
        if (_card == null) return false;
        for (int i = 0; i < AttackKeywords.Length; i++)
            if (_card.HasKeyword(AttackKeywords[i])) return true;
        return false;
    }

    /// <summary>실효 체력 비율(0~1). 분자에 <c>bonusHp</c>를 넣는 이유: 추가생명력·덩치는 피해를 먼저 먹는
    /// 껍데기라 실제 생존력에 포함된다. 분모는 인스턴스의 <c>maxHp</c>(= data.maxHp + 영구 강화분)로,
    /// bonusHp는 여기 안 들어가므로 비율이 1을 넘을 수 있다 → 상한 클램프한다.
    /// 힐러 오버힐로 hp가 maxHp를 넘는 경우도 같은 클램프가 받는다.</summary>
    public static float EffectiveHpRatio(CardInstance _card)
    {
        if (_card == null || _card.maxHp <= 0) return 0f;
        return Mathf.Clamp01((float)(_card.hp + _card.bonusHp) / _card.maxHp);
    }

    /// <summary>빈사인가(실효 체력 비율 &lt; <see cref="LowHpRatio"/>).</summary>
    public static bool IsLowHp(CardInstance _card) => EffectiveHpRatio(_card) < LowHpRatio;

    /// <summary>체력 가중치 1~10. 빈사면 1로 고정, 그 외에는 비율을 10칸으로 환산한다.
    /// 하한이 1이라 "가중치 0이라 절대 안 뽑히는 카드"는 나오지 않는다.</summary>
    public static int HpWeight(CardInstance _card)
    {
        if (_card == null) return HpWeightMin;

        float t_ratio = EffectiveHpRatio(_card);
        if (t_ratio < LowHpRatio) return HpWeightMin;
        return Mathf.Clamp(Mathf.RoundToInt(t_ratio * HpWeightMax), HpWeightMin, HpWeightMax);
    }

    /// <summary>공격자 선택 계층. 값이 작을수록 우선한다.</summary>
    public enum AttackerTier
    {
        /// <summary>체력 30% 이상 + 공격 키워드 보유.</summary>
        KeywordHealthy,
        /// <summary>체력 30% 이상(키워드 무관).</summary>
        Healthy,
        /// <summary>전원 빈사 — 전체가 후보이고 약한 카드가 먼저 나간다.</summary>
        Desperate,
    }

    /// <summary>이 카드가 해당 계층의 후보인가.</summary>
    static bool InTier(CardInstance _card, AttackerTier _tier)
    {
        // 죽었는데 아직 정리 전인 카드는 어느 계층에도 넣지 않는다.
        // GetActiveCards()는 hp 0 카드를 포함하는데, 빈사 계층은 약할수록 가중치가 커서
        // 걸러 두지 않으면 시체가 최우선 공격자로 뽑히고 EnemyTurn의 IsAlive 게이트에 걸려 턴이 통째로 빈다.
        if (_card == null || !_card.IsAlive) return false;
        if (_tier == AttackerTier.Desperate) return true;

        bool t_healthy = !IsLowHp(_card);
        if (_tier == AttackerTier.Healthy) return t_healthy;
        return t_healthy && HasAttackKeyword(_card);   // KeywordHealthy
    }

    /// <summary>계층 안에서 쓰는 룰렛 가중치. 항상 1 이상이라 후보가 있으면 합도 반드시 양수다.
    ///
    /// 빈사 계층만 가중치를 뒤집는다(<c>HpWeightMax + 1 - w</c>): 어차피 다음 공격에 죽을 카드를 먼저
    /// 내보내 슬롯을 비우고 새 카드를 받는 편이 낫다. 성한 카드를 아끼는 게 아니라 <b>보드를 회전</b>시키는 수다.</summary>
    public static int SelectWeight(CardInstance _card, AttackerTier _tier)
    {
        int t_w = HpWeight(_card);
        return _tier == AttackerTier.Desperate ? HpWeightMax + HpWeightMin - t_w : t_w;
    }

    /// <summary>후보 중 실제로 쓸 계층을 정한다. 위에서부터 훑어 비어 있지 않은 첫 계층.
    /// <see cref="AttackerTier.Desperate"/>는 모두를 포함하므로 후보가 하나라도 있으면 반드시 성립한다.</summary>
    public static AttackerTier ResolveTier(IReadOnlyList<CardInstance> _candidates)
    {
        if (HasAny(_candidates, AttackerTier.KeywordHealthy)) return AttackerTier.KeywordHealthy;
        if (HasAny(_candidates, AttackerTier.Healthy))        return AttackerTier.Healthy;
        return AttackerTier.Desperate;
    }

    static bool HasAny(IReadOnlyList<CardInstance> _candidates, AttackerTier _tier)
    {
        for (int i = 0; i < _candidates.Count; i++)
            if (InTier(_candidates[i], _tier)) return true;
        return false;
    }

    /// <summary>공격자 1장을 고른다. 후보가 비면 null.
    ///
    /// 계층을 먼저 확정한 뒤 그 계층 안에서만 가중 룰렛을 돌린다 —
    /// <see cref="MatchRandom"/> 소비는 어느 계층이든 정확히 1회다(시드 재현성 유지).</summary>
    public static CardInstance PickAttacker(IReadOnlyList<CardInstance> _candidates)
    {
        if (_candidates == null || _candidates.Count == 0) return null;

        AttackerTier t_tier = ResolveTier(_candidates);

        int t_total = 0;
        for (int i = 0; i < _candidates.Count; i++)
            if (InTier(_candidates[i], t_tier)) t_total += SelectWeight(_candidates[i], t_tier);

        // 후보가 전부 null이거나 전부 죽은 경우에만 성립. 랜덤을 소비하지 않고 빠진다(뽑을 대상 자체가 없다).
        if (t_total <= 0) return null;

        int t_roll = MatchRandom.Range(t_total);
        CardInstance t_last = null;
        for (int i = 0; i < _candidates.Count; i++)
        {
            CardInstance t_c = _candidates[i];
            if (!InTier(t_c, t_tier)) continue;

            t_last  = t_c;
            t_roll -= SelectWeight(t_c, t_tier);
            if (t_roll < 0) return t_c;
        }
        return t_last;   // 부동소수 없는 정수 합이라 도달 불가. 방어용.
    }

    /// <summary>타깃 우선순위 기준값. 낮을수록 먼저 맞는다.
    /// 보너스HP는 피해를 먼저 먹는 껍데기라 실효 체력에 포함한다(치사 판정 <see cref="CardInstance"/>와 같은 기준).</summary>
    public static int EffectiveHp(CardInstance _card)
        => _card == null ? int.MaxValue : _card.hp + _card.bonusHp;

    /// <summary>타깃 1장을 고른다: 실효 체력 최소 → 동점이면 슬롯 오름차순. 랜덤 미소비.
    /// 후보 목록은 반드시 <see cref="BattleField.GetValidTargets"/> 결과여야 한다
    /// (지정 타깃·도발 필터는 규칙 쪽 단독 책임 — 여기서 다시 판단하지 않는다).</summary>
    public static CardInstance PickTarget(IReadOnlyList<CardInstance> _targets)
    {
        if (_targets == null || _targets.Count == 0) return null;

        CardInstance t_best = null;
        for (int i = 0; i < _targets.Count; i++)
        {
            CardInstance t_c = _targets[i];
            if (t_c == null) continue;
            if (t_best == null) { t_best = t_c; continue; }

            int t_cmp = EffectiveHp(t_c).CompareTo(EffectiveHp(t_best));
            if (t_cmp < 0 || (t_cmp == 0 && t_c.slotIndex < t_best.slotIndex)) t_best = t_c;
        }
        return t_best;
    }
}
