[System.Flags]
public enum CardKeyword
{
    None      = 0,
    Ranged    = 1 << 0,  // 원거리: 반격 없음
    Peerless  = 1 << 1,  // 무쌍: 인접 50% 광역
    Execution = 1 << 2,  // 처형: 처치 시 재공격
    Taunt     = 1 << 3,  // 도발: 공격 피해 절반
    Cunning   = 1 << 4,  // 교활: 공격 후 교체, 반격 없음
    Mark      = 1 << 5,  // 표식: 공격자 반격 면제
    Healer    = 1 << 6,  // 힐러: 턴 시작 아군 1 회복(최대 체력 초과 가능)
    Invincible = 1 << 7, // 무적: 피해 면역
    BonusHp   = 1 << 8, // 추가 생명력
}
