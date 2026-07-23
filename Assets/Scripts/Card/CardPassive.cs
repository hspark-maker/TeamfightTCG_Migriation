using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 카드 고유 효과 1건. 활성 조건 = "이 카드가 있다"(상시). 카드당 1개(CardData.passive 단수).
///
/// **훅은 <see cref="BattleEffect"/>에 선언돼 있다** — 여기엔 표시 유틸만 둔다.
/// 어느 타이밍이 있는지·계약이 뭔지는 <see cref="BattleTimings"/>를 봐라.
/// 덱 조합으로 열리는 효과는 이쪽이 아니라 SynergyEffect다(활성 조건만 다르고 훅은 동일).
/// </summary>
public abstract class CardPassive : BattleEffect
{
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

    /// <summary>효과 발동 배너. _iconOverride를 주면 카드 초상화 대신 그 아이콘을 띄운다
    /// (시너지 발동은 어느 시너지인지가 핵심이라 시너지 아이콘을 넘긴다 — SynergyTriggers.Fire).</summary>
    public static void Notify(CardInstance _self, string _effectLabel, Sprite _iconOverride = null)
    {
        if (string.IsNullOrEmpty(_effectLabel)) return;
        SoundManager.Instance?.PlayPassive();
        SoundManager.Instance?.PlayRandom(_self.data?.effectClips);
        SoundManager.Instance?.PlayEffectVoice(_self.data?.effectVoices);
        UIPoolManager.instance?.AddOrUpdateUI<EffectNotifyUI>(new EffectNotifyData
        {
            portrait       = _iconOverride != null ? _iconOverride : _self.data.fullImage,
            preserveAspect = _iconOverride != null,   // 아이콘은 정사각이라 늘리면 찌그러짐
            cardName       = _self.data.displayName,
            effectLabel    = _effectLabel,
        });
    }
}
