using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>키워드 대본의 공통 배역 배선. 앞자리는 언제나 그 카드, 나머지는 저작(KeywordDemoConfig)이 정한다.</summary>
abstract class KeywordDemoScript : IUnlockDemoScript
{
    protected readonly CardKeyword Keyword;

    readonly EDemoExtraSlot m_extra;

    protected KeywordDemoScript(CardKeyword _keyword, EDemoExtraSlot _extra)
    {
        this.Keyword = _keyword;
        this.m_extra = _extra;
    }

    public bool TryBuildCast(int _card, KeywordDemoConfig _config, out UnlockDemoCast _cast)
    {
        _cast = default;

        int t_opponent = 0;
        int t_side     = 0;
        _config?.Roles(this.Keyword, out t_opponent, out t_side);

        if (t_opponent <= 0)
        {
            Debug.LogWarning($"[UnlockDemoStage] {this.Keyword} 데모의 상대 카드가 저작되지 않았습니다(KeywordDemoConfig 확인).");
            return false;
        }

        int t_neighbor  = this.m_extra == EDemoExtraSlot.EnemyNeighbor ? t_side : 0;
        int t_companion = this.m_extra == EDemoExtraSlot.AllyCompanion ? t_side : 0;

        _cast = UnlockDemoCast.OfKeyword(t_opponent, t_neighbor, t_companion, this.Keyword);
        return true;
    }

    public abstract UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token);
}

/// <summary>기본 한 방. 대본이 따로 없는 키워드가 모두 여기로 오고, 무쌍은 윗줄 곁자리를 광역 대상으로 얹는다.</summary>
sealed class SwingDemoScript : KeywordDemoScript
{
    readonly bool m_splashesNeighbor;

    public SwingDemoScript(CardKeyword _keyword, bool _splashesNeighbor = false)
        : base(_keyword, _splashesNeighbor ? EDemoExtraSlot.EnemyNeighbor : EDemoExtraSlot.None)
    {
        this.m_splashesNeighbor = _splashesNeighbor;
    }

    public override UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk    = _stage.Attacker;
        CardView t_def    = _stage.Defender;
        CardView t_splash = this.m_splashesNeighbor ? _stage.Neighbor : null;

        return _stage.Swing(t_atk, t_def, t_splash, _token);
    }
}

/// <summary>처형. "한 번 더"가 본체라 마법진이 돌고 같은 공격이 이어진다.</summary>
sealed class ExecutionDemoScript : KeywordDemoScript
{
    public ExecutionDemoScript(CardKeyword _keyword) : base(_keyword, EDemoExtraSlot.None) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk = _stage.Attacker;
        CardView t_def = _stage.Defender;

        await _stage.Swing(t_atk, t_def, null, _token);
        if (_token.IsCancellationRequested) return;

        ExecutionVfx.Play(t_atk);
        await _stage.Swing(t_atk, t_def, null, _token);
    }
}

/// <summary>교활. 때린 뒤 사라지는 것이 본체라 뒷면으로 물러났다가 같은 카드가 다시 들어온다.</summary>
// 덱에서 다른 아군이 나오는 그림은 카드 한 장을 더 세워야 해서 이 무대에선 접었다.
sealed class CunningDemoScript : KeywordDemoScript
{
    public CunningDemoScript(CardKeyword _keyword) : base(_keyword, EDemoExtraSlot.None) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk = _stage.Attacker;
        CardView t_def = _stage.Defender;

        await _stage.Swing(t_atk, t_def, null, _token);
        if (_token.IsCancellationRequested) return;

        await CunningVfx.PlayExit(t_atk);
        if (_token.IsCancellationRequested) return;

        await CunningVfx.PlayEnter(t_atk);
    }
}

