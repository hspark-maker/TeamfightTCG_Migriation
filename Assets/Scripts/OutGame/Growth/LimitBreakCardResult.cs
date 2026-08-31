using Newtonsoft.Json;

// 서버 limitBreakCard 응답. 도달 단계·실제 차감한 간식의 진실원이다
// (단계와 간식 잔량 자체는 updatedSlots 의 cardGrowth 슬롯이 갈아끼운다).
internal sealed class LimitBreakCardResult : ServerCommandResult
{
    // 서버가 확정한 도달 단계.
    [JsonProperty("stage")] public int Stage { get; set; }

    // 이번 단계의 체력 가산분. 화면은 슬롯에서 다시 읽으므로 이 값은 로그·대조용이다.
    [JsonProperty("hpGain")] public int HpGain { get; set; }

    // 차감은 서버가 이미 했고 이 두 값도 로그·대조용이다.
    [JsonProperty("snackCost")] public int SnackCost { get; set; }

    [JsonProperty("snackLeft")] public int SnackLeft { get; set; }
}
