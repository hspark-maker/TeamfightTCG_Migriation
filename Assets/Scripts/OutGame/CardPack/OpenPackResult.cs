using System.Collections.Generic;
using Newtonsoft.Json;

// 서버 openPack 응답. 무엇이 뽑혔는지·중복 보상이 무엇인지의 진실원이다.
internal sealed class OpenPackResult : ServerCommandResult
{
    [JsonProperty("packId")] public string PackId { get; set; }

    [JsonProperty("cards")] public List<OpenPackCard> Cards { get; set; }

    [JsonProperty("granted")] public List<ClaimRewardGain> Granted { get; set; }

    // 지급량은 서버 응답만 읽는다. 중복 카드 수나 현재 잔액으로 추정하지 않는다.
    public CurrencyGain ResolveCardDustGain()
    {
        long t_amount = 0;
        if (Granted != null)
        {
            foreach (var t_gain in Granted)
            {
                if (t_gain == null || t_gain.Amount <= 0) continue;
                if (CurrencyCode.TryParse(t_gain.Currency, out var t_type) && t_type == ECurrencyType.CardDust)
                    t_amount += t_gain.Amount;
            }
        }
        return new CurrencyGain(ECurrencyType.CardDust, t_amount);
    }

    // ECurrencyType 이름 문자열. 미지 표기는 팩 가격 규약을 따라 Gold로 떨어진다.
    [JsonProperty("refundType")] public string RefundType { get; set; }

    /// <summary>환급 재화 종류를 푼다. 파싱 실패 시 Gold.</summary>
    public ECurrencyType ResolveRefundType()
        => CurrencyCode.TryParse(RefundType, out ECurrencyType t_type) ? t_type : ECurrencyType.Gold;
}

// 서버가 뽑아 준 카드 1장.
internal sealed class OpenPackCard
{
    [JsonProperty("cardId")] public int CardId { get; set; }

    [JsonProperty("isNew")] public bool IsNew { get; set; }

}
