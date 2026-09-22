using Newtonsoft.Json;

/// <summary>서버가 확정한 칭호 지급 결과.</summary>
public sealed class GrantedTitle
{
    [JsonProperty("titleId")] public string TitleId { get; set; }
    [JsonProperty("isNew")] public bool IsNew { get; set; }
}
