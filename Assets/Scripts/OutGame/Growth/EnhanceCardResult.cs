using Newtonsoft.Json;

// 서버 enhanceCard 응답. 성패·시도 후 레벨·실제 차감액의 진실원이다
// (레벨과 잔액 자체는 updatedSlots 의 cardGrowth·currency 슬롯이 갈아끼운다).
internal sealed class EnhanceCardResult : ServerCommandResult
{
    // EEnhanceOutcome 이름 문자열("Success"|"Failed"). 확률 실패도 결제는 끝난 것이다.
    [JsonProperty("outcome")] public string Outcome { get; set; }

    [JsonProperty("level")] public int Level { get; set; }

    // ECurrencyType 이름 문자열. 차감은 서버가 이미 했고 이 값은 로그·대조용이다.
    [JsonProperty("currency")] public string Currency { get; set; }

    [JsonProperty("cost")] public int Cost { get; set; }

    [JsonProperty("freeShotUsed")] public bool FreeShotUsed { get; set; }

    /// <summary>성패를 푼다. 모르는 표기는 성공으로 읽는다 — 여기 왔다는 것은 결제가 끝났다는 뜻이고
    /// 보여줄 레벨은 응답이 들고 왔다.</summary>
    public EEnhanceOutcome ResolveOutcome()
        => string.Equals(Outcome, "Failed", System.StringComparison.OrdinalIgnoreCase)
            ? EEnhanceOutcome.Failed
            : EEnhanceOutcome.Success;
}
