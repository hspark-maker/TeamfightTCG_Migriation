using Cysharp.Threading.Tasks;

// 제출 거절과 매치 무효 확정을 구분한다. 과거 제출의 실패도 이 창구로 도착할 수 있다.
public static class MatchResultFailurePopup
{
    public static void Show(bool _voided = false) => ShowWhenReadyAsync(_voided).Forget();

    static async UniTaskVoid ShowWhenReadyAsync(bool _voided)
    {
        // 검증 응답이 복귀 로딩 중 도착하면 커버 아래에 띄우지 않는다. 전투 정리가 풀을 비운 뒤 알린다.
        await UniTask.WaitUntil(() => !LoadingCoverView.IsCovering);
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = _voided
                ? "서버에서 전투 결과를 무효 처리했습니다.\n해당 전투의 보상과 랭크는 반영되지 않습니다."
                : "서버에서 전투 결과 제출을 거절했습니다.\n이미 확정된 보상과 랭크는 정상 반영됩니다.",
            yesText   = "확인",
            noText    = "닫기",
        });
    }
}
