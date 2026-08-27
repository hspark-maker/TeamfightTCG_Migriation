using Newtonsoft.Json;

// ensureAccount 응답. ServerCommandResult를 상속하지 않는다 — 부트 게이트 전이라 슬롯 채택 계약 밖이고,
// 클라는 이 응답 대신 문서를 다시 읽어 정상 부트 경로로 합류한다.
internal sealed class EnsureAccountResult
{
    /// <summary>확보된 문서의 revision. 방금 만들었으면 1이다.</summary>
    [JsonProperty("revision")] public long Revision { get; set; }

    /// <summary>이 호출이 문서를 만들었는가. false면 이미 있어 아무것도 쓰지 않았다.</summary>
    [JsonProperty("created")] public bool Created { get; set; }

    /// <summary>스타터 카드의 출처(spec / fallback / specError). 지급이 어긋났을 때 갈래를 가른다.</summary>
    [JsonProperty("starterSource")] public string StarterSource { get; set; }
}
