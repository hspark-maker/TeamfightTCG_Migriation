using UnityEngine;

// 랭크 보상 진입 버튼의 "수령 가능" 알림 점.
// 판정 근거는 RankRewardManager.HasAnyClaimable 하나뿐 — UI가 상태 규칙을 복제하지 않는다.
public class RankRewardAlertDot : MonoBehaviour
{
    [Tooltip("켜고 끌 점 노드. 이 컴포넌트가 붙은 노드의 자식이어야 한다(자기 자신을 물리면 꺼진 뒤 구독이 끊긴다).")]
    [SerializeField] GameObject dot;

    // 최초 렌더를 Start로 미루기 위한 표식 — RankConfig 주입(DataLibrary.Awake)보다 OnEnable이 먼저 돌 수 있다.
    bool m_started;

    void Start()
    {
        this.m_started = true;
        this.Render();
    }

    // 수령 즉시 꺼져야 하므로 표시 시점 재조회만으로는 부족하다 — 변경 통지도 함께 받는다.
    void OnEnable()
    {
        RankRewardManager.OnChanged += this.Render;

        if (!this.m_started) return;   // 첫 활성화는 Start가 담당(탭 재진입만 여기서).
        this.Render();
    }

    void OnDisable()
    {
        RankRewardManager.OnChanged -= this.Render;
    }

    void Render()
    {
        if (this.dot != null) this.dot.SetActive(RankRewardManager.HasAnyClaimable);
    }
}
