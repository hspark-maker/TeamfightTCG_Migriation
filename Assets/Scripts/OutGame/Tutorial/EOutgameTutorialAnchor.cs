// 아웃게임 튜토리얼 안내 타깃 키
// SO에 int로 직렬화 → 새 값은 끝에만 추가(재배치·삭제 시 저작된 스텝이 엉뚱한 타깃을 가리킨다)
public enum EOutgameTutorialAnchor
{
    None                  = 0,
    LobbyPlayButton       = 1,
    LobbyPackTab          = 2,
    PackBuyButton         = 3,
    PackAcquireButton     = 4,
    LobbyDeckTab          = 5,
    LobbyMatchTab         = 6,
    DeckCreateSlot        = 7,
    DeckCollectionArea    = 8,    // 8~11·14는 로비/매치 덱 편집(프리팹 배리언트)이 공유하는 키
    DeckSlotArea          = 9,
    DeckAutoEquipButton   = 10,
    DeckEditBackButton    = 11,
    MatchDeckEditButton   = 12,
    MatchDeckBattleButton = 13,
    DeckUnequipAllButton  = 14,
    MatchDeckMySection    = 15,
    MatchDeckEnemySection = 16,
    MatchDeckTutorialDeck = 17,
    LobbyCollectionTab    = 18,
    AlbumThemeCell        = 19,   // 갤러리 셀은 런타임 생성이라 아직 안 꽂은 카드가 있는 테마의 칸이 스스로 등록한다
    AlbumCardSlot         = 20,   // 페이지 오버레이의 첫 소유 칸. 칸도 런타임 생성이라 슬롯이 스스로 등록한다
    CardDetailEnhanceButton = 21, // 카드 상세의 강화 버튼(진화 버튼과 자리를 번갈아 써서 코드가 등록한다)
    KeywordGrowthCell       = 22, // 키워드 강화 격자의 칸. 칸도 런타임 생성이라 지금 선택된 칸이 스스로 등록한다
    KeywordGrowthUpgradeButton = 23, // 키워드 강화 하단의 업그레이드 버튼(패널이 열려 있는 동안만)
    TournamentButton           = 24, // 로비 매치 탭의 보상 토너먼트 버튼
}
