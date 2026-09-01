using Newtonsoft.Json;

/// <summary>verifyMatchTicket 응답. 서명이 깨졌거나 만료면 <see cref="Valid"/>가 false 이고
/// 그때는 티어를 모르는 상대로 다룬다(지어내지 않는다).</summary>
internal sealed class MatchTicketVerifyResult
{
    [JsonProperty("valid")]     public bool   Valid     { get; set; }
    [JsonProperty("uid")]       public string Uid       { get; set; }
    [JsonProperty("tierIndex")] public int    TierIndex { get; set; }
    [JsonProperty("reason")]    public string Reason    { get; set; }
}
