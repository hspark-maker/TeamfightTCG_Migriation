using System.Collections.Generic;
using Newtonsoft.Json;

// 서버가 돌려주는 지갑 한 벌(callable 응답 DTO). 세이브 슬롯이 아니라 별도 문서라 [FirestoreData]를 붙이지 않는다.
//
// 불변식: null == 이 명령은 지갑을 건드리지 않았다. 그래서 프로퍼티 이니셜라이저를 두지 않는다.
internal sealed class WalletPatch
{
    // 단조 증가만 보장된다 — 세이브 revision과 달리 "정확히 +1"은 계약이 아니다(클라가 모르는 정당한 쓰기가 있다).
    [JsonProperty("rev")] public long Rev { get; set; }

    // 항상 4키(Gold·Diamond·Energy·Shard). 빠진 키는 채택이 0으로 읽는다.
    [JsonProperty("balances")] public Dictionary<string, long> Balances { get; set; }
}
