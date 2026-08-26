// 원격 적용·충돌 보존의 결과. async에서는 Try*+out을 못 쓰므로 실패 사유를 값으로 돌려준다.
public readonly struct SaveApplyReport
{
    public bool Success { get; }
    public string Error { get; }

    SaveApplyReport(bool _success, string _error)
    {
        Success = _success;
        Error   = _error ?? "";
    }

    public static SaveApplyReport Ok() => new SaveApplyReport(true, "");

    public static SaveApplyReport Fail(string _error) => new SaveApplyReport(false, _error);
}
