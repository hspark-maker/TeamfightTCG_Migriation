using TMPro;
using UnityEngine;

public class GoldHud : MonoBehaviour
{
    [SerializeField] TMP_Text goldText;

    // 획득 연출 중에는 실제 잔액 대신 연출이 지정한 표시값을 보여준다(코인이 도착하며 숫자가 오르는 구간).
    bool m_held;

    /// <summary>골드 수치 텍스트의 RectTransform. 연출이 도착 지점·강조 대상으로 쓴다.</summary>
    public RectTransform TextRect => this.goldText != null ? (RectTransform)this.goldText.transform : null;

    /// <summary>표시값을 연출용으로 고정한다. 실제 잔액 변경은 ReleaseDisplay까지 화면에 반영되지 않는다.</summary>
    public void HoldDisplay(long _value)
    {
        m_held = true;
        this.Render(_value);
    }

    /// <summary>고정을 풀고 실제 잔액으로 되돌린다.</summary>
    public void ReleaseDisplay()
    {
        m_held = false;
        this.Render(CurrencyManager.Gold);
    }

    void Awake()
    {
        if (this.goldText == null) this.goldText = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        // 활성화 시점의 실제 잔액으로 먼저 맞춘 뒤 이후 변경을 구독.
        CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
        this.Render(CurrencyManager.Gold);
    }

    void OnDisable()
    {
        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;
        // 연출 도중 꺼지면 해제 호출이 오지 않는다 — 고정을 여기서 풀어 다음 활성화가 잔액을 못 따라가는 상태를 막는다.
        m_held = false;
    }

    // 골드 변경만 반영. 다른 재화 종류는 무시.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (m_held) return;
        if (_type != ECurrencyType.Gold) return;
        this.Render(_balance);
    }

    // 천단위 콤마 포맷
    void Render(long _gold)
    {
        if (this.goldText == null) return;
        this.goldText.text = _gold.ToString("N0");
    }
}
