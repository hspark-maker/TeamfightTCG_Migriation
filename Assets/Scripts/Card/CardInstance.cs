using Cysharp.Threading.Tasks;

public class CardInstance
{
    public CardData data;
    public int hp;
    public int bonusHp;
    public int slotIndex;   // -1 = waiting queue
    public bool isRevealed;
    public bool wasEverRevealed;
    public int ownerIndex;   // 싱글: 0=player, 1=enemy / 멀티: TurnState.LocalOwnerIndex가 아군, 나머지 적
    public CardKeyword runtimeKeywords;
    public int attackCount;

    // 덱 복귀 시 체력 보존용 (-1 = 미저장)
    public int savedHp      = -1;
    public int savedBonusHp = -1;

    // 스폰 직후 OnTurnStart 1회 스킵용 (피즈·그웬 무적 즉시 소멸 방지)
    public bool justSpawned;

    public bool IsAlive => this.hp > 0;
    public bool HasKeyword(CardKeyword _kw) => (this.data.keywords | this.runtimeKeywords).HasFlag(_kw);

    public CardInstance(CardData _data, int _ownerIndex)
    {
        this.data    = _data;
        this.hp      = _data.maxHp;
        this.bonusHp = _data.bonusHp;
        this.slotIndex   = -1;
        this.isRevealed  = false;
        this.ownerIndex  = _ownerIndex;
    }

    public void TakeDamage(int _damage)
    {
        if (HasKeyword(CardKeyword.Invincible))
        {
            this.runtimeKeywords &= ~CardKeyword.Invincible;
            return;
        }
        int t_bonusDrain = UnityEngine.Mathf.Min(this.bonusHp, _damage);
        this.bonusHp -= t_bonusDrain;
        this.hp = UnityEngine.Mathf.Max(0, this.hp - (_damage - t_bonusDrain));
        if (_damage > 0)
            this.data.passive?.OnHit(this, _damage).Forget();
    }
}
