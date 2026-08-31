using System.Collections.Generic;
using Newtonsoft.Json;

// 서버 claimReward 응답. 무엇이 지급됐는지의 진실원이다(잔액 자체는 updatedSlots 의 currency 슬롯이 갈아끼운다).
internal sealed class ClaimRewardResult : ServerCommandResult
{
    [JsonProperty("granted")] public List<ClaimRewardGain> Granted { get; set; }
}

// 서버가 지급한 재화 한 줄. 배열 순서가 곧 스펙시트 order 순서다.
internal sealed class ClaimRewardGain
{
    // ECurrencyType 이름 문자열. 못 읽는 표기는 서버가 그 줄을 아예 버리므로 여기 오지 않는다.
    [JsonProperty("currency")] public string Currency { get; set; }

    [JsonProperty("amount")] public long Amount { get; set; }
}
