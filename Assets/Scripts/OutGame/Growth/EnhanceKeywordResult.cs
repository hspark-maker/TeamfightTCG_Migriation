using Newtonsoft.Json;

// 서버 enhanceKeyword 응답. 카드 강화와 달리 확률 실패가 없어 outcome 은 언제나 "Success" 다
// (레벨과 잔액 자체는 updatedSlots 의 keywordGrowth·currency 슬롯이 갈아끼운다).
internal sealed class EnhanceKeywordResult : ServerCommandResult
{
    // EEnhanceOutcome 이름 문자열. 모양을 카드 강화와 맞춰 두어야 응답 해석이 한 갈래로 유지된다.
    [JsonProperty("outcome")] public string Outcome { get; set; }

    [JsonProperty("level")] public int Level { get; set; }

    // ECurrencyType 이름 문자열. 차감은 서버가 이미 했고 이 값은 로그·대조용이다.
    [JsonProperty("currency")] public string Currency { get; set; }

    [JsonProperty("cost")] public int Cost { get; set; }

    [JsonProperty("freeShotUsed")] public bool FreeShotUsed { get; set; }

    /// <summary>성패를 푼다. 모르는 표기는 성공으로 읽는다 — 여기 왔다는 것은 결제가 끝났다는 뜻이다.</summary>
    public EEnhanceOutcome ResolveOutcome()
        => string.Equals(Outcome, "Failed", System.StringComparison.OrdinalIgnoreCase)
            ? EEnhanceOutcome.Failed
            : EEnhanceOutcome.Success;
}
