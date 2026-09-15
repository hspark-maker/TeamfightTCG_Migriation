using System;
using UnityEngine;

// 시작 UI가 아닌 게임 콘텐츠의 저작 참조. 원격으로 적재한 뒤 기존 초기화 스텝에 공급한다.
[CreateAssetMenu(menuName = "Card Battle/Runtime Content Catalog")]
public sealed class RuntimeContentCatalog : ScriptableObject
{
    public BattleTimingConfig battleTimingConfig;
    public RankConfig rankConfig;
    public BattleVfxLibrary battleVfxLibrary;
    public SynergyRegistry synergyRegistry;
    public CardAlbumConfig albumConfig;
    public CurrencyLook currencyLook;
    public AdventureConfig adventureConfig;
    public ProfileConfig profileConfig;
    public EmoteCatalog emoteCatalog;
    public DeckImageCatalog deckImageCatalog;
    public RouletteConfig rouletteConfig;
    public OutgameTutorialData tutorialData;
    public ContentUnlockData contentUnlockData;
    public KeywordIconConfig keywordIconConfig;
    public CardFrameConfig cardFrameConfig;

    public void Validate()
    {
        if (battleTimingConfig == null || rankConfig == null || battleVfxLibrary == null ||
            synergyRegistry == null || albumConfig == null || currencyLook == null ||
            adventureConfig == null || profileConfig == null || emoteCatalog == null ||
            deckImageCatalog == null || rouletteConfig == null || tutorialData == null ||
            keywordIconConfig == null || cardFrameConfig == null || contentUnlockData == null)
            throw new InvalidOperationException("RuntimeContentCatalog has missing authored references.");
    }
}
