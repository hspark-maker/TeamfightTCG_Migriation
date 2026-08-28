using System.Collections.Generic;
using Newtonsoft.Json;

// 서버 openPack 응답. 무엇이 뽑혔는지·중복 보상이 무엇인지의 진실원이다.
internal sealed class OpenPackResult : ServerCommandResult
{
    [JsonProperty("packId")] public string PackId { get; set; }

    [JsonProperty("cards")] public List<OpenPackCard> Cards { get; set; }

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

    [JsonProperty("snack")] public int Snack { get; set; }
}
