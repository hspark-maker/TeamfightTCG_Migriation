// 모험 정점의 종류. 표현만 가른다 — 진입 자격·전투 흐름·보상 지급은 종류와 무관하다.
// 새 종류를 더해도 기존 저작은 안 깨진다(미저작 = 0 = Battle).
public enum EAdventureNodeKind
{
    Battle = 0,   // 보통 정점(기본)
    Elite  = 1,   // 강적 — 그 판의 우두머리
}
