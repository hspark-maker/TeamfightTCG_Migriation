using System;
using TMPro;
using UnityEngine;

public class GoldHud : MonoBehaviour
{
    [SerializeField] TMP_Text goldText;

    // 표시할 재화 종류. 기본값이 Gold라 기존 골드 HUD 배선은 그대로 동작하고,
    // 다이아 등 다른 재화는 이 값만 바꿔 같은 컴포넌트를 재사용한다(연출 API도 종류를 따라간다).
    [SerializeField] ECurrencyType type = ECurrencyType.Gold;

    // 획득 연출 중에는 실제 잔액 대신 연출이 지정한 표시값을 보여준다(코인이 도착하며 숫자가 오르는 구간).
    bool m_held;

    /// <summary>골드 수치 텍스트의 RectTransform. 연출이 도착 지점·강조 대상으로 쓴다.</summary>
    public RectTransform TextRect => this.goldText != null ? (RectTransform)this.goldText.transform : null;

    /// <summary>
    /// 획득 연출용 숫자 롤업을 시작하고, 코인 도착 콜백(도착 장수, 전체 장수)에 물릴 진행 핸들러를 돌려준다.
    /// 잔액이 이미 최종값이라는 전제 — 지급·저장이 끝난 뒤에 부른다(획득분만큼 되돌렸다가 도착에 맞춰 다시 올린다).
    /// 연출이 끊겨도 고정이 풀리도록 호출부는 시퀀스 OnKill에 ReleaseDisplay를 걸어둘 것.
    /// </summary>
    public Action<int, int> BeginGainRollUp(long _gain, float _punch = UiPunch.DEFAULT_SCALE)
    {
        long t_start = CurrencyManager.GetBalance(this.type) - _gain;
        this.HoldDisplay(t_start);

        return (_arrived, _total) =>
        {
            if (_total <= 0 || _arrived >= _total) this.ReleaseDisplay();
            else this.HoldDisplay(t_start + (long)(_gain * (_arrived / (float)_total)));

            UiPunch.Play(this.TextRect, _punch);
        };
    }

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
        this.Render(CurrencyManager.GetBalance(this.type));
    }

    void Awake()
    {
        if (this.goldText == null) this.goldText = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        // 활성화 시점의 실제 잔액으로 먼저 맞춘 뒤 이후 변경을 구독.
        CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
        this.Render(CurrencyManager.GetBalance(this.type));
    }

    void OnDisable()
    {
        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;
        // 연출 도중 꺼지면 해제 호출이 오지 않는다 — 고정을 여기서 풀어 다음 활성화가 잔액을 못 따라가는 상태를 막는다.
        m_held = false;
    }

    // 이 HUD가 맡은 재화의 변경만 반영. 다른 종류는 무시.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (m_held) return;
        if (_type != this.type) return;
        this.Render(_balance);
    }

    // 천단위 콤마 포맷
    void Render(long _amount)
    {
        if (this.goldText == null) return;
        this.goldText.text = _amount.ToString("N0");
    }
}
