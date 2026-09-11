// 챕터의 성격. 값은 직렬화되므로 재배치하지 않는다 — 0이 Forced라 기존 저작은 손대지 않아도 강제로 읽힌다.
public enum EOutgameTutorialChapterKind
{
    Forced = 0,   // 첫 시작 시퀀스. 세이브 좌표가 커서이고 기능 잠금이 여기서 파생된다
    Guided = 1,   // 졸업 뒤 자율 안내. 트리거로 깨어나 메모리 커서로 돌고 완주 낙인만 남긴다
}
