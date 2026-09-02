using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>시너지 대본의 공통 배역 배선. 아군 둘이 아랫줄에, 적 하나가 맞은편에 선다.</summary>
// 시너지는 같은 편이 모여야 성립하므로 곁자리 진영에 분기가 없다.
abstract class SynergyDemoScript : IUnlockDemoScript
{
    protected readonly SynergyData Synergy;

    protected SynergyDemoScript(SynergyData _synergy)
    {
        this.Synergy = _synergy;
    }

    public bool TryBuildCast(int _card, KeywordDemoConfig _config, out UnlockDemoCast _cast)
    {
        _cast = default;

        if (this.Synergy == null) return false;

        if (this.Synergy.vfx == null)
        {
            Debug.LogWarning($"[UnlockDemoStage] {this.Synergy.SynergyId}: 연출 에셋(vfx) 미배선 — 무대 없이 글자만 남깁니다.");
            return false;
        }

        int t_opponent = 0;
        int t_unused   = 0;

        // 시너지에는 배역 저작 축이 없다 — 맞은편은 키워드 표의 기본값을 쓰고 동료는 코드가 고른다.
        _config?.Roles(CardKeyword.None, out t_opponent, out t_unused);

        if (t_opponent <= 0)
        {
            Debug.LogWarning("[UnlockDemoStage] 시너지 데모의 상대 카드가 저작되지 않았습니다(KeywordDemoConfig의 기본 배역 확인).");
            return false;
        }

        _cast = UnlockDemoCast.OfSynergy(t_opponent,
                                         FindSynergyCompanion(_card, this.Synergy, t_opponent),
                                         MakeShowState(this.Synergy));
        return true;
    }

    public abstract UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token);

    /// <summary>연출 에셋이 기대한 타입인가. 어긋나면 경고를 내고 false — 부른 대본은 폴백으로 떨어진다.</summary>
    protected bool TryVfx<T>(out T _cfg) where T : SynergyVfxConfig
    {
        _cfg = this.Synergy.vfx as T;
        if (_cfg != null) return true;

        WarnVfxType(typeof(T).Name);
        return false;
    }

    /// <summary>연출 에셋 타입이 어긋났다고 알린다. 대본이 통째로 무음이 되는 것과 깨진 것을 로그로 가른다.</summary>
    protected void WarnVfxType(string _expected)
        => Debug.LogWarning($"[UnlockDemoStage] {this.Synergy.SynergyId}: vfx가 {_expected}가 아니라 기본 대본으로 떨어집니다.");

    /// <summary>대본이 아직 없는 시너지와 연출 에셋 타입이 어긋난 경우의 폴백.</summary>
    protected async UniTask PlayFallbackAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk = _stage.Attacker;
        CardView t_def = _stage.Defender;

        SynergyEmblemTiming t_timing = this.Synergy.PlaysEmblemAt(SynergyEmblemTiming.Triggered)
                                     ? SynergyEmblemTiming.Triggered
                                     : SynergyEmblemTiming.Placed;

        if (this.Synergy.PlaysEmblemAt(t_timing))
        {
            SynergyEmblemVfx.Play(t_atk, this.Synergy, t_timing);
            await _stage.Hold(SynergyEmblemVfx.DurationOf(this.Synergy, t_timing), _token);
            if (_token.IsCancellationRequested) return;
        }

        await _stage.Swing(t_atk, t_def, null, _token);
    }

    // 열거 순서가 아니라 최소값으로 고른다 — CardCatalog.AllIds는 Dictionary 열거라 순서가 보장되지 않는데,
    // 같은 시너지에는 언제나 같은 동료가 서야 한다.
    static int FindSynergyCompanion(int _card, SynergyData _synergy, int _opponent)
    {
        // 미준비 상태에서 RequireSynergies를 부르면 throw한다.
        if (!CardCatalog.IsReady || _synergy == null) return 0;

        int t_best = 0;

        foreach (int t_id in CardCatalog.AllIds)
        {
            if (t_id == _card || t_id == _opponent) continue;
            if (t_best > 0 && t_id > t_best) continue;

            IReadOnlyList<SynergyData> t_list = CardCatalog.RequireSynergies(t_id);
            if (t_list == null) continue;

            foreach (SynergyData t_s in t_list)
                if (t_s == _synergy) { t_best = t_id; break; }
        }

        return t_best;
    }

    // 배지를 켜기 위한 표시 전용 상태. 안 넘기면 CardVisualRules.IsSynergyActive가 전부 false라 배지가 한 장도 안 뜬다.
    // 티어는 카드가 시너지를 둘 이상 가질 때의 정렬에만 쓰이므로 첫 단계로 둔다.
    static SynergyState MakeShowState(SynergyData _synergy)
    {
        if (_synergy == null) return null;

        SynergyTier t_tier = _synergy.tiers != null && _synergy.tiers.Length > 0 ? _synergy.tiers[0] : null;

        var t_active = new ActiveSynergy
        {
            Synergy   = _synergy,
            Count     = UnlockDemoNumbers.SYNERGY_SHOW_COUNT,
            TierIndex = 0,
            Tier      = t_tier,
        };

        return new SynergyState(new List<ActiveSynergy> { t_active });
    }
}

