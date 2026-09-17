using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>UI 밖의 화면 탭과 시작 입력. UI 제스처는 InputPointer로 시작 장치를 보관한다.</summary>
public static class GameInput
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
    }

    public static Vector2 PointerPosition
    {
        get
        {
            if (Touch.activeTouches.Count > 0) return Touch.activeTouches[0].screenPosition;
            return Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
        }
    }

    public static bool TryGetPress(out InputPointer _pointer)
    {
        foreach (Touch t_touch in Touch.activeTouches)
        {
            if (t_touch.phase != TouchPhase.Began) continue;
            _pointer = InputPointer.Capture(t_touch);
            return true;
        }
        // 터치를 마우스로 중복 소비하지 않는다.
        if (Touch.activeTouches.Count == 0 && Pointer.current is Pointer t_pointer && !(t_pointer is Touchscreen)
            && (t_pointer is Mouse t_mouse ? t_mouse.leftButton.wasPressedThisFrame : t_pointer.press.wasPressedThisFrame))
        {
            _pointer = InputPointer.Capture(t_pointer);
            return true;
        }
        _pointer = default;
        return false;
    }

    public static bool AnyPressedThisFrame => AnyButton(true);
    public static bool AnyHeld => AnyButton(false);

    static bool AnyButton(bool _pressedThisFrame)
    {
        foreach (Touch t_touch in Touch.activeTouches)
            if (_pressedThisFrame ? t_touch.phase == TouchPhase.Began
                : t_touch.phase != TouchPhase.Ended && t_touch.phase != TouchPhase.Canceled) return true;

        foreach (InputDevice t_device in InputSystem.devices)
        {
            if (!t_device.enabled || t_device is Touchscreen) continue;
            foreach (InputControl t_control in t_device.allControls)
                if (t_control is ButtonControl t_button
                    && (_pressedThisFrame ? t_button.wasPressedThisFrame : t_button.isPressed)) return true;
        }
        return false;
    }
}
