// 튜토리얼 진행에 따라 열리는 기능 단위
// SO에 int로 직렬화 → 새 값은 끝에만 추가(재배치·삭제 시 저작된 스텝이 엉뚱한 기능을 연다)
public enum EOutgameFeature
{
    None               = 0,
    LobbyShopTab       = 1,
    LobbyPackTab       = 2,
    LobbyMatchTab      = 3,
    LobbyDeckTab       = 4,
    LobbyCollectionTab = 5,
    LobbyPlay          = 6,
    PackBuy            = 7,
    PackCarousel       = 8,
    DeckCreate         = 9,
    DeckEditToggle     = 10,
    DeckAutoEquip      = 11,
    CollectionHarvest  = 12,   // 폐기(구 도감 수확) — 소비처 0. 값은 뒤 항목이 밀리지 않게 남긴다
    RankReward         = 13,
    CardEnhance        = 14,   // 카드 상세의 성장 한 방(강화 버튼 한 칸이 진화 관문에서 얼굴만 갈아입는다)
    KeywordGrowth      = 15,   // 로비 매치 탭의 키워드 강화 패널
    Tournament         = 16,   // 로비 매치 탭의 보상 토너먼트 진입 버튼 — 온보딩 마지막 스텝이 연다
}
