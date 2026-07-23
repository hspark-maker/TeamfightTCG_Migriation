using System;
using TMPro;
using UnityEngine;

public class GoldHud : MonoBehaviour
{
    [SerializeField] TMP_Text goldText;

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
    }

    // 골드 변경만 반영. 다른 재화 종류는 무시.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
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
