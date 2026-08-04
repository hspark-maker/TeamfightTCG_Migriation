// 튜토리얼 진행에 따라 열리는 기능 단위. 스텝 행의 unlocks에 저작해 "이 스텝부터 열림"을 지정한다.
// SO는 int로 직렬화 → 새 값은 반드시 끝에만 추가. 기존 값 재배치·삭제 금지(저작된 스텝이 엉뚱한 기능을 연다).
//
// EOutgameTutorialAnchor와 일부러 분리했다 — 앵커는 "지금 가리킬 곳"(그 스텝 동안만),
// 이쪽은 "쓸 수 있는 기능"(한 번 열리면 계속)이라 수명도 대상 범위도 다르다.
// 잠금 대상은 앵커가 붙지 않은 곳(상점 탭·도감 수확·캐러셀)까지 넓다.
public enum EOutgameFeature
{
    None               = 0,
    LobbyShopTab       = 1,    // 하단바 탭 0(상점)
    LobbyPackTab       = 2,    // 하단바 탭 1(뽑기)
    LobbyMatchTab      = 3,    // 하단바 탭 2(배틀). 기본 탭이라 저작에서 잠그지 않는다
    LobbyDeckTab       = 4,    // 하단바 탭 3(덱)
    LobbyCollectionTab = 5,    // 하단바 탭 4(도감)
    LobbyPlay          = 6,    // 로비 MatchContent/PlayBtn
    PackBuy            = 7,    // 팩 구매 버튼
    PackCarousel       = 8,    // 팩 캐러셀 좌우 넘김
    DeckCreate         = 9,    // 덱 목록 "신규 생성" 칸
    DeckEditToggle     = 10,   // 덱 목록 편집(삭제) 모드 토글
    DeckAutoEquip      = 11,   // 덱 편집 자동 편성
    CollectionHarvest  = 12,   // 도감 생산물 수확
    RankReward         = 13,   // 랭크 보상 수령 — 현재 배선 없음(RankRewardRowView가 자체 잠금 축을 이미 갖고 있다)
}
