using System;

// 쓸 수 없는 닉네임을 되돌려 보냈음을 알리는 단일 창구(NetworkFailurePopup 과 같은 관용구).
// 어느 낱말이 걸렸는지는 알리지 않는다 — 사전을 되짚어 우회하는 길을 열어 주지 않으려는 것이다.
public static class NicknameRejectNotice
{
    /// <summary>이름이 거절되었음을 알린다. <paramref name="_onClosed"/>는 어느 버튼으로 닫든 한 번 불리므로
    /// 입력칸을 되살리는 자리로 쓴다.</summary>
    public static void Show(Action _onClosed = null)
    {
        SimpleYNPopup t_popup = UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "사용할 수 없는 이름입니다.\n다른 이름을 지어 주세요.",
            yesText   = "확인",
            noText    = "닫기",
            onHide    = _onClosed,
        });

        // 판을 세우지 못했으면(풀 미기동·프리팹 누락) onHide가 영영 오지 않는다. 입력칸을 되살리는 일이
        // 그 통지에 매여 있으므로, 안내는 못 하더라도 편집 자세만은 그 자리에서 풀어 준다.
        if (t_popup == null) _onClosed?.Invoke();
    }
}
