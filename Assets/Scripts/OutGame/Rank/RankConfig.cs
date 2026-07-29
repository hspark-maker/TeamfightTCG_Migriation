using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 랭크 등급 테이블 + 승패 포인트 튜닝 파라미터. 티어(등급 × 단계) 해석 규칙의 단일 진실원.
/// </summary>
[CreateAssetMenu(fileName = "RankConfig", menuName = "Card Battle/Rank Config")]
public class RankConfig : ScriptableObject
{
    // 등급당 단계 수(1이 최하위, 4가 최상위). 티어 인덱스 = 등급인덱스 * 4 + (단계 - 1).
    public const int DivisionsPerGrade = 4;

    // long 필드에 [Min]을 붙이지 않는다 — Unity MinDrawer가 intValue로 처리해 값이 잘린다(BattleReward 선례).
    [Tooltip("승리 시 더할 랭크 포인트.")]
    public long winPoints = 10;

    [Tooltip("패배 시 뺄 랭크 포인트. 양수로 입력한다(코드에서 뺀다).")]
    public long losePoints = 5;

    // 필드 초기화자로 기본 테이블을 코드가 보증한다 — SO 미배선(CreateInstance fallback) 시에도 등급이 비지 않게.
    // RankConfig.asset과 값이 일치해야 한다(양쪽 드리프트 방지).
    [Tooltip("등급 테이블. entryPoints 오름차순으로 저작한다. 4단계에서 다음 등급 entryPoints를 넘기면 인덱스 연속성으로 다음 등급 1단계가 된다.")]
    public List<RankGradeConfig> grades = new List<RankGradeConfig>
    {
        new RankGradeConfig { grade = ERankGrade.Bronze,   displayName = "브론즈",     entryPoints = 100,   pointsPerDivision = 25, rewardGold = 100,  rewardGoldPerDivision = 50 },
        new RankGradeConfig { grade = ERankGrade.Silver,   displayName = "실버",       entryPoints = 200, pointsPerDivision = 25, rewardGold = 300,  rewardGoldPerDivision = 50 },
        new RankGradeConfig { grade = ERankGrade.Gold,     displayName = "골드",       entryPoints = 300, pointsPerDivision = 25, rewardGold = 500,  rewardGoldPerDivision = 100 },
        new RankGradeConfig { grade = ERankGrade.Platinum, displayName = "플래티넘",   entryPoints = 400, pointsPerDivision = 25, rewardGold = 900,  rewardGoldPerDivision = 100 },
        new RankGradeConfig { grade = ERankGrade.Diamond,  displayName = "다이아몬드", entryPoints = 500, pointsPerDivision = 25, rewardGold = 1400, rewardGoldPerDivision = 200 },
    };

    /// <summary>전체 티어 수(등급 수 × 단계 수). 소비처는 행 수를 이 값에서 파생한다(상수 하드코딩 금지).</summary>
    public int TierCount => grades != null ? grades.Count * DivisionsPerGrade : 0;

    /// <summary>임계치 &lt;= points를 만족하는 최대 티어 인덱스. 매치가 없으면 0으로 클램프(최하위 티어).</summary>
    public int ResolveTierIndex(long _points)
    {
        if (grades == null) return 0;

        // 임계치만 비교한다 — TryGetTier를 인덱스마다 호출하면 표시명 문자열 조합까지 매번 돈다.
        // 등급 역순 → 단계 역순이라 오름차순 저작 전제에서 첫 매치가 곧 최대 인덱스다.
        // null 등급 행은 임계치를 알 수 없어 매치 대상에서 빠진다(TryGetTier도 같은 행에서 false).
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

    /// <summary>티어 스냅샷 파생. 범위 밖·null 등급 행이면 false(_tier는 RankTier.None이라 소비처가 null을 다시 방어하지 않아도 된다).</summary>
    public bool TryGetTier(int _index, out RankTier _tier)
    {
        _tier = RankTier.None;
        if (grades == null || _index < 0 || _index >= TierCount) return false;

        RankGradeConfig t_grade = grades[_index / DivisionsPerGrade];
        if (t_grade == null) return false;

        int t_division = _index % DivisionsPerGrade + 1; // 1이 최하위
        long t_step = t_division - 1;

        _tier = new RankTier(
            _index,
            t_grade.grade,
            t_division,
            $"{t_grade.displayName} {t_division}",
            t_grade.badge,
            t_grade.entryPoints + t_step * t_grade.pointsPerDivision,
            t_grade.rewardGold + t_step * t_grade.rewardGoldPerDivision);
        return true;
    }
}

/// <summary>랭크 등급(단계와 무관한 상위 구분). 코드가 등급을 참조하는 정본 키다.</summary>
public enum ERankGrade
{
    Bronze,
    Silver,
    Gold,
    Platinum,
    Diamond,
}

/// <summary>
/// 등급 1개 정의(저작 단위). 4단계는 여기서 파생되므로 20행을 손으로 적지 않는다.
/// </summary>
[Serializable]
public class RankGradeConfig
{
    [Tooltip("등급 식별자. 리스트 순서와 일치하게 저작한다 — 티어 인덱스는 리스트 순서에서 파생되며 이 값은 검증되지 않는다.")]
    public ERankGrade grade;

