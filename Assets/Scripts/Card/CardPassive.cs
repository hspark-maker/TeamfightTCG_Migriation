using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
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
        // 우측 슬라이드 배너는 BattleUxFlags.EffectNotifyBanner로 블라인드 중(가독성·학습성 판단).
        // 소리/보이스는 그대로 둔다 — 발동 자체의 피드백은 남겨야 한다. 배지 pop(SynergyTriggers.Fire)도 유지.
        if (!BattleUxFlags.EffectNotifyBanner) return;

        UIPoolManager.Instance?.AddOrUpdateUI<EffectNotifyUI>(new EffectNotifyData
        {
            portrait       = _iconOverride != null ? _iconOverride : CardVisualRules.PickCardArt(_self.cardId, _self.evolutionStage),
            preserveAspect = _iconOverride != null,   // 아이콘은 정사각이라 늘리면 찌그러짐
            cardName       = _self.spec.DisplayName,
            effectLabel    = _effectLabel,
        });
    }
}
