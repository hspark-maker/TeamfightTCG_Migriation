using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>승부를 가른 한 방을 <b>그 타격 순간에</b> 강조하는 연출.
///
/// <para>승패 확정(BattleLoop.Run의 턴 끝 종료 판정)은 그 턴의 공격·사망 연출과 충원까지 다 끝난 뒤에 일어난다.
/// 그래서 결과 시점에는 강조할 공격자도 피격자도 남아 있지 않다 — 죽은 카드 View는 이미 페이드됐고,
/// 슬롯은 새 카드에 재바인딩됐을 수 있다. 결정타를 보여주려면 <b>피해가 적용된 그 자리</b>에서 잡아야 한다.</para>
///
/// <para>그래서 판정과 표시를 나눈다:
/// <list type="bullet">
/// <item><see cref="Arm"/> — AttackProcessor가 피해·사망 정리를 끝낸 직후, "이 공격으로 어느 편이 전멸했는가"만 기록.</item>
/// <item><see cref="TryPlay"/> — AttackSequence가 사망 연출을 시작하기 <b>전에</b> 호출. View가 아직 살아 있는 마지막 지점.</item>
/// </list>
/// 진짜 승패·보상은 여전히 TurnRunner가 확정한다. 이 클래스는 규칙을 읽기만 하고 아무것도 바꾸지 않는다 —
/// RNG도 소비하지 않으므로 멀티에서 RPC가 필요 없다(양쪽이 같은 피해 결과에서 각자 같은 판정을 낸다).</para>
///
/// <para><b>CardView·CardInstance 참조를 보관하지 않는다.</b> View는 슬롯 단위로 재사용되므로,
/// 한 프레임이라도 들고 있으면 충원된 엉뚱한 카드를 가리키게 된다. 필요한 건 그 자리에서 값으로 다 뽑는다.</para></summary>
public static class BattleFinisher
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Disarm();

    const int NoOwner = -1;

    static int s_loserOwner = NoOwner;   // 이번 공격으로 전멸한 편의 ownerIndex(NoOwner = 없음)
    static bool s_approachArmed;
    static bool s_approachActive;

    /// <summary>이 공격이 <b>실제로 전투를 끝내는</b> 한 방이면 접근 연출을 준비한다.
    ///
    /// <para>판정은 <see cref="BattleOverForecast"/> 단독 — 여기서 보드를 세지 않는다.
    /// "필드에 한 장 남았나"는 예측을 돌릴지 말지를 정하는 <b>사전 게이트</b>일 뿐이고
    /// (<see cref="BattleOverForecast.CouldEnd"/>), 연출을 켜는 기준은 전멸 예측 결과다 —
    /// 마지막 한 장이어도 안 죽는 공격이 훨씬 많아서, 남은 장수로 켜면 연출이 계속 헛나간다.</para>
    ///
    /// <para><paramref name="_preSelectedSplash"/>는 이미 뽑아둔 무쌍 광역 대상을 그대로 넘긴다 —
    /// 예측이 다시 뽑으면 MatchRandom 스트림이 어긋나 멀티가 갈라진다.</para></summary>
    public static void ArmApproach(CardInstance _attacker, CardInstance _defender,
                                   BattleField _attackerField, BattleField _defenderField,
                                   CardInstance _preSelectedSplash = null)
    {
        s_approachArmed = false;
        if (_attacker == null || _defender == null || _attackerField == null || _defenderField == null)
            return;

        if (!BattleOverForecast.CouldEnd(_attackerField, _defenderField,
                                         _attacker.HasKeyword(CardKeyword.Peerless)))
            return;

        s_approachArmed = BattleOverForecast.WillEnd(_attacker, _defender, _attackerField, _defenderField,
                                                     _preSelectedSplash, out _);
    }

    /// <summary>필드 모델이 없는 공격 애니메이션 테스터용 접근 연출 래치.</summary>
    public static void ArmApproachPreview() => s_approachArmed = true;

    public static void CancelApproachArm() => s_approachArmed = false;

    /// <summary>준비된 접근 연출을 한 번 소비한다. 시네마 공격은 기존 카메라 연출을 사용한다.</summary>
    public static bool TryBeginApproach(CardView _attacker, CardView _defender)
    {
        bool t_armed = s_approachArmed;
        s_approachArmed = false;
        if (!t_armed || _attacker == null || _defender == null) return false;

        s_approachActive = true;
        // 좌표가 아니라 카드를 넘긴다 — 접근 중 공격자는 윈드업·돌진으로 계속 움직인다.
        BattleCamera.ApproachFocus(_attacker.transform, _defender.transform, GameTiming.Battle.ApproachFocusIn);
        return true;
    }

    /// <summary>접근 중인 공격 모션에만 적용할 시간 배율. 전역 timeScale은 건드리지 않는다.</summary>
    public static float ApproachDurationFactor
        => s_approachActive ? GameTiming.Battle.ApproachDurationFactor : 1f;

    /// <summary>지금 매치포인트 접근 연출이 도는 중인가. <b>시간이 아니라 거리·자세</b>를 바꿔야 하는
    /// 항목이 이걸 본다(접근 배율은 시간에만 곱하므로 그쪽으로는 표현할 수 없다).</summary>
    public static bool ApproachActive => s_approachActive;

    /// <summary>비종전 공격은 카메라를 복구하고, 결정타가 열렸으면 현재 위치를 피니시에 넘긴다.</summary>
    public static void EndApproach()
    {
        s_approachArmed = false;
        if (!s_approachActive) return;

        s_approachActive = false;
        if (BattleResultBeat.FinishPlayed) return;

        BattleCamera.RestoreFromFinish(GameTiming.Battle.ApproachFocusOut);
    }

    /// <summary>피해·사망 정리 직후, 이 공격이 판을 끝냈는지 기록한다. 공격 한 번마다 반드시 불린다
    /// (끝내지 않았으면 해제) — 안 그러면 지난 공격의 판정이 다음 타격에 새어 나온다.
    ///
    /// <para><see cref="BattleField.IsEmpty"/>는 필드뿐 아니라 대기열까지 본다. 그래서 이 시점의 판정이
    /// 나중에 충원까지 끝난 뒤의 BattleLoop.Run 종료 판정과 같은 답을 낸다 — 충원은 대기열에서 꺼내
    /// 옮기는 것이라 "비었는가"를 바꾸지 않는다. 부활(언데드)도 RemoveDead 안에서 이미 반영됐다.</para></summary>
    public static void Arm(BattleField _attackerField, BattleField _defenderField)
    {
        s_loserOwner = NoOwner;

        bool t_atkEmpty = _attackerField != null && _attackerField.IsEmpty;
        bool t_defEmpty = _defenderField != null && _defenderField.IsEmpty;
        if (!t_atkEmpty && !t_defEmpty) return;

        // 동시 전멸: 로컬 기준 **상대 편을 패자로** 잡는다 — BattleLoop.Run의 종료 판정이 적 필드를 먼저 보고
        // 로컬 승리로 판정하므로, 여기서 다르게 고르면 피니시 색감과 실제 결과 팝업이 어긋난다.
        // (동시 전멸 정책 자체가 확정되면 그때 양쪽을 같이 고쳐야 한다.)
        if (t_atkEmpty && t_defEmpty)
        {
            s_loserOwner = _attackerField.OwnerIndex == TurnState.LocalOwnerIndex
                ? _defenderField.OwnerIndex
                : _attackerField.OwnerIndex;
            return;
        }

        s_loserOwner = t_atkEmpty ? _attackerField.OwnerIndex : _defenderField.OwnerIndex;
    }

    /// <summary>이 공격이 판을 끝냈는가를 <b>소비하지 않고</b> 미리 본다. <see cref="Arm"/> 이후,
    /// 즉 피해 적용(_onEffect) 이후에만 유효하다.
    ///
    /// <para>호출부가 "타격 표시를 기다릴지"를 정하는 데 쓴다 — 결정타면 표시를 기다리면 안 된다.
    /// 기다린 뒤에 얼리면 얼어붙는 프레임이 부딪힌 순간이 아니라 피격 연출이 다 끝난 뒤가 된다.</para></summary>
    public static bool WillFinish => s_loserOwner != NoOwner;

    public static void Disarm()
    {
        s_loserOwner = NoOwner;
        s_approachArmed = false;
        s_approachActive = false;
    }

    /// <summary>이번 타격이 판을 끝냈으면 클로즈업·임팩트를 띄우고 화면을 <b>느린 채로 남긴 뒤</b> true.
    /// 아니면 아무것도 하지 않고 false. 사망 연출 <b>직전</b>에 부를 것 — 죽는 카드의 View와 좌표가
    /// 살아 있어야 하고, 그 사망이 슬로우 안에서 재생돼야 한다.
    ///
    /// <para>true를 받았으면 사망 연출 뒤에 반드시 <see cref="End"/>로 닫는다(호출부는 finally로 보장).</para>
    ///
    /// <para>죽는 쪽이 공격자면(반격사) 강조 대상과 방향이 통째로 뒤집힌다: 카메라는 공격자 자리로 가고,
    /// 임팩트는 "방어자 → 공격자" 방향으로 눕는다. 상대가 때리러 왔다가 되받고 쓰러지는 그림이다.</para></summary>
    public static async UniTask<bool> TryBegin(CardView _attacker, CardView _defender, CardView _splash,
                                               bool _attackerKilled, bool _defenderKilled, bool _splashKilled,
                                               CancellationToken _ct = default)
    {
        int t_loser = s_loserOwner;
        // 종전 래치만 소비한다. 접근 상태까지 지우면 판정과 실제 사망 View가 어긋난 경우
        // TryBegin(false) 뒤 PlayCore.finally가 접근 카메라를 복구하지 못한다.
        s_loserOwner = NoOwner;
        if (t_loser == NoOwner) return false;

        // 패자 편에서 이번 타격에 쓰러진 카드들. 이게 강조 대상이다.
        bool t_atkVictim = IsVictim(_attacker, _attackerKilled, t_loser);
        bool t_defVictim = IsVictim(_defender, _defenderKilled, t_loser);
        bool t_splVictim = IsVictim(_splash,   _splashKilled,   t_loser);

        // 카메라가 **따라갈** 카드. 좌표를 찍어두지 않는 이유 — 죽는 카드는 그 자리에 서 있지 않다.
        // 특히 반격사에서 죽는 쪽은 공격자이고, 그 카드는 돌진 지점에서 제 슬롯으로 밀려 돌아가는 중이라
        // 좌표로 잡으면 카메라가 충돌 지점에 굳어 "맞은 카드를 비추는" 그림이 된다.
        // 공격자와 방어자는 서로 다른 필드라 한 편에서 죽는 카드는 최대 둘(주 대상 + 광역)이다.
        Transform t_victimA = t_atkVictim ? _attacker.transform
                            : t_defVictim ? _defender.transform
                            : t_splVictim ? _splash.transform : null;
        Transform t_victimB = t_atkVictim ? null
                            : (t_defVictim && t_splVictim) ? _splash.transform : null;

        // 이번 타격으로 쓰러진 카드가 패자 편에 없다 = 전멸을 만든 게 이 타격이 아니다(교활 퇴장 등).
        // 강조할 그림이 없으므로 조용히 접고 기존 결과 여운에 맡긴다.
        if (t_victimA == null) return false;

        // 임팩트 VFX·방향은 그 순간의 좌표로 한 번만 찍는다(터지고 사라지는 연출이라 추적이 필요 없다).
        Vector3 t_focus = t_victimB != null
            ? Vector3.Lerp(t_victimA.position, t_victimB.position, 0.5f)
            : t_victimA.position;

        // 때린 쪽 = 죽는 게 공격자면 방어자(반격), 아니면 공격자. 방향이 여기서 갈린다.
        CardView t_source = t_atkVictim ? _defender : _attacker;
        Vector3  t_dir    = t_source != null ? (t_focus - t_source.transform.position) : Vector3.zero;

        bool t_won = t_loser != TurnState.LocalOwnerIndex;

        BattleTimingConfig t_cfg = GameTiming.Battle;

        try
        {
            // 카메라는 얼어붙는 순간에 확 붙고(얼어붙기+진입), 그 뒤 사망 연출이 도는 내내 천천히 더 다가간다.
            BattleCamera.FinishFocus(t_victimA, t_victimB, t_cfg.FinishHitStop + t_cfg.FinishIn, t_cfg.FinishCreep);
            PlayImpactVfx(_attacker, _defender, _splash, t_atkVictim, t_defVictim, t_splVictim, t_focus, t_dir);
            BattleCamera.Shake(1f);

            await BattleResultBeat.BeginFinish(t_won, _ct);
            // 진입이 끝난 뒤에만 접근 소유권을 넘긴다. 그 전 예외는 아래에서 접근 카메라까지 복구한다.
            s_approachActive = false;
            return true;
        }
        catch
        {
            s_approachActive = false;
            BattleCamera.RestoreFromFinish(GameTiming.Battle.ApproachFocusOut);
            throw;
        }
    }

    /// <summary>피니시를 닫는다 — 짧은 여운 뒤 정상 배속 복귀, 카메라도 같이 놓는다.
    /// <see cref="TryBegin"/>이 false였으면(또는 이미 닫혔으면) 무동작.</summary>
    public static async UniTask End(CancellationToken _ct = default)
    {
        if (!BattleResultBeat.FinishActive) return;

        // 화면이 다시 빨라질 때 카메라도 같이 물러나야 "끝났다"가 한 동작으로 읽힌다.
        // **거리(z)까지 되돌린다** — 당긴 채로 두면 결과 팝업이 클로즈업에 갇힌 보드 위에 뜨고,
        // 그 뒤로는 z를 되돌릴 지점이 씬 종료밖에 없다(여운 경로는 피니시 뒤엔 카메라를 건드리지 않는다).
        // 배속 복귀와 같은 길이라 물러남과 속도 복구가 한 동작으로 맞는다.
        BattleTimingConfig t_cfg = GameTiming.Battle;
        BattleCamera.RestoreFromFinish(t_cfg.FinishHold + t_cfg.FinishOut);

        await BattleResultBeat.EndFinish(_ct);
    }

    // 이 View가 "패자 편에서 이번 타격에 쓰러진 카드"인가.
    static bool IsVictim(CardView _view, bool _killed, int _loserOwner)
        => _killed && _view != null && _view.BoundCard != null && _view.BoundCard.ownerIndex == _loserOwner;

    /// <summary>피니시 임팩트. 전용 프리팹(<see cref="BattleVfxId.FinishImpact"/>)이 배선돼 있으면 그걸 쓰고,
    /// 없으면 <b>기존 피격 연출을 최대 세기로</b> 쓰러지는 카드마다 터뜨린다.
    /// 폴백을 두는 이유 — 전용 프리팹을 꽂기 전에도 "한 방이 갈랐다"가 화면에 남아야 하고,
    /// 꽂는 순간 자동으로 전용 쪽이 이겨서 이 코드를 다시 고칠 일이 없다.</summary>
    static void PlayImpactVfx(CardView _attacker, CardView _defender, CardView _splash,
                              bool _atkVictim, bool _defVictim, bool _splVictim,
                              Vector3 _focus, Vector3 _direction)
    {
        if (BattleVfx.TryGetEntry(BattleVfxId.FinishImpact, out _))
        {
            BattleVfx.PlayDirected(BattleVfxId.FinishImpact, _focus,
                                   SortingLayerOf(_defender, _attacker, _splash), _direction);
            return;
        }

        if (_atkVictim) Burst(_attacker, _direction);
        if (_defVictim) Burst(_defender, _direction);
        if (_splVictim) Burst(_splash,   _direction);
    }

    // 세기 1 = 항목에 배선된 countByStrength/speedByStrength의 최대값. 평소 피격보다 확실히 크게 인다.
    static void Burst(CardView _view, Vector3 _direction)
    {
        if (_view == null) return;
        BattleVfx.PlayAttached(BattleVfxId.Hit, _view.transform, _view.IsEnemySide,
                               _view.VfxSortingLayerId, _direction, _strength01: 1f);
    }

    // 정렬 레이어는 살아 있는 View 아무거나에서 빌린다 — 셋 다 같은 Card 레이어를 쓴다.
    static int SortingLayerOf(params CardView[] _views)
    {
        foreach (CardView t_v in _views)
            if (t_v != null) return t_v.VfxSortingLayerId;
        return 0;
    }
}
