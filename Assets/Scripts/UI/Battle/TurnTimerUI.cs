using TMPro;
using UnityEngine;

/// <summary>
/// 내 턴 생각시간 남은 초 표시. 시간은 TurnThinkTimer(단일 소스)가 소유하고,
/// 여기선 읽어서 표시만 한다(자체 카운트 금지 → 드리프트 방지).
/// TurnThinkTimer.Active(내 턴 InputAllowed 구간)일 때만 보이고 그 외엔 숨김.
/// 표시 전용이라 결정론/멀티와 무관.
/// </summary>
public class TurnTimerUI : MonoBehaviour
{
    [SerializeField] TMP_Text label;

    void Awake()
    {
        if (this.label == null) this.label = GetComponent<TMP_Text>();
    }

    void Update()
    {
        bool t_show = TurnThinkTimer.Active;
        if (this.label != null && this.label.enabled != t_show)
            this.label.enabled = t_show;
        if (!t_show) return;

        this.label.text = Mathf.CeilToInt(TurnThinkTimer.Remaining).ToString();
    }
}
