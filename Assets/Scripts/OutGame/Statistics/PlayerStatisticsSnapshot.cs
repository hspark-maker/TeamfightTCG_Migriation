using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>서버 통계의 읽기 전용 사본. 기존 누적과 동일 기간의 전적을 구분한다.</summary>
internal sealed class PlayerStatisticsSnapshot
{
    [JsonProperty("revision")] public long Revision { get; set; }
    [JsonProperty("achievementRevision")] public long AchievementRevision { get; set; }
    [JsonProperty("trackedSinceMs")] public long TrackedSinceMs { get; set; }
    [JsonProperty("lifetime")] public PlayerLifetimeStatistics Lifetime { get; set; }
    [JsonProperty("battle")] public PlayerBattleStatisticsGroups Battle { get; set; }
}

internal sealed class PlayerLifetimeStatistics
{
    [JsonProperty("wins")] public long Wins { get; set; }
    [JsonProperty("cardsDestroyed")] public long CardsDestroyed { get; set; }
    [JsonProperty("currentWinStreak")] public long CurrentWinStreak { get; set; }
    [JsonProperty("bestWinStreak")] public long BestWinStreak { get; set; }
    [JsonProperty("packsOpened")] public long PacksOpened { get; set; }
    [JsonProperty("albumsCompleted")] public long AlbumsCompleted { get; set; }
    [JsonProperty("synergyPlays")] public Dictionary<string, long> SynergyPlays { get; set; }
}

internal sealed class PlayerBattleStatisticsGroups
{
    [JsonProperty("all")] public PlayerBattleStatistics All { get; set; }
    [JsonProperty("ranked")] public PlayerBattleStatistics Ranked { get; set; }
    [JsonProperty("adventure")] public PlayerBattleStatistics Adventure { get; set; }
}

internal sealed class PlayerBattleStatistics
{
    [JsonProperty("battles")] public long Battles { get; set; }
    [JsonProperty("wins")] public long Wins { get; set; }
    [JsonProperty("losses")] public long Losses { get; set; }
    [JsonProperty("draws")] public long Draws { get; set; }
    [JsonProperty("cardsDestroyed")] public long CardsDestroyed { get; set; }
    [JsonProperty("attacks")] public long Attacks { get; set; }
    [JsonProperty("damageDealt")] public long DamageDealt { get; set; }
    [JsonProperty("healed")] public long Healed { get; set; }
    [JsonProperty("synergyTriggers")] public long SynergyTriggers { get; set; }
    [JsonProperty("currentWinStreak")] public long CurrentWinStreak { get; set; }
    [JsonProperty("bestWinStreak")] public long BestWinStreak { get; set; }

    // 과거 업적 승리를 분자로 쓰지 않는다. 분자·분모 모두 trackedSinceMs 이후의 전적이다.
    [JsonIgnore] public double? WinRatePercent => Battles > 0 ? 100d * Wins / Battles : (double?)null;
}

internal sealed class PlayerStatisticsGetResponse
{
    [JsonProperty("statistics")] public PlayerStatisticsSnapshot Statistics { get; set; }
    [JsonProperty("achievements")] public AchievementSnapshot Achievements { get; set; }
}
