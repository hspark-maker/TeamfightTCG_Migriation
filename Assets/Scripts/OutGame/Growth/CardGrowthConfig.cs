using System.Collections.Generic;
using UnityEngine;

// 카드 강화 튜닝 데이터
[CreateAssetMenu(fileName = "CardGrowthConfig", menuName = "Card Battle/Card Growth Config")]
public class CardGrowthConfig : ScriptableObject
{
    [Header("전역 기본식 (레벨 오버라이드가 없을 때 적용)")]
    [Tooltip("강화 상한 레벨. 미강화가 Lv1이므로 강화 횟수는 이 값 - 1이다.")]
    [Min(CardGrowth.BaseLevel)] [SerializeField] int maxLevel = 10;
    [Min(0)] [SerializeField] int hpPerLevel = 2;

    [Tooltip("첫 강화(Lv2로 올릴 때)의 골드 비용.")]
    [SerializeField] long baseGoldCost = 100;

    [Tooltip("레벨마다 늘어나는 비용. 레벨 N 비용 = baseGoldCost + (N-2) * 이 값.")]
    [SerializeField] long costGrowthPerLevel = 50;
    [Range(0f, 1f)] [SerializeField] float baseSuccessRate = 1f;
    [Range(0f, 1f)] [SerializeField] float rateDropPerLevel = 0.08f;

    [Header("레벨별 오버라이드 (override 체크한 필드만 기본식을 대체)")]
    [SerializeField] List<GrowthStepOverride> stepOverrides = new List<GrowthStepOverride>();

    // 강화 상한 레벨(바닥 아래 오설정은 바닥으로 보정 = 강화 없음)
    public int MaxLevel => maxLevel < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : maxLevel;

    // 레벨 _level로 올리는 한 스텝(범위 밖이면 false). 바닥 레벨은 강화로 도달하는 레벨이 아니다.
    public bool TryGetStep(int _level, out GrowthStep _step)
    {
        _step = default;
        if (_level <= CardGrowth.BaseLevel || _level > MaxLevel) return false;

        _step = StepAt(_level);
        return true;
    }

    // 레벨 _level까지의 누적 HP 보너스
    public int HpBonusAt(int _level)
    {
        if (_level <= CardGrowth.BaseLevel) return 0;

        int t_top = _level > MaxLevel ? MaxLevel : _level;
        int t_sum = 0;
        for (int t_i = CardGrowth.BaseLevel + 1; t_i <= t_top; t_i++)
        {
            t_sum += StepAt(t_i).HpGain;
        }
        return t_sum;
    }

    GrowthStep StepAt(int _level)
    {
        // 첫 강화(바닥 바로 위)가 곡선의 0번째 칸이다 — 그래야 baseGoldCost·baseSuccessRate가 첫 강화의 값이 된다.
        int t_step = _level - CardGrowth.BaseLevel - 1;

        int   t_hp   = hpPerLevel;
        long  t_cost = baseGoldCost + costGrowthPerLevel * t_step;
        float t_rate = baseSuccessRate - rateDropPerLevel * t_step;

        if (TryGetOverride(_level, out var t_over))
        {
            if (t_over.overrideHpGain)      t_hp   = t_over.hpGain;
            if (t_over.overrideCost)        t_cost = t_over.cost;
            if (t_over.overrideSuccessRate) t_rate = t_over.successRate;
        }

        if (t_cost < 0) t_cost = 0;
        if (t_hp < 0)   t_hp   = 0;

        return new GrowthStep(_level, t_hp, t_cost, Mathf.Clamp01(t_rate));
    }

    bool TryGetOverride(int _level, out GrowthStepOverride _override)
    {
        _override = default;
        if (stepOverrides == null) return false;

        for (int t_i = 0; t_i < stepOverrides.Count; t_i++)
        {
            if (stepOverrides[t_i].level != _level) continue;

            _override = stepOverrides[t_i];
            return true;
        }
        return false;
    }
}

// 레벨 하나의 저작 오버라이드(override 플래그로 "미지정"을 표현)
[System.Serializable]
public struct GrowthStepOverride
{
    [Tooltip("대상 레벨(2 = 첫 강화). 바닥 레벨(1) 이하는 어떤 스텝에도 적용되지 않는다.")]
    public int level;

    public bool overrideHpGain;
    [Min(0)] [Tooltip("이 레벨업으로 얻는 최대 체력 가산분.")]
    public int hpGain;

    public bool overrideCost;
    [Tooltip("이 레벨업의 골드 비용.")]
    public long cost;

    public bool overrideSuccessRate;
    [Range(0f, 1f)] [Tooltip("이 레벨업의 성공률(0~1). 실패하면 비용만 소모되고 레벨은 유지된다.")]
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