    [Tooltip("등급 표시명(단계 숫자 없이). 티어 표시명은 \"표시명 단계\"로 조합된다.")]
    public string displayName = "";

    [Tooltip("등급 뱃지 스프라이트(선택, 4단계 공용). 비워두면 UI가 기존 표시를 유지한다.")]
    public Sprite badge;

    // long 필드라 [Min]을 붙이지 않는다(MinDrawer가 intValue로 잘라낸다) — 음수·0은 Earn이 무시하므로 지급 경로는 안전하다.
    [Tooltip("이 등급 1단계 진입 임계치. 저작 규칙: 임계치는 하향만 — 상향하면 임계치 아래로 내려간 기존 유저가 소급 강등된다.")]
    public long entryPoints;

    [Tooltip("단계 간 포인트 간격. 단계 N의 임계치 = entryPoints + (N-1) * 이 값. 저작 규칙: entryPoints와 동일하게 하향만 — 상향하면 단계 2~4 임계치가 올라 기존 유저가 소급 강등된다.")]
    public long pointsPerDivision;

    [Tooltip("이 등급 1단계 달성 시 1회 수령하는 골드. 0이면 수령해도 지급이 없다(진행만 넘어간다).")]
    public long rewardGold;

    [Tooltip("단계마다 늘어나는 보상 증가분. 단계 N의 보상 = rewardGold + (N-1) * 이 값.")]
    public long rewardGoldPerDivision;
}

/// <summary>
/// 티어 1개의 파생 스냅샷. 저작 데이터가 아니라 RankConfig가 등급 행에서 계산해 내주는 값이다.
/// </summary>
public readonly struct RankTier
{
    /// <summary>
    /// 조회 실패용 빈 스냅샷. DisplayName이 빈 문자열이라 소비처가 null을 다시 방어하지 않아도 된다.
    /// 조회 실패는 반드시 TryGetTier의 반환값으로 판정한다 — None의 Index=0·Grade=Bronze는 유효한 브론즈 1과 값으로 구분되지 않는다.
    /// </summary>
    public static readonly RankTier None = new RankTier(0, ERankGrade.Bronze, 0, null, null, 0, 0);

    public readonly int Index;            // 티어 인덱스(0 = 최하위)
    public readonly ERankGrade Grade;     // 등급
    public readonly int Division;         // 등급 내 단계(1~4, 1이 최하위). None은 0.
    public readonly string DisplayName;   // "브론즈 1" 조합(항상 non-null)
    public readonly Sprite Badge;         // 등급 뱃지(없을 수 있음)
    public readonly long RequiredPoints;  // 이 티어 진입 임계치
    public readonly long RewardGold;      // 달성 시 1회 수령 골드

    public RankTier(int _index, ERankGrade _grade, int _division, string _displayName, Sprite _badge, long _requiredPoints, long _rewardGold)
    {
        Index = _index;
        Grade = _grade;
        Division = _division;
        DisplayName = _displayName != null ? _displayName : string.Empty; // 생성자에서 정규화 — TMP NRE 방어를 한 곳으로 모은다.
        Badge = _badge;
        RequiredPoints = _requiredPoints;
        RewardGold = _rewardGold;
    }
}
