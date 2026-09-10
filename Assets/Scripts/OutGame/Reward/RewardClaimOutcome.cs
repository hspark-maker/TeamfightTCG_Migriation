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
    public readonly IReadOnlyList<DrawnCard> Cards;
    public readonly IReadOnlyList<GrantedRewardPack> Packs;
    // null이면 기존 팩 → 직접 지급 순서. 서버 flat 순서가 있으면 그 순서로 재생한다.
    public readonly IReadOnlyList<RewardPresentationBatch> PresentationBatches;
    public bool HasCards => (Cards?.Count ?? 0) > 0 || (Packs?.Count ?? 0) > 0;

    public RewardClaimOutcome(IReadOnlyList<CurrencyGain> _granted, IReadOnlyList<DrawnCard> _cards = null,
        IReadOnlyList<GrantedRewardPack> _packs = null,
        IReadOnlyList<RewardPresentationBatch> _presentationBatches = null)
    {
        Succeeded = true;
        Granted = _granted;
        Cards = _cards;
        Packs = _packs;
        PresentationBatches = _presentationBatches;
    }
}

/// <summary>팩 한 개 또는 연속된 직접 지급 카드 묶음. PackId가 없으면 직접 지급이다.</summary>
public readonly struct RewardPresentationBatch
{
    public readonly string PackId;
    public readonly IReadOnlyList<DrawnCard> Cards;
    public bool IsPack => !string.IsNullOrEmpty(PackId);

    public RewardPresentationBatch(IReadOnlyList<DrawnCard> _cards, string _packId = null)
    {
        Cards = _cards;
        PackId = _packId;
    }
}

/// <summary>서버에서 지급이 끝난 팩 한 개의 개봉 결과. 화면에서 다시 추첨하지 않는다.</summary>
public sealed class GrantedRewardPack
{
    public string PackId;
    public List<DrawnCard> Cards;
}