/// <summary>대본이 저작되지 않은 시너지가 받는 기본 안무.</summary>
sealed class AnySynergyDemoScript : SynergyDemoScript
{
    public AnySynergyDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
        => PlayFallbackAsync(_stage, _token);
}

/// <summary>덩치. 같은 편이 모이니 몸이 커진다 — 볼거리가 배치 그 자체라 공격을 붙이지 않는다.</summary>
sealed class BulkDemoScript : SynergyDemoScript
{
    public BulkDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk  = _stage.Attacker;
        CardView t_ally = _stage.Ally;

        if (t_ally != null)
        {
            SynergyEmblemVfx.Play(t_ally, this.Synergy, SynergyEmblemTiming.Placed);
            await _stage.Hold(UnlockDemoNumbers.SYNERGY_STEP, _token);
            if (_token.IsCancellationRequested) return;
        }

        SynergyEmblemVfx.Play(t_atk, this.Synergy, SynergyEmblemTiming.Placed);
        await _stage.Hold(SynergyEmblemVfx.DurationOf(this.Synergy, SynergyEmblemTiming.Placed), _token);
        if (_token.IsCancellationRequested) return;

        SynergyEmblemVfx.Play(t_atk, this.Synergy, SynergyEmblemTiming.Triggered);
        DemoHpDisplay.ShowBonusHp(t_atk, UnlockDemoNumbers.BULK_BONUS_HP);

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>비늘. 맞아도 덜 아프다 — 감쇄된 만큼만 깎이는 그림을 내고 접촉 순간의 엠블럼이 그 원인을 밝힌다.</summary>
// 피해 숫자가 아예 안 뜨면 "덜 아프다"가 아니라 "무적"으로 읽힌다.
sealed class ScaleDemoScript : SynergyDemoScript
{
    public ScaleDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk  = _stage.Attacker;
        CardView t_def  = _stage.Defender;
        CardView t_ally = _stage.Ally;

        if (t_ally != null) SynergyEmblemVfx.Play(t_ally, this.Synergy, SynergyEmblemTiming.Placed);
        SynergyEmblemVfx.Play(t_atk, this.Synergy, SynergyEmblemTiming.Placed);

        await _stage.Hold(SynergyEmblemVfx.DurationOf(this.Synergy, SynergyEmblemTiming.Placed), _token);
        if (_token.IsCancellationRequested) return;

