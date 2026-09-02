using Cysharp.Threading.Tasks;
using UnityEngine;

// 카드별 간식(카드팩 중복 보상)의 조회와 한계돌파.
// 간식은 그 카드에만 쓰는 재화라 전역 잔액이 아니라 cardId로 갈린 CardGrowthEntry에 얹혀 있다.
// 적립은 서버 openPack이 한다 — 클라에는 간식을 늘리는 경로가 없다.
//
// 화면이 읽는 간식·단계는 언제나 서버가 확정한 값이다. 왕복 중인 한 방을 미리 얹지 않는 이유는
// 강화·진화와 규율을 맞추기 위해서다 — 왕복 길이는 대기 화면(ServerWaitOverlay)이 덮고,
// 오른 단계는 응답이 도착한 프레임에 한꺼번에 드러난다.
public static partial class CardGrowthManager
{
    /// <summary>카드 번호의 간식 보유량(기록 없으면 0).</summary>
    public static int SnackOf(int _id)
    {
        if (_id <= 0) return 0;
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return 0;

        // 음수 세이브는 0으로 읽는다.
        return t_entry.Snack > 0 ? t_entry.Snack : 0;
    }

    /// <summary>카드 번호의 한계돌파 단계(기록 없으면 0). 체력 가산분이 여기서 나와 전투·서버 제출까지 간다.</summary>
    public static int LimitBreakOf(int _id)
    {
        if (_id <= 0) return 0;
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return 0;

        return Mathf.Clamp(t_entry.LimitBreak, 0, GrowthRules.MaxLimitBreak);
    }

    /// <summary>다음에 살 수 있는 한계돌파 한 단계(최대 단계면 false).</summary>
    public static bool TryGetNextLimitBreakStep(int _cardId, out LimitBreakStep _step)
    {
        _step = default;
        if (!s_initialized || _cardId <= 0) return false;

        // 강화 레벨을 보지 않는다 — 한계돌파는 간식으로만 무는 별개 축이라 0성부터 열려 있다.
        int t_id = _cardId;
        if (!OwnershipManager.IsOwned(t_id)) return false;

        return GrowthRules.TryGetLimitBreakStep(LimitBreakOf(t_id) + 1, out _step);
    }

    /// <summary>한계돌파 1회를 서버에 요청한다(간식 차감과 단계 증가는 서버에서 한 몸이다).
    /// 곡선·차감·단계의 진실원은 서버 limitBreakCard 다 — 아래 선검사는 왕복을 아끼는 낙관 검사일 뿐이라
    /// 서버가 다른 답을 주면 그쪽이 이긴다.</summary>
    public static async UniTask<ELimitBreakOutcome> TryLimitBreakAsync(int _cardId)
    {
        int t_id = _cardId;

        // 미초기화·미소유를 먼저 갈라내야 TryGetNextLimitBreakStep이 낸 false를 "최대 단계"로 읽을 수 있다.
        if (!s_initialized || t_id <= 0) return ELimitBreakOutcome.NotReady;
        if (!OwnershipManager.IsOwned(t_id)) return ELimitBreakOutcome.NotReady;

        if (!TryGetNextLimitBreakStep(t_id, out LimitBreakStep t_step)) return ELimitBreakOutcome.MaxStage;
        if (SnackOf(t_id) < t_step.SnackCost) return ELimitBreakOutcome.NotEnoughSnack;

        ELimitBreakOutcome t_outcome = await LimitBreakCommand.LimitBreakAsync(t_id);
        if (t_outcome != ELimitBreakOutcome.Success) return t_outcome;

        // 단계·간식은 응답 채택이 갈아끼운 슬롯을 ServerSlotRehydrator가 Init으로 다시 태워 이미 캐시에 있다 —
        // 여기서 대입하거나 저장하면 서버와 이중 진실원이 된다.
        OnGrowthChanged?.Invoke();

        return ELimitBreakOutcome.Success;
    }
}
