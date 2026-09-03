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
    MatchDeckTutorialDeck = 17,   // 폐기 — 가로 덱 리스트가 사라졌다(값은 재사용 금지)
    LobbyCollectionTab    = 18,
    AlbumThemeCell        = 19,   // 갤러리 셀은 런타임 생성이라 아직 안 꽂은 카드가 있는 테마의 칸이 스스로 등록한다
    AlbumCardSlot         = 20,   // 페이지 오버레이의 첫 소유 칸. 칸도 런타임 생성이라 슬롯이 스스로 등록한다
    CardDetailEnhanceButton = 21, // 카드 상세의 강화 버튼(진화 관문에도 같은 버튼이라 코드가 등록한다)
    KeywordGrowthCell       = 22, // 키워드 강화 격자의 칸. 칸도 런타임 생성이라 지금 선택된 칸이 스스로 등록한다
    KeywordGrowthUpgradeButton = 23, // 키워드 강화 하단의 업그레이드 버튼(패널이 열려 있는 동안만)
    AdventureButton           = 24, // 로비 매치 탭의 모험 버튼
    DeckEditCollectionCard     = 25, // 덱 편집 컬렉션 격자에서 지목된 카드 타일. 타일이 런타임 생성이라 격자가 등록한다
    AdventureNode             = 26, // 모험 지도에서 지금 도전할 정점. 지목은 맵이 소유하므로 맵이 켠 정점만 등록한다
    MatchDeckPowerBadge        = 27, // 매치 덱 화면의 내 덱 파워 배지
    MatchDeckEnemyPowerBadge   = 28, // 매치 덱 화면의 상대 덱 파워 배지
    DeckEditSaveButton         = 29, // 덱 편집 우하단의 저장 버튼. 로비 덱 편집에만 있다 — 매치 화면은 이탈 확인 팝업으로 저장한다
    KeywordGrowthPanel         = 30, // 키워드 강화 패널 본체(Root/Panel). 누를 대상이 아니라 "함께 밝힐 영역"으로만 쓴다
}