        await _stage.Swing(t_def, t_atk, null, _token, _afterHit: () =>
        {
            SynergyEmblemVfx.Play(t_atk, this.Synergy, SynergyEmblemTiming.Triggered);

            // 표기 조작은 반드시 공격 뒤다 — PlayHitAnim이 표기를 모델 값으로 되돌리고 _afterHit이 그 뒤에 온다.
            DemoHpDisplay.ShowReducedHit(t_atk, t_def, UnlockDemoNumbers.SCALE_DMG_REDUCTION);
            return UniTask.CompletedTask;
        });
        if (_token.IsCancellationRequested) return;

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>수호자. 배치되며 막이 서고, 그 막이 한 대를 삼킨다.</summary>
// CardInstance.GrantShield를 부르지 않는다 — 모델을 안 건드려야 PlayShieldBreakEffect가 끝나며 표시를 정확히 꺼준다.
sealed class GuardianDemoScript : SynergyDemoScript
{
    public GuardianDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk  = _stage.Attacker;
        CardView t_def  = _stage.Defender;
        CardView t_ally = _stage.Ally;

        t_atk.SetShieldVisible(true);
        SynergyEmblemVfx.Play(t_atk, this.Synergy, SynergyEmblemTiming.Placed);

        if (t_ally != null)
        {
            t_ally.SetShieldVisible(true);
            SynergyEmblemVfx.Play(t_ally, this.Synergy, SynergyEmblemTiming.Placed);
        }

        await _stage.Hold(SynergyEmblemVfx.DurationOf(this.Synergy, SynergyEmblemTiming.Placed), _token);
        if (_token.IsCancellationRequested) return;

        SynergyEmblemVfx.Play(t_atk, this.Synergy, SynergyEmblemTiming.Triggered);
        if (t_ally != null) SynergyEmblemVfx.Play(t_ally, this.Synergy, SynergyEmblemTiming.Triggered);

        await _stage.Hold(SynergyEmblemVfx.DurationOf(this.Synergy, SynergyEmblemTiming.Triggered), _token);
        if (_token.IsCancellationRequested) return;

        await _stage.Swing(t_def, t_atk, null, _token, _afterHit: () =>
        {
            t_atk.PlayShieldBreakEffect();
            return UniTask.CompletedTask;
        });
        if (_token.IsCancellationRequested) return;

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>돌보미. 동료가 나오면 서로를 돌본다 — 엠블럼이 전원 위에 뜨고 회복 표기가 같은 순간 각자 자리에서 터진다.</summary>
// 회복과 추가 생명력을 둘 다 내는 것은 CaretakerSynergyEffect가 같은 값으로 Heal + GrantBonusHp를 함께 하기 때문이다.
sealed class CaretakerDemoScript : SynergyDemoScript
{
    public CaretakerDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk  = _stage.Attacker;
        CardView t_ally = _stage.Ally;

        int t_amount = UnlockDemoNumbers.CARETAKER_AMOUNT;

        // 만피에서 회복하면 숫자가 안 움직인다 — 표기를 먼저 낮춰 그 한 칸이 도로 차오르게 한다.
        DemoHpDisplay.WoundDisplay(t_atk, t_amount);
        DemoHpDisplay.WoundDisplay(t_ally, t_amount);

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        SynergyEmblemVfx.Play(t_atk, this.Synergy, SynergyEmblemTiming.Triggered);
        if (t_ally != null) SynergyEmblemVfx.Play(t_ally, this.Synergy, SynergyEmblemTiming.Triggered);

        // 데모엔 유예된 표기가 없으므로 _consumeDeferred는 기본값(false) — 그래야 "+N"이 실제로 뜬다.
        if (t_atk.BoundCard != null) t_atk.PlayHealEffect(t_amount);
        if (t_ally != null && t_ally.BoundCard != null) t_ally.PlayHealEffect(t_amount);

        await _stage.Hold(SynergyEmblemVfx.DurationOf(this.Synergy, SynergyEmblemTiming.Triggered), _token);
        if (_token.IsCancellationRequested) return;

        // 회복 굴림이 끝난 뒤에 얹는다 — 같은 프레임에 내면 어느 숫자가 움직였는지 안 읽힌다.
        DemoHpDisplay.ShowBonusHp(t_atk, t_amount);
        DemoHpDisplay.ShowBonusHp(t_ally, t_amount);

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>흐름. 동료가 늘수록 바람이 커진다 — 스택이 1에서 2로 오르는 것을 크기로 읽게 한다.</summary>
sealed class FlowDemoScript : SynergyDemoScript
{
    const float FLOW_STEP = 0.35f;

