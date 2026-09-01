using Cysharp.Threading.Tasks;
using UnityEngine;

// 카드팩 구매 왕복의 단일 진입점. 구매를 시작하는 자리가 셋(상점 진열·재개봉·튜토리얼 자동구매)이라
// 대기 표시 · 왕복 · 거절 안내의 순서를 여기 한 곳에 모은다 — 흩어 두면 진입점마다 순서가 갈린다.
// "지금 결제가 도는가"의 진실원도 여기다: 진입점마다 세면 그중 하나가 어긋나 같은 결제가 두 번 나간다.
public static class PackPurchaseFlow
{
    static int s_inFlight;

    /// <summary>지금 구매 왕복이 도는 중인가. 굳은 잠금을 푸는 복구 장치(진열의 OnEnable 등)가
    /// 진행 중인 결제의 잠금까지 지우지 않도록 묻는 자리다.</summary>
    public static bool IsPurchasing => s_inFlight > 0;

    /// <summary>대기 표시를 켠 채 구매 왕복을 태우고, 실패는 그 자리에서 안내한 뒤 null 을 돌려준다.</summary>
    public static async UniTask<OpenedPack> PurchaseAsync(CardPackData _pack, object _owner)
    {
        OpenedPack t_opened;

        s_inFlight++;
        ServerWaitOverlay.Hold(_owner);
        try
        {
            t_opened = await CardPackOpener.PurchaseAsync(_pack);
        }
        finally
        {
            ServerWaitOverlay.Release(_owner);
            s_inFlight--;
        }

        if (t_opened.Success) return t_opened;

        // 안내는 대기 표시를 걷은 "뒤"에 띄운다. 이 순서 하나가 유일한 보장이다 —
        // 대기 표시도 실패 팝업도 같은 풀 컨테이너에 담겨 형제 순서로만 위아래가 갈리므로(정렬 층이 갈라주지 않는다),
        // 순서를 뒤집으면 안내가 대기 화면에 묻힌다. Release는 위 finally에서 이미 끝나 있어야 한다.
        PackPurchaseFailurePopup.Show(_pack, t_opened.Result);
        return null;
    }

    // 도메인 리로드를 끈 에디터에서는 플레이를 멈춰도 static이 살아남는다 — 왕복 도중에 멈추면
    // 다음 플레이가 "결제 중"으로 시작해 상점 잠금이 영영 안 풀린다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_inFlight = 0;
    }
}
