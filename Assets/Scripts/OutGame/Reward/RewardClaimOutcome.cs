using System.Collections.Generic;

/// <summary>보상 수령 한 번의 결과 — 성사 여부와 <b>서버가 실제로 준 것</b>을 함께 나른다.
/// 분출·잔액 롤업의 입력이다. 클라 스펙 캐시로 연출을 태우면 표가 갈렸을 때 숫자가 튄다.</summary>
public readonly struct RewardClaimOutcome
{
    /// <summary>거절·통신 실패는 여기가 false다(그때 <see cref="Granted"/>는 비어 있다).</summary>
    public readonly bool Succeeded;

    /// <summary>지급 목록. 성사돼도 비어 있을 수 있다(보상 미저작 정점처럼 지급 0건인 수령).</summary>
    public readonly IReadOnlyList<CurrencyGain> Granted;

    public RewardClaimOutcome(IReadOnlyList<CurrencyGain> _granted)
    {
        Succeeded = true;
        Granted = _granted;
    }
}
