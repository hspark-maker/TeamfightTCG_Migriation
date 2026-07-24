using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 도감 한 행의 생산 진행바. 완성된 행이 개별 생산하는 재화의 "다음 1단위까지 사이클 진행률"을 표시한다.
// 소유 집계 바가 아니다 — 특정 행(rowKey) 하나의 생산 상태만 반영한다(행마다 1개, RowView가 소유).
// 생산 누적은 시간 함수라 이벤트가 없으므로, 소유자(RowView)의 폴링 틱이 Refresh()를 주기 호출한다.
public class CollectionProgressView : MonoBehaviour
{
    [SerializeField] Image fillImage;       // 사이클 진행률 0~1 (Filled 타입 Image)
    [SerializeField] TMP_Text progressText; // 진행률 % 표기(선택 — 미배선 시 null 가드)

    // 이 바가 표시할 행의 안정 키. Bind에서 저장, Refresh가 사용.
    string m_rowKey;

    // 표시 대상 행을 지정(재빌드/바인딩 시 RowView가 호출). 즉시 1회 갱신.
    public void Bind(string _rowKey)
    {
        m_rowKey = _rowKey;
        Refresh();
    }

    // 생산 사이클 진행률로 바 갱신. 시간 누적 반영을 위해 소유자 폴링 틱에서 주기 호출된다.
    //   생산 중: 현재 사이클 소수부 진행(0~1) / 만땅: 가득(1) / 잠김: 비움(0, 생산 정지)
    public void Refresh()
    {
        if (string.IsNullOrEmpty(m_rowKey))
        {
            if (fillImage != null) fillImage.fillAmount = 0f;
            if (progressText != null) progressText.text = string.Empty;
            return;
        }

        var t_info = CollectionProductionManager.GetInfo(m_rowKey);

        float t_fill =
            t_info.State == EProductionState.Capped ? 1f :
            t_info.State == EProductionState.Producing ? Mathf.Clamp01(t_info.CycleProgress01) :
            0f;

        if (fillImage != null) fillImage.fillAmount = t_fill;
        if (progressText != null) progressText.text = $"{Mathf.RoundToInt(t_fill * 100f)}%";
    }
}
