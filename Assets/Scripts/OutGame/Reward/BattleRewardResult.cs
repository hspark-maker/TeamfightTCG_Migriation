using Newtonsoft.Json;

// 서버 claimBattleReward 응답. 전투 보상은 언제나 한 줄이라 granted 도 한 건이다
// (잔액 자체는 응답의 wallet 이 갈아끼운다 — 이 명령은 세이브를 쓰지 않아 revision·updatedSlots 가 없다).
internal sealed class BattleRewardResult : ServerCommandResult
{
    [JsonProperty("granted")] public ClaimRewardGain Granted { get; set; }
}
