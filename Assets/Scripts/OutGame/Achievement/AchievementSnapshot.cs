using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>기간 초기화가 없는 서버 업적 상태. 세이브 revision과 독립적으로 증가한다.</summary>
internal sealed class AchievementSnapshot
{
    [JsonProperty("revision")] public long Revision { get; set; }
    [JsonProperty("progress")] public Dictionary<string, long> Progress { get; set; }
    [JsonProperty("claimed")] public Dictionary<string, bool> Claimed { get; set; }
}

internal sealed class AchievementDefinition
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("groupId")] public string GroupId { get; set; }
    [JsonProperty("stage")] public int Stage { get; set; }
    [JsonProperty("event")] public string Event { get; set; }
    [JsonProperty("synergyId")] public string SynergyId { get; set; }
    [JsonProperty("target")] public long Target { get; set; }
    [JsonProperty("title")] public string Title { get; set; }
    [JsonProperty("description")] public string Description { get; set; }
    [JsonProperty("sortOrder")] public int SortOrder { get; set; }
    [JsonProperty("reward")] public AchievementReward Reward { get; set; }
}

internal sealed class AchievementReward
{
    [JsonProperty("currencies")] public List<ClaimRewardGain> Currencies { get; set; }
    [JsonProperty("items")] public List<ClaimRewardItem> Items { get; set; }
}

internal sealed class AchievementGetResponse
{
    [JsonProperty("statistics")] public PlayerStatisticsSnapshot Statistics { get; set; }
    [JsonProperty("achievements")] public AchievementSnapshot Achievements { get; set; }
    [JsonProperty("definitions")] public List<AchievementDefinition> Definitions { get; set; }
}

internal sealed class ClaimAchievementResult : ServerCommandResult
{
    [JsonProperty("achievementId")] public string AchievementId { get; set; }
    [JsonProperty("granted")] public List<ClaimRewardGain> Granted { get; set; }
}
