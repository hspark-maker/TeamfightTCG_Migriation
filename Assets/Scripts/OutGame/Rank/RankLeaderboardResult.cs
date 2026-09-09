using Newtonsoft.Json;

internal sealed class RankLeaderboardResult
{
    [JsonProperty("season")] public RankLeaderboardSeason Season { get; set; }
    [JsonProperty("entries")] public RankLeaderboardEntry[] Entries { get; set; }
    [JsonProperty("self")] public RankLeaderboardEntry Self { get; set; }
}

internal sealed class RankLeaderboardSeason
{
    [JsonProperty("seasonId")] public string SeasonId { get; set; }
    [JsonProperty("endAtMs")] public long EndAtMs { get; set; }
}

internal sealed class RankLeaderboardEntry
{
    [JsonProperty("rank")] public int Rank { get; set; }
    [JsonProperty("points")] public long Points { get; set; }
    [JsonProperty("nickname")] public string Nickname { get; set; }
    [JsonProperty("avatarId")] public string AvatarId { get; set; }
    [JsonProperty("frameId")] public string FrameId { get; set; }
    [JsonProperty("isSelf")] public bool IsSelf { get; set; }
}
