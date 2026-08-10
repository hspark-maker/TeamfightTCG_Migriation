using System;
using System.Collections.Generic;
using UnityEngine;

// 랭크 등급 테이블 + 승패 포인트 튜닝 파라미터
[CreateAssetMenu(fileName = "RankConfig", menuName = "Card Battle/Rank Config")]
public class RankConfig : ScriptableObject
{
    // 등급당 단계 수(티어 인덱스 = 등급인덱스 * 4 + 단계 - 1)
    public const int DivisionsPerGrade = 4;

    // 승리 시 더할 랭크 포인트
    [Tooltip("승리 시 더할 랭크 포인트.")]
    public long winPoints = 10;

    // 패배 시 뺄 랭크 포인트(양수로 입력)
    [Tooltip("패배 시 뺄 랭크 포인트. 양수로 입력한다(코드에서 뺀다).")]
    public long losePoints = 5;

    // 첫 티어 미도달(언랭크) 상태의 표시명
    [Tooltip("첫 티어 미도달(언랭크) 상태의 표시명. 랭크는 튜토리얼 졸업과 함께 첫 등급 1단계로 진입하므로, " +
             "그 전까지 표시되는 문구다. 티어 표시명과 달리 단계 숫자가 붙지 않는다.")]
    public string unrankedDisplayName = "언랭크";

    // 언랭크 상태의 배지(첫 등급 배지 대신 쓴다)
    [Tooltip("첫 티어 미도달(언랭크) 상태에서 쓸 배지 스프라이트. 비워두면 첫 등급 배지가 그대로 보여 도달한 것처럼 읽히므로 저작을 권한다.")]
    public Sprite unrankedBadge;

    // 등급 테이블(기본값은 RankConfig.asset과 일치해야 한다)
    [Tooltip("등급 테이블. entryPoints 오름차순으로 저작한다. 4단계에서 다음 등급 entryPoints를 넘기면 인덱스 연속성으로 다음 등급 1단계가 된다.")]
    public List<RankGradeConfig> grades = new List<RankGradeConfig>
    {
        new RankGradeConfig { grade = ERankGrade.Bronze,   displayName = "브론즈",     entryPoints = 100,   pointsPerDivision = 25, rewardGold = 100,  rewardGoldPerDivision = 50 },
        new RankGradeConfig { grade = ERankGrade.Silver,   displayName = "실버",       entryPoints = 200, pointsPerDivision = 25, rewardGold = 300,  rewardGoldPerDivision = 50 },
        new RankGradeConfig { grade = ERankGrade.Gold,     displayName = "골드",       entryPoints = 300, pointsPerDivision = 25, rewardGold = 500,  rewardGoldPerDivision = 100 },
        new RankGradeConfig { grade = ERankGrade.Platinum, displayName = "플래티넘",   entryPoints = 400, pointsPerDivision = 25, rewardGold = 900,  rewardGoldPerDivision = 100 },
        new RankGradeConfig { grade = ERankGrade.Diamond,  displayName = "다이아몬드", entryPoints = 500, pointsPerDivision = 25, rewardGold = 1400, rewardGoldPerDivision = 200 },
    };

    /// <summary>티어별 AI 카드 레벨(index = 티어 인덱스). 난이도 곡선의 단일 진실원.
    /// **브론즈~실버 중반은 플레이어보다 낮게, 실버 끝에서 동급, 골드부터 높게** 저작한다 —
    /// 그래야 강화가 체감되면서도 후반에 도전이 남는다.
    /// 목록이 티어 수보다 짧으면 마지막 값이 이어진다(비면 전부 바닥 레벨 = 성장 없음).</summary>
    [Tooltip("티어별 AI 카드 레벨. index = 티어 인덱스(등급×4 + 단계-1). 짧으면 마지막 값이 이어진다.")]
    public List<int> aiCardLevels = new List<int>
    {
        1, 1, 2, 2,      // 브론즈 1~4 — 플레이어보다 확실히 약하다
        3, 3, 4, 5,      // 실버   1~4 — 따라붙어 실버 끝에서 동급
        6, 6, 7, 7,      // 골드   1~4 — 여기서부터 플레이어보다 강하다
        8, 8, 9, 9,      // 루비   1~4
        10, 10, 10, 10,  // 다이아  1~4 — 만렙
    };

