// 전투 결과가 서버 정산까지 가지 못했음을 알리는 단일 창구(NetworkFailurePopup·PackPurchaseFailurePopup 관용구).
// 결과 팝업은 이미 보상을 보여 준 뒤라, 아무 말 없이 큐만 비우면 유저에게는 보상이 증발한 것으로 보인다.
// 망 문제와 갈라 두는 이유는 원인의 자리가 다르기 때문이다 — 이쪽은 다시 연결해도 이 판은 돌아오지 않는다.
// 재시도 버튼은 두지 않는다. 서버가 이미 무효로 확정했거나(무효 처리) 같은 답이 반복되는 거절이라 다시 보낼 것이 없다.
public static class MatchResultFailurePopup
{
    /// <summary>이번 판이 정산되지 않았음을 알린다. 서버 사유 코드는 문구에 넣지 않는다 —
    /// 유저가 할 수 있는 일이 없어 화면에는 결과 한 줄만 남기고, 사유는 호출부가 로그로 남긴다.</summary>
    public static void Show()
    {
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "전투 결과를 서버가 확정하지 못했습니다.\n이번 판의 보상과 랭크는 반영되지 않습니다.",
            yesText   = "확인",
            noText    = "닫기",
        });
    }
}
