// 아웃게임 튜토리얼 안내 타깃 키. 씬 경로 문자열 대신 enum으로 식별한다(탭 버튼이 프리팹 내부라 경로가 취약).
// SO는 int로 직렬화 → 새 값은 반드시 끝에만 추가. 기존 값 재배치·삭제 금지(저작된 스텝이 엉뚱한 타깃을 가리키게 된다).
public enum EOutgameTutorialAnchor
{
    None              = 0,
    LobbyPlayButton   = 1,   // LobbyScene: MatchContent/PlayBtn
    LobbyPackTab      = 2,   // LobbyScene: 하단바 탭 1(Pack)
    PackBuyButton     = 3,   // LobbyScene: PackContent 쇼케이스 구매 버튼
    PackAcquireButton = 4,   // CardPack: AcquireButton
}
