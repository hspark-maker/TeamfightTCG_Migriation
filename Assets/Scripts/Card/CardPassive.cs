using Cysharp.Threading.Tasks;
using UnityEngine;

public abstract class CardPassive : ScriptableObject
{
    public virtual UniTask OnSpawn(CardInstance _self) => UniTask.CompletedTask;
    public virtual UniTask OnTurnStart(CardInstance _self) => UniTask.CompletedTask;
    public virtual UniTask OnAfterAttack(CardInstance _self, CardInstance _target, BattleField _ownField) => UniTask.CompletedTask;
    public virtual UniTask OnKill(CardInstance _self, CardInstance _killed) => UniTask.CompletedTask;
    public virtual UniTask OnDealDamage(CardInstance _self, int _damage, bool _isRetaliation = false) => UniTask.CompletedTask;
    public virtual UniTask OnHit(CardInstance _self, int _damage) => UniTask.CompletedTask;
    public virtual UniTask OnAttackedBy(CardInstance _self, CardInstance _attacker) => UniTask.CompletedTask;
    public virtual UniTask OnSwapOut(CardInstance _self, CardInstance _incoming) => UniTask.CompletedTask;
    public virtual UniTask OnDeath(CardInstance _self, BattleField _ownField) => UniTask.CompletedTask;

    protected static UniTask Glow(CardInstance _self)
        => CardView.GetView(_self)?.PlayPassiveGlow() ?? UniTask.CompletedTask;

    public static void Notify(CardInstance _self, CardKeyword _kw)
    {
        string t_label = _kw.ToString();
        if (DataLibrary.instance?.keywordIconConfig != null &&
            DataLibrary.instance.keywordIconConfig.TryGetEntry(_kw, out var t_entry) &&
            !string.IsNullOrEmpty(t_entry.effectLabel))
            t_label = t_entry.effectLabel;
        Notify(_self, t_label);
    }

    public static void Notify(CardInstance _self, string _effectLabel)
    {
        if (string.IsNullOrEmpty(_effectLabel)) return;
        SoundManager.Instance?.PlayPassive();
        SoundManager.Instance?.PlayRandom(_self.data?.effectClips);
        SoundManager.Instance?.PlayEffectVoice(_self.data?.effectVoices);
        UIPoolManager.instance?.AddOrUpdateUI<EffectNotifyUI>(new EffectNotifyData
        {
            portrait = _self.data.fullImage,
            cardName = _self.data.displayName,
            effectLabel = _effectLabel,
        });
    }
}
