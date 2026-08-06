using System.Collections.Generic;
using UnityEngine;

// 카드 강화 튜닝 데이터
[CreateAssetMenu(fileName = "CardGrowthConfig", menuName = "Card Battle/Card Growth Config")]
public class CardGrowthConfig : ScriptableObject
{
    [Header("전역 기본식 (레벨 오버라이드가 없을 때 적용)")]
    [Tooltip("강화 상한 레벨. 미강화가 Lv1이므로 강화 횟수는 이 값 - 1이다(11 = 10강화 = 표의 hp10까지).")]
    [Min(CardGrowth.BaseLevel)] [SerializeField] int maxLevel = 11;
    [Min(0)] [SerializeField] int hpPerLevel = 2;

    [Tooltip("첫 강화(Lv2로 올릴 때)의 골드 비용.")]
    [SerializeField] long baseGoldCost = 100;

    [Tooltip("레벨마다 늘어나는 비용. 레벨 N 비용 = baseGoldCost + (N-2) * 이 값.")]
    [SerializeField] long costGrowthPerLevel = 50;
    [Range(0f, 1f)] [SerializeField] float baseSuccessRate = 1f;
    [Range(0f, 1f)] [SerializeField] float rateDropPerLevel = 0.08f;

    [Header("진화 레벨 (전역 — 카드 SO에 적지 않는다)")]
    // 키워드 해금 레벨은 여기 없다. 카드마다 다르므로 CardData.keywordUnlockLevel이 소유한다.
    [Tooltip("1차 진화 레벨. 도달하면 진화 단계 1 + 시너지 기능이 열린다.")]
    [Min(CardGrowth.BaseLevel)] [SerializeField] int firstEvolutionLevel = 5;

    [Tooltip("2차 진화 레벨. 도달하면 진화 단계 2 + 키워드 강화.")]
    [Min(CardGrowth.BaseLevel)] [SerializeField] int secondEvolutionLevel = 10;

    [Header("레벨별 상세 (비어 있는 레벨은 위 기본식으로 계산)")]
    [Tooltip("레벨 하나하나의 체력 증가·비용·성공률. 레벨당 체력을 다르게 주려면 여기에 행을 채운다.")]
    [SerializeField] List<GrowthLevelStep> levelSteps = new List<GrowthLevelStep>();

    // 강화 상한 레벨(바닥 아래 오설정은 바닥으로 보정 = 강화 없음)
    public int MaxLevel => maxLevel < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : maxLevel;

    public int FirstEvolutionLevel  => firstEvolutionLevel;
    public int SecondEvolutionLevel => secondEvolutionLevel;

    /// <summary>레벨 _level에서의 진화 단계(0=미진화). 2차가 1차보다 낮게 설정돼도 높은 쪽이 이긴다 —
    /// 오설정으로 단계가 역행하지 않게 도달한 관문 중 가장 높은 단계를 준다.</summary>
    public int EvolutionStageAt(int _level)
    {
        int t_stage = 0;
        if (_level >= firstEvolutionLevel)  t_stage = 1;
        if (_level >= secondEvolutionLevel) t_stage = 2;
        return t_stage > CardData.MaxEvolutionStage ? CardData.MaxEvolutionStage : t_stage;
    }

    /// <summary>레벨 _level에서 **실제로 켜져 있는** 카드 키워드. 카드의 기본 키워드에 더하는 값이 아니라
    /// 그것을 대체하는 값이다 — 키워드는 해금 전까지 아예 없는 것으로 친다.
    /// 해금 레벨 미지정(0)이면 처음부터 열려 있다(해금 레벨을 아직 안 정한 카드 = 기본 키워드 카드).</summary>
    public CardKeyword UnlockedKeywordsAt(CardData _card, int _level)
    {
        if (_card == null) return CardKeyword.None;
        return _level >= _card.keywordUnlockLevel ? _card.keywords : CardKeyword.None;
    }

    /// <summary>1차 진화(= 시너지 기능 해금) 도달 여부.</summary>
    public bool SynergyUnlockedAt(int _level) => _level >= firstEvolutionLevel;

    // 레벨 _level로 올리는 한 스텝(범위 밖이면 false). 바닥 레벨은 강화로 도달하는 레벨이 아니다.
    public bool TryGetStep(CardData _card, int _level, out GrowthStep _step)
    {
        _step = default;
        if (_level <= CardGrowth.BaseLevel || _level > MaxLevel) return false;

        _step = StepAt(_card, _level);
        return true;
    }

