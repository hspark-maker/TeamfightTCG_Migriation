using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 카드 고유 효과 1건. 활성 조건 = "이 카드가 있다"(상시). 카드당 1개(CardData.passive 단수).
/// 훅 이름·컨텍스트·계약은 <see cref="BattleTimings"/>의 타이밍 택소노미를 따른다 — 여기서 새 이름을 만들지 마라.
/// 덱 조합으로 열리는 효과는 이쪽이 아니라 SynergyEffect다.
/// </summary>
public abstract class CardPassive : ScriptableObject
{
    // ── 배치·등장 ──
    // Placed와 Entered는 **다른 타이밍**이다. 구 OnSpawn이 둘을 겸해서 생긴 혼란을 분리한 것.
    // 오프닝에도 런타임 등장에도 반응해야 하면 둘 다 override 하고 공통 로직을 private 메서드로 뺀다.

    /// <summary>[Placed] 오프닝 초기 배치. **등장이 아니다** — 시너지 Entered는 여기서 안 터진다.
    /// 동기 완결 계약(.Forget 발화).</summary>
    public virtual UniTask OnPlaced(SpawnCtx _ctx) => UniTask.CompletedTask;

    /// <summary>[Entered] 런타임 등장(빈 슬롯 보충 / 교활 스왑 / 원격 미러 스폰).
    /// 동기 완결 계약(.Forget 발화).</summary>
    public virtual UniTask OnEntered(SpawnCtx _ctx) => UniTask.CompletedTask;

    // ── 턴 ──

    /// <summary>[TurnBegan] 내 턴 시작. await 계약. 스폰 직후 1턴은 스킵된다(justSpawned).</summary>
    public virtual UniTask OnTurnBegan(TurnCtx _ctx) => UniTask.CompletedTask;

    // ── 공격 ──

    /// <summary>[Attacked] 피격 반응(가시 반격 등). self=방어자, 직격이 이미 적용된 뒤.
    /// **동기 완결 필수** — 반격이 치사 래치보다 먼저 hp에 반영돼야 한다(await 금지, .Forget 발화).</summary>
    public virtual UniTask OnAttacked(AttackedCtx _ctx) => UniTask.CompletedTask;

    /// <summary>[DamageDealt] 피해를 입힌 뒤 통보. 반격분이면 ctx.isRetaliation=true. .Forget 발화.</summary>
    public virtual UniTask OnDamageDealt(DamageDealtCtx _ctx) => UniTask.CompletedTask;

    /// <summary>[SwappedOut] 교활 등으로 필드를 떠남. .Forget 발화.</summary>
    public virtual UniTask OnSwappedOut(SwapOutCtx _ctx) => UniTask.CompletedTask;

    /// <summary>[Removed] 슬롯 제거 직전. **취소 불가** — 시너지 Lethal이 부활시켰으면 여기까지 오지 않는다.
    /// .Forget 발화.</summary>
    public virtual UniTask OnRemoved(DeathCtx _ctx) => UniTask.CompletedTask;

    /// <summary>[AfterAttack] 공격 완료(연출 후). await 계약. 구 OnKill 흡수 — 처치 판정은 ctx.defenderKilled.
    /// **주의: defenderKilled는 치사 래치값(Lethal 전에 확정)이라 언데드가 부활해도 true다.**
    /// "hp를 0으로 만들었나"이지 "실제로 사라졌나"가 아니다. 후자가 필요하면 ctx.target.IsAlive를 따로 봐라.</summary>
    public virtual UniTask OnAfterAttack(AfterAttackCtx _ctx) => UniTask.CompletedTask;

    // ── 표시 유틸 ──

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
