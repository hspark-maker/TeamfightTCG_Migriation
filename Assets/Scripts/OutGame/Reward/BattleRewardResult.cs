using Newtonsoft.Json;

// 서버 claimBattleReward 응답. 전투 보상은 언제나 한 줄이라 granted 도 한 건이다
// (잔액 자체는 updatedSlots 의 currency 슬롯이 갈아끼운다).
internal sealed class BattleRewardResult : ServerCommandResult
{
    [JsonProperty("granted")] public ClaimRewardGain Granted { get; set; }
}
