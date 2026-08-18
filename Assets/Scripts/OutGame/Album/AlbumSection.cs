using System.Collections.Generic;

// 테마·페이지가 공유하는 앨범 구획 — 완성 판정 모수와 수령 낙인 키의 공통 축(저작 def에서 파생, 런타임 불변)
public abstract class AlbumSection
{
    public string Key { get; }

    // 완성 보상 저작값(빈 목록 = 보상 미저작)
    public IReadOnlyList<AlbumRewardDef> Rewards { get; }

    // 완성 판정 모수(null 슬롯 제외) — 다른 곳에서 Cards.Count로 재산출 금지
    public IReadOnlyList<int> CardIds { get; }

    // 수령 낙인 키 — 조립은 파생 생성자에서만. 안정 키(themeId/pageId) 미저작이면 null
    public string RewardKey { get; }

    // 거짓이면 이 구획의 보상은 영구 Locked다 — 낙인 키 유무와 같은 사실이라 따로 담지 않는다
    public bool HasStableKey => RewardKey != null;

    // 파생은 같은 어셈블리로 봉인 — CardIds가 null인 구획이 들어오면 진행도 산출이 터진다
    private protected AlbumSection(
        string _key,
        IReadOnlyList<AlbumRewardDef> _rewards,
        IReadOnlyList<int> _cardIds,
        string _rewardKey)
    {
        Key = _key;
        Rewards = _rewards;
        CardIds = _cardIds;
        RewardKey = _rewardKey;
    }
}
