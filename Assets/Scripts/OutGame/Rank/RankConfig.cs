using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 랭크 티어 테이블 + 승패 포인트 튜닝 파라미터
/// </summary>
[CreateAssetMenu(fileName = "RankConfig", menuName = "Card Battle/Rank Config")]
public class RankConfig : ScriptableObject
{
    // long 필드에 [Min]을 붙이지 않는다 — Unity MinDrawer가 intValue로 처리해 값이 잘린다(BattleReward 선례).
    [Tooltip("승리 시 더할 랭크 포인트.")]
    public long winPoints = 10;

    [Tooltip("패배 시 뺄 랭크 포인트. 양수로 입력한다(코드에서 뺀다).")]
    public long losePoints = 5;

    // 필드 초기화자로 기본 테이블을 코드가 보증한다 — SO 미배선(CreateInstance fallback) 시에도 티어가 비지 않게.
    // 5개 랭크 × 4단계(1~4) = 20티어. 단계 숫자가 클수록 상위이며, 각 랭크 4단계에서 임계치를 넘기면 다음 랭크 1단계로 승급한다
    // (예: 브론즈 4 → 실버 1). 균등 25포인트 간격 기본값 — 실제 튜닝은 RankConfig.asset에서.
    [Tooltip("티어 테이블. requiredPoints 오름차순으로 저작한다. 인덱스 0이 최하위이며, 도달 티어는 points의 순수 파생이라 세이브에 저장하지 않는다.")]
    public List<RankTier> tiers = new List<RankTier>
    {
        new RankTier { displayName = "브론즈 1",     requiredPoints = 0 },
        new RankTier { displayName = "브론즈 2",     requiredPoints = 25 },
        new RankTier { displayName = "브론즈 3",     requiredPoints = 50 },
        new RankTier { displayName = "브론즈 4",     requiredPoints = 75 },
        new RankTier { displayName = "실버 1",       requiredPoints = 100 },
        new RankTier { displayName = "실버 2",       requiredPoints = 125 },
        new RankTier { displayName = "실버 3",       requiredPoints = 150 },
        new RankTier { displayName = "실버 4",       requiredPoints = 175 },
        new RankTier { displayName = "골드 1",       requiredPoints = 200 },
        new RankTier { displayName = "골드 2",       requiredPoints = 225 },
        new RankTier { displayName = "골드 3",       requiredPoints = 250 },
        new RankTier { displayName = "골드 4",       requiredPoints = 275 },
        new RankTier { displayName = "플래티넘 1",   requiredPoints = 300 },
        new RankTier { displayName = "플래티넘 2",   requiredPoints = 325 },
        new RankTier { displayName = "플래티넘 3",   requiredPoints = 350 },
        new RankTier { displayName = "플래티넘 4",   requiredPoints = 375 },
        new RankTier { displayName = "다이아몬드 1", requiredPoints = 400 },
        new RankTier { displayName = "다이아몬드 2", requiredPoints = 425 },
        new RankTier { displayName = "다이아몬드 3", requiredPoints = 450 },
        new RankTier { displayName = "다이아몬드 4", requiredPoints = 475 },
    };
}

/// <summary>
/// 티어 1개 정의(표시명·진입 임계치·뱃지)
/// </summary>
[Serializable]
public class RankTier
{
    [Tooltip("티어 표시명(UI 정본).")]
    public string displayName = "";

    [Tooltip("이 티어에 진입하는 최소 포인트(오름차순 저작). 저작 규칙: 임계치는 하향만 — 상향하면 임계치 아래로 내려간 기존 유저가 소급 강등된다.")]
    public long requiredPoints;

    [Tooltip("티어 뱃지 스프라이트(선택). 비워두면 UI가 기존 표시를 유지한다.")]
    public Sprite badge;
}
