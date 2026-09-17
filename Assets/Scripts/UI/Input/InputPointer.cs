using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>누르기를 시작한 장치와 손가락을 유지한다. 다른 손가락으로 드래그가 넘어가지 않는다.</summary>
public struct InputPointer
{
    Pointer device;
    int touchId;
    Vector2 lastPosition;

    public static InputPointer Capture(PointerEventData _data)
    {
        if (_data is ExtendedPointerEventData t_extended)
            return new InputPointer
            {
                device = t_extended.device as Pointer,
                touchId = t_extended.touchId,
                lastPosition = _data.position,
            };
        return new InputPointer { device = Pointer.current, lastPosition = _data.position };
    }

    public static InputPointer Capture(Touch _touch)
        => new InputPointer { device = _touch.finger.screen, touchId = _touch.touchId, lastPosition = _touch.screenPosition };

    public static InputPointer Capture(Pointer _device)
        => new InputPointer { device = _device, lastPosition = _device.position.ReadValue() };

    public bool Matches(PointerEventData _data)
        => _data is ExtendedPointerEventData t_extended
            ? ReferenceEquals(this.device, t_extended.device) && this.touchId == t_extended.touchId
            : this.touchId == 0;

    public bool TryRead(out Vector2 _position, out bool _held, out bool _canceled)
    {
        _position = this.lastPosition;
        _held = false;
        _canceled = true;
        if (this.device == null || !this.device.added || !this.device.enabled) return false;

        if (this.touchId != 0)
        {
            foreach (Touch t_touch in Touch.activeTouches)
            {
                if (t_touch.touchId != this.touchId || !ReferenceEquals(t_touch.finger.screen, this.device)) continue;
                _position = this.lastPosition = t_touch.screenPosition;
                _canceled = t_touch.phase == TouchPhase.Canceled;
                _held = !_canceled && t_touch.phase != TouchPhase.Ended;
                return true;
            }
            return false;
        }

        _position = this.lastPosition = this.device.position.ReadValue();
        _held = this.device is Mouse t_mouse ? t_mouse.leftButton.isPressed : this.device.press.isPressed;
        _canceled = false;
        return true;
    }
}
