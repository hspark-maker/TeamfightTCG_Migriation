using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적(AI) 공격 선택의 단일 진실원. 공격자(아군) 선택과 타깃(적군) 선택 규칙을 여기에만 둔다 —
/// <see cref="EnemyTurn"/>은 이 함수만 부른다(튜토리얼 자유공격 스텝 포함).
///
/// 규칙
///  · 공격자: 키워드 가중치 합 + 체력 가중치(0~10)로 룰렛 선택. 가중치가 클수록 자주 공격한다.
///  · 타깃  : 유효 타깃 중 **실효 체력이 가장 낮은** 카드(= 가장 잘 죽는 카드) 우선.
///
/// 결정론: 랜덤은 <see cref="MatchRandom"/>만 쓰고, 룰렛은 후보 리스트 순서(슬롯 오름차순)를 그대로 훑는다.
/// 타깃 선택은 랜덤을 아예 소비하지 않는다(동점은 슬롯 오름차순으로 깬다).
/// </summary>
public static class EnemyAi
{
    // ── 공격자 가중치 표 ──────────────────────────────────────────────
    // 여러 키워드를 가진 카드는 **합산**한다(원거리+처형 = 20). 키워드가 많을수록 공격 가치가 크다는 전제.
    public const int WeightBase      = 0;   // 키워드 없음
    public const int WeightHealer    = 0;   // 힐러: 공격보다 살아 있는 쪽이 이득
    public const int WeightTaunt     = 2;
    public const int WeightCunning   = 5;
    public const int WeightRanged    = 10;
    public const int WeightExecution = 10;
    public const int WeightPeerless  = 10;

    /// <summary>체력 가중치 상한. 현재 체력 / 최대 체력 비율을 0~이 값으로 환산해 키워드 가중치에 더한다.</summary>
    public const int HpWeightMax = 10;

    /// <summary>키워드 가중치 합(체력 몫 제외). 판정은 <see cref="CardInstance.HasKeyword"/> —
    /// 시너지·패시브가 런타임에 얹어 준 키워드도 그대로 반영된다.</summary>
    public static int KeywordWeight(CardInstance _card)
    {
        if (_card == null) return 0;

        int t_w = WeightBase;
        if (_card.HasKeyword(CardKeyword.Healer))    t_w += WeightHealer;
        if (_card.HasKeyword(CardKeyword.Taunt))     t_w += WeightTaunt;
        if (_card.HasKeyword(CardKeyword.Cunning))   t_w += WeightCunning;
        if (_card.HasKeyword(CardKeyword.Ranged))    t_w += WeightRanged;
        if (_card.HasKeyword(CardKeyword.Execution)) t_w += WeightExecution;
        if (_card.HasKeyword(CardKeyword.Peerless))  t_w += WeightPeerless;
        return t_w;
    }

    /// <summary>체력 가중치. 최대 체력 대비 현재 체력 비율을 0~<see cref="HpWeightMax"/>로 환산.
    /// 기준은 인스턴스의 <c>maxHp</c>(= data.maxHp + 강화분)다 — 공유 에셋 값이 아니라 실제 상한.
    /// 힐러 오버힐로 hp가 maxHp를 넘을 수 있어 상한 클램프가 필요하다.</summary>
    public static int HpWeight(CardInstance _card)
    {
        if (_card == null || _card.maxHp <= 0) return 0;
        float t_ratio = (float)_card.hp / _card.maxHp;
        return Mathf.Clamp(Mathf.RoundToInt(t_ratio * HpWeightMax), 0, HpWeightMax);
    }

    /// <summary>공격자 선택에 쓰는 최종 가중치 = 키워드 합 + 체력 몫. 음수는 나오지 않는다.</summary>
    public static int AttackerWeight(CardInstance _card)
        => _card == null ? 0 : Mathf.Max(0, KeywordWeight(_card) + HpWeight(_card));

    /// <summary>타깃 우선순위 기준값. 낮을수록 먼저 맞는다.
    /// 보너스HP는 피해를 먼저 먹는 껍데기라 실효 체력에 포함한다(치사 판정 <see cref="CardInstance"/>와 같은 기준).</summary>
    public static int EffectiveHp(CardInstance _card)
        => _card == null ? int.MaxValue : _card.hp + _card.bonusHp;

    /// <summary>공격자 1장을 가중치 룰렛으로 고른다. 후보가 비면 null.
    /// 전원 가중치 0(예: 힐러 하나만 남고 체력 몫도 0으로 반올림)이면 균등 추첨으로 떨어진다 —
    /// 여기서 null을 돌려주면 턴이 통째로 비어 버린다.</summary>
    public static CardInstance PickAttacker(IReadOnlyList<CardInstance> _candidates)
    {
        if (_candidates == null || _candidates.Count == 0) return null;

        int t_total = 0;
        for (int i = 0; i < _candidates.Count; i++)
            t_total += AttackerWeight(_candidates[i]);

        if (t_total <= 0) return _candidates[MatchRandom.Range(_candidates.Count)];

        int t_roll = MatchRandom.Range(t_total);
        for (int i = 0; i < _candidates.Count; i++)
        {
            t_roll -= AttackerWeight(_candidates[i]);
            if (t_roll < 0) return _candidates[i];
        }
        return _candidates[_candidates.Count - 1];   // 부동소수 없는 정수 합이라 도달 불가. 방어용.
    }

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
