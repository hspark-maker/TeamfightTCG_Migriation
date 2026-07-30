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

    /// <summary>카드 한 장에 그릴 아트 스프라이트를 고른다(없으면 null → 호출부가 렌더러를 끈다).
    /// 우선순위 battleImage → fullImage → portrait.
    /// battleImage가 최우선인 이유: 인게임 CardView.Render가 그리는 게 battleImage라, 로비 비주얼을
    /// 인게임과 통일하려면 같은 소스를 써야 한다. battleImage가 비어 있는 카드만 기존 아웃게임 소스로 폴백한다.
    /// 덱 대표 이미지(deckPreview)처럼 "카드 아트가 아닌 목적 전용" 필드는 여기 넣지 않는다 — 호출부가 앞단에서 고른다.</summary>
    public static Sprite PickCardArt(CardData _card)
    {
        if (_card == null) return null;
        if (_card.battleImage != null) return _card.battleImage;
        if (_card.fullImage   != null) return _card.fullImage;
        return _card.portrait;
    }

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

    // ── 키워드 아이콘 표시 대상 판정 ────────────────────────────────────────
    //
    // 키워드 아이콘 줄에는 **그 캐릭터의 고유 특성만** 띄운다. 일회용·디버프(무적, 추가 체력,
    // 전투 중 걸린 표식 등)는 아이콘으로 뜨지 않는다 — 카드 정체성이 아니라 잠깐 붙은 상태이고,
    // 아이콘 줄에 섞이면 "이 카드가 원래 뭐 하는 카드인지"가 안 읽힌다.
    //
    // 규칙 enum(CardKeyword)은 쪼개지 않는다. asset/prefab에 int로 직렬화돼 있어 비트 재넘버링은
    // 컴파일 에러 없이 값을 오해석시키고, 표시 파이프(아이콘/글로우/배너/AttackResult)는 단일 어휘를 요구한다.
    // 대신 "무엇을 띄울지"만 여기서 판정한다 — 전투 규칙은 이 파일을 보지 않는다.
    //
    // 제외 기준은 두 축이다:
    //  1) 출처 — 같은 표식(Mark)이라도 CardData.keywords에 박힌 것(대장부리)은 그 카드의 특성이라 띄우고,
    //     패시브가 전투 중 붙인 runtimeKeywords는 걸린 디버프라 안 띄운다.
    //  2) 키워드 자체 — 무적/추가 체력은 어느 필드에서 오든 항상 상태다(AlwaysStatus).

    /// <summary>출처와 무관하게 아이콘을 띄우지 않는 키워드. 카드 정체성이 아니라 걸렸다 풀리는 것들.
    /// Invincible=피해 1회 면역(TakeDamage에서 소모), BonusHp=수치가 붙어 있는 임시 체력(HP 옆 "+N"으로 이미 보인다).</summary>
    public const CardKeyword AlwaysStatus = CardKeyword.Invincible | CardKeyword.BonusHp;

    /// <summary>아이콘으로 띄울 키워드 = 마스터 데이터 + 시너지 부여(둘 다 전투 내내 불변) − 항상상태.
    /// runtimeKeywords(패시브가 전투 중 부여/해제하는 것)는 통째로 빠진다.</summary>
    public static CardKeyword TraitKeywords(CardInstance _card)
        => _card == null ? CardKeyword.None
         : ((_card.data != null ? _card.data.keywords : CardKeyword.None) | _card.synergyKeywords) & ~AlwaysStatus;

    /// <summary>전투 인스턴스가 없는 아웃게임(도감/로비)용 같은 판정. 판정식을 여기 한 곳에만 둔다 —
    /// 호출부가 각자 `& ~AlwaysStatus`를 복제하면 로비와 전투 표시가 조용히 갈라진다.</summary>
    public static CardKeyword TraitKeywords(CardData _card)
        => _card == null ? CardKeyword.None : _card.keywords & ~AlwaysStatus;

    /// <summary>아이콘 줄에서만 빼는 키워드. 프레임 장식으로는 그대로 보여준다.
    /// Mark(표식)=반격을 못 주는 대가라 프레임 테두리로 알리는 편이 맞고, 아이콘 줄에 넣으면
    /// 자리(최대 3칸)를 특성 키워드에서 빼앗는다. 프레임/아이콘이 갈라지는 지점은 이 상수 하나뿐.</summary>
    public const CardKeyword IconRowExcluded = CardKeyword.Mark;

    /// <summary>아이콘 줄에 띄울 키워드 = TraitKeywords − 아이콘 줄 제외분. 프레임은 TraitKeywords를 그대로 쓴다.</summary>
    public static CardKeyword IconKeywords(CardInstance _card) => TraitKeywords(_card) & ~IconRowExcluded;

    /// <summary>아웃게임(도감/로비) 아이콘 줄용. 인게임과 같은 제외 규칙.</summary>
    public static CardKeyword IconKeywords(CardData _card) => TraitKeywords(_card) & ~IconRowExcluded;

    /// <summary>비트마스크에서 표시할 키워드 아이콘 목록을 뽑는다(표시 순서 = 리스트 순서).
    /// None은 스킵, 아이콘이 등록되지 않은 키워드도 스킵(배경만 뜬 빈 아이콘 방지).
    /// 결과가 비면(키워드 없는 캐릭터, 또는 가진 키워드가 전부 위 조건에 걸린 경우)
    /// config의 기본 아이콘 1개로 채운다 — 폴백 판정을 여기 한 곳에 두어야 로비/전투가 갈라지지 않는다.</summary>
    public static List<KeywordIcon> CollectKeywordIcons(CardKeyword _keywords, KeywordIconConfig _config)
    {
        var t_result = new List<KeywordIcon>();
        if (_config == null) return t_result;

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

        // 폴백은 "아이콘이 0개일 때"만. 키워드는 None으로 둔다 —
        // 실제 보유 키워드가 아니므로 CardView의 iconMap 역참조(PlayKeywordGlow)가 이걸 집으면 안 된다.
        if (t_result.Count == 0 && _config.DefaultIcon != null)
            t_result.Add(new KeywordIcon(CardKeyword.None, _config.DefaultIcon));

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
