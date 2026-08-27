using Newtonsoft.Json;

// 세이브를 쓰는 callable 응답의 공통 머리. R5~R8의 도메인 응답이 이걸 상속해 자기 필드를 덧붙이는 확장점이다.
internal class ServerCommandResult
{
    [JsonProperty("revision")] public long Revision { get; set; }

    [JsonProperty("updatedSlots")] public ServerSlotPatch UpdatedSlots { get; set; }
}

// 세이브를 쓰지 않는 진단용 callable(ping) 응답.
internal sealed class PingResult
{
    [JsonProperty("ok")] public bool Ok { get; set; }

    [JsonProperty("envKnown")] public bool EnvKnown { get; set; }

    [JsonProperty("uid")] public string Uid { get; set; }

    [JsonProperty("env")] public string Env { get; set; }

    [JsonProperty("database")] public string Database { get; set; }

    [JsonProperty("schemaVersion")] public long SchemaVersion { get; set; }

    [JsonProperty("exists")] public bool Exists { get; set; }

    [JsonProperty("revision")] public long Revision { get; set; }

    [JsonProperty("readError")] public string ReadError { get; set; }
}
