using Newtonsoft.Json;

// 세이브·지갑을 쓰는 callable 응답의 공통 머리. R5~R8의 도메인 응답이 이걸 상속해 자기 필드를 덧붙이는 확장점이다.
//
// 두 문서는 따로 채택된다 — 지갑만 쓰는 명령(claimBattleReward·devGrantCurrency·claimPayout ack)은
// revision·updatedSlots 키를 아예 싣지 않는다.
internal class ServerCommandResult
{
    /// <summary>0/누락은 <b>이 명령이 세이브 문서를 쓰지 않았다</b>는 센티널이다.
    /// 세이브는 ensureSaveDocument가 revision 1로 만들고 증가만 하므로 0은 다른 뜻을 가질 수 없다.</summary>
    [JsonProperty("revision")] public long Revision { get; set; }

    [JsonProperty("updatedSlots")] public ServerSlotPatch UpdatedSlots { get; set; }

    /// <summary>null이면 이 명령은 지갑을 쓰지 않았다.</summary>
    [JsonProperty("wallet")] public WalletPatch Wallet { get; set; }
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

    // 서버 상수가 아니라 문서에 실제로 적힌 값이다. 문서를 읽지 못했으면 null이라 nullable이어야 한다.
    // SchemaVersion과 어긋나면 쓰기 callable이 전부 거부되므로, 쓰기 전에 드리프트를 보는 자리가 여기다.
    [JsonProperty("documentSchemaVersion")] public long? DocumentSchemaVersion { get; set; }

    [JsonProperty("exists")] public bool Exists { get; set; }

    [JsonProperty("revision")] public long Revision { get; set; }

    [JsonProperty("readError")] public string ReadError { get; set; }
}
