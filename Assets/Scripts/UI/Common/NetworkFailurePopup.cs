// 망 문제로 요청이 끝나지 못했음을 알리는 전역 단일 창구(SimpleYNPopup 관용구).
// 기능별 실패 팝업과 갈라 두는 이유는 원인의 자리가 다르기 때문이다 — 재화·잠금은 유저가 스스로 푸는 조건이지만
// 연결은 그렇지 않다. 한 문구로 뭉치면 유저가 자기 계정 사정으로 오독한다.
// 재시도 버튼은 두지 않는다 — 이 팝업은 무엇이 실패했는지만 알 뿐, 무엇을 다시 태워야 하는지는 모른다.
public static class NetworkFailurePopup
{
    /// <summary>연결 문제 안내를 띄운다. 무엇이 끝나지 못했는지(<paramref name="_whatFailed"/>)를 넘기면
    /// 그 한 줄이 앞머리에 붙는다 — 비우면 연결 안내만 남는다.</summary>
    public static void Show(string _whatFailed = null)
    {
        string t_message = string.IsNullOrEmpty(_whatFailed)
            ? "네트워크 연결이 원활하지 않습니다.\n잠시 후 다시 시도해 주세요."
            : $"{_whatFailed}\n네트워크 연결을 확인한 뒤 다시 시도해 주세요.";

        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = t_message,
            yesText   = "확인",
            noText    = "닫기",
        });
    }
}
