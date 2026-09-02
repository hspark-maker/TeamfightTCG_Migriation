using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 시너지 하나가 일하는 모습을 보여주는 대본들. 얻는 문은 UnlockDemoScriptTable 하나라
// 바깥에 이름을 내지 않는다.

/// <summary>시너지 대본의 공통 배역 배선. 아군 둘이 아랫줄에 서고 적 하나가 맞은편에 선다 —
/// 시너지는 같은 편이 모여야 성립하므로 곁자리 진영에 분기가 없다.</summary>
abstract class SynergyDemoScript : IUnlockDemoScript
{
    /// <summary>이번 무대가 안내하는 시너지. 연출 에셋과 엠블럼을 여기서 꺼낸다.</summary>
    protected readonly SynergyData Synergy;

    protected SynergyDemoScript(SynergyData _synergy)
    {
        this.Synergy = _synergy;
    }

    public bool TryBuildCast(int _card, KeywordDemoConfig _config, out UnlockDemoCast _cast)
    {
        _cast = default;

        if (this.Synergy == null) return false;

        // 연출 에셋이 없으면 그 시너지는 규칙만 있고 보여줄 것이 없다 — 무대를 세우지 않는다.
        if (this.Synergy.vfx == null)
        {
            Debug.LogWarning($"[UnlockDemoStage] {this.Synergy.SynergyId}: 연출 에셋(vfx) 미배선 — 무대 없이 글자만 남깁니다.");
            return false;
        }

        int t_opponent = 0;
        int t_unused   = 0;

        // 시너지에는 배역 저작 축이 없다 — 맞은편은 키워드 표의 기본값을 그대로 쓰고 동료는 코드가 고른다.
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

    /// <summary>연출 에셋이 기대한 타입인가. 어긋나면 경고를 내고 false —
    /// 부른 대본은 <c>PlayFallbackAsync</c>로 떨어진다.</summary>
    protected bool TryVfx<T>(out T _cfg) where T : SynergyVfxConfig
    {
        _cfg = this.Synergy.vfx as T;
        if (_cfg != null) return true;

        WarnVfxType(typeof(T).Name);
        return false;
    }

    /// <summary>연출 에셋 타입이 어긋나면 그 대본은 통째로 무음이 된다 —
    /// "안 뜬다"와 "깨졌다"를 로그로 가른다.</summary>
    protected void WarnVfxType(string _expected)
        => Debug.LogWarning($"[UnlockDemoStage] {this.Synergy.SynergyId}: vfx가 {_expected}가 아니라 기본 대본으로 떨어집니다.");

    /// <summary>대본이 아직 없는 시너지(새로 늘어난 것)와 연출 에셋 타입이 어긋난 경우의 폴백.</summary>
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

    /// <summary>같은 시너지를 가진 다른 카드 중 가장 작은 ID. 없으면 0(곁자리를 비운다).
    /// 열거 순서가 아니라 최소값으로 고르는 이유는 <c>CardCatalog.AllIds</c>가 Dictionary 열거 결과라
    /// 런타임이 순서를 보장하지 않기 때문이다 — 같은 시너지에는 언제나 같은 동료가 서야 한다.</summary>
    static int FindSynergyCompanion(int _card, SynergyData _synergy, int _opponent)
    {
        // 미준비 상태에서 RequireSynergies를 부르면 throw한다 — 안내창이 예외로 죽는 것보다 곁자리를 비우는 편이 낫다.
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

    /// <summary>배지를 켜기 위한 표시 전용 시너지 상태. 무대는 규칙을 돌리지 않으므로(SynergyResolver를
    /// 부르지 않는다) 여기서 담는 것은 "이 시너지가 켜져 있다"는 사실 하나뿐이고, 배지가 읽는 것도 그것뿐이다.
    /// 이 상태를 넘기지 않으면 <c>CardVisualRules.IsSynergyActive</c>가 전부 false를 돌려줘
    /// 카드에 시너지 배지가 **한 장도** 뜨지 않는다.</summary>
    static SynergyState MakeShowState(SynergyData _synergy)
    {
        if (_synergy == null) return null;

        // 티어는 첫 단계로 둔다. 배지는 티어를 그리지 않고, 이 값은 카드가 시너지를 둘 이상 가질 때의
        // 정렬(CardVisualRules.GetBadgeRequiredCount)에만 쓰인다 — 비어 있어도 그쪽이 null을 견딘다.
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

/// <summary>비늘. 맞아도 덜 아프다.</summary>
// 피해 숫자가 아예 안 뜨면 "덜 아프다"가 아니라 "무적"으로 읽히므로, 배틀과 같이 **감쇄된 만큼만**
// 체력이 깎이는 그림을 낸다. 접촉 순간의 엠블럼이 그 원인을 밝힌다.
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

            // 표기 조작은 반드시 공격 뒤다 — PlayHitAnim이 표기를 모델 값으로 되돌린다(_afterHit이 그 뒤에 온다).
            DemoHpDisplay.ShowReducedHit(t_atk, t_def, UnlockDemoNumbers.SCALE_DMG_REDUCTION);
            return UniTask.CompletedTask;
        });
        if (_token.IsCancellationRequested) return;

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>수호자. 배치되며 막이 서고, 그 막이 한 대를 삼킨다.</summary>
// CardInstance.GrantShield를 부르지 않는다 — 모델을 안 건드려야 PlayShieldBreakEffect가
// 끝나면서 표시를 정확히 꺼준다(그 분기가 boundCard.hasShield를 읽는다).
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

/// <summary>돌보미. 동료가 나오면 서로를 돌본다.</summary>
// 게임 경로와 같은 그림 — 엠블럼이 돌보미 전원 위에 뜨고 회복 표기가 **같은 순간** 각자 자리에서
// 터진다(힐러 투사체는 쓰지 않는다). 회복과 추가 생명력을 둘 다 내는 것은
// CaretakerSynergyEffect가 같은 값으로 Heal + GrantBonusHp를 함께 하기 때문이다.
sealed class CaretakerDemoScript : SynergyDemoScript
{
    public CaretakerDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk  = _stage.Attacker;
        CardView t_ally = _stage.Ally;

        int t_amount = UnlockDemoNumbers.CARETAKER_AMOUNT;

        // 만피에서 회복하면 숫자가 움직이지 않는다 — 표기를 먼저 낮춰 그 한 칸이 도로 차오르게 한다
        // (HealerDemoScript와 같은 이유).
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

        // 추가 생명력은 회복 굴림이 끝난 뒤에 얹는다 — 같은 프레임에 내면 어느 숫자가 움직였는지 안 읽힌다.
        DemoHpDisplay.ShowBonusHp(t_atk, t_amount);
        DemoHpDisplay.ShowBonusHp(t_ally, t_amount);

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>흐름. 동료가 늘수록 바람이 커진다 — 스택이 1에서 2로 오르는 것을 크기로 읽게 한다.</summary>
sealed class FlowDemoScript : SynergyDemoScript
{
    const float FLOW_STEP = 0.35f;   // 바람이 한 단 커지는 간격

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

        // 표기 전용 볼리다(착탄이 PlayHitAnim + OverrideHpDisplay만 부른다) — 실제 체력은 그대로다.
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

        // 공격력과 실제 적용량은 규칙에 위임한다(AttackDamage · ClampDamage). 비율 적용만 여기서 쓴다 —
        // 그 짝은 UnlockDemoNumbers의 주석에 적어 두었다.
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
    const float TRACE_MARK_HOLD = 0.35f;   // 표식이 붙고 동료가 달려들기까지

    public TraceDemoScript(SynergyData _synergy) : base(_synergy) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        if (!TryVfx(out TraceSynergyVfxConfig t_cfg))
        {
            await PlayFallbackAsync(_stage, _token);
            return;
        }

        // 타입이 맞아도 붙일 표식이 없으면 대본이 성립하지 않는다 — 같은 자리로 떨어진다.
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

        // 숫자는 내지 않는다 — 1단계 표식이 주는 것은 "표식을 붙인다"뿐이고, 추가 생명력은 2단계 값이다.
        await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
    }
}

/// <summary>유산. 턴마다 쌓은 것이 쓰러질 때 동료에게 간다.</summary>
// Show/Fly의 개수 인자 오버로드는 게임 상태를 안 바꾸는 미리보기 진입점이다 — 회복량은 넘기되
// 그것으로 움직이는 것은 동료의 **표기**뿐이다(모델은 이 무대에서 변하지 않는다).
sealed class LegacyDemoScript : SynergyDemoScript
{
    const float LEGACY_STEP = 0.55f;   // 왕관이 한 개 늘어나는 간격

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

        // 왕관 수 = 쌓인 스택이고, 회복량도 그 스택이다(LegacySynergyEffect.OnLethal) — 대본은 두 턴을 보여준다.
        int t_amount = UnlockDemoNumbers.LEGACY_AMOUNT;
        int t_stack  = t_amount;

        LegacyCrownVfx.Show(t_atk.BoundCard, this.Synergy, t_stack);
        await _stage.Hold(LEGACY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        t_stack += t_amount;
        LegacyCrownVfx.Show(t_atk.BoundCard, this.Synergy, t_stack);
        await _stage.Hold(LEGACY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        if (t_ally == null || t_ally.BoundCard == null)
        {
            // 받을 동료가 없으면 쌓이는 것까지만 보여준다 — 갈 곳 없는 비행은 "누구에게"가 빠진 그림이다.
            await _stage.Hold(UnlockDemoNumbers.SYNERGY_HOLD, _token);
            return;
        }

        // 만피면 왕관이 닿아도 숫자가 안 움직인다 — 받을 자리를 먼저 비워 둔다.
        int t_heal = DemoHpDisplay.WoundDisplay(t_ally, t_stack);

        // 유예를 먼저 걸어야 숫자가 오른다: 도착 처리(LegacyCrownVfx.FlyOne)가 PlayHealEffect(_consumeDeferred: true)를
        // 부르고, 그 분기는 **미리 예약된 몫만큼만** 표기를 올린다(HealerDemoScript와 같은 규약).
        t_ally.DeferHpDisplay(t_heal);

        LegacyCrownVfx.Fly(t_atk.BoundCard, new[] { t_ally.BoundCard }, t_heal, this.Synergy, t_stack);
        await _stage.Hold(t_cfg.flyDuration + t_cfg.arriveHold, _token);

        // 유예분은 반드시 여기서 푼다 — 왕관 프리팹이 미배선이면 Fly가 아무것도 안 띄우고 돌아가
        // **도착이 없으므로**, 낮춰 둔 동료의 체력 표기가 그 판 내내 굳는다.
        // (바퀴를 넘기는 누수는 아니다: Render가 카드 교체 프레임에 유예를 스스로 지운다.)
        // 끊겨서 도착이 없었던 경우까지 덮어야 하므로 취소 검사 앞이다.
        DemoHpDisplay.SnapHpDisplay(t_ally);
    }
}
