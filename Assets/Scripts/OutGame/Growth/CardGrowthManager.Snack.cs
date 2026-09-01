using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 카드별 간식(카드팩 중복 보상)의 조회와 한계돌파.
// 간식은 그 카드에만 쓰는 재화라 전역 잔액이 아니라 cardId로 갈린 CardGrowthEntry에 얹혀 있다.
// 적립은 서버 openPack이 한다 — 클라에는 간식을 늘리는 경로가 없다.
//
// 화면이 읽는 간식·단계는 저장값에 "서버가 아직 확정하지 않은 한 방"을 얹은 값이다. 그 낙관분은 저장되지도
// 진실로 읽히지도 않고, 응답 채택(Init)이 버리거나 LimitBreakPendingTicket이 걷는다 —
// 서버 왕복 길이를 화면에서 떼어놓는 장치이고, 규율은 재화 쪽 CurrencyManager와 같다.
public static partial class CardGrowthManager
{
    // 아직 서버가 확정하지 않은 한계돌파. 카드마다 갈리고 겹칠 수 있어 플래그가 아니라 누계다.
    static readonly Dictionary<int, PendingLimitBreak> s_pendingLimitBreak = new Dictionary<int, PendingLimitBreak>();

    // 낙관분을 통째로 버린 횟수. 그 전에 발행된 표는 이미 버려진 몫을 또 되돌리게 되므로 이 값으로 걸러낸다.
    static int s_pendingLimitBreakEpoch;

    /// <summary>지금 발행되는 표가 속할 세대. <see cref="LimitBreakPendingTicket"/> 이 걷어도 되는지 가릴 때 쓴다.</summary>
    internal static int PendingLimitBreakEpoch => s_pendingLimitBreakEpoch;

    /// <summary>카드 번호의 간식 보유량(기록 없으면 0). 아직 확정되지 않은 차감이 이미 빠진 <b>표시값</b>이다 —
    /// 간식은 서버로 나가는 값이 아니라(요청은 cardId만 싣는다) 축이 하나뿐이다.</summary>
    public static int SnackOf(int _id)
    {
        if (_id <= 0) return 0;

        int t_snack = SavedSnackOf(_id) - PendingOf(_id).Snack;
        return t_snack > 0 ? t_snack : 0;
    }

    /// <summary>서버가 확정한 한계돌파 단계. 체력 가산분이 여기서 나와 전투·서버 제출까지 가므로
    /// 왕복 중인 한 방은 들어 있지 않다 — 화면에 그릴 단계는 <see cref="DisplayLimitBreakOf"/> 가 답한다.</summary>
    public static int LimitBreakOf(int _id)
    {
        if (_id <= 0) return 0;

        return SavedLimitBreakOf(_id);
    }

    /// <summary>화면에 그릴 한계돌파 단계. 서버가 아직 확정하지 않은 한 방이 얹혀 있다.</summary>
    public static int DisplayLimitBreakOf(int _id)
    {
        if (_id <= 0) return 0;

        return Mathf.Clamp(SavedLimitBreakOf(_id) + PendingOf(_id).Stage, 0, GrowthRules.MaxLimitBreak);
    }

    /// <summary>다음에 살 수 있는 한계돌파 한 단계(최대 단계면 false). 표시값 기준이라 왕복 중에는
    /// 이미 그 다음 단계를 답한다 — 버튼·비용 글자가 앞세운 표시를 따라가고, 같은 단계를 두 번 사지 못한다.</summary>
    public static bool TryGetNextLimitBreakStep(int _cardId, out LimitBreakStep _step)
    {
        _step = default;
        if (!s_initialized || _cardId <= 0) return false;

        // 강화 레벨을 보지 않는다 — 한계돌파는 간식으로만 무는 별개 축이라 0성부터 열려 있다.
        int t_id = _cardId;
        if (!OwnershipManager.IsOwned(t_id)) return false;

        return GrowthRules.TryGetLimitBreakStep(DisplayLimitBreakOf(t_id) + 1, out _step);
    }

