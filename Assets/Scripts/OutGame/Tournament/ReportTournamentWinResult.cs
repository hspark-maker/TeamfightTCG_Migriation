using Newtonsoft.Json;

// reportTournamentWin 응답. 서버가 낙인을 세운 정점을 그대로 되돌려준다.
internal sealed class ReportTournamentWinResult : ServerCommandResult
{
    /// <summary>서버가 미수령으로 낙인한 정점. null 이면 서버가 이 값을 싣지 않았다.</summary>
    [JsonProperty("nodeId")] public string NodeId { get; set; }
}