    [Tooltip("AI 카드 레벨 하향 편차. 티어 레벨보다 최대 이만큼 낮은 카드가 섞인다. 0이면 하향 없음.")]
    public int aiLevelSpreadDown = 1;

    [Tooltip("AI 카드 레벨 상향 편차. 티어 레벨보다 최대 이만큼 높은 카드가 섞인다. 0이면 상향 없음.")]
    public int aiLevelSpreadUp = 1;

    // 전체 티어 수(등급 수 × 단계 수). 소비처는 행 수를 이 값에서 파생한다
    public int TierCount => grades != null ? grades.Count * DivisionsPerGrade : 0;

    /// <summary>첫 티어(1등급 1단계) 진입 임계치 = 랭크 도달 여부의 단일 기준.
    /// ResolveTierIndex는 미도달도 0으로 폴백하므로 "인덱스 0"만으로는 도달을 판정할 수 없다.</summary>
    public long FirstTierPoints => grades != null && grades.Count > 0 && grades[0] != null ? grades[0].entryPoints : 0;

    /// <summary>티어 _index에서 AI가 쓸 카드 레벨. 미저작이면 바닥 레벨(성장 없음 = 종전 동작).</summary>
    public int AiCardLevelAt(int _index)
    {
        if (aiCardLevels == null || aiCardLevels.Count == 0) return CardGrowth.BaseLevel;

        int t_i = _index < 0 ? 0 : (_index >= aiCardLevels.Count ? aiCardLevels.Count - 1 : _index);
        int t_level = aiCardLevels[t_i];
        return t_level < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : t_level;
    }

    /// <summary>티어 _tierIndex에서 카드 _cardId 한 장이 쓸 레벨. 기준 레벨(<see cref="AiCardLevelAt(int)"/>) 주변에
    /// 카드 번호에서 파생한 고정 편차를 얹는다 — 난수가 아니라 파생값이라 덱 미리보기와 전투가 갈리지 않는다.
    /// 편차는 카드 고유값이라 티어가 올라도 순서가 뒤집히지 않는다(기준이 오르면 그 카드 레벨도 같이 오른다).
    /// 바닥(BaseLevel)에서 잘리므로 저티어에서는 평균이 기준보다 살짝 위다.</summary>
    public int AiCardLevelForCard(int _tierIndex, int _cardId)
    {
        int t_base = AiCardLevelAt(_tierIndex);

        int t_down = Mathf.Max(0, aiLevelSpreadDown);
        int t_up   = Mathf.Max(0, aiLevelSpreadUp);
        int t_span = t_down + t_up + 1;
        if (t_span <= 1 || _cardId <= 0) return t_base;

        int t_offset = (int)(Mix((uint)_cardId) % (uint)t_span) - t_down;
        int t_level  = t_base + t_offset;
        return t_level < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : t_level;
    }

    // 임계치 <= _points를 만족하는 최대 티어 인덱스(없으면 0)
    public int ResolveTierIndex(long _points)
    {
        if (grades == null) return 0;

        for (int t_g = grades.Count - 1; t_g >= 0; t_g--)
        {
            RankGradeConfig t_grade = grades[t_g];
            if (t_grade == null) continue;

            for (int t_d = DivisionsPerGrade - 1; t_d >= 0; t_d--)
            {
                if (t_grade.entryPoints + t_d * t_grade.pointsPerDivision <= _points)
                    return t_g * DivisionsPerGrade + t_d;
            }
        }
        return 0;
    }

    // 티어 스냅샷 파생(범위 밖·null 등급 행이면 false + RankTier.None)
    public bool TryGetTier(int _index, out RankTier _tier)
    {
        _tier = RankTier.None;
        if (grades == null || _index < 0 || _index >= TierCount) return false;

        RankGradeConfig t_grade = grades[_index / DivisionsPerGrade];
        if (t_grade == null) return false;

        int t_division = _index % DivisionsPerGrade + 1;
        long t_step = t_division - 1;

        _tier = new RankTier(
            _index,
            t_grade.grade,
            t_division,
            $"{t_grade.displayName} {t_division}",
            t_grade.badge,
            t_grade.entryPoints + t_step * t_grade.pointsPerDivision,
            new CurrencyGain(t_grade.rewardType, t_grade.rewardGold + t_step * t_grade.rewardGoldPerDivision));
        return true;
    }

