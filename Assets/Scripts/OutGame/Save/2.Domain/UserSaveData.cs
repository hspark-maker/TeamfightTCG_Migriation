using System;

// 아웃게임 세이브 값 객체 — 전투 밖 유저 상태의 스냅샷
[Serializable]
public class UserSaveData
{
    // 하위호환 — 슬롯·필드 추가는 VERSION 유지, 의미 변경·삭제·리네임 시에만 올린다
    // 2: 도감 방치 생산 폐기로 collection 슬롯 삭제
    public const int VERSION = 2;

    public int version = VERSION;

    public CurrencySaveData currency = new CurrencySaveData();
    public OwnershipSaveData ownership = new OwnershipSaveData();
    public DeckSaveData deck = new DeckSaveData();

    public TutorialSaveData tutorial = new TutorialSaveData();

    public RankSaveData rank = new RankSaveData();

    public CardGrowthSaveData cardGrowth = new CardGrowthSaveData();

    public KeywordGrowthSaveData keywordGrowth = new KeywordGrowthSaveData();

    public AlbumRewardSaveData albumReward = new AlbumRewardSaveData();

    public TournamentSaveData tournament = new TournamentSaveData();
}
