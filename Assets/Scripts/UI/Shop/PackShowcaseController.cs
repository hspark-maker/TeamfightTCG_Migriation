using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// Tab_Pack의 단일 카드팩 쇼케이스 컨트롤러. 진열할 대표 팩을 인스펙터에서 직접 할당받아
// 이름·가격을 채우고, 구매 버튼 클릭 시 TryPurchase → 캐리어(PackHandoff) → CardPack 씬 전환을 수행한다.
// 이 흐름은 튜토리얼 자동 구매 스텝(OutgameTutorialRunner)이 쓰는 경로와 동일하며, 버튼 트리거로 재현한 것.
// 경계: 구매·소유·차감은 TryPurchase가 원자 영속하고, 뷰는 표시·결과 분기·전환만 담당한다.
// 진열 대상 팩(packData)과 중복 환급액(duplicateRefundGold)은 이 뷰가 직접 소유해 TryPurchase에 넘긴다
//   — 상점 SO 미개입(팩 미할당이면 구매 잠금).
public class PackShowcaseController : MonoBehaviour
{
    [SerializeField] Button buyButton;              // 구매 → 개봉 씬 전환 트리거.
    [SerializeField] TextMeshProUGUI packNameText;  // 대표 팩 표시명(옵션 — 미배선 무시).
    [SerializeField] TextMeshProUGUI priceText;     // 가격(Gold, 옵션 — 미배선 무시).
    [Tooltip("진열할 대표 팩 SO. 미할당이면 구매 잠금.")]
    [SerializeField] CardPackData packData;         // 진열·구매 대상(TryPurchase에 이 SO를 직접 넘긴다).
    [Tooltip("이 팩에서 이미 소유한 카드를 뽑았을 때(중복) 되돌려주는 Gold.")]
    [Min(0)] [SerializeField] long duplicateRefundGold = 10;

    // 전환은 1회만(같은 프레임 멀티탭 이중결제 차단). 씬을 떠나므로 리셋 불필요하나,
    // 탭 재진입·로비 복귀 시 다시 살 수 있게 OnEnable에서 해제한다.
    static bool s_transitioning;

    void OnEnable()
    {
        s_transitioning = false;
        Bind();

        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(OnBuyPressed);
            buyButton.onClick.AddListener(OnBuyPressed);
            buyButton.interactable = packData != null;   // 팩 미할당이면 구매 잠금.
        }
    }

    void OnDisable()
    {
        if (buyButton != null) buyButton.onClick.RemoveListener(OnBuyPressed);
    }

    // 대표 팩의 표시명·가격을 UI에 반영(참조는 전부 옵션).
    void Bind()
    {
        if (packNameText != null) packNameText.text = packData != null ? packData.DisplayName : string.Empty;
        if (priceText != null) priceText.text = packData != null ? $"{packData.Price:N0}" : string.Empty;
    }

    // 구매 클릭: 성공이면 캐리어에 실어 CardPack 씬으로, 실패면 사유별 팝업(전역 1회 가드).
    void OnBuyPressed()
    {
        if (s_transitioning) return;
        if (packData == null) return;

        var t_opened = CardPackOpener.TryPurchase(packData, duplicateRefundGold);
        if (t_opened != null && t_opened.Success)
        {
            s_transitioning = true;
            // 일반 구매 목적지는 로비 복귀, 튜토리얼 없음(첫실행 경로와 구분).
            PackHandoff.Set(t_opened, "LobbyScene", false);
            SceneManager.LoadScene("CardPack");
            return;
        }

        // 실패는 차감 없이 반환됨(TryPurchase 보장) — 사유만 안내하고 진열은 그대로 둔다.
        ShowFailPopup(t_opened != null ? t_opened.Result : (EPackOpenResult?)null);
    }

    // 실패 사유를 사용자 메시지로 갈라 SimpleYNPopup 표시(LobbyMatchLauncher 팝업 관용구).
    void ShowFailPopup(EPackOpenResult? _result)
    {
        string t_message = _result == EPackOpenResult.InsufficientGold
            ? "골드가 부족합니다."
            : "구매할 수 없습니다.";

        UIPoolManager.instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = t_message,
            yesText   = "확인",
            noText    = "닫기",
        });
    }
}
