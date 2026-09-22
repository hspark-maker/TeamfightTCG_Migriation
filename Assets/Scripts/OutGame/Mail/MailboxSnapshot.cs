using System.Collections.Generic;
using Newtonsoft.Json;

internal sealed class MailboxEntry
{
    [JsonProperty("mailId")] public string MailId;
    [JsonProperty("title")] public string Title;
    [JsonProperty("body")] public string Body;
    [JsonProperty("rewards")] public MailboxRewards Rewards;
    [JsonProperty("createdAtMs")] public long CreatedAtMs;
    [JsonProperty("expiresAtMs")] public long ExpiresAtMs;
    [JsonProperty("claimedAtMs")] public long? ClaimedAtMs;
    [JsonProperty("state")] public string State;
}

internal sealed class MailboxRewards
{
    [JsonProperty("currencies")] public List<ClaimRewardGain> Currencies;
    [JsonProperty("items")] public List<ClaimRewardItem> Items;
}

internal sealed class MailboxCursor
{
    [JsonProperty("createdAtMs")] public long CreatedAtMs;
    [JsonProperty("mailId")] public string MailId;
}

internal sealed class MailboxResponse
{
    [JsonProperty("mails")] public List<MailboxEntry> Mails;
    [JsonProperty("nextCursor")] public MailboxCursor NextCursor;
    [JsonProperty("hasClaimable")] public bool HasClaimable;
    [JsonProperty("serverNowMs")] public long ServerNowMs;
}

internal sealed class ClaimMailResult : ServerCommandResult
{
    [JsonProperty("claimedMailIds")] public List<string> ClaimedMailIds;
    [JsonProperty("granted")] public List<ClaimRewardGain> Granted;
    [JsonProperty("cards")] public List<OpenPackCard> Cards;
    [JsonProperty("packs")] public List<ClaimRewardPack> Packs;
    [JsonProperty("hasMore")] public bool HasMore;
}
