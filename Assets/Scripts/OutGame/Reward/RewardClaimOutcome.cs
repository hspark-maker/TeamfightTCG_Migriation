using System.Collections.Generic;

/// <summary>보상 수령 한 번의 결과 — 성사 여부와 <b>서버가 실제로 준 것</b>을 함께 나른다.
/// <para>⚠ <see cref="Granted"/>를 지금 읽는 곳은 없다. 팝업이 응답을 기다리지 않게 되면서
/// 분출·롤업이 표시 목록(예고)으로 서기 때문이다. 값은 계속 채워 두므로, 실패 표면이나
/// 예고와 실지급의 대조가 필요해지면 여기서 꺼내면 된다.</para></summary>
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
