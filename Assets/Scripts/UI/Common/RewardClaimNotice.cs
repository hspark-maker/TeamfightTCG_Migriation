using UnityEngine;

// 보상 수령을 서버가 거절했음을 알리는 단일 창구(NetworkFailurePopup 과 같은 관용구).
// 발화 조건은 명시적 거절 하나뿐이다 — 통신 실패·타임아웃까지 알리면 예고와 실지급이 갈리는 정상 케이스가
// 전부 경보가 되고, 반대로 전부 침묵하면 "받은 게 사라졌다"로 읽힌다.
// 거절된 수령은 세이브에 미수령으로 남아 다시 받을 수 있으므로, 문구도 "잃었다"가 아니라 "아직 못 받았다"로 쓴다.
public static class RewardClaimNotice
{
    /// <summary>보상 수령이 거절되었음을 알린다. 무엇을 받으려 했는지(<paramref name="_whatFailed"/>)를 넘기면
    /// 그 한 줄이 앞머리에 붙는다 — 비우면 기본 안내만 남는다.</summary>
    public static void Show(string _whatFailed = null)
    {
        string t_message = string.IsNullOrEmpty(_whatFailed)
            ? "보상을 받지 못했습니다.\n보상은 그대로 남아 있으니 잠시 후 다시 받아 주세요."
            : $"{_whatFailed}\n보상은 그대로 남아 있으니 잠시 후 다시 받아 주세요.";

        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = t_message,
            yesText   = "확인",
            noText    = "닫기",
        });
    }

    // 구독을 UI 쪽에 두어 의존 방향을 지킨다 — 커맨드는 거절을 알릴 뿐, 어떤 화면이 그것을 그리는지 모른다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        RewardClaimCommand.OnRejected -= HandleClaimRejected;
        RewardClaimCommand.OnRejected += HandleClaimRejected;
    }

    static void HandleClaimRejected() => Show();
}
