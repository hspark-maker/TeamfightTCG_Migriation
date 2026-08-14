// 트리거 발화 튜토리얼의 발화 키
// 세이브엔 enum 이름 문자열로 남는다 → 리네임·삭제 금지(완주 낙인이 풀린다)
public enum EOutgameTutorialTrigger
{
    None = 0,
    DeckTabFirstEnter,
    CollectionTabFirstEnter,
    FirstEvolutionReady,      // 첫 진화 관문 도달 — 탭이 아니라 강화 결과가 깨운다
    KeywordGrowthFirstOpen,   // 키워드 강화 화면 첫 진입 — 탭이 아니라 오버레이가 열리는 것이 깨운다
}
