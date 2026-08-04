using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카드 강화·진화 튜닝 데이터. 전역 기본식으로 전 레벨을 덮고, 손봐야 하는 레벨만 오버라이드로 찍는다
/// (CollectionLayoutConfig의 "전역 기본 + 개별 오버라이드"와 같은 결).
/// 성장 곡선을 아는 것은 이 SO 하나뿐 — 전투는 결과값(CardGrowth)만 받는다.
/// </summary>
[CreateAssetMenu(fileName = "CardGrowthConfig", menuName = "Card Battle/Card Growth Config")]
public class CardGrowthConfig : ScriptableObject
{
    [Header("전역 기본식 (레벨 오버라이드가 없을 때 적용)")]
    [Min(0)] [SerializeField] int maxLevel = 10;               // 강화 상한 레벨. 도달하면 더 강화할 수 없다.
    [Min(0)] [SerializeField] int hpPerLevel = 2;              // 레벨업 1회당 최대 체력 가산분.
    // long 필드에 [Min]을 붙이지 않는다 — Unity MinDrawer가 intValue로 처리해 값이 잘린다(BattleReward·RankConfig 선례).
    [Tooltip("레벨 1로 올릴 때의 골드 비용.")]
    [SerializeField] long baseGoldCost = 100;
    [Tooltip("레벨마다 늘어나는 비용. 레벨 N 비용 = baseGoldCost + (N-1) * 이 값.")]
    [SerializeField] long costGrowthPerLevel = 50;
    [Range(0f, 1f)] [SerializeField] float baseSuccessRate = 1f;      // 레벨 1의 성공률.
    [Range(0f, 1f)] [SerializeField] float rateDropPerLevel = 0.08f;  // 레벨마다 깎이는 성공률. 레벨 N = base - (N-1) * 이 값(0~1 클램프).

    [Header("레벨별 오버라이드 (override 체크한 필드만 기본식을 대체)")]
    [SerializeField] List<GrowthStepOverride> stepOverrides = new List<GrowthStepOverride>();

    // 기본 게이트를 필드 초기화자로 코드가 보증한다 — SO 미배선(CreateInstance fallback)에서도 진화 축이 비지 않게.
    // CardGrowthConfig.asset과 값이 일치해야 한다(양쪽 드리프트 방지).
    [Header("진화 게이트 (해당 레벨에서 진화하기 전까지 다음 강화가 막힌다)")]
    [SerializeField] List<EvolutionGate> evolutionGates = new List<EvolutionGate>
    {
        new EvolutionGate { atLevel = 5,  toStage = 1, costType = ECurrencyType.Diamond, cost = 50 },
        // 최종 진화의 stage 3은 임의값이 아니다 — CardCinematicRules의 시네마 공격 자격이 stage >= 3이다.
        new EvolutionGate { atLevel = 10, toStage = 3, costType = ECurrencyType.Diamond, cost = 200 },
    };

    public int MaxLevel => maxLevel < 0 ? 0 : maxLevel;   // 음수 오설정이 레벨 판정을 뒤집지 않게 하한 보정.

    /// <summary>레벨 _level로 올리는 한 스텝(hpGain/cost/successRate). 범위(1~MaxLevel) 밖이면 false.</summary>
    public bool TryGetStep(int _level, out GrowthStep _step)
    {
        _step = default;
        if (_level < 1 || _level > MaxLevel) return false;

        _step = StepAt(_level);
        return true;
    }

    /// <summary>
    /// 현재 레벨·단계에서 진행을 막고 있는 게이트. 없으면 false.
    /// "도달했지만 아직 안 올린" 게이트가 여럿이면 가장 낮은 레벨부터 — 진화는 단계를 건너뛰지 않는다.
    /// 판정을 도달 레벨 이상(&gt;=)으로 잡는 이유: 게이트를 나중에 추가·하향 저작해도 이미 지나친 유저가 소급 대상이 된다.
    /// </summary>
    public bool TryGetPendingGate(int _level, int _stage, out EvolutionGate _gate)
    {
        _gate = default;
        if (evolutionGates == null) return false;

        bool t_found = false;
        for (int t_i = 0; t_i < evolutionGates.Count; t_i++)
        {
            EvolutionGate t_candidate = evolutionGates[t_i];
            if (_level < t_candidate.atLevel) continue;   // 아직 도달 못 한 게이트
            if (_stage >= t_candidate.toStage) continue;  // 이미 통과한 게이트
            if (t_found && _gate.atLevel <= t_candidate.atLevel) continue;

            _gate    = t_candidate;
            t_found  = true;
        }
        return t_found;
    }

