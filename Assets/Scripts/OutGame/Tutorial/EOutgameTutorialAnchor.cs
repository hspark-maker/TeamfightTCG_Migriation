// 아웃게임 튜토리얼 안내 타깃 키. 씬 경로 문자열 대신 enum으로 식별한다(탭 버튼이 프리팹 내부라 경로가 취약).
// SO는 int로 직렬화 → 새 값은 반드시 끝에만 추가. 기존 값 재배치·삭제 금지(저작된 스텝이 엉뚱한 타깃을 가리키게 된다).
//
// 8~11·14는 두 화면이 같은 키를 공유한다 — 매치 덱 편집(MatchDeckEditPanel)이 로비 DeckEditPanel의
// 프리팹 배리언트라 앵커 컴포넌트가 그대로 상속되기 때문. 덕분에 덱 편집 안내 스텝을 두 화면에서 그대로 재사용한다.
// 레지스트리는 키당 1개만 보관하는데 두 화면이 이제 같은 로비 씬에 함께 산다 —
// 안전장치는 TutorialAnchor의 소유권 기반 해제(Unregister(key, rect))다. 씬 분리에 기대지 않는다.
public enum EOutgameTutorialAnchor
{
    None                  = 0,
    LobbyPlayButton       = 1,    // LobbyScene: MatchContent/PlayBtn
    LobbyPackTab          = 2,    // LobbyScene: 하단바 탭 1(Pack)
    PackBuyButton         = 3,    // LobbyScene: PackContent 쇼케이스 구매 버튼
    PackAcquireButton     = 4,    // CardPack: AcquireButton
    LobbyDeckTab          = 5,    // LobbyScene: 하단바 탭 3(Deck) — 현재 저작 미사용
    LobbyMatchTab         = 6,    // LobbyScene: 하단바 탭 2(Match) — 현재 저작 미사용
    DeckCreateSlot        = 7,    // LobbyScene: 덱 목록 "신규 생성" 칸(런타임 생성) — 현재 저작 미사용
    DeckCollectionArea    = 8,    // 덱 편집 소유 카드 영역 (덱 탭 DeckEditPanel / 매치 MatchDeckEditPanel 공용)
    DeckSlotArea          = 9,    // 덱 편집 편성 6칸 영역 (공용)
    DeckAutoEquipButton   = 10,   // 덱 편집 Btn_AutoEquip (공용)
    DeckEditBackButton    = 11,   // 덱 편집 뒤로가기 (덱 탭은 BackButton, 매치는 BottomBar/Btn_MatchBack)
    MatchDeckEditButton   = 12,   // 매치 덱 오버레이: MatchDeckPanel/BottomBar/EditButton
    MatchDeckBattleButton = 13,   // 매치 덱 오버레이: MatchDeckPanel/BottomBar/BattleButton
    DeckUnequipAllButton  = 14,   // 덱 편집 Btn_UnequipAll (공용). 자동편성은 덱이 6/6이면 잠기므로 그 앞에 비우는 스텝을 둔다
    MatchDeckMySection    = 15,   // 매치 덱 오버레이: MatchDeckPanel/Content/MySection (Button 없음 → Message 액션 전용)
    MatchDeckEnemySection = 16,   // 매치 덱 오버레이: MatchDeckPanel/Content/EnemySection (동상)
    MatchDeckTutorialDeck = 17,   // 매치 덱 리스트에서 이번 튜토리얼 덱 칸(런타임 등록). 로비 목록은 이 키를 쓰지 않아 8~11과 달리 공유 충돌이 없다
}
