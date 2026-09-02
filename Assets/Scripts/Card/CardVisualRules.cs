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
    /// 소스는 battleImage 하나뿐이다 — 인게임 CardView.Render가 그리는 것과 같은 그림이라 로비/전투가 갈라지지 않는다.
    /// (예전엔 fullImage → portrait 폴백이 뒤에 붙어 있었지만, battleImage가 늘 채워져 있어 도달한 적이 없다.)
    /// "카드 아트가 아닌 목적 전용" 그림은 여기 넣지 않는다 — 호출부가 앞단에서 고른다.</summary>
    static System.Func<int, int> s_evolutionStage;

    /// <summary>아웃게임에서 내 카드의 현재 진화 단계를 공급한다. 미주입이면 미진화 아트를 쓴다.</summary>
    public static System.Func<int, int> EvolutionStageProvider
    {
        set => s_evolutionStage = value;
    }

    public static Sprite PickCardArt(int _cardId)
    {
        if (_cardId <= 0) return null;
        return PickCardArt(_cardId, s_evolutionStage != null ? s_evolutionStage(_cardId) : 0);
    }


    /// <summary>지정 진화 단계의 아트. 해당 단계가 비었으면 이전 단계부터 미진화까지 차례로 폴백한다.
    ///
    /// 폴백 판정은 **"그 단계에 그림이 배선되어 있는가"** 로 한다. 결과가 null인지로 판정하면
    /// Addressables 이관 뒤에 깨진다 — 배선은 됐지만 아직 안 받아온 단계가 "빈 슬롯"으로 오해되어
    /// 한 단계 아래 그림이 뜨고, 로드가 끝나면 그림이 갑자기 바뀐다. 배선 여부는 로드 없이 판정 가능하다.</summary>
    public static Sprite PickCardArt(int _cardId, int _stage)
    {
        if (!CardCatalog.TryGetSpec(_cardId, out CardSpec t_spec)) return null;

        for (int t_stage = Mathf.Min(_stage, CardSpec.MaxEvolutionStage); t_stage >= 0; t_stage--)
        {
            string t_address = CardArtCache.AddressOf(t_spec, t_stage);
            if (!CardArtCache.Exists(t_address)) continue;
            return CardArtCache.Get(t_address);
        }

        return null;
    }

    /// <summary>전투 카드 인스턴스의 진화 단계를 반영한 아트.</summary>
    public static Sprite PickBattleArt(CardInstance _card)
        => _card == null ? null : PickCardArt(_card.cardId, _card.evolutionStage);

    /// <summary>표시할 키워드 아이콘 1개 = (어떤 키워드, 어떤 스프라이트).
    /// 인게임·아웃게임 둘 다 스프라이트를 쓰고, 키워드는 어느 칸이 무엇인지 가리는 식별자다.
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
    // 제외 기준은 **키워드 자체** 하나다 — 무적/추가 체력은 어느 필드에서 오든 항상 상태다(AlwaysStatus).
    //
    // 출처(unlocked / synergy / runtime)로는 가르지 않는다. 전에는 runtimeKeywords를 통째로 뺐는데,
    // 추적 시너지가 적에게 표식을 걸면서 "규칙은 걸렸는데 화면엔 아무 흔적이 없는" 상태가 생겼다.
    // 지금 runtimeKeywords에 들어가는 것은 무적(AlwaysStatus로 어차피 빠진다)과 표식뿐이고,
    // 표식은 아이콘 줄이 아니라 프레임 장식으로 나간다(IconRowExcluded) — 아이콘 줄이 디버프로
    // 오염되지 않는다는 원래 의도는 그 상수가 대신 지킨다.

    /// <summary>출처와 무관하게 아이콘을 띄우지 않는 키워드. 카드 정체성이 아니라 걸렸다 풀리는 것들.
    /// Invincible=피해 1회 면역(TakeDamage에서 소모), BonusHp=수치가 붙어 있는 임시 체력(HP 옆 "+N"으로 이미 보인다).</summary>
    public const CardKeyword AlwaysStatus = CardKeyword.Invincible | CardKeyword.BonusHp;

    /// <summary>띄울 키워드 = 그 인스턴스가 지금 가진 것 전부 − 항상상태.
    /// data.keywords가 아니라 인스턴스의 unlockedKeywords를 보는 이유: 아직 해금 안 된 키워드를 띄우면
    /// 표시와 규칙이 갈라진다. 같은 이유로 runtimeKeywords(전투 중 부여/해제)도 포함한다 —
    /// <see cref="CardInstance.HasKeyword"/>가 보는 세 필드와 여기가 어긋나면 표시가 규칙을 속인다.</summary>
    public static CardKeyword TraitKeywords(CardInstance _card)
        => _card == null ? CardKeyword.None
         : (_card.unlockedKeywords | _card.runtimeKeywords | _card.synergyKeywords) & ~AlwaysStatus;

    static System.Func<int, CardKeyword> s_unlockedKeywords;

    /// <summary>강화로 **지금 실제 열려 있는** 카드 키워드 공급자. 초기화가 OutGame의 성장값을 꽂는다 —
    /// 표시 규칙(여기)이 OutGame을 직접 참조하지 않게 값 생산자를 상위에서 밀어넣는 기존 규약과 같다.
    ///
    /// 미주입(null)이면 마스터 데이터 그대로 = 성장 없는 경로(전투 씬 단독 실행)의 종전 동작.</summary>
    public static System.Func<int, CardKeyword> UnlockedKeywordProvider
    {
        set => s_unlockedKeywords = value;
    }

    /// <summary>이 카드가 지금 가진 키워드. 해금 전이면 비어 있다 —
    /// `data.keywords`를 직접 읽으면 아직 못 쓰는 키워드가 화면에 뜨고 규칙과 갈라진다.</summary>
    static CardKeyword OwnedKeywords(int _cardId)
        => s_unlockedKeywords != null ? s_unlockedKeywords(_cardId) : SpecKeywords(_cardId);

    static CardKeyword SpecKeywords(int _cardId)
        => CardCatalog.TryGetSpec(_cardId, out CardSpec t_spec) ? t_spec.Keywords : CardKeyword.None;

    /// <summary>전투 인스턴스가 없는 아웃게임(도감/로비)용 같은 판정. 판정식을 여기 한 곳에만 둔다 —
    /// 호출부가 각자 `& ~AlwaysStatus`를 복제하면 로비와 전투 표시가 조용히 갈라진다.</summary>
    public static CardKeyword TraitKeywords(int _cardId)
        => _cardId <= 0 ? CardKeyword.None : OwnedKeywords(_cardId) & ~AlwaysStatus;


    /// <summary>이 카드가 가졌지만 **아직 해금 레벨에 닿지 않은** 키워드. 정보창이 잠김 룩으로 띄우는 대상이며,
    /// 아이콘 줄·프레임 장식은 이걸 띄우지 않는다(카드 위 표시는 지금 쓸 수 있는 것만).
    /// 아직 해금되지 않은 키워드만 반환한다.</summary>
    public static CardKeyword LockedKeywords(int _cardId)
        => _cardId <= 0 ? CardKeyword.None : SpecKeywords(_cardId) & ~OwnedKeywords(_cardId);


    /// <summary>정보창이 **한 줄이라도 그릴** 키워드 전체 = 지금 가진 것 + 설명 전용 + 아직 잠긴 것.
    /// 잠긴 것을 목록에서 빼면 "이 카드가 앞으로 뭘 여는지"가 화면에서 사라진다 —
    /// 표시 여부와 잠김 여부는 다른 축이라 이 집합과 <see cref="LockedKeywords"/>를 짝으로 쓴다.</summary>
    public static CardKeyword InfoKeywordsWithLocked(int _cardId)
        => _cardId <= 0 ? CardKeyword.None : InfoKeywords(_cardId) | SpecKeywords(_cardId);


    /// <summary>**카드 정보창**이 띄울 키워드 = 지금 가진 키워드.
    /// 타일의 아이콘 줄과 목적이 다르다(설명까지 보여주는 창이라 AlwaysStatus를 빼지 않는다).
    /// 이 규칙을 호출부가 각자 복제하면 해금 반영이 한쪽에서만 빠진다.</summary>
    public static CardKeyword InfoKeywords(int _cardId)
        => _cardId <= 0 ? CardKeyword.None : OwnedKeywords(_cardId);


    /// <summary>전투 인스턴스용 정보창 키워드. **인스턴스가 있으면 이쪽이 정답이다** —
    /// 적 카드에 내 성장을 얹지 않으려면 공급자(내 강화값)가 아니라 그 인스턴스의 값을 봐야 한다.</summary>
    public static CardKeyword InfoKeywords(CardInstance _card)
        => _card == null ? CardKeyword.None
         : _card.unlockedKeywords | _card.runtimeKeywords | _card.synergyKeywords;

    /// <summary>아이콘 줄에서만 빼는 키워드. 프레임 장식으로는 그대로 보여준다.
    /// Mark(표식)=반격을 못 주는 대가라 프레임 테두리로 알리는 편이 맞고, 아이콘 줄에 넣으면
    /// 자리(최대 3칸)를 특성 키워드에서 빼앗는다. 프레임/아이콘이 갈라지는 지점은 이 상수 하나뿐.</summary>
    public const CardKeyword IconRowExcluded = CardKeyword.Mark;

    /// <summary>아이콘 줄에 띄울 키워드 = TraitKeywords − 아이콘 줄 제외분. 프레임은 TraitKeywords를 그대로 쓴다.</summary>
    public static CardKeyword IconKeywords(CardInstance _card) => TraitKeywords(_card) & ~IconRowExcluded;

    /// <summary>아웃게임(도감/로비) 아이콘 줄용. 인게임과 같은 제외 규칙.</summary>
    public static CardKeyword IconKeywords(int _cardId) => TraitKeywords(_cardId) & ~IconRowExcluded;


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

        // 폴백은 "아이콘이 0개일 때"만. 실제 보유 키워드가 아니므로 키워드는 None으로 둔다.
        if (t_result.Count == 0 && _config.DefaultIcon != null)
            t_result.Add(new KeywordIcon(CardKeyword.None, _config.DefaultIcon));

        return t_result;
    }

    /// <summary>카드가 가진 시너지 중 배지로 표시할 것들을 표시 순서대로 뽑는다.
    /// null 스킵 → 같은 참조 중복은 1회 → 활성 우선, 동급이면 requiredCount 내림차순 정렬 → 상한 적용.
    /// _state가 null이면(아웃게임엔 전투 스냅샷이 없다) 활성 판정만 전부 false가 되고 requiredCount 정렬은 그대로 산다.</summary>
    public static List<SynergyData> CollectSynergyBadges(IReadOnlyList<SynergyData> _synergies, SynergyState _state, int _max = MaxSynergyBadges)
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
