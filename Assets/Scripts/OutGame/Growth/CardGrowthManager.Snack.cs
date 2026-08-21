using UnityEngine;

// 카드별 간식(카드팩 중복 보상)의 조회·적립.
//
// 간식은 그 카드에만 쓰는 재화라 전역 잔액 배열(CurrencySaveData.balances)에 못 담는다. 이미 cardId로
// 갈라져 있는 CardGrowthEntry에 얹었고, 그래서 캐시·flush를 쥔 이 매니저가 그대로 창구가 된다.
//
// 적립 지점은 CardPackOpener 하나다. OwnershipManager.Grant 안에 넣으면 스타터덱·튜토리얼·디버그
// 전체 해금까지 간식을 뿌리고, 소유 관리가 성장 도메인을 아는 역방향 의존이 생긴다.
public static partial class CardGrowthManager
{
    // 카드 번호의 간식 보유량(기록 없으면 0). 음수 세이브는 0으로 읽는다.
    public static int SnackOf(int _id)
    {
        if (_id <= 0) return 0;
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return 0;

        return t_entry.snack > 0 ? t_entry.snack : 0;
    }

    public static int SnackOf(CardData _card) => SnackOf(CardCatalog.IdOf(_card));

    /// <summary>간식을 적립한다(적립됐으면 true). <b>디스크에 쓰지 않는다</b> — 호출부가 흐름 끝에
    /// <see cref="FlushToData"/>나 <see cref="Save"/>로 반영해야 한다. 한 번 개봉에 중복이 여러 장
    /// 나올 수 있어 장당 저장을 피하려는 분할이다. 미초기화·잘못된 번호·0 이하 수량은 조용히 거절.</summary>
    public static bool AddSnack(int _id, int _amount)
    {
        if (!s_initialized) return false;
        if (!CardCatalog.Contains(_id)) return false;
        if (_amount <= 0) return false;

        CardGrowthEntry t_entry = Entry(_id);
        int t_current = t_entry.snack > 0 ? t_entry.snack : 0;

        // long으로 더한 뒤 상한에서 자른다 — int 넘침 방지.
        long t_next = (long)t_current + _amount;
        t_entry.snack = t_next > int.MaxValue ? int.MaxValue : (int)t_next;

        OnGrowthChanged?.Invoke();
        return true;
    }

    public static bool AddSnack(CardData _card, int _amount) => AddSnack(CardCatalog.IdOf(_card), _amount);

    public static int LimitBreakOf(int _id)
    {
        if (_id <= 0) return 0;
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return 0;

        return Mathf.Clamp(t_entry.limitBreak, 0, Config.MaxLimitBreak);
    }

    public static int LimitBreakOf(CardData _card) => LimitBreakOf(CardCatalog.IdOf(_card));

    public static bool TryGetNextLimitBreakStep(CardData _card, out LimitBreakStep _step)
    {
        _step = default;
        if (!s_initialized || !s_configInjected || _card == null) return false;

        // 강화 레벨을 보지 않는다 — 한계돌파는 간식으로만 무는 별개 축이라 0성부터 열려 있다.
        int t_id = CardCatalog.IdOf(_card);
        if (!OwnershipManager.IsOwned(t_id)) return false;

        return Config.TryGetLimitBreakStep(LimitBreakOf(t_id) + 1, out _step);
    }

    // 간식 차감과 단계 증가는 반드시 함께 저장한다.
    public static bool TryLimitBreak(CardData _card)
    {
        if (!TryGetNextLimitBreakStep(_card, out LimitBreakStep t_step)) return false;

        int t_id = CardCatalog.IdOf(_card);
        CardGrowthEntry t_entry = Entry(t_id);
        int t_snack = t_entry.snack > 0 ? t_entry.snack : 0;
        if (t_snack < t_step.SnackCost) return false;

        t_entry.snack = t_snack - t_step.SnackCost;
        t_entry.limitBreak = t_step.Stage;
        Save();
        OnGrowthChanged?.Invoke();
        return true;
    }
}
