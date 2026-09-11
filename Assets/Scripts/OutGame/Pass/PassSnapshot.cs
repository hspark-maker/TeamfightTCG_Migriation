using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>서버가 확정한 시즌 정의. 기간·최대 레벨의 진실원은 PassSeason 표다.</summary>
internal sealed class PassSeasonDefinition
{
    [JsonProperty("seasonId")] public string SeasonId { get; set; }

    [JsonProperty("displayName")] public string DisplayName { get; set; }

    [JsonProperty("startAtMs")] public long StartAtMs { get; set; }

    [JsonProperty("endAtMs")] public long EndAtMs { get; set; }

    [JsonProperty("maxLevel")] public int MaxLevel { get; set; }
}

/// <summary>레벨 하나의 문턱과 무료 트랙 보상. requiredExp 는 누적 총량이다.</summary>
internal sealed class PassLevelDefinition
{
    [JsonProperty("premiumReward")] public List<ClaimRewardGain> PremiumReward { get; set; }
    [JsonProperty("premiumItems")] public List<ClaimRewardItem> PremiumItems { get; set; }
    [JsonProperty("items")] public List<ClaimRewardItem> Items { get; set; }
    [JsonProperty("level")] public int Level { get; set; }

    [JsonProperty("requiredExp")] public long RequiredExp { get; set; }

    [JsonProperty("reward")] public List<ClaimRewardGain> Reward { get; set; }
}

/// <summary>이 유저의 시즌 진행 상태. 시즌이 바뀌면 서버가 exp·수령 낙인을 갈아 준다.</summary>
internal sealed class PassProgress
{
    [JsonProperty("premiumUnlocked")] public bool PremiumUnlocked { get; set; }
    [JsonProperty("premiumClaimed")] public Dictionary<string, bool> PremiumClaimed { get; set; }
    [JsonProperty("repeatClaimed")] public long RepeatClaimed { get; set; }
    [JsonProperty("seasonId")] public string SeasonId { get; set; }

    [JsonProperty("exp")] public long Exp { get; set; }

    [JsonProperty("claimed")] public Dictionary<string, bool> Claimed { get; set; }
}

/// <summary>getPass 응답. 화면이 그릴 것을 한 번에 담는다 — 추가 왕복이 없다.</summary>
internal sealed class PassGetResponse
{
    [JsonProperty("repeat")] public PassRepeatDefinition Repeat { get; set; }
    [JsonProperty("packChoices")] public List<string> PackChoices { get; set; }
    [JsonProperty("season")] public PassSeasonDefinition Season { get; set; }

    [JsonProperty("progress")] public PassProgress Progress { get; set; }

    [JsonProperty("currentLevel")] public int CurrentLevel { get; set; }

    [JsonProperty("nextRequiredExp")] public long? NextRequiredExp { get; set; }

    [JsonProperty("levels")] public List<PassLevelDefinition> Levels { get; set; }
}

internal sealed class PassRepeatDefinition
{
    [JsonProperty("requiredExp")] public long RequiredExp { get; set; }
    [JsonProperty("reward")] public List<ClaimRewardGain> Reward { get; set; }
    [JsonProperty("availableClaims")] public long AvailableClaims { get; set; }
    [JsonProperty("claimedCount")] public long ClaimedCount { get; set; }
    [JsonProperty("progressExp")] public long ProgressExp { get; set; }
}

internal sealed class ClaimPassRepeatRewardResult : ServerCommandResult
{
    [JsonProperty("granted")] public List<ClaimRewardGain> Granted { get; set; }
    [JsonProperty("progress")] public PassProgress Progress { get; set; }
    [JsonProperty("repeat")] public PassRepeatDefinition Repeat { get; set; }
}

/// <summary>claimPassReward 응답. 지갑·세이브 채택은 공통 배관이 하고 여기 값은 표시용이다.</summary>
internal sealed class ClaimPassRewardResult : ServerCommandResult
{
    [JsonProperty("track")] public string Track { get; set; }
    [JsonProperty("packs")] public List<ClaimRewardPack> Packs { get; set; }
    [JsonProperty("cards")] public List<OpenPackCard> Cards { get; set; }
    [JsonProperty("seasonId")] public string SeasonId { get; set; }

    [JsonProperty("level")] public int Level { get; set; }

    [JsonProperty("granted")] public List<ClaimRewardGain> Granted { get; set; }

    [JsonProperty("progress")] public PassProgress Progress { get; set; }
}