    // 카드 번호를 고정 규칙으로 흩는다(플랫폼·런타임 무관). 난수원이 아니라 파생 해시다.
    static uint Mix(uint _a)
    {
        uint t_h = _a * 2654435761u;
        t_h ^= t_h >> 15; t_h *= 2246822519u;
        t_h ^= t_h >> 13; t_h *= 3266489917u;
        return t_h ^ (t_h >> 16);
    }
}

// 랭크 등급(단계와 무관한 상위 구분)
public enum ERankGrade
{
    Bronze,
    Silver,
    Gold,
    Platinum,
    Diamond,
}

// 등급 1개 정의(저작 단위, 4단계는 여기서 파생)
[Serializable]
public class RankGradeConfig
{
    // 등급 식별자(티어 인덱스는 리스트 순서에서 파생되며 이 값은 검증되지 않는다)
    [Tooltip("등급 식별자. 리스트 순서와 일치하게 저작한다 — 티어 인덱스는 리스트 순서에서 파생되며 이 값은 검증되지 않는다.")]
    public ERankGrade grade;

    // 등급 표시명(단계 숫자 없이)
    [Tooltip("등급 표시명(단계 숫자 없이). 티어 표시명은 \"표시명 단계\"로 조합된다.")]
    public string displayName = "";

    // 등급 뱃지 스프라이트(선택, 4단계 공용)
    [Tooltip("등급 뱃지 스프라이트(선택, 4단계 공용). 비워두면 UI가 기존 표시를 유지한다.")]
    public Sprite badge;

    // 이 등급 1단계 진입 임계치
    [Tooltip("이 등급 1단계 진입 임계치. 저작 규칙: 임계치는 하향만 — 상향하면 임계치 아래로 내려간 기존 유저가 소급 강등된다.")]
    public long entryPoints;

    // 단계 간 포인트 간격
    [Tooltip("단계 간 포인트 간격. 단계 N의 임계치 = entryPoints + (N-1) * 이 값. 저작 규칙: entryPoints와 동일하게 하향만 — 상향하면 단계 2~4 임계치가 올라 기존 유저가 소급 강등된다.")]
    public long pointsPerDivision;

    // 이 등급 보상으로 지급할 재화 종류(4단계 공용)
    [Tooltip("이 등급 보상으로 지급할 재화 종류(4단계 공용).")]
    public ECurrencyType rewardType = ECurrencyType.Gold;

    // 이 등급 1단계 달성 시 1회 수령하는 골드
    [Tooltip("이 등급 1단계 달성 시 1회 수령하는 골드. 0이면 수령해도 지급이 없다(진행만 넘어간다).")]
    public long rewardGold;

    // 단계마다 늘어나는 보상 증가분
    [Tooltip("단계마다 늘어나는 보상 증가분. 단계 N의 보상 = rewardGold + (N-1) * 이 값.")]
    public long rewardGoldPerDivision;
}

// 티어 1개의 파생 스냅샷(RankConfig가 등급 행에서 계산해 내주는 값)
public readonly struct RankTier
{
    // 조회 실패용 빈 스냅샷 — 실패 판정은 반드시 TryGetTier 반환값으로(None은 브론즈 1과 값으로 구분되지 않는다)
    public static readonly RankTier None = new RankTier(0, ERankGrade.Bronze, 0, null, null, 0, CurrencyGain.None);

    public readonly int Index;
    public readonly ERankGrade Grade;
    public readonly int Division;
    public readonly string DisplayName;
    public readonly Sprite Badge;
    public readonly long RequiredPoints;
    public readonly CurrencyGain Reward;

    public RankTier(int _index, ERankGrade _grade, int _division, string _displayName, Sprite _badge, long _requiredPoints, CurrencyGain _reward)
    {
        Index = _index;
        Grade = _grade;
        Division = _division;
        DisplayName = _displayName != null ? _displayName : string.Empty;
        Badge = _badge;
        RequiredPoints = _requiredPoints;
        Reward = _reward;
    }
}