    /// <summary>한계돌파 1회를 서버에 요청한다(간식 차감과 단계 증가는 서버에서 한 몸이다).
    /// 곡선·차감·단계의 진실원은 서버 limitBreakCard 다 — 아래 선검사는 왕복을 아끼는 낙관 검사일 뿐이라
    /// 서버가 다른 답을 주면 그쪽이 이긴다.</summary>
    /// <param name="_onReserved">간식·단계를 미리 얹은 직후, 첫 await 이전에 딱 한 번 불린다 —
    /// 화면이 누른 프레임에 새 값을 그리는 자리다. 선검사에 걸려 요청이 나가지 않으면 불리지 않는다.</param>
    public static async UniTask<ELimitBreakOutcome> TryLimitBreakAsync(int _cardId, Action _onReserved = null)
    {
        int t_id = _cardId;

        // 미초기화·미소유를 먼저 갈라내야 TryGetNextLimitBreakStep이 낸 false를 "최대 단계"로 읽을 수 있다.
        if (!s_initialized || t_id <= 0) return ELimitBreakOutcome.NotReady;
        if (!OwnershipManager.IsOwned(t_id)) return ELimitBreakOutcome.NotReady;

        if (!TryGetNextLimitBreakStep(t_id, out LimitBreakStep t_step)) return ELimitBreakOutcome.MaxStage;
        if (SnackOf(t_id) < t_step.SnackCost) return ELimitBreakOutcome.NotEnoughSnack;

        // 첫 await 이전이어야 유저가 누른 프레임에 간식이 줄고 단계·체력이 오른다. 조기 반환 뒤인 이유는
        // 요청이 나가지도 않은 갈래에 걷을 몫을 남기지 않기 위해서다.
        LimitBreakPendingTicket t_pending = LimitBreakPendingTicket.Hold(t_id, t_step.SnackCost);
        _onReserved?.Invoke();

        ELimitBreakOutcome t_outcome = await LimitBreakCommand.LimitBreakAsync(t_id);

        // 막힌 결말에서만 걷는다. 성공은 서버가 이미 올린 단계라 되돌릴 것이 없고, 응답에 cardGrowth 슬롯이
        // 실리지 않아 채택이 없었더라도 낙관분이 그 변경분을 대신 서 있다가 다음 채택 때 함께 버려진다.
        if (t_outcome != ELimitBreakOutcome.Success)
        {
            t_pending.Settle();
            return t_outcome;
        }

        t_pending.Discard();

        // 단계·간식은 응답 채택이 갈아끼운 슬롯을 ServerSlotRehydrator가 Init으로 다시 태워 이미 캐시에 있다 —
        // 여기서 대입하거나 저장하면 서버와 이중 진실원이 된다.
        OnGrowthChanged?.Invoke();

        return ELimitBreakOutcome.Success;
    }

    /// <summary>서버가 확정하기 전의 한계돌파 한 방을 표시값에 미리 얹는다.
    /// 부르는 곳은 <see cref="LimitBreakPendingTicket"/> 하나다 — 걷는 짝이 없으면 표시값이 영영 어긋난다.</summary>
    internal static void HoldPendingLimitBreak(int _cardId, int _snackCost)
    {
        if (_cardId <= 0) return;

        PendingLimitBreak t_pending = PendingOf(_cardId);
        t_pending.Snack += _snackCost > 0 ? _snackCost : 0;
        t_pending.Stage += 1;
        s_pendingLimitBreak[_cardId] = t_pending;

        OnGrowthChanged?.Invoke();
    }

    /// <summary>얹어둔 한 방을 걷는다. 부르는 곳은 <see cref="LimitBreakPendingTicket"/> 하나다.</summary>
    internal static void ReleasePendingLimitBreak(int _cardId, int _snackCost)
    {
        if (!s_pendingLimitBreak.TryGetValue(_cardId, out PendingLimitBreak t_pending)) return;

        t_pending.Snack -= _snackCost > 0 ? _snackCost : 0;
        t_pending.Stage -= 1;

        if (t_pending.Stage <= 0) s_pendingLimitBreak.Remove(_cardId);
        else                      s_pendingLimitBreak[_cardId] = t_pending;

        OnGrowthChanged?.Invoke();
    }

    /// <summary>남은 낙관분을 전부 버린다. 서버가 갈아끼운 성장 슬롯을 캐시에 다시 세우는 자리(<see cref="Init"/>)에서만 부른다.</summary>
    // 카드를 가리지 않고 비우는 근거: 세이브 명령은 ServerSaveCommands가 한 줄로 세우고 상세창도 한 장이라,
    // 낙관분이 두 카드에 동시에 서는 일이 없다. 그 전제가 깨지면(동시 요청이 생기면) 아직 응답 전인 카드의
    // 표시가 여기서 조용히 되돌아가므로, 그때는 "채택이 그 카드의 값을 실어 왔는가"를 카드별로 가려야 한다.
    internal static void ClearPendingLimitBreak()
    {
        // 아직 왕복이 살아 있는 표가 나중에 자기 몫을 또 되돌리면 간식이 모자라 보인다 — 세대를 올려 그것들을 무효로 만든다.
        s_pendingLimitBreakEpoch++;

        // 무음이다 — 여기서 통지하면 채택 직전의 "변동 전" 값이 화면에 한 번 닿아 숫자가 되돌아갔다 다시 간다.
        s_pendingLimitBreak.Clear();
    }

    static PendingLimitBreak PendingOf(int _cardId)
        => s_pendingLimitBreak.TryGetValue(_cardId, out PendingLimitBreak t_pending) ? t_pending : default;

    // 음수 세이브는 0으로 읽는다.
    static int SavedSnackOf(int _id)
    {
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return 0;

        return t_entry.Snack > 0 ? t_entry.Snack : 0;
    }

    static int SavedLimitBreakOf(int _id)
    {
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return 0;

        return Mathf.Clamp(t_entry.LimitBreak, 0, GrowthRules.MaxLimitBreak);
    }

    // 한 카드에 걸린 낙관분. 왕복이 겹칠 수 있어 단계도 누계다.
    struct PendingLimitBreak
    {
        public int Snack;
        public int Stage;
    }
}
