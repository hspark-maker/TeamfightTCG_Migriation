using Newtonsoft.Json;

/// <summary>getRankSnapshot 응답. 세이브를 바꾸지 않는 읽기라 <see cref="ServerCommandResult"/>를 상속하지 않는다
/// (revision·슬롯 채택 경로를 타면 안 된다).</summary>
internal class RankProgressResult
{
    [JsonProperty("points")]    public long Points    { get; set; }
    [JsonProperty("seasonId")] public string SeasonId { get; set; }
    [JsonProperty("bestTierIndex")] public int BestTierIndex { get; set; } = -1;
    [JsonProperty("claimedTierIndexes")] public int[] ClaimedTierIndexes { get; set; }
}

internal sealed class RankSnapshotResult : RankProgressResult
{
    [JsonProperty("tierIndex")] public int TierIndex { get; set; }
    [JsonProperty("ticket")] public string Ticket { get; set; }
}
