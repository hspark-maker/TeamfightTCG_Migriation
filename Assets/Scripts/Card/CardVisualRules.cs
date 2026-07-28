using System;
using System.Collections.Generic;
using UnityEngine;

// 카드 한 장을 그릴 때 "무엇을 / 어떤 순서로 / 몇 개까지" 보여줄지에 대한 표시 규칙의 단일 진실원.
//
// 인게임 CardView(월드스페이스 SpriteRenderer)와 아웃게임 CardVisualView(uGUI Image)는 렌더러만 다를 뿐
// 선택·정렬 규칙은 같아야 한다. 규칙을 각자 손으로 복제하면 조용히 갈라져(실제로 시너지 배지 정렬이 갈라졌었다)
// 로비와 전투의 같은 카드가 다르게 보인다 → 규칙은 여기에만 두고 양쪽이 이걸 호출한다.
//
// 여기에 넣지 않는 것: 렌더러 타입(SpriteRenderer/Image), 좌표·간격, 프리팹 Instantiate, tween 정리.
// 전부 "어떻게 그리는가"라서 호출부(뷰) 책임이다. 이 파일은 순수 선택·정렬만 한다.
public static class CardVisualRules
{
    /// <summary>카드 한 장에 표시할 시너지 배지 최대 개수. 초과분은 정렬 후 드롭.
    /// 프리팹 직렬화 값(CardView / CardVisualView의 synergyMaxBadges)의 기본값 소스 = 여기 하나.</summary>
    public const int MaxSynergyBadges = 3;

    /// <summary>표시할 키워드 아이콘 1개 = (어떤 키워드, 어떤 스프라이트).
    /// 인게임은 키워드까지 필요하고(iconMap → PlayKeywordGlow 역참조), 아웃게임은 스프라이트만 쓴다.
    /// 두 값이 항상 짝이라 병렬 리스트 대신 한 덩어리로 돌려준다.</summary>
    public readonly struct KeywordIcon
    {
        public readonly CardKeyword Keyword;
        public readonly Sprite      Icon;

        public KeywordIcon(CardKeyword _keyword, Sprite _icon)
        {
            this.Keyword = _keyword;
            this.Icon    = _icon;
        }
    }

    /// <summary>비트마스크에서 표시할 키워드 아이콘 목록을 뽑는다(표시 순서 = 리스트 순서).
    /// None은 스킵, 아이콘이 등록되지 않은 키워드도 스킵(배경만 뜬 빈 아이콘 방지).</summary>
    public static List<KeywordIcon> CollectKeywordIcons(CardKeyword _keywords, KeywordIconConfig _config)
    {
        var t_result = new List<KeywordIcon>();
        if (_config == null || _keywords == CardKeyword.None) return t_result;

        // 순회 순서 = CardKeyword 선언 순(Enum.GetValues). 이 순서가 곧 아이콘이 늘어서는 순서라
        // 바꾸면 같은 카드가 로비/전투에서 다른 배열로 보인다. 정렬을 끼워넣지 말 것.
        foreach (CardKeyword t_kw in Enum.GetValues(typeof(CardKeyword)))
        {
            if (t_kw == CardKeyword.None) continue;
            if (!_keywords.HasFlag(t_kw)) continue;

            Sprite t_icon = _config.GetIcon(t_kw);
            if (t_icon == null) continue;

            t_result.Add(new KeywordIcon(t_kw, t_icon));
        }
        return t_result;
    }

    /// <summary>카드가 가진 시너지 중 배지로 표시할 것들을 표시 순서대로 뽑는다.
    /// null 스킵 → 같은 참조 중복은 1회 → 활성 우선, 동급이면 requiredCount 내림차순 정렬 → 상한 적용.
    /// _state가 null이면(아웃게임엔 전투 스냅샷이 없다) 활성 판정만 전부 false가 되고 requiredCount 정렬은 그대로 산다.</summary>
    public static List<SynergyData> CollectSynergyBadges(SynergyData[] _synergies, SynergyState _state, int _max = MaxSynergyBadges)
    {
        var t_tags = new List<SynergyData>();
        if (_synergies != null)
        {
            foreach (SynergyData t_syn in _synergies)
            {
                if (t_syn == null) continue;
                if (t_tags.Contains(t_syn)) continue;   // 중복 나열 방어(배지 1회)
                t_tags.Add(t_syn);
            }
        }
        if (t_tags.Count == 0) return t_tags;

        // 활성(위쪽) 먼저, 동급이면 requiredCount 높은 순.
        t_tags.Sort((_a, _b) =>
        {
            bool t_activeA = IsSynergyActive(_state, _a);
            bool t_activeB = IsSynergyActive(_state, _b);
            if (t_activeA != t_activeB) return t_activeB.CompareTo(t_activeA);            // 활성(true) 먼저
            return GetBadgeRequiredCount(_state, _b).CompareTo(GetBadgeRequiredCount(_state, _a)); // requiredCount 내림차순
        });

        // 자르기는 반드시 정렬 "뒤". 먼저 자르면 데이터 나열 순서에 따라 표시 대상 자체가 달라진다.
        int t_limit = Mathf.Max(0, _max);
        if (t_tags.Count > t_limit) t_tags.RemoveRange(t_limit, t_tags.Count - t_limit);
        return t_tags;
    }

    /// <summary>활성 = 확정 스냅샷 Active에 해당 SynergyData가 참조로 존재하는지. 카운트/티어 재계산 없음.</summary>
    public static bool IsSynergyActive(SynergyState _state, SynergyData _tag)
    {
        if (_state == null || _tag == null) return false;
        foreach (ActiveSynergy t_a in _state.Active)
            if (t_a.Synergy == _tag) return true;
        return false;
    }

    /// <summary>정렬용 requiredCount: 활성이면 확정 스냅샷의 활성 티어 requiredCount,
    /// 비활성(또는 스냅샷 자체가 없는 아웃게임)이면 tiers 중 최고값(없으면 0).</summary>
    public static int GetBadgeRequiredCount(SynergyState _state, SynergyData _tag)
    {
        if (_tag == null) return 0;
        if (_state != null)
        {
            foreach (ActiveSynergy t_a in _state.Active)
                if (t_a.Synergy == _tag)
                    return t_a.Tier != null ? t_a.Tier.requiredCount : 0;
        }
        // 비활성: 정의된 티어 중 최고 requiredCount.
        int t_max = 0;
        if (_tag.tiers != null)
            foreach (SynergyTier t_tier in _tag.tiers)
                if (t_tier != null && t_tier.requiredCount > t_max) t_max = t_tier.requiredCount;
        return t_max;
    }
}
