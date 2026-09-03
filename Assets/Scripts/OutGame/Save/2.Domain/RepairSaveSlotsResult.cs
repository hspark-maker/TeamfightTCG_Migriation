using Newtonsoft.Json;

// repairSaveSlots 응답. EnsureAccountResult와 같은 이유로 ServerCommandResult를 상속하지 않는다 —
// 초기화 게이트 전이라 슬롯 채택 계약 밖이고, 클라는 이 응답 대신 문서를 다시 읽어 합류한다.
internal sealed class RepairSaveSlotsResult
{
    /// <summary>이 호출이 문서를 고쳤는가. false면 고칠 것이 없어 아무것도 쓰지 않았다.</summary>
    [JsonProperty("repaired")] public bool Repaired { get; set; }

    /// <summary>옮긴 슬롯("옛이름-&gt;새이름"). 로그로만 쓴다.</summary>
    [JsonProperty("renamed")] public string[] Renamed { get; set; }

    /// <summary>비어 있어 세워 준 슬롯. 로그로만 쓴다.</summary>
    [JsonProperty("filled")] public string[] Filled { get; set; }
}
