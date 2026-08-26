using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 한 경기에서 사용할 카드 성장 스냅샷의 출처.
/// 전투와 와이어는 최종 <see cref="CardGrowth"/>만 소비하며, 로컬 세이브/Firebase 여부를 알지 않는다.
/// </summary>
public interface IMatchGrowthSource
{
    /// <summary>구현은 취소 토큰을 지키고 초기화 상한 안에 끝내야 한다. Firebase 구현은 전투 진입 전 캐시를 권장한다.</summary>
    UniTask<CardGrowth[]> ResolveMyGrowth(IReadOnlyList<int> _deck, CancellationToken _ct);

    /// <summary>상대가 보고한 최종 성장 스냅샷을 출처의 정본과 대조한다.
    /// 로컬 임시 구현은 신뢰하고, Firebase 구현은 안정 UserId 기준 서버 값과 비교한다.</summary>
    UniTask<bool> VerifyOpponentGrowth(MatchGrowthOpponent _opponent, IReadOnlyList<int> _cardIds,
                                       IReadOnlyList<CardGrowth> _growth, CancellationToken _ct);
}

public readonly struct MatchGrowthOpponent
{
    public readonly int OwnerIndex;
    public readonly string TransportId;
    public readonly string StableUserId;

    public MatchGrowthOpponent(int _ownerIndex, string _transportId, string _stableUserId)
    {
        this.OwnerIndex = _ownerIndex;
        this.TransportId = _transportId;
        this.StableUserId = _stableUserId;
    }
}

/// <summary>부트 계층이 공급자를 교체하는 단일 슬롯. Firebase가 먼저 주입되면 로컬 폴백이 덮어쓰지 않는다.</summary>
public static class MatchGrowthSource
{
    public static IMatchGrowthSource Current { get; private set; }

    /// <summary>Firebase 로그인 수명 주체가 권위 공급자를 주입한다. 같은 시점에
    /// NetworkGameController.SetStablePlayerIdProvider도 설정해야 상대 계정 정본을 대조할 수 있다.</summary>
    public static void Set(IMatchGrowthSource _source) => Current = _source;

    /// <summary>Firebase 로그아웃·계정 전환·테스트 종료 시 소유자가 명시적으로 수명을 끝낸다.</summary>
    public static void Clear(IMatchGrowthSource _expected = null)
    {
        if (_expected == null || object.ReferenceEquals(Current, _expected)) Current = null;
    }

    public static void SetFallback(IMatchGrowthSource _source)
    {
        if (Current == null) Current = _source;
    }
}

/// <summary>출처와 와이어가 공유하는 최종 성장 스냅샷의 구조 검증.</summary>
public static class MatchGrowthValidation
{
    static readonly CardKeyword KnownKeywords = BuildKnownKeywords();

    public static bool IsValid(int _cardId, CardGrowth _growth, out string _error)
    {
        if (_cardId <= 0) { _error = $"카드 ID 오류({_cardId})"; return false; }
        if (!CardCatalog.Contains(_cardId)) { _error = $"현재 프로필에 없는 카드({_cardId})"; return false; }
        if (!_growth.Applied) { _error = $"레벨 오류({_growth.Level})"; return false; }
        if (_growth.HpBonus < 0 || _growth.HpBonus > int.MaxValue - CardCatalog.RequireSpec(_cardId).MaxHp)
        {
            _error = $"HP 보너스 오류({_growth.HpBonus})";
            return false;
        }
        if (_growth.EvolutionStage < 0 || _growth.EvolutionStage > CardSpec.MaxEvolutionStage)
        {
            _error = $"진화 단계 오류({_growth.EvolutionStage})";
            return false;
        }
        if ((_growth.UnlockedKeywords & ~KnownKeywords) != 0)
        {
            _error = $"알 수 없는 키워드 비트({(int)_growth.UnlockedKeywords})";
            return false;
        }

        _error = null;
        return true;
    }

    static CardKeyword BuildKnownKeywords()
    {
        CardKeyword t_result = CardKeyword.None;
        foreach (CardKeyword t_keyword in System.Enum.GetValues(typeof(CardKeyword)))
            t_result |= t_keyword;
        return t_result;
    }
}
