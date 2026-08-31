using System.Collections.Generic;
using Newtonsoft.Json;

// claimPayout(action=list) 응답. 아무 문서도 쓰지 않으므로 채택 계약(ServerCommandResult) 밖이다.
internal sealed class PayoutListResult
{
    [JsonProperty("payouts")] public List<PayoutEntry> Payouts { get; set; }
}

// 서버가 확정해 둔 지급 한 건. 액수·랭크 전이는 매치 확정 시점에 이미 적힌 값이다(여기서 다시 계산하지 않는다).
internal sealed class PayoutEntry
{
    [JsonProperty("matchId")] public string MatchId { get; set; }

    [JsonProperty("currency")] public PayoutCurrencyLine Currency { get; set; }

    [JsonProperty("rank")] public PayoutRankLine Rank { get; set; }

    [JsonProperty("rankSequence")] public long RankSequence { get; set; }

    [JsonProperty("settledAtMs")] public long SettledAtMs { get; set; }
}

internal sealed class PayoutCurrencyLine
{
    // ECurrencyType 이름 문자열. 못 읽는 표기는 그 줄만 건너뛴다.
    [JsonProperty("currency")] public string Currency { get; set; }

    [JsonProperty("amount")] public long Amount { get; set; }
}

internal sealed class PayoutRankLine
{
    [JsonProperty("before")] public long Before { get; set; }

    [JsonProperty("after")] public long After { get; set; }
}

// claimPayout(action=ack) 응답. 잔액 크레딧이 여기서 나므로 wallet 을 싣는다 — 세이브는 쓰지 않아 revision 이 없다.
internal sealed class PayoutAckResult : ServerCommandResult
{
    [JsonProperty("acked")] public List<string> Acked { get; set; }
}
