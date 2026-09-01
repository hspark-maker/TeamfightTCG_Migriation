using Newtonsoft.Json;

/// <summary>getRankSnapshot 응답. 세이브를 바꾸지 않는 읽기라 <see cref="ServerCommandResult"/>를 상속하지 않는다
/// (revision·슬롯 채택 경로를 타면 안 된다).</summary>
internal sealed class RankSnapshotResult
{
    [JsonProperty("points")]    public long Points    { get; set; }
    [JsonProperty("tierIndex")] public int  TierIndex { get; set; }
    [JsonProperty("ticket")]    public string Ticket   { get; set; }
}
