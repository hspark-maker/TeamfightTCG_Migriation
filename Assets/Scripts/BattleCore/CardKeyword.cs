[System.Flags]
public enum CardKeyword
{
    None       = 0,
    Ranged     = 1 << 0,
    Peerless   = 1 << 1,
    Execution  = 1 << 2,
    Taunt      = 1 << 3,
    Cunning    = 1 << 4,
    Mark       = 1 << 5,
    Healer     = 1 << 6,
    Invincible = 1 << 7,
    BonusHp    = 1 << 8,
    Immortal   = 1 << 9,   // 불사: 전투 중 1회, 치사 피해 시 최대 체력 50%로 부활(CardInstance.ReviveAtHalf)
}