    public FlowDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        if (!TryVfx(out FlowSynergyVfxConfig t_cfg))
        {
            await PlayFallbackAsync(_stage, _token);
            return;
        }

        CardView t_atk  = _stage.Attacker;
        CardView t_def  = _stage.Defender;
        CardView t_ally = _stage.Ally;

        int t_stack = 1;

        if (t_ally != null)
        {
            SynergyVfx.PlayFlowWind(t_ally, t_cfg, t_stack);
            await _stage.Hold(FLOW_STEP, _token);
            if (_token.IsCancellationRequested) return;
            t_stack = 2;
        }

        SynergyVfx.PlayFlowWind(t_atk, t_cfg, t_stack);
        await _stage.Hold(FLOW_STEP, _token);
        if (_token.IsCancellationRequested) return;

        // 공격 개시와 함께 한 번 더 — 인게임의 공격 직전 발동과 같은 그림이다.
        SynergyVfx.PlayFlowWind(t_atk, t_cfg, t_stack);
        await _stage.Swing(t_atk, t_def, null, _token);
    }
}

/// <summary>낙인. 낙인 전원이 먼저 쏘고, 그 다음에 친다.</summary>
sealed class BrandDemoScript : SynergyDemoScript
{
    public BrandDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        if (!TryVfx(out BrandSynergyVfxConfig t_cfg))
        {
            await PlayFallbackAsync(_stage, _token);
            return;
        }

        CardView t_atk  = _stage.Attacker;
        CardView t_def  = _stage.Defender;
        CardView t_ally = _stage.Ally;

        SynergyEmblemVfx.Play(t_atk, this.Synergy, SynergyEmblemTiming.Triggered);
        if (t_ally != null) SynergyEmblemVfx.Play(t_ally, this.Synergy, SynergyEmblemTiming.Triggered);

        await _stage.Hold(SynergyEmblemVfx.DurationOf(this.Synergy, SynergyEmblemTiming.Triggered), _token);
        if (_token.IsCancellationRequested) return;

        var t_sources = new List<CardView> { t_atk };
        if (t_ally != null) t_sources.Add(t_ally);

        int t_damage  = UnlockDemoNumbers.BRAND_DAMAGE_PER_MEMBER;
        var t_damages = new int[t_sources.Count];
        for (int t_i = 0; t_i < t_damages.Length; t_i++) t_damages[t_i] = t_damage;

        // 표기 전용 볼리다 — 착탄이 PlayHitAnim + OverrideHpDisplay만 부르므로 실제 체력은 그대로다.
        await BrandVolleyVfx.PlayVolley(t_sources, t_def, t_damages,
                                        t_def.BoundCard.hp, t_def.BoundCard.bonusHp, t_cfg);
        if (_token.IsCancellationRequested) return;

        await _stage.Swing(t_atk, t_def, null, _token);
    }
}

/// <summary>포식자. 때린 만큼 되마신다.</summary>
// 곁자리는 세우지 않는다 — 흡혈은 개인 효과라 동료를 세워도 그 자리가 하는 일이 없다.
sealed class PredatorDemoScript : SynergyDemoScript
{
    public PredatorDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        if (!TryVfx(out PredatorSynergyVfxConfig t_cfg))
        {
            await PlayFallbackAsync(_stage, _token);
            return;
        }

        CardView t_atk = _stage.Attacker;
        CardView t_def = _stage.Defender;

        int t_hp    = t_atk.BoundCard.hp;
        int t_bonus = t_atk.BoundCard.bonusHp;

        // 공격력과 실제 적용량은 규칙에 위임한다(AttackDamage · ClampDamage). 비율 적용만 여기서 다시 쓴다.
        int t_percent = UnlockDemoNumbers.PREDATOR_LIFESTEAL_PERCENT;
        int t_dealt   = t_def.BoundCard != null
                      ? t_def.BoundCard.ClampDamage(t_atk.BoundCard.AttackDamage())
                      : t_atk.BoundCard.AttackDamage();