    /// <summary>레벨 _level까지의 누적 HP 보너스. 스텝 오버라이드가 있어 레벨 × hpPerLevel로 단축할 수 없다.</summary>
    public int HpBonusAt(int _level)
    {
        if (_level <= 0) return 0;

        // 상한을 나중에 낮춰도 구 세이브의 초과 레벨이 없는 스텝을 읽지 않도록 클램프.
        int t_top = _level > MaxLevel ? MaxLevel : _level;
        int t_sum = 0;
        for (int t_i = 1; t_i <= t_top; t_i++)
        {
            t_sum += StepAt(t_i).HpGain;
        }
        return t_sum;
    }

    // 기본식 계산 후 오버라이드 적용. 조회·누적 계산이 이 한 곳을 공유해 곡선 정의가 갈리지 않게 한다.
    GrowthStep StepAt(int _level)
    {
        int   t_hp   = hpPerLevel;
        long  t_cost = baseGoldCost + costGrowthPerLevel * (_level - 1);
        float t_rate = baseSuccessRate - rateDropPerLevel * (_level - 1);

        if (TryGetOverride(_level, out var t_over))
        {
            if (t_over.overrideHpGain)      t_hp   = t_over.hpGain;
            if (t_over.overrideCost)        t_cost = t_over.cost;
            if (t_over.overrideSuccessRate) t_rate = t_over.successRate;
        }

        // 음수 비용(=적립)·음수 HP는 저작 실수로만 나온다. 소비처가 재방어하지 않도록 여기서 정규화.
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
            return true;   // 같은 레벨이 여러 번 저작되면 첫 항목만 쓴다
        }
        return false;
    }
}

/// <summary>
/// 레벨 하나의 저작 오버라이드. override 플래그로 "미지정"을 표현한다 —
/// 센티널(0/음수)을 쓰면 "HP 0 증가"·"무료"·"성공률 0%" 같은 정상 저작값과 구분되지 않는다.
/// 새 항목의 기본값(전부 false)은 곧 전역 기본식이라 인스펙터에서 추가만 해도 안전하다.
/// </summary>
[System.Serializable]
public struct GrowthStepOverride
{
    [Tooltip("대상 레벨(1 = 첫 강화). 0은 유효 레벨이 아니라 어떤 스텝에도 적용되지 않는다.")]
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

/// <summary>
/// 진화 게이트 하나. 이 레벨에 도달하면 진화하기 전까지 다음 강화가 막힌다(막는 것 자체가 게이트의 목적).
/// 진화는 실패가 없고 스탯도 바꾸지 않는다 — 단계는 아트·연출 자격의 입력이다.
/// </summary>
[System.Serializable]
public struct EvolutionGate
{
    [Tooltip("게이트가 걸리는 강화 레벨.")]
    public int atLevel;

    [Tooltip("진화 후 단계. 0은 미진화라 게이트가 즉시 통과 처리된다(1 이상으로 저작할 것).")]
    public int toStage;

    [Tooltip("진화 비용 재화 종류.")]
    public ECurrencyType costType;

    [Tooltip("진화 비용.")]
    public long cost;
}

/// <summary>
/// 레벨 하나의 파생 스냅샷(저작 데이터가 아니라 CardGrowthConfig가 기본식+오버라이드로 계산해 내주는 값).
/// </summary>
public readonly struct GrowthStep
{
    public readonly int Level;          // 이 스텝을 밟으면 도달하는 레벨
    public readonly int HpGain;         // 성공 시 늘어나는 최대 체력
    public readonly long Cost;          // 소모 골드(성공·실패 무관하게 소모)
    public readonly float SuccessRate;  // 성공률(0~1)

    public GrowthStep(int _level, int _hpGain, long _cost, float _successRate)
    {
        Level       = _level;
        HpGain      = _hpGain;
        Cost        = _cost;
        SuccessRate = _successRate;
    }
}
