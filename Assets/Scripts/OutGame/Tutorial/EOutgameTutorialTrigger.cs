// 자율 안내 챕터를 깨우는 발화 키. 세이브에 이름 문자열로 남으므로(완주 낙인) 리네임·삭제·값 재배치 금지.
public enum EOutgameTutorialTrigger
{
    None = 0,
    DeckTabFirstEnter,
    CollectionTabFirstEnter,
    FirstEvolutionReady,      // 폐기(첫 진화 안내) — 발화처 0. 뒤 항목이 밀리지 않게 값만 남긴다
    KeywordGrowthFirstOpen,   // 키워드 강화 화면 첫 진입 — 탭이 아니라 오버레이가 열리는 것이 깨운다
    AdventureMapFirstOpen,   // 모험 맵 첫 진입 — 탭이 아니라 오버레이가 열리는 것이 깨운다
    AdventureUnlocked,       // 모험 해금 후 로비에서 소개 챕터를 시작한다
    RankDivisionFirstUp,      // 랭크 단계가 처음 오른 순간(브1 → 브2) — 제자리 상승 연출이 끝난 뒤에 깨운다
    RankGradeFirstUp,         // 랭크 등급이 처음 갈린 순간(브론즈 → 실버) — 승급 오버레이·보상까지 걷힌 뒤에 깨운다
    ContentUnlocksAvailable = 9, // 미션·룰렛 해금 소개
    GuideCaretakerEnhanceArrived = 10, // 폐기(가이드 도착 코치마크) — 카드 소지가 전제라 성립하지 않아 저작·발화처 0. 값만 남긴다
    GuideAceEnhanceArrived = 11,       // 폐기(가이드 도착 코치마크) — 위와 같음
}
