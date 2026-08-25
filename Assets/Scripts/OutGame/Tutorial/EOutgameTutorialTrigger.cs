// 트리거 발화 튜토리얼의 발화 키
// 세이브엔 enum 이름 문자열로 남는다 → 리네임·삭제 금지(완주 낙인이 풀린다)
public enum EOutgameTutorialTrigger
{
    None = 0,
    DeckTabFirstEnter,
    CollectionTabFirstEnter,
    FirstEvolutionReady,      // 폐기(첫 진화 안내) — 발화처 0. 뒤 항목이 밀리지 않게 값만 남긴다
    KeywordGrowthFirstOpen,   // 키워드 강화 화면 첫 진입 — 탭이 아니라 오버레이가 열리는 것이 깨운다
    TournamentMapFirstOpen,   // 토너먼트 맵 첫 진입 — 탭이 아니라 오버레이가 열리는 것이 깨운다
    TournamentUnlocked,       // 폐기 — 온보딩 챕터 "토너먼트 오픈"이 대신한다. 발화처 0, 뒤 항목이 밀리지 않게 값만 남긴다
    RankDivisionFirstUp,      // 랭크 단계가 처음 오른 순간(브1 → 브2) — 제자리 상승 연출이 끝난 뒤에 깨운다
    RankGradeFirstUp,         // 랭크 등급이 처음 갈린 순간(브론즈 → 실버) — 승급 오버레이·보상까지 걷힌 뒤에 깨운다
}
