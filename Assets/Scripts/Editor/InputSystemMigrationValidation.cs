using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>테스트 장치의 실제 입력 이벤트로 포인터 소유권과 종료 처리를 검사한다.</summary>
public static class InputSystemMigrationValidation
{
    [MenuItem("Tools/Input/Validate Input System Migration")]
    public static async void Run()
    {
        Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Run in unpaused play mode with the Game view focused.");
        bool t_enabled = EnhancedTouchSupport.enabled;
        var t_modules = new List<InputSystemUIInputModule>();
        Pointer t_previousPointer = Pointer.current;
        Mouse t_previousMouse = Mouse.current;
        Touchscreen t_previousScreen = Touchscreen.current;
        Mouse t_mouse = null;
        Touchscreen t_screen = null;
        Touchscreen t_otherScreen = null;
        try
        {
            foreach (InputSystemUIInputModule t_module in UnityEngine.Object.FindObjectsByType<InputSystemUIInputModule>(FindObjectsSortMode.None))
            {
                if (!t_module.isActiveAndEnabled) continue;
                t_modules.Add(t_module);
                t_module.enabled = false;
            }
            if (!t_enabled) EnhancedTouchSupport.Enable();
            t_mouse = InputSystem.AddDevice<Mouse>("MigrationValidationMouse");
            t_screen = InputSystem.AddDevice<Touchscreen>("MigrationValidationTouchscreen");
            t_otherScreen = InputSystem.AddDevice<Touchscreen>("MigrationValidationOtherTouchscreen");
            Pump();
            Require(UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count == 0,
                "Release physical touchscreen contacts before validation.");

            CheckMouse(t_mouse);
            CheckTouch(t_screen, t_otherScreen);
            CheckQuickTap(t_screen);

            SendTouch(t_screen, 40, TouchPhase.Began, new Vector2(140, 240));
            Require(GameInput.TryGetPress(out InputPointer t_removed), "Touch press before removal missing.");
            InputSystem.RemoveDevice(t_screen);
            CheckMissing(ref t_removed, new Vector2(140, 240), "removed touchscreen");
            await CheckEventDelivery();
            Debug.Log("[InputSystemMigrationValidation] PASS: mouse, touch ownership/movement/release/cancel, multi-touch, multi-screen, quick tap, disabled/removed devices, UI button and Collider2D event delivery.");
        }
        finally
        {
            if (t_mouse != null && t_mouse.added) InputSystem.RemoveDevice(t_mouse);
            if (t_screen != null && t_screen.added) InputSystem.RemoveDevice(t_screen);
            if (t_otherScreen != null && t_otherScreen.added) InputSystem.RemoveDevice(t_otherScreen);
            if (!t_enabled) EnhancedTouchSupport.Disable();
            if (t_previousScreen != null && t_previousScreen.added) t_previousScreen.MakeCurrent();
            if (t_previousMouse != null && t_previousMouse.added) t_previousMouse.MakeCurrent();
            if (t_previousPointer != null && t_previousPointer.added) t_previousPointer.MakeCurrent();
            foreach (InputSystemUIInputModule t_module in t_modules)
                if (t_module != null) t_module.enabled = true;
        }
    }

