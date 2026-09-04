using System.Threading;
using Cysharp.Threading.Tasks;

// 룰렛 회전 1회의 결과 코드.
// 1단계가 만들 수 없는 갈래까지 지금 전부 선언한다 — 화면의 문구 분기를 한 번만 쓰고 2단계에 손대지 않기 위해서다.
public enum ERouletteSpinResult
{
    Success,
    NotConfigured,
    EmptyPool,
    Canceled,

    // 아래 넷은 서버 판정이 붙는 2단계에서만 나온다.
    RouletteNotFound,
    InsufficientTicket,
    Rejected,
    NetworkFailed,
}

// 회전 1회의 결과 스냅샷(불변). 실패면 SlotIndex = -1.
public readonly struct RouletteSpinOutcome
{
    public const int INVALID_SLOT = -1;

    public readonly ERouletteSpinResult Result;

    /// <summary>멈출 칸의 유일한 출처. 화면이 칸을 스스로 고르면 2단계에서 연출과 실지급이 갈린다.</summary>
    public readonly int SlotIndex;

    // 상품을 결과가 직접 운반한다 — 화면이 RouletteConfig를 되읽으면 서버 표와 클라 저작이 어긋나는 순간 거짓말을 그린다.
    public readonly ECurrencyType Currency;
    public readonly long Amount;
    public readonly bool IsJackpot;

    public bool Success => Result == ERouletteSpinResult.Success;

    public static RouletteSpinOutcome CreateSuccess(int _slotIndex, ECurrencyType _currency, long _amount, bool _isJackpot)
        => new RouletteSpinOutcome(ERouletteSpinResult.Success, _slotIndex, _currency, _amount, _isJackpot);

    public static RouletteSpinOutcome CreateFailure(ERouletteSpinResult _result)
        => new RouletteSpinOutcome(_result, INVALID_SLOT, ECurrencyType.Gold, 0L, false);

    RouletteSpinOutcome(ERouletteSpinResult _result, int _slotIndex, ECurrencyType _currency, long _amount, bool _isJackpot)
    {
        Result = _result;
        SlotIndex = _slotIndex;
        Currency = _currency;
        Amount = _amount;
        IsJackpot = _isJackpot;
    }
}

// 멈출 칸을 판정해 오는 창구. 로컬 추첨 → 서버 판정 교체는 이 구현 하나를 갈아끼우는 것으로 끝난다.
public interface IRouletteSpinSource
{
    // 실패·거절·취소는 전부 결과값으로 돌린다. 예외를 던지지 않는 것이 계약이다 — 망·거절 예외를 접는 자리가 구현이다.
    UniTask<RouletteSpinOutcome> SpinAsync(CancellationToken _ct);
}
