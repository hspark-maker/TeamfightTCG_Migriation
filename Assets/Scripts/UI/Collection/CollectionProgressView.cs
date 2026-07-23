using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 도감 헤더 진행바(소유 n / 전체). 헤더 오브젝트에 부착.
// 크기는 데이터에서 파생 — total은 CardCatalog.Count(하드코딩 상수 금지).
// 소유 변경 시에만 바뀌므로 OnOwnershipChanged 구독으로 충분(생산 폴링 불필요).
public class CollectionProgressView : MonoBehaviour
{
    [SerializeField] Image fillImage;      // fillAmount 0~1 (Filled 타입 Image)
    [SerializeField] TMP_Text progressText; // "12 / 30" 표기

    void OnEnable()
    {
        OwnershipManager.OnOwnershipChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= Refresh;
    }

    // 소유 수 / 전체 카드 수로 진행바·텍스트 갱신. 전체 0(빈 카탈로그)은 0%로 안전 처리.
    void Refresh()
    {
        int t_owned = OwnershipManager.OwnedCount;
        int t_total = CardCatalog.Count;

        float t_ratio = t_total > 0 ? (float)t_owned / t_total : 0f;

        if (fillImage != null) fillImage.fillAmount = t_ratio;
        if (progressText != null) progressText.text = $"{t_owned} / {t_total}";
    }
}
