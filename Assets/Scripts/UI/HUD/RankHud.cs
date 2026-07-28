using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 로비 랭크 표시(배지 = 티어, 텍스트 = 티어명/포인트).
// 랭크는 전투 씬에서만 변하므로 변경 이벤트 없이 표시 시점에 RankManager를 재조회한다.
public class RankHud : MonoBehaviour
{
    [SerializeField] Image badgeImage;   // 티어 배지
    [SerializeField] TMP_Text descText;  // 티어 표시명("브론즈 1")
    [SerializeField] TMP_Text pointText; // 랭크 포인트

    // 최초 렌더를 Start로 미루기 위한 표식 — RankConfig 주입(DataLibrary.Awake)보다 OnEnable이 먼저 돌 수 있다.
    bool m_started;

    void Start()
    {
        this.m_started = true;
        this.Render();
    }

    // 탭 재진입(SetActive 토글)만 처리. 첫 활성화는 Start가 담당.
    void OnEnable()
    {
        if (!this.m_started) return;
        this.Render();
    }

    void Render()
    {
        var t_info = RankManager.GetInfo();

        // 배지 미저작(null)이면 씬에 배선된 기존 스프라이트를 그대로 둔다.
        if (this.badgeImage != null && t_info.Badge != null) this.badgeImage.sprite = t_info.Badge;
        if (this.descText != null) this.descText.text = t_info.DisplayName; // RankInfo가 non-null 보증
        if (this.pointText != null) this.pointText.text = t_info.Points.ToString("N0");
    }
}
