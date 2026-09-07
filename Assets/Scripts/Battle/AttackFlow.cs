using Cysharp.Threading.Tasks;
using DG.Tweening;
using TeamfightTCG.BattleCore;
using UnityEngine;

/// <summary>
/// 공격 해결 흐름의 공유 조각들. PlayerTurn / EnemyTurn / Multiplayer* 4개가
/// 복붙하던 부분을 추출. 각 턴 클래스는 방향(내/상대 field)과 네트워크 훅만 다름.
/// </summary>
public static class AttackFlow
{
    /// <summary>공격 1회의 입력. 네 드라이버가 다른 건 **방향(내/상대)과 이 두 옵션뿐**이다.</summary>
    public readonly struct AttackRequest
    {
        public readonly CardInstance Attacker;
        public readonly CardInstance Defender;
        public readonly BattleField AttackerField;
        public readonly BattleField DefenderField;
        public readonly BattleFieldView AttackerFieldView;
        public readonly BattleFieldView DefenderFieldView;

        /// <summary>교활 스왑을 와이어 값으로 강제할지. null 이면 규칙이 로컬로 재계산한다 —
        /// 솔로는 미러가 없어 null, 멀티는 송신·수신이 같은 값을 봐야 해서 와이어 값을 넣는다.</summary>
        public readonly bool? ForceCunningSwap;

        /// <summary>처형 재공격인가. **명령 로그에 그대로 실린다** — 서버 재생기는 canAttackAgain 뒤에
        /// derived 명령이 오기를 기대하고, 빠지면 매치를 missing_derived_attack 으로 무효 처리한다.</summary>
        public readonly bool DerivedCommand;

        public AttackRequest(CardInstance _attacker, CardInstance _defender,
            BattleField _attackerField, BattleField _defenderField,
            BattleFieldView _attackerFieldView, BattleFieldView _defenderFieldView,
            bool? _forceCunningSwap, bool _derivedCommand)
        {
            Attacker = _attacker;
            Defender = _defender;
            AttackerField = _attackerField;
            DefenderField = _defenderField;
            AttackerFieldView = _attackerFieldView;
            DefenderFieldView = _defenderFieldView;
            ForceCunningSwap = _forceCunningSwap;
            DerivedCommand = _derivedCommand;
        }
    }

    /// <summary>공격 1회의 결과. 뷰까지 돌려주는 이유는 호출부가 보충 단계에서 죽은 슬롯을 숨기는 데 쓰기 때문이다.</summary>
    public readonly struct AttackOutcome
    {
        public readonly AttackResult Result;
        public readonly CardView AttackerView;
        public readonly CardView DefenderView;
        public readonly CardInstance Splash;
        public readonly CardView SplashView;

        public AttackOutcome(AttackResult _result, CardView _attackerView, CardView _defenderView,
            CardInstance _splash, CardView _splashView)
        {
            Result = _result;
            AttackerView = _attackerView;
            DefenderView = _defenderView;
            Splash = _splash;
            SplashView = _splashView;
        }
    }

