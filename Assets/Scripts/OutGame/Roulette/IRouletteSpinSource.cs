using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// 룰렛 회전 1회의 결과 코드.
public enum ERouletteSpinResult
{
    Success,
    NotConfigured,
    EmptyPool,
    Canceled,

    // 아래 다섯은 서버가 판정한다.
    RouletteNotFound,
    InsufficientTicket,
    Rejected,
    NetworkFailed,

    // 티켓은 빠졌고 상품도 지갑에 들어왔는데 그것을 그릴 수 없는 자리.
    // Rejected 와 갈라 두는 이유는 문구 하나뿐이다 — "거절되었다"고 말하면 늘어난 잔액이 거짓말이 된다.
    RewardUnreadable,
}

// 회전 1회의 결과 스냅샷(불변). 실패면 SlotIndex = -1.
public readonly struct RouletteSpinOutcome
{
    public const int INVALID_SLOT = -1;

    public readonly ERouletteSpinResult Result;

    /// <summary>멈출 칸의 유일한 출처. 화면이 칸을 스스로 고르면 연출과 서버 실지급이 갈린다.</summary>
    public readonly int SlotIndex;

    // 상품을 결과가 직접 운반한다 — 화면이 RouletteConfig를 되읽으면 서버 표와 클라 저작이 어긋나는 순간 거짓말을 그린다.
    public readonly ECurrencyType Currency;
    public readonly long Amount;
    public readonly string PackId;
    public readonly IReadOnlyList<DrawnCard> Cards;
    public readonly IReadOnlyList<CurrencyGain> Granted;
    public readonly IReadOnlyList<GrantedRewardPack> Packs;
    public bool IsPack => !string.IsNullOrEmpty(PackId);

    public bool Success => Result == ERouletteSpinResult.Success;

    public static RouletteSpinOutcome CreateSuccess(int _slotIndex, ECurrencyType _currency, long _amount)
        => new RouletteSpinOutcome(ERouletteSpinResult.Success, _slotIndex, _currency, _amount);

    public static RouletteSpinOutcome CreateFailure(ERouletteSpinResult _result)
        => new RouletteSpinOutcome(_result, INVALID_SLOT, ECurrencyType.Gold, 0L);

    public static RouletteSpinOutcome CreatePack(int _slotIndex, string _packId, long _amount,
        IReadOnlyList<DrawnCard> _cards, IReadOnlyList<CurrencyGain> _granted,
        IReadOnlyList<GrantedRewardPack> _packs = null)
        => new RouletteSpinOutcome(ERouletteSpinResult.Success, _slotIndex, default, _amount, _packId, _cards, _granted, _packs);

    RouletteSpinOutcome(ERouletteSpinResult _result, int _slotIndex, ECurrencyType _currency, long _amount,
        string _packId = null, IReadOnlyList<DrawnCard> _cards = null, IReadOnlyList<CurrencyGain> _granted = null,
        IReadOnlyList<GrantedRewardPack> _packs = null)
    {
        Result = _result;
        SlotIndex = _slotIndex;
        Currency = _currency;
        Amount = _amount;
        PackId = _packId;
        Cards = _cards;
        Granted = _granted;
        Packs = _packs;
    }
}

// 멈출 칸을 판정해 오는 창구. 로컬 추첨 → 서버 판정 교체는 이 구현 하나를 갈아끼우는 것으로 끝난다.
public interface IRouletteSpinSource
{
    // 실패·거절·취소는 전부 결과값으로 돌린다. 예외를 던지지 않는 것이 계약이다 — 망·거절 예외를 접는 자리가 구현이다.
    UniTask<RouletteSpinOutcome> SpinAsync(CancellationToken _ct);
}
