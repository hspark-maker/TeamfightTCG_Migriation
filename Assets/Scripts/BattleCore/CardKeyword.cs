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
}
