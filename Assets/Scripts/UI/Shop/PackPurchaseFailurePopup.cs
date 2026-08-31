// 카드팩 구매 거절을 유저에게 알리는 단일 창구(LobbyMatchLauncher 팝업 관용구).
// 진열의 낙관 검사로 걸리든 개봉 화면이 받은 서버 응답으로 걸리든 같은 문구·같은 팝업으로 모인다 —
// 갈라 두면 같은 거절이 걸린 시점에 따라 다르게 보인다.
public static class PackPurchaseFailurePopup
{
    /// <summary>거절 사유를 사용자 문구로 갈라 띄운다. 문구는 거절된 <b>그 팩</b>에서 만든다 —
    /// 진열이 그 사이 다른 팩을 가리키고 있어도 재화 이름·해금 안내가 어긋나지 않는다.</summary>
    public static void Show(CardPackData _pack, EPackOpenResult _result)
    {
        // 망 문제는 팩 사정이 아니므로 전역 창구가 받는다. 갈래를 호출부에 두면 진입점마다 각자 판정하게 되고,
        // 한 곳만 빠뜨려도 연결 문제가 "구매할 수 없습니다"로 뭉개진다 — 여기서 돌려보내면 팝업이 두 번 뜰 경로도 없다.
        if (_result == EPackOpenResult.NetworkFailed)
        {
            NetworkFailurePopup.Show("구매 결과를 확인하지 못했습니다.");
            return;
        }

        // 잔액 부족 문구는 그 팩의 결제 재화를 따라간다(팩마다 다를 수 있다).
        string t_currency = CurrencyLook.NameOf(_pack != null ? _pack.PriceType : ECurrencyType.Gold);

        string t_message = _result == EPackOpenResult.RankLocked
            ? PackUnlockRules.UnlockLabel(_pack)
            : _result == EPackOpenResult.InsufficientGold
                ? $"{t_currency}{KoreanText.Subject(t_currency)} 부족합니다."
                : "구매할 수 없습니다.";

        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = t_message,
            yesText   = "확인",
            noText    = "닫기",
        });
    }
}