    /// <summary>
    /// 공격 1회의 고정 순서. 네 드라이버(<c>PlayerTurn</c> · <c>EnemyTurn</c> ·
    /// <c>MultiplayerPlayerTurn</c> · <c>MultiplayerOpponentTurn</c>)가 각자 조립하던 것을 한 자리로 모았다.
    ///
    /// <para>조립이 네 벌이던 시절 실제로 난 사고: 솔로 두 곳만 <c>_derivedCommand</c> 를 안 넘겨
    /// 처형 재공격이 플래그 없이 기록됐고, 서버가 그 매치를 전부 무효 처리했다. 선택 인자가
    /// 아니라 <see cref="AttackRequest"/> 필드가 된 지금은 빠뜨리면 컴파일이 막는다.</para>
    ///
    /// <para><b>보드 보충은 여기 없다.</b> 솔로는 양쪽을 로컬로 채우고 멀티는 자기 필드만 채운 뒤
    /// 상대 보충을 RPC 로 받는다 — 그 차이가 결정론 계약이라 호출부가 계속 소유한다.</para>
    /// </summary>
    public static async UniTask<AttackOutcome> RunOneAttack(AttackRequest _request)
    {
        CardView t_attackerView = _request.AttackerFieldView.GetSlotView(_request.Attacker.slotIndex);
        CardView t_defenderView = _request.DefenderFieldView.GetSlotView(_request.Defender.slotIndex);

        var (t_splash, t_splashView) = PreSelectSplash(
            _request.Attacker, _request.Defender, _request.DefenderField, _request.DefenderFieldView);

        await RunBeforeAttack(_request.Attacker, _request.Defender,
                              _request.AttackerField, _request.DefenderField, t_splash);   // 낙인 선피해(Execute 전 원자)

        AttackResult t_result;
        using (BattleEventStream.CaptureScope t_events = BattleEventStream.BeginCapture())
        {
            t_result = AttackProcessor.Execute(
                _request.Attacker, _request.Defender,
                _request.AttackerField.State, _request.DefenderField.State,
                t_splash, _request.ForceCunningSwap, _request.DerivedCommand);
            t_result.events = t_events.ToArray();
        }

        await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
            t_result.events,
            () => RunAfterAttackPhase(t_attackerView, _request.Attacker, _request.Defender,
                                      _request.AttackerField, _request.DefenderField, t_result));

        // 교활 퇴장은 보충 **전**에 — 슬롯 뷰가 아직 물러나는 카드를 그리고 있는 동안만 가능하다.
        await PlayCunningSwap(_request.AttackerFieldView, t_attackerView, t_result);

