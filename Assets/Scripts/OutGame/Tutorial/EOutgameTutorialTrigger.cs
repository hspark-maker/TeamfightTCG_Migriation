// 미션 흐름이 참조하는 자율 챕터 식별자. 기존 이름은 완료 기록 문자열이므로 리네임·삭제·값 재배치 금지.
public enum EOutgameTutorialTrigger
{
    None = 0,
    DeckTabFirstEnter,
    CollectionTabFirstEnter,
    FirstEvolutionReady,      // 폐기(첫 진화 안내) — 발화처 0. 뒤 항목이 밀리지 않게 값만 남긴다
    KeywordGrowthFirstOpen,   // 폐기 — 기존 저장·직렬화 값 보존.
    AdventureMapFirstOpen,    // 이전 모험 소개 완료 기록
    AdventureUnlocked,       // 모험 미션에 연결된 소개 챕터
    RankDivisionFirstUp,      // 이전 랭크 단계 상승 안내 식별자
    RankGradeFirstUp,         // 이전 랭크 등급 상승 안내 식별자
    ContentUnlocksAvailable = 9, // 미션·룰렛·카드 강화 해금 소개
    GuideCaretakerEnhanceArrived = 10, // 폐기(가이드 도착 코치마크) — 카드 소지가 전제라 성립하지 않아 저작·발화처 0. 값만 남긴다
    GuideAceEnhanceArrived = 11,       // 폐기(가이드 도착 코치마크) — 위와 같음
    GuideMissionIntroduction = 12,
    KeywordIntroduction = 13, // 폐기 — 카드 강화 스텝으로 이관. 기존 저장값만 보존한다
    SynergyIntroduction = 14, // 폐기 — 기존 저장값만 보존한다
    CaretakerPreparation = 15, // 폐기 — 기존 저장값만 보존한다
    CaretakerReady = 16, // 폐기 — 기존 저장값만 보존한다
    CaretakerActivation = 17, // 폐기 — 기존 저장값만 보존한다
    SynergyGrowthIntroduction = 18,
    SynergyBattleIntroduction = 19,
    RankRewardIntroduction = 20, // 첫 랭크 보상 수령 안내 표시 이력
}
