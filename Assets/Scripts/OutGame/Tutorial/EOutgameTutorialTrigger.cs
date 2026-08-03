// 트리거 발화 튜토리얼의 발화 키. 세이브엔 enum.ToString() 이름 문자열로 남는다
// → 값 재배치·중간 삽입은 안전하지만 멤버 리네임·삭제는 금지(완주 낙인이 풀려 이미 본 튜토리얼이 다시 뜬다).
public enum EOutgameTutorialTrigger
{
    None = 0,
    DeckTabFirstEnter,         // LobbyScene: 덱 탭 첫 진입
    CollectionTabFirstEnter,   // LobbyScene: 도감 탭 첫 진입
}
