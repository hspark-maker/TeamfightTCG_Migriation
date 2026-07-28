using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 덱 타이틀 옆에 붙는 압축 시너지 표시 1개. 아이콘 + 그 아래 숫자.
/// 롱프레스하면 <see cref="SynergyTooltip"/>으로 설명이 뜬다(콜백만 쏘고 툴팁은 스트립이 소유).
/// </summary>
public class SynergyCountIcon : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] Image     icon;
    [SerializeField] TMP_Text  countText;
    [SerializeField] GameObject activeRoot;    // 활성 표시(선택)
    [SerializeField] GameObject inactiveRoot;  // 비활성 표시(선택)

    [Header("Format")]
    [Tooltip("해제(기본): '현재/필요' 통상 표기. 체크: '필요/현재'로 뒤집기.")]
    [SerializeField] bool requiredFirst = false;

    [Header("Tint")]
    [SerializeField] bool  tintBySynergyColor = true;
    [SerializeField] float inactiveAlpha      = 0.45f;

    [Header("Long Press")]
    [SerializeField] float longPressSeconds = 0.3f;
    [SerializeField] float cancelDragPixels = 20f;

    public Action<SynergyCountIcon> onLongPress;
    public Action<SynergyCountIcon> onLongPressEnd;

    bool    pressing, fired;
    float   pressStartTime;
    Vector2 pressStartPos;

    public SynergyProgress Progress { get; private set; }

    public void Set(SynergyProgress _p)
    {
        CancelPress();

        if (_p == null || _p.Synergy == null)
        {
            gameObject.SetActive(false);
            this.Progress = null;
            return;
        }
        gameObject.SetActive(true);
        this.Progress = _p;

        SynergyData t_data   = _p.Synergy;
        bool        t_active = _p.IsActive;

        // color 미배정(알파 0)이면 틴트 없이 원본 아이콘 그대로 — 안 그러면 투명해져서 안 보인다.
        Color t_tint = this.tintBySynergyColor ? t_data.TintOrWhite : Color.white;
        if (!t_active) t_tint.a *= this.inactiveAlpha;

        if (this.icon != null)
        {
            this.icon.sprite  = t_data.activeIcon;
            this.icon.enabled = t_data.activeIcon != null;
            this.icon.color   = t_tint;
        }

        if (this.countText != null)
            this.countText.text = BuildCount(_p);

        if (this.activeRoot   != null) this.activeRoot.SetActive(t_active);
        if (this.inactiveRoot != null) this.inactiveRoot.SetActive(!t_active);
    }

    /// <summary>필요 수는 '다음 티어'가 있으면 그 요구치, 최고 티어 도달이면 현재 열린 티어 요구치.
    /// (Goal은 다음 티어가 없으면 0이라 그대로 쓰면 "0/4"가 된다.)</summary>
    string BuildCount(SynergyProgress _p)
    {
        int t_required = _p.NextTier?.requiredCount
                      ?? _p.ActiveTier?.requiredCount
                      ?? 0;
        if (t_required <= 0) return _p.Count.ToString();   // 티어 정의가 없는 시너지

        return this.requiredFirst
            ? $"{t_required}/{_p.Count}"
            : $"{_p.Count}/{t_required}";
    }

    // ── Long Press ────────────────────────────────────────────────────────
    // IDragHandler는 구현하지 않는다 — 구현하면 부모 스크롤이 드래그를 못 받는다.

    public void OnPointerDown(PointerEventData _e)
    {
        if (this.Progress == null) return;
        this.pressing       = true;
        this.fired          = false;
        this.pressStartTime = Time.unscaledTime;
        this.pressStartPos  = _e.position;
    }

    public void OnPointerUp(PointerEventData _e)   => CancelPress();
    public void OnPointerExit(PointerEventData _e) => CancelPress();

    void OnDisable() => CancelPress();

    void Update()
    {
        if (!this.pressing || this.fired) return;

        if (Vector2.Distance(CurrentPointerPos(), this.pressStartPos) > this.cancelDragPixels)
        {
            CancelPress();
            return;
        }
        if (Time.unscaledTime - this.pressStartTime < this.longPressSeconds) return;

        this.fired = true;
        this.onLongPress?.Invoke(this);
    }

    void CancelPress()
    {
        bool t_wasFired = this.fired;
        this.pressing = false;
        this.fired    = false;
        if (t_wasFired) this.onLongPressEnd?.Invoke(this);
    }

    static Vector2 CurrentPointerPos()
        => Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
}
