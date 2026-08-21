using System;

// 아웃게임 세이브 값 객체 — 전투 밖 유저 상태의 스냅샷
[Serializable]
public class UserSaveData
{
    // 런칭 전 정책: 어느 도메인이든 스키마를 변경하면 이 전역 버전을 올린다.
    // 버전 불일치는 원본 JSON을 백업한 뒤 전체 세이브를 초기화한다.
    // 2: 도감 방치 생산 폐기로 collection 슬롯 삭제
    // 3: 카드 강화 레벨제를 0~3성 등급제로 전환
    public const int VERSION = 3;

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
