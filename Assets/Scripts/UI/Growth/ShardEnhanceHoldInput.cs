using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>강화 버튼의 홀드 입력. 기존 Button 클릭을 유지하고 홀드 뒤의 클릭만 소비한다.</summary>
[DisallowMultipleComponent]
public sealed class ShardEnhanceHoldInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, ICancelHandler
{
    const float Threshold = 0.35f;
    public Action<float> OnHoldTick;
    public Action OnHoldEnded;
    bool m_pressed;
    bool m_suppressClick;
    int m_pointerId;
    float m_startedAt;

    public bool IsPressed => m_pressed;

    public bool ConsumeClick()
    {
        bool t_consume = m_suppressClick;
        m_suppressClick = false;
        return t_consume;
    }

    public void OnPointerDown(PointerEventData _event)
    {
        if (_event.button != PointerEventData.InputButton.Left || m_pressed) return;
        var t_button = GetComponent<Button>();
        if (t_button == null || !t_button.IsInteractable())
        {
            // 왕복 중 시작한 누름이 버튼 복구 뒤의 PointerUp에서 새 클릭이 되지 않게 한다.
            m_suppressClick = true;
            return;
        }
        m_pressed = true;
        m_suppressClick = false;
        m_pointerId = _event.pointerId;
        m_startedAt = Time.unscaledTime;
    }

    public void OnPointerUp(PointerEventData _event)
    {
        if (_event.pointerId == m_pointerId) End(false);
    }

    public void OnPointerExit(PointerEventData _event)
    {
        if (_event.pointerId == m_pointerId) Cancel();
    }

    void Update()
    {
        if (!m_pressed) return;
        float t_elapsed = Time.unscaledTime - m_startedAt;
        if (t_elapsed < Threshold) return;
        m_suppressClick = true;
        OnHoldTick?.Invoke(t_elapsed - Threshold);
    }

    // EventSystem의 pointerId를 그대로 짝짓는다. 새 Input System의 device/touch ID를 legacy 입력과 섞지 않는다.
    public void OnCancel(BaseEventData _event) => Cancel();
    public void Cancel() => End(true);
    void OnDisable() => Cancel();
    void OnApplicationFocus(bool _focused) { if (!_focused) Cancel(); }
    void OnApplicationPause(bool _paused) { if (_paused) Cancel(); }

    void End(bool _suppress)
    {
        if (!m_pressed) return;
        m_pressed = false;
        m_suppressClick |= _suppress;
        OnHoldEnded?.Invoke();
    }
}