        // 만피에서 시작하면 회복이 안 읽히므로 표기를 먼저 낮춰 둔다.
        int t_drain = DemoHpDisplay.WoundDisplay(t_atk, Mathf.FloorToInt(t_dealt * (t_percent / 100f)));

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        await _stage.Swing(t_atk, t_def, null, _token);
        if (_token.IsCancellationRequested) return;

        // 공격 연출이 표기를 모델 값으로 되돌렸을 수 있다 — 흡수 직전에 낮춘 값을 다시 세운다.
        t_atk.OverrideHpDisplay(t_hp - t_drain, t_bonus);

        await PredatorVfx.PlayDrain(t_def, t_atk, t_cfg);
        if (_token.IsCancellationRequested) return;

        t_atk.OverrideHpDisplay(t_hp, t_bonus);
        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>표식. 때린 자리에 표식이 남고, 동료가 그 적을 문다.</summary>
// 곁자리를 가장 잘 쓰는 대본이다 — 동료의 두 번째 공격이 없으면 표식이 "그래서 뭐가 좋은가"를 못 말한다.
sealed class TraceDemoScript : SynergyDemoScript
{
    const float TRACE_MARK_HOLD = 0.35f;

    public TraceDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        if (!TryVfx(out TraceSynergyVfxConfig t_cfg))
        {
            await PlayFallbackAsync(_stage, _token);
            return;
        }

        // 타입이 맞아도 붙일 표식이 없으면 대본이 성립하지 않는다.
        if (t_cfg.mark.prefab == null)
        {
            WarnVfxType(nameof(TraceSynergyVfxConfig));
            await PlayFallbackAsync(_stage, _token);
            return;
        }

        CardView t_atk  = _stage.Attacker;
        CardView t_def  = _stage.Defender;
        CardView t_ally = _stage.Ally;

        await _stage.Swing(t_atk, t_def, null, _token, _afterHit: () =>
        {
            BattleVfx.Play(t_cfg.mark, t_def.SlotPosition, t_def.VfxSortingLayerId);
            return UniTask.CompletedTask;
        });
        if (_token.IsCancellationRequested) return;

        await _stage.Hold(TRACE_MARK_HOLD, _token);
        if (_token.IsCancellationRequested) return;

        if (t_ally != null)
        {
            await _stage.Swing(t_ally, t_def, null, _token);
            if (_token.IsCancellationRequested) return;
        }

        // 숫자는 내지 않는다 — 1단계 표식이 주는 것은 "표식을 붙인다"뿐이고 추가 생명력은 2단계 값이다.
        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>유산. 턴마다 쌓은 것이 쓰러질 때 동료에게 간다.</summary>
// Show/Fly의 개수 인자 오버로드는 게임 상태를 안 바꾸는 미리보기 진입점이라, 회복량으로 움직이는 것은 동료의 표기뿐이다.
sealed class LegacyDemoScript : SynergyDemoScript
{
    const float LEGACY_STEP = 0.55f;

    public LegacyDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        if (!TryVfx(out LegacySynergyVfxConfig t_cfg))
        {
            await PlayFallbackAsync(_stage, _token);
            return;
        }

        CardView t_atk  = _stage.Attacker;
        CardView t_ally = _stage.Ally;

        // 왕관 수 = 쌓인 스택이고 회복량도 그 스택이다(LegacySynergyEffect.OnLethal) — 대본은 두 턴을 보여준다.
        int t_amount = UnlockDemoNumbers.LEGACY_AMOUNT;
        int t_stack  = t_amount;

        LegacyCrownVfx.Show(t_atk.BoundCard, this.Synergy, t_stack);
        await _stage.Hold(LEGACY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        t_stack += t_amount;
        LegacyCrownVfx.Show(t_atk.BoundCard, this.Synergy, t_stack);
        await _stage.Hold(LEGACY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        // 파괴 국면(왕관 비행)은 연출 자체가 제거됐다 — LegacyCrownVfx 에 Fly 가 없다.
        // 남는 것은 회복 숫자뿐이라 보여줄 그림이 없어, 대본은 "쌓이는 것"까지만 보여주고 끝낸다.
        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}