    static void CheckMouse(Mouse _mouse)
    {
        _mouse.MakeCurrent();
        InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(10, 20) }.WithButton(MouseButton.Left));
        Pump();
        _mouse.MakeCurrent();
        Require(GameInput.TryGetPress(out InputPointer t_pointer),
            $"Mouse press missing: update={InputState.currentUpdateType}, value={_mouse.leftButton.ReadValue()}, " +
            $"pressed={_mouse.leftButton.wasPressedThisFrame}, updated={_mouse.wasUpdatedThisFrame}, " +
            $"position={_mouse.position.ReadValue()}, touches={UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count}.");
        Check(ref t_pointer, new Vector2(10, 20), true, false, "mouse press");
        Require(GameInput.AnyPressedThisFrame && GameInput.AnyHeld, "Mouse activity missing.");

        InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(30, 40) }.WithButton(MouseButton.Left));
        Pump();
        _mouse.MakeCurrent();
        Require(!GameInput.TryGetPress(out _), "Held mouse repeated its press.");
        Check(ref t_pointer, new Vector2(30, 40), true, false, "mouse move");
        InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(50, 60) });
        Pump();
        Check(ref t_pointer, new Vector2(50, 60), false, false, "mouse release");

        InputSystem.DisableDevice(_mouse);
        CheckMissing(ref t_pointer, new Vector2(50, 60), "disabled mouse");
        InputSystem.EnableDevice(_mouse);
        InputSystem.RemoveDevice(_mouse);
        CheckMissing(ref t_pointer, new Vector2(50, 60), "removed mouse");
    }

    static void CheckTouch(Touchscreen _screen, Touchscreen _otherScreen)
    {
        SendTouch(_screen, 11, TouchPhase.Began, new Vector2(100, 200));
        Require(GameInput.TryGetPress(out InputPointer t_first), "First touch press missing.");
        Check(ref t_first, new Vector2(100, 200), true, false, "touch press");
        Require(GameInput.AnyPressedThisFrame && GameInput.AnyHeld, "Touch activity missing.");

        SendTouch(_screen, 22, TouchPhase.Began, new Vector2(700, 800));
        Require(GameInput.TryGetPress(out InputPointer t_second), "Second touch press missing.");
        Check(ref t_first, new Vector2(100, 200), true, false, "first touch stays owned");
        Check(ref t_second, new Vector2(700, 800), true, false, "second touch owns its position");
        var t_event = new ExtendedPointerEventData(null) { device = _screen, touchId = 11, position = new Vector2(100, 200) };
        InputPointer t_fromEvent = InputPointer.Capture(t_event);
        Require(t_first.Matches(t_event) && !t_second.Matches(t_event), "UI touch ownership mismatch.");
        t_event.device = _otherScreen;
        Require(!t_first.Matches(t_event), "Same touch ID on another screen matched.");
        SendTouch(_otherScreen, 11, TouchPhase.Began, new Vector2(900, 950));
        Check(ref t_first, new Vector2(100, 200), true, false, "other screen cannot steal touch");

        SendTouch(_screen, 11, TouchPhase.Moved, new Vector2(150, 250));
        Check(ref t_first, new Vector2(150, 250), true, false, "first touch move");
        Check(ref t_fromEvent, new Vector2(150, 250), true, false, "UI event capture tracks move");
        Check(ref t_second, new Vector2(700, 800), true, false, "second touch remains stationary");
        SendTouch(_screen, 11, TouchPhase.Ended, new Vector2(180, 280));
        Check(ref t_first, new Vector2(180, 280), false, false, "first touch release");
        Check(ref t_second, new Vector2(700, 800), true, false, "release does not end second touch");
        SendTouch(_screen, 22, TouchPhase.Canceled, new Vector2(710, 810));
        Check(ref t_second, new Vector2(710, 810), false, true, "touch cancellation");
        CheckMissing(ref t_first, new Vector2(180, 280), "expired touch does not switch fingers");
        SendTouch(_otherScreen, 11, TouchPhase.Ended, new Vector2(900, 950));
        Pump();
    }

    static void CheckQuickTap(Touchscreen _screen)
    {
        QueueTouch(_screen, 33, TouchPhase.Began, new Vector2(300, 400));
        QueueTouch(_screen, 33, TouchPhase.Ended, new Vector2(310, 410));
        Pump();
        Require(GameInput.TryGetPress(out InputPointer t_pointer), "Same-update quick tap lost its press.");
        Check(ref t_pointer, new Vector2(300, 400), true, false, "quick tap begin");
        Pump();
        Check(ref t_pointer, new Vector2(310, 410), false, false, "quick tap deferred release");
        Require(!GameInput.TryGetPress(out _), "Quick tap emitted a second press.");
        Pump();
        CheckMissing(ref t_pointer, new Vector2(310, 410), "quick tap expired");
    }

    static async UniTask CheckEventDelivery()
    {
        EventSystem t_previousEventSystem = EventSystem.current;
        var t_raycasters = new List<BaseRaycaster>();
        var t_eventSystems = new List<EventSystem>();
        GameObject t_root = null;
        Mouse t_mouse = null;
        try
        {
            foreach (EventSystem t_eventSystem in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            {
                if (!t_eventSystem.isActiveAndEnabled) continue;
                t_eventSystems.Add(t_eventSystem);
                t_eventSystem.enabled = false;
            }
            // 테스트 좌표가 기존 게임 UI나 전투 콜라이더에 닿지 않도록 레이캐스터도 격리한다.
            foreach (BaseRaycaster t_raycaster in UnityEngine.Object.FindObjectsByType<BaseRaycaster>(FindObjectsSortMode.None))
            {
                if (!t_raycaster.isActiveAndEnabled) continue;
                t_raycasters.Add(t_raycaster);
                t_raycaster.enabled = false;
            }
            t_root = new GameObject("InputMigrationEventValidation", typeof(EventSystem), typeof(InputSystemUIInputModule));
            t_root.hideFlags = HideFlags.HideAndDontSave;
            EventSystem.current = t_root.GetComponent<EventSystem>();
            InputSystemUIInputModule t_module = t_root.GetComponent<InputSystemUIInputModule>();
            t_mouse = InputSystem.AddDevice<Mouse>("MigrationValidationEventMouse");
            var t_canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            t_canvasGo.transform.SetParent(t_root.transform, false);
            t_canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var t_buttonGo = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            t_buttonGo.transform.SetParent(t_canvasGo.transform, false);
            var t_rect = t_buttonGo.GetComponent<RectTransform>();
            t_rect.anchorMin = t_rect.anchorMax = new Vector2(0.5f, 0.5f);
            t_rect.sizeDelta = new Vector2(160, 160);
            int t_clicks = 0;
            t_buttonGo.GetComponent<Button>().onClick.AddListener(() => ++t_clicks);
            PointerEvents t_uiEvents = Observe(t_buttonGo, t_mouse);
            Canvas.ForceUpdateCanvases();
            // Graphic.depth는 실제 렌더 뒤에 배정된다. 입력을 발행하기 전에만 한 프레임 기다린다.
            await UniTask.NextFrame();
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Play mode stopped during UI preparation.");
            Vector2 t_position = RectTransformUtility.WorldToScreenPoint(null, t_rect.position);
            Click(t_module, t_mouse, t_position);
            Require(t_clicks == 1 && t_uiEvents.IsSingleClick,
                $"GraphicRaycaster delivery: button={t_clicks}, {t_uiEvents}, depth={t_buttonGo.GetComponent<Image>().depth}, position={t_position}, focused={EventSystem.current.isFocused}, actions={t_module.actionsAsset != null}.");
            t_canvasGo.SetActive(false);

            var t_cameraGo = new GameObject("Camera", typeof(Camera), typeof(Physics2DRaycaster));
            t_cameraGo.transform.SetParent(t_root.transform, false);
            t_cameraGo.transform.position = new Vector3(10000, 10000, -10);
            Camera t_camera = t_cameraGo.GetComponent<Camera>();
            t_camera.enabled = false;
            t_camera.orthographic = true;
            t_camera.orthographicSize = 5;
            t_camera.cullingMask = 1 << 31;
            var t_target = new GameObject("Collider2D", typeof(BoxCollider2D));
            t_target.transform.SetParent(t_root.transform, false);
            t_target.transform.position = new Vector3(10000, 10000, 0);
            t_target.layer = 31;
            PointerEvents t_worldEvents = Observe(t_target, t_mouse);
            Physics2D.SyncTransforms();
            Click(t_module, t_mouse, t_camera.WorldToScreenPoint(t_target.transform.position));
            Require(t_worldEvents.IsSingleClick, $"Physics2DRaycaster delivery: {t_worldEvents}.");
        }
        finally
        {
            if (t_root != null) UnityEngine.Object.DestroyImmediate(t_root);
            if (t_mouse != null && t_mouse.added) InputSystem.RemoveDevice(t_mouse);
            foreach (EventSystem t_eventSystem in t_eventSystems)
                if (t_eventSystem != null) t_eventSystem.enabled = true;
            EventSystem.current = t_previousEventSystem;
            foreach (BaseRaycaster t_raycaster in t_raycasters)
                if (t_raycaster != null) t_raycaster.enabled = true;
        }
    }

    static void Click(InputSystemUIInputModule _module, Mouse _mouse, Vector2 _position)
    {
        foreach (bool t_held in new[] { false, true, false })
        {
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _position }.WithButton(MouseButton.Left, t_held));
            Pump();
            _module.Process();
        }
    }

    sealed class PointerEvents
    {
        public int down, up, click;
        public bool correctDevice = true;
        public bool IsSingleClick => down == 1 && up == 1 && click == 1 && correctDevice;
        public override string ToString() => $"down={down}, up={up}, click={click}, correctDevice={correctDevice}";
    }

    static PointerEvents Observe(GameObject _target, Mouse _mouse)
    {
        var t_result = new PointerEvents();
        EventTrigger t_trigger = _target.AddComponent<EventTrigger>();
        foreach (EventTriggerType t_type in new[] { EventTriggerType.PointerDown, EventTriggerType.PointerUp, EventTriggerType.PointerClick })
        {
            var t_entry = new EventTrigger.Entry { eventID = t_type };
            t_entry.callback.AddListener(_event =>
            {
                t_result.correctDevice &= _event is ExtendedPointerEventData t_data && ReferenceEquals(t_data.device, _mouse);
                if (t_type == EventTriggerType.PointerDown) ++t_result.down;
                else if (t_type == EventTriggerType.PointerUp) ++t_result.up;
                else ++t_result.click;
            });
            t_trigger.triggers.Add(t_entry);
        }
        return t_result;
    }

    static void SendTouch(Touchscreen _screen, int _id, TouchPhase _phase, Vector2 _position)
    {
        QueueTouch(_screen, _id, _phase, _position);
        Pump();
    }

    static void Pump()
    {
        InputSystem.Update();
        Require(InputState.currentUpdateType == InputUpdateType.Dynamic,
            "Validation requires a Dynamic input update. Focus the Game view and run again in play mode.");
    }

    static void QueueTouch(Touchscreen _screen, int _id, TouchPhase _phase, Vector2 _position)
        => InputSystem.QueueStateEvent(_screen, new TouchState { touchId = _id, phase = _phase, position = _position });

    static void Check(ref InputPointer _pointer, Vector2 _position, bool _held, bool _canceled, string _case)
    {
        Require(_pointer.TryRead(out Vector2 t_position, out bool t_held, out bool t_canceled), _case + ": read failed.");
        Require(t_position == _position && t_held == _held && t_canceled == _canceled,
            $"{_case}: position={t_position}, held={t_held}, canceled={t_canceled}.");
    }

    static void CheckMissing(ref InputPointer _pointer, Vector2 _lastPosition, string _case)
    {
        Require(!_pointer.TryRead(out Vector2 t_position, out bool t_held, out bool t_canceled), _case + ": unexpectedly readable.");
        Require(t_position == _lastPosition && !t_held && t_canceled, _case + ": stale position/cancel state lost.");
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException("[InputSystemMigrationValidation] " + _message);
    }
}
