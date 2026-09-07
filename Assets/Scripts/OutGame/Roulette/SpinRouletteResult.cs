using Newtonsoft.Json;

// 서버 spinRoulette 응답. 멈출 칸과 지급 한 줄의 진실원이다
// (잔액 자체는 응답의 wallet 이 갈아끼운다 — 이 명령은 세이브를 쓰지 않아 revision·updatedSlots 가 없다).
internal sealed class SpinRouletteResult : ServerCommandResult
{
    // 요청한 판과 같은지 대조하는 용도다. 어긋나도 지갑은 이미 움직였으므로 실패로 접지 않는다.
    [JsonProperty("rouletteId")] public string RouletteId { get; set; }

    [JsonProperty("slotIndex")] public int SlotIndex { get; set; }

    [JsonProperty("gain")] public ClaimRewardGain Gain { get; set; }
}
