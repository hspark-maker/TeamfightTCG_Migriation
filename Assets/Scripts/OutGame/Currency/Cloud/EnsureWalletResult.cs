using Newtonsoft.Json;

// ensureWallet 응답. ServerCommandResult를 상속하지 않는다 — 초기화 게이트 전이라 슬롯 채택 계약 밖이고,
// 세이브 승급분(revision)은 호출부가 직접 기준선에 반영한다.
internal sealed class EnsureWalletResult
{
    /// <summary>이 호출이 지갑을 만들었는가. false면 이미 있어 아무것도 쓰지 않았다.</summary>
    [JsonProperty("created")] public bool Created { get; set; }

    /// <summary>세이브를 승급했을 때만 실린다. 0/누락은 "세이브를 쓰지 않았다"다.</summary>
    [JsonProperty("revision")] public long Revision { get; set; }

    [JsonProperty("wallet")] public WalletPatch Wallet { get; set; }
}