        return new AttackOutcome(t_result, t_attackerView, t_defenderView, t_splash, t_splashView);
    }

    /// <summary>무쌍(Peerless) 광역 대상 사전 선정. 비무쌍이면 (null, null).</summary>
    public static (CardInstance splash, CardView splashView) PreSelectSplash(
        CardInstance _attacker, CardInstance _defender,
        BattleField _defenderField, BattleFieldView _defenderFieldView)
    {
        if (!_attacker.HasKeyword(CardKeyword.Peerless)) return (null, null);
        CardInstance t_splash = AttackProcessor.PreSelectSplash(_defender.slotIndex, _defenderField.State);
        CardView t_view = t_splash != null ? _defenderFieldView.GetSlotView(t_splash.slotIndex) : null;
        return (t_splash, t_view);
    }

    /// <summary>[BeforeAttack] 공격 개시 직전 공격자 트리거 발동(패시브 → 시너지 낙인 선피해 등).
    /// AttackSequence.Play 직전(PreSelectSplash 이후)에 호출 → Execute 전 원자 완료. RNG 미소비(splash 스트림 미교란).</summary>
    public static async UniTask RunBeforeAttack(
        CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField,
        CardInstance _preSelectedSplash = null)
    {
        BattleFinisher.ArmApproach(null, null, null, null);   // 이전 공격의 미소비 래치 제거
        if (!_attacker.IsAlive) return;
        var t_ctx = new BeforeAttackCtx(_attacker, _defender, _attackerField.State, _defenderField.State);
        await SynergyTriggers.BeforeAttack(t_ctx);
        // 낙인 선피해가 반영된 **최신 보드**에서 전투 종료를 예측한다 — 선피해로 이미 hp 0이 된 카드가
        // 슬롯에 남아 있는 상태라, 여기보다 앞에서 계산하면 그 피해를 빼먹는다.
        BattleFinisher.ArmApproach(_attacker, _defender, _attackerField, _defenderField, _preSelectedSplash);
    }

    /// <summary>[AfterAttack] 공격 직후 공격자 패시브 + 시너지 발동. 처치 판정은 ctx.defenderKilled(구 OnKill 게이트와 동일 소스).</summary>
    public static async UniTask RunAfterAttack(
        CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField, AttackResult _result)
    {
        if (!_attacker.IsAlive) return;
        var t_ctx = new AfterAttackCtx(_attacker, _defender, _attackerField.State, _defenderField.State,
                                       _result.damageDealt, _result.defenderKilled);
        // 시너지 공격-후 트리거(포식자 회복 등). 패시브 발화 직후, 생존 가드 이후.
        await SynergyTriggers.AfterAttack(t_ctx);
    }

    /// <summary>[4] 공격 후 단계 묶음 — 규칙 트리거(RunAfterAttack)를 돌리고
    /// 마지막에 ⑥ 처치 연출을 **예약만** 하고 끝난다(EnqueueKillFlourish).
    ///
    /// **AttackSequence 의 _afterHit 로 넘겨** 사망 연출보다 앞에서 돌린다. 6단계 고정 순서
    /// (공격 전 → 공격 중 → 피격 → 공격 후 → 사망 → 처치, 표는 AttackSequence.PlayCore)에서 이 묶음이 ④다.
    ///
    /// 키워드 글로우(발동 키워드·표식)는 폐기됐다 — 이 자리에 있던 결과 연출 PlayResultFlourish도 함께 사라졌다.
    /// 남은 결과 연출은 처형 하나이고 그건 ⑥이다.</summary>
    public static async UniTask RunAfterAttackPhase(
        CardView _attackerView, CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField, AttackResult _result)
    {
        await RunAfterAttack(_attacker, _defender, _attackerField, _defenderField, _result);
        EnqueueKillFlourish(_attacker, _result);
    }

    /// <summary>[6] 처치 연출 예약 — 처형 발동 그림. **여기서 재생하지 않는다.**
    ///
    /// 처형은 "죽였다"가 조건이라 ④가 아니라 ⑥이다. 쓰러지는 그림(⑤)보다 앞서 뜨면
    /// 아직 서 있는 적 위에서 처형이 터지고, 같은 박에 뜨면 둘이 뭉갠다.
    /// 큐에 담아 AttackSequence가 사망 연출 뒤에 푼다(BattlePresentationQueue.DrainKillsAsync).
    ///
    /// 판정은 AttackProcessor가 세운 attackerKeywords 그대로 — 여기서 처치/키워드를 다시 보지 않는다.</summary>
    static void EnqueueKillFlourish(CardInstance _attacker, AttackResult _result)
    {
        if (!_result.attackerKeywords.HasFlag(CardKeyword.Execution)) return;

        CardInstance t_attacker = _attacker;
        // 뷰는 그때 다시 찾는다 — 예약과 재생 사이에 슬롯이 갈릴 수 있다.
        BattlePresentationQueue.RunOnKill(() => ExecutionVfx.Play(CardView.GetView(t_attacker)));
    }

    /// <summary>교활 스왑 교대 연출: 물러나는 카드 퇴장 → 슬롯 재렌더 → 들어온 카드가 덱에서 등장.
    ///
    /// **보드 보충(FillEmptySlots/Refresh) 직전에** 부를 것 — 그 전에는 슬롯 뷰가 아직 물러나는 카드를
    /// 그리고 있고, 그 창을 놓치면 엉뚱한 카드가 나가는 그림이 된다.
    /// 스왑된 슬롯은 비어 있지 않아 보충 연출(PlayFillAnim) 대상이 아니므로 등장도 여기서 책임진다.
    /// 스왑 여부는 AttackProcessor가 세운 결과 그대로 읽는다.</summary>
    public static async UniTask PlayCunningSwap(
        BattleFieldView _attackerFieldView, CardView _attackerView, AttackResult _result)
    {
        if (!_result.attackerSwapped) return;

        await CunningVfx.PlayExit(_attackerView);

        // 들어온 카드를 슬롯에 그린 뒤 등장 — 재렌더 전에 등장을 돌리면 나간 카드가 되돌아온다.
        _attackerFieldView?.Refresh();
        await CunningVfx.PlayEnter(_attackerView);
    }

}
