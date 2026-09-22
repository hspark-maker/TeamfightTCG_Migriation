using Newtonsoft.Json;

/// <summary>서버가 확정한 영구 외형 아이템 지급 결과.</summary>
public sealed class GrantedCosmetic
{
    [JsonProperty("itemType")] public string ItemType { get; set; }
    [JsonProperty("itemId")] public string ItemId { get; set; }
    [JsonProperty("isNew")] public bool IsNew { get; set; }
}
