using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 키워드 하나가 발동하는 모습을 보여주는 대본들. 얻는 문은 UnlockDemoScriptTable 하나라
// 바깥에 이름을 내지 않는다.

/// <summary>키워드 대본의 공통 배역 배선. 앞자리는 언제나 그 카드, 나머지는 저작(KeywordDemoConfig)이 정한다.
/// 저작이 주는 곁자리 카드는 한 장이고, 그 카드를 **어느 줄에 세울지**를 대본마다 EDemoExtraSlot으로 밝힌다.</summary>
abstract class KeywordDemoScript : IUnlockDemoScript
{
    /// <summary>이번 무대가 안내하는 키워드. 배역 저작을 찾는 키이자 카드 배지에 띄울 축이다.</summary>
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

/// <summary>기본 한 방. 대본이 따로 없는 키워드가 모두 여기로 오고, 무쌍은 같은 안무에
/// 윗줄 곁자리를 광역 대상으로 얹는다.</summary>
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

/// <summary>처형. "한 번 더"가 본체다 — 마법진이 돌고 같은 공격이 이어져야 그 뜻이 나온다.</summary>
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

/// <summary>교활. 때린 뒤 사라지는 것이 본체라 뒷면으로 물러났다가 같은 카드가 다시 들어온다
/// (덱에서 다른 아군이 나오는 그림은 카드 한 장을 더 세워야 해서 이 무대에선 접었다).</summary>
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

/// <summary>원거리·표식. **반격이 안 오는 것**이 본체라, 반격 역재생을 붙이지 않는 것 자체가 대본이다.
/// 그 대신 맞은 쪽이 되받으려다 마는 시늉을 한 박 넣어 "안 왔다"를 눈에 띄게 한다.</summary>
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

/// <summary>도발. **적이** 곁의 아군을 노리다 이 카드에 끌려와, 결국 이 카드를 친다.</summary>
// 다른 대본과 달리 앞자리가 맞는 쪽이다 — 도발은 내가 하는 일이 아니라 남이 나를 치게 만드는 일이라,
// 앞자리를 공격자로 두면 정작 배운 키워드가 남의 카드에서 빛난다.
//
// 연출 짝은 인게임(CardInputController)과 같다: 막힌 공격자 위에 TauntBlocked, 도발 보유자에게 TauntGuard.
// 한쪽만 있으면 "왜 막혔는지"나 "누가 막는지" 중 하나가 빠진다.
sealed class TauntDemoScript : KeywordDemoScript
{
    // 이 대본에만 있는 두 박자. 저작 축으로 뺄 값이 아니다 — 한 대본의 내부 호흡이라,
    // 인스펙터에 내면 다른 키워드에도 있는 값처럼 읽힌다.
    const float TAUNT_AIM_HOLD      = 0.35f;   // 적이 아군을 노리는 동안
    const float TAUNT_REDIRECT_HOLD = 0.6f;    // 끌려오고 나서 치기까지

    public TauntDemoScript(CardKeyword _keyword) : base(_keyword, EDemoExtraSlot.AllyCompanion) { }

    public override async UniTask PlayAsync(IUnlockDemoStage _stage, CancellationToken _token)
    {
        CardView t_taunter = _stage.Attacker;
        CardView t_enemy   = _stage.Defender;

        // 지켜줄 아군. 없으면 노리는 박자를 통째로 건너뛴다 — 대신 맞아줄 상대가 없으면 도발이 성립하지 않는다.
        // 아랫줄에서 고른다: 이 화면에서 편을 가르는 단서는 줄뿐이라 윗줄에 세우면 적을 지켜주는 그림이 된다.
        CardView t_wanted = _stage.Ally;

        if (t_wanted != null)
        {
            // 1) 적이 아군을 노린다.
            t_wanted.SetHighlight(true);
            await _stage.Hold(TAUNT_AIM_HOLD, _token);
            if (_token.IsCancellationRequested) { t_wanted.SetHighlight(false); return; }

            // 2) 못 친다 — 노리던 쪽이 튕기고, 치려던 적 위에 차단 표식이 선다.
            t_wanted.PlayRejectShake();
            BattleVfx.PlayAttached(BattleVfxId.TauntBlocked, t_enemy.transform,
                                   _flip: false, t_enemy.VfxSortingLayerId);
        }

        // 3) "이쪽을 쳐라" — 도발 카드가 대답한다.
        // 키워드 글로우를 따로 부르지 않는다: PlayAttentionPulse가 도발 카드면 스스로 띄운다(인게임 거절 경로와 같다).
        // 여기서 또 부르면 같은 프레임에 글로우가 두 장 스폰돼 혼자 두 배로 밝아진다.
        BattleVfx.PlayAttached(BattleVfxId.TauntGuard, t_taunter.transform,
                               _flip: false, t_taunter.VfxSortingLayerId);
        t_taunter.PlayAttentionPulse();

        await _stage.Hold(TAUNT_REDIRECT_HOLD, _token);
        if (t_wanted != null) t_wanted.SetHighlight(false);
        if (_token.IsCancellationRequested) return;

        // 4) 그래서 이 카드가 맞는다. 끌려온 것으로 끝내면 "대신 맞는다"의 뒷말이 빠진다.
        await _stage.Swing(t_enemy, t_taunter, null, _token);
    }
}

/// <summary>힐러. 때리는 것이 아니라 아군을 살린다.</summary>
// 대상은 **아랫줄 곁자리 한 장**이다 — 맞은편(윗줄)은 적 자리라 그쪽으로 투사체를 보내면 적을 살리는 그림이 된다.
// 만피에서 회복하면 숫자가 움직이지 않으므로, 표기를 먼저 낮춰 두고 그 한 칸이 도로 차오르는 것을 보여준다.
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

        // 유예를 먼저 걸어야 숫자가 오른다: HealVfx의 도착 처리는 PlayHealEffect(_consumeDeferred: true)를 부르고,
        // 그 분기는 **미리 예약된 몫만큼만** 표기를 올린다 — 예약이 0이면 투사체만 날고 숫자는 그대로 멈춘다.
        // CaretakerDemoScript가 CardView 쪽을 기본값(false)으로 직접 부르는 것과 갈리는 자리다.
        t_target.DeferHpDisplay(t_heal);

        t_healer.PlayKeywordGlow(CardKeyword.Healer).Forget();
        HealVfx.PlayHealBurst(t_healer, new List<(CardView view, CardInstance card, int amount)>
        {
            (t_target, t_card, t_heal)
        });

        // 이 연출은 스스로 끝을 알리지 않는다 — 길이는 HealVfx가 아는 값을 그대로 받아 쓴다.
        await _stage.Hold(HealVfx.BurstDuration(1), _token);
    }
}
