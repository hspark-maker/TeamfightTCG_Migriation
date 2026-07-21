using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "PoppyPassive", menuName = "Card Battle/Passives/Poppy")]
public class PoppyPassive : CardPassive
{
    [SerializeField] string effectLabel;

    public override async UniTask OnDealDamage(CardInstance _self, int _damage, bool _isRetaliation = false)
    {
        if (_isRetaliation) return;
        _self.bonusHp += Mathf.FloorToInt(_damage * 0.5f);
        Notify(_self, this.effectLabel);
        await Glow(_self);
    }
}
