using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>서버가 계산한 현재 미션 진행 상태. 기간 키와 리셋 시각도 서버 값만 신뢰한다.</summary>
internal sealed class MissionSnapshot
{
    [JsonProperty("dailyKey")] public string DailyKey { get; set; }
    [JsonProperty("weeklyKey")] public string WeeklyKey { get; set; }
    [JsonProperty("progress")] public Dictionary<string, long> Progress { get; set; }
    [JsonProperty("claimed")] public Dictionary<string, bool> Claimed { get; set; }
    [JsonProperty("passExp")] public long PassExp { get; set; }
    [JsonProperty("dailyResetAtMs")] public long DailyResetAtMs { get; set; }
    [JsonProperty("weeklyResetAtMs")] public long WeeklyResetAtMs { get; set; }
}

/// <summary>표시와 완료 판정에 필요한 미션 정의. 클라이언트에 별도 정의 사본을 두지 않는다.</summary>
internal sealed class MissionDefinition
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("period")] public string Period { get; set; }
    [JsonProperty("event")] public string Event { get; set; }
    [JsonProperty("target")] public long Target { get; set; }
    [JsonProperty("title")] public string Title { get; set; }
    [JsonProperty("description")] public string Description { get; set; }
    [JsonProperty("sortOrder")] public int SortOrder { get; set; }
    [JsonProperty("reward")] public MissionReward Reward { get; set; }
}

internal sealed class MissionReward
{
    [JsonProperty("items")] public List<ClaimRewardItem> Items { get; set; }
    [JsonProperty("currencies")] public List<ClaimRewardGain> Currencies { get; set; }
    [JsonProperty("passExp")] public long PassExp { get; set; }
}

internal sealed class MissionGetResponse
{
    [JsonProperty("missions")] public MissionSnapshot Missions { get; set; }
    [JsonProperty("definitions")] public List<MissionDefinition> Definitions { get; set; }
}

internal sealed class ClaimMissionResult : ServerCommandResult
{
    [JsonProperty("pass")] public PassProgress Pass { get; set; }
    [JsonProperty("packs")] public List<ClaimRewardPack> Packs { get; set; }
    [JsonProperty("cards")] public List<OpenPackCard> Cards { get; set; }
    [JsonProperty("missionId")] public string MissionId { get; set; }
    [JsonProperty("granted")] public List<ClaimRewardGain> Granted { get; set; }
    [JsonProperty("grantedPassExp")] public long GrantedPassExp { get; set; }
}
