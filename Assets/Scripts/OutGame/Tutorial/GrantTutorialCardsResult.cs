using System.Collections.Generic;
using Newtonsoft.Json;

// 튜토리얼 카드 지급(grantTutorialCards) 응답 DTO.
//
// 불변식: null == 서버가 그 목록을 싣지 않았다(ServerSlotPatch와 같은 규약).
// 그래서 프로퍼티 이니셜라이저를 두지 않는다 — = new List<int>()를 달면 "빈 목록을 받았다"와 구분이 사라져
// 호출부가 SO 폴백으로 돌아갈 자리를 잃는다.
internal sealed class GrantTutorialCardsResult : ServerCommandResult
{
    /// <summary>이 스텝이 보장하는 카드 전량(이미 갖고 있던 것 포함).</summary>
    [JsonProperty("cardIds")] public List<int> CardIds { get; set; }

    /// <summary>이번 호출로 새로 늘어난 카드만.</summary>
    [JsonProperty("granted")] public List<int> Granted { get; set; }
}