    /// <summary>레벨 _level에서의 최대 체력(절대값). 카드 표(CardData.maxHpByLevel)가 값을 적어둔 레벨은 그 값이 곧
    /// 총 체력이고, 비워둔 레벨만 직전 총 체력에 전역 증가분을 더해 잇는다 — 표의 빈칸이 레벨 하나만 건너뛰게 하려면
    /// 증가분이 아니라 이렇게 레벨을 아래에서부터 훑어야 한다.</summary>
    public int MaxHpAt(CardData _card, int _level)
    {
        if (_card == null) return 0;

        int t_top = Mathf.Clamp(_level, CardGrowth.BaseLevel, MaxLevel);
        int t_hp  = _card.maxHp;                             // Lv1 기준 체력은 표의 maxHp 열이 소유한다
        for (int t_i = CardGrowth.BaseLevel + 1; t_i <= t_top; t_i++)
        {
            t_hp = _card.TryGetMaxHp(t_i, out int t_authored) ? t_authored : t_hp + BaseHpGainAt(t_i);
        }
        return t_hp;
    }

    // 레벨 _level까지의 누적 HP 보너스
    public int HpBonusAt(CardData _card, int _level)
    {
        if (_level <= CardGrowth.BaseLevel) return 0;

        int t_top = _level > MaxLevel ? MaxLevel : _level;

        // 카드를 못 찾은 경로(카탈로그 미준비)는 절대값 표를 읽을 수 없다 — 전역 증가분만 누적한다.
        if (_card == null)
        {
            int t_sum = 0;
            for (int t_i = CardGrowth.BaseLevel + 1; t_i <= t_top; t_i++) t_sum += BaseHpGainAt(t_i);
            return t_sum;
        }

        return MaxHpAt(_card, t_top) - _card.maxHp;
    }

    // 표가 비어 있을 때 쓰는 레벨당 증가분. levelSteps에 행이 있으면 그 값이 전역 기본선이 된다.
    int BaseHpGainAt(int _level)
    {
        int t_hp = TryGetLevelStep(_level, out var t_row) ? t_row.hpGain : hpPerLevel;
        return t_hp < 0 ? 0 : t_hp;
    }

    GrowthStep StepAt(CardData _card, int _level)
    {
        // 첫 강화(바닥 바로 위)가 곡선의 0번째 칸이다 — 그래야 baseGoldCost·baseSuccessRate가 첫 강화의 값이 된다.
        int t_step = _level - CardGrowth.BaseLevel - 1;

        long  t_cost = baseGoldCost + costGrowthPerLevel * t_step;
        float t_rate = baseSuccessRate - rateDropPerLevel * t_step;

        if (TryGetLevelStep(_level, out var t_row))
        {
            if (t_row.cost        > 0)    t_cost = t_row.cost;          // 0 이하 = 미지정 → 기본식
            if (t_row.successRate >= 0f)  t_rate = t_row.successRate;   // 음수 = 미지정 → 기본식
        }

        // 체력은 절대값 곡선의 차분이다. 여기서 따로 정하면 표와 두 갈래가 된다.
        int t_hp = HpBonusAt(_card, _level) - HpBonusAt(_card, _level - 1);

        if (t_cost < 0) t_cost = 0;
        if (t_hp < 0)   t_hp   = 0;

        return new GrowthStep(_level, t_hp, t_cost, Mathf.Clamp01(t_rate));
    }

    bool TryGetLevelStep(int _level, out GrowthLevelStep _row)
    {
        _row = default;
        if (levelSteps == null) return false;

        for (int t_i = 0; t_i < levelSteps.Count; t_i++)
        {
            if (levelSteps[t_i].level != _level) continue;

            _row = levelSteps[t_i];
            return true;
        }
        return false;
    }
}

/// <summary>레벨 하나의 저작 값. <see cref="hpGain"/>은 **카드 표가 그 레벨을 비워뒀을 때의 기본선**이다 —
/// 카드별 체력은 표의 hp0~hp10(총 체력)이 소유하고 여기서는 못 이긴다.
/// 비용·성공률은 미지정을 허용한다(각각 0 이하 / 음수 = 기본식 사용).</summary>
[System.Serializable]
public struct GrowthLevelStep
{
    [Tooltip("대상 레벨(2 = 첫 강화). 바닥 레벨(1) 이하는 어떤 스텝에도 적용되지 않는다.")]
    public int level;

    [Min(0)] [Tooltip("카드 표에 그 레벨의 체력이 없을 때 얻는 최대 체력 가산분.")]
    public int hpGain;

    [Tooltip("이 레벨업의 골드 비용. 0 이하면 기본식을 쓴다.")]
    public long cost;

    [Range(-1f, 1f)] [Tooltip("이 레벨업의 성공률(0~1). 음수면 기본식을 쓴다. 실패해도 비용만 소모되고 레벨은 유지된다.")]
    public float successRate;
}

// 레벨 하나의 파생 스냅샷(CardGrowthConfig가 기본식+오버라이드로 계산해 내주는 값)
public readonly struct GrowthStep
{
    public readonly int Level;
    public readonly int HpGain;
    // 성공·실패 무관하게 소모되는 골드
    public readonly long Cost;
    public readonly float SuccessRate;

    public GrowthStep(int _level, int _hpGain, long _cost, float _successRate)
    {
        Level       = _level;
        HpGain      = _hpGain;
        Cost        = _cost;
        SuccessRate = _successRate;
    }
}
