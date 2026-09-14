using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>강화 버튼의 홀드 입력. 기존 Button 클릭을 유지하고 홀드 뒤의 클릭만 소비한다.</summary>
[DisallowMultipleComponent]
public sealed class ShardEnhanceHoldInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IPointerExitHandler, IPointerMoveHandler, IInitializePotentialDragHandler, IDragHandler, ICancelHandler
{
    const float Threshold = 0.35f;
    public Action<float> OnHoldTick;
    public Action OnHoldEnded;
    bool m_pressed;
    bool m_suppressClick;
    int m_pointerId;
    float m_startedAt;
    PointerEventData m_pointerEvent;

    public bool IsPressed => m_pressed;
    public Vector2? PointerPosition { get; private set; }
    public int PressVersion { get; private set; }

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
        m_pointerEvent = _event;
        PointerPosition = _event.position;
        PressVersion++;
        m_startedAt = Time.unscaledTime;
    }

    public void OnPointerUp(PointerEventData _event)
    {
        if (_event.pointerId != m_pointerId) return;
        TrackPointer(_event);
        End(false);
    }

    public void OnPointerExit(PointerEventData _event)
    {
        // 자식 Graphic 사이의 이동도 Exit를 보낼 수 있다. 버튼 밖으로 나갔을 때만 끊는다.
        TrackPointer(_event);
    }

    public void OnPointerMove(PointerEventData _event) => TrackPointer(_event);
    public void OnDrag(PointerEventData _event) => TrackPointer(_event);

    // 부모 ScrollRect가 드래그를 가져가며 PointerUp을 보내지 않도록 버튼이 직접 받는다.
    public void OnInitializePotentialDrag(PointerEventData _event) => _event.useDragThreshold = false;

    void TrackPointer(PointerEventData _event)
    {
        if (!m_pressed || _event.pointerId != m_pointerId) return;
        m_pointerEvent = _event;
        if (!RectTransformUtility.RectangleContainsScreenPoint(
            transform as RectTransform, _event.position, _event.pressEventCamera))
        {
            Cancel();
            return;
        }
        PointerPosition = _event.position;
    }

    void Update()
    {
        if (!m_pressed) return;
        TrackPointer(m_pointerEvent);
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