/// <summary>원거리·표식. 반격이 안 오는 것이 본체라, 맞은 쪽이 되받으려다 마는 시늉으로 "안 왔다"를 드러낸다.</summary>
sealed class NoRiposteDemoScript : KeywordDemoScript
{
    public NoRiposteDemoScript(CardKeyword _keyword) : base(_keyword, EDemoExtraSlot.None) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_atk = _stage.Attacker;
        CardView t_def = _stage.Defender;

        await _stage.Swing(t_atk, t_def, null, _token);
        if (_token.IsCancellationRequested) return;

        t_def.PlayRejectShake();
    }
}

/// <summary>도발. 적이 곁의 아군을 노리다 이 카드에 끌려와 결국 이 카드를 친다.</summary>
// 다른 대본과 달리 앞자리가 맞는 쪽이다 — 공격자로 두면 정작 배운 키워드가 남의 카드에서 빛난다.
sealed class TauntDemoScript : KeywordDemoScript
{
    const float TAUNT_AIM_HOLD      = 0.35f;
    const float TAUNT_REDIRECT_HOLD = 0.6f;

    public TauntDemoScript(CardKeyword _keyword) : base(_keyword, EDemoExtraSlot.AllyCompanion) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_taunter = _stage.Attacker;
        CardView t_enemy   = _stage.Defender;

        // 지켜줄 아군이 없으면 노리는 박자를 통째로 건너뛴다 — 대신 맞아줄 상대가 없으면 도발이 성립하지 않는다.
        CardView t_wanted = _stage.Ally;

        if (t_wanted != null)
        {
            t_wanted.SetHighlight(true);
            await _stage.Hold(TAUNT_AIM_HOLD, _token);
            if (_token.IsCancellationRequested) { t_wanted.SetHighlight(false); return; }

            t_wanted.PlayRejectShake();
            BattleVfx.PlayAttached(BattleVfxId.TauntBlocked, t_enemy.transform,
                                   _flip: false, t_enemy.VfxSortingLayerId);
        }

        // 키워드 글로우를 따로 부르지 않는다 — PlayAttentionPulse가 도발 카드면 스스로 띄워, 두 장이 겹치면 두 배로 밝아진다.
        BattleVfx.PlayAttached(BattleVfxId.TauntGuard, t_taunter.transform,
                               _flip: false, t_taunter.VfxSortingLayerId);
        t_taunter.PlayAttentionPulse();

        await _stage.Hold(TAUNT_REDIRECT_HOLD, _token);
        if (t_wanted != null) t_wanted.SetHighlight(false);
        if (_token.IsCancellationRequested) return;

        // 끌려온 것으로 끝내면 "대신 맞는다"의 뒷말이 빠진다.
        await _stage.Swing(t_enemy, t_taunter, null, _token);
    }
}

/// <summary>힐러. 때리는 것이 아니라 아랫줄 아군 한 장을 살린다.</summary>
sealed class HealerDemoScript : KeywordDemoScript
{
    public HealerDemoScript(CardKeyword _keyword) : base(_keyword, EDemoExtraSlot.AllyCompanion) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_healer = _stage.Attacker;
        CardView t_target = _stage.Ally;
        if (t_target == null || t_target.BoundCard == null) return;

        CardInstance t_card = t_target.BoundCard;
        int          t_heal = DemoHpDisplay.WoundDisplay(t_target, UnlockDemoNumbers.HEALER_SHOW_HEAL);

        await _stage.Hold(UnlockDemoNumbers.SYNERGY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        // 유예를 먼저 걸어야 숫자가 오른다 — HealVfx의 도착 처리는 미리 예약된 몫만큼만 표기를 올린다.
        t_target.DeferHpDisplay(t_heal);

        HealVfx.PlayHealBurst(t_healer, new List<(CardView view, CardInstance card, int amount)>
        {
            (t_target, t_card, t_heal)
        });

        // 이 연출은 스스로 끝을 알리지 않아 길이를 HealVfx에게 묻는다.
        await _stage.Hold(HealVfx.BurstDuration(1), _token);
    }
}
