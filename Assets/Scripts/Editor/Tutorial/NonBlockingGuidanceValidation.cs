#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>프리팹 사본에서 게이트 모드 전환과 콜백 계약을 검증한다. 에셋 저장·서버 접근 없음.</summary>
public static class NonBlockingGuidanceValidation
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Tutorial/Validate Non Blocking Guidance")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Stop play mode before isolated validation.");
        Require(OutgameTutorialGateUI.Instance == null, "Close the active tutorial gate before validation.");
        var instanceField = typeof(OutgameTutorialGateUI).GetField("<Instance>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        Require(instanceField != null, "Gate singleton backing field changed.");
        var contents = PrefabUtility.LoadPrefabContents(
            "Assets/Assets/Prefabs/UI/Tutorial/OutgameTutorialGate.prefab");
        OutgameTutorialGateUI gate = null;
        try
        {
            gate = contents.GetComponent<OutgameTutorialGateUI>();
            Require(gate != null, "Gate component missing.");
            var action = Field<Button>(gate, "hintActionButton");
            var dismiss = Field<Button>(gate, "hintDismissButton");
            var actionText = Field<TextMeshProUGUI>(gate, "hintActionText");
            var message = Field<RectTransform>(gate, "messageRect");
            var text = Field<TextMeshProUGUI>(gate, "messageText");
            var blocker = Field<Image>(gate, "blocker");
            Require(action != null && dismiss != null && actionText != null && message != null
                && text != null && blocker != null, "Required prefab references missing.");
            Require(action.transform.IsChildOf(message) && dismiss.transform.IsChildOf(message),
                "Hint buttons must stay inside the existing message panel.");
            Require(!action.gameObject.activeSelf && !dismiss.gameObject.activeSelf,
                "Authored hint buttons must start hidden.");
            Require(action.targetGraphic != null && dismiss.targetGraphic != null,
                "Hint buttons need authored hit graphics.");
            Require(action.onClick.GetPersistentEventCount() == 0 && dismiss.onClick.GetPersistentEventCount() == 0,
                "Hint buttons must not have persistent gameplay callbacks.");

            // Awake 대신 UI 준비만 실행한다. 현재 씬에 EventSystem을 생성하지 않는다.
            instanceField.SetValue(null, gate);
            foreach (string method in new[] { "CacheDimColor", "CacheRoots", "CacheBlockerButton",
                         "LiftOrnaments", "NormalizeGraphics", "CacheHintControls" }) Invoke(gate, method);
            gate.ClearForce();
            Vector2 size = message.sizeDelta;
            Vector2 min = text.rectTransform.offsetMin;
            Vector2 max = text.rectTransform.offsetMax;
            var raycaster = message.GetComponent<GraphicRaycaster>();
            Require(raycaster != null && !raycaster.enabled, "Message raycaster must start disabled.");

            int actionCount = 0;
            int dismissCount = 0;
            Require(gate.TryShowHint(gate, "재화가 부족해요.", "모험 보기", () =>
            {
                Require(!gate.IsOwnedBy(gate) && !OutgameTutorialGateUI.IsShowing,
                    "Action callback ran before gate release.");
                actionCount++;
            }, () => dismissCount++), "Initial hint rejected.");
            Require(!blocker.enabled && !blocker.raycastTarget && raycaster.enabled,
                "Hint must receive only local button input.");
            Require(action.gameObject.activeSelf && dismiss.gameObject.activeSelf
                && action.targetGraphic.raycastTarget && dismiss.targetGraphic.raycastTarget,
                "Hint button hit areas unavailable.");
            Require(actionText.text == "모험 보기" && message.sizeDelta.y > size.y,
                "Hint label or expanded message layout missing.");
            Require(!gate.TryShowHint(action, "다른 안내", null, null, null),
                "Other owner replaced an active hint.");
            gate.Clear(action);
            Require(gate.IsOwnedBy(gate) && OutgameTutorialGateUI.IsShowing,
                "Other owner cleared the hint.");
            action.onClick.Invoke();
            action.onClick.Invoke();
            dismiss.onClick.Invoke();
            Require(actionCount == 1 && dismissCount == 0, "Action listeners must fire once and then detach.");
            Require(message.sizeDelta == size && text.rectTransform.offsetMin == min
                && text.rectTransform.offsetMax == max && !raycaster.enabled,
                "Hint release did not restore the original layout/input.");

            Require(gate.TryShowHint(gate, "선택 안내", "이동", () => actionCount++, null), "Second hint rejected.");
            Require(gate.TryShowHint(gate, "갱신 안내", null, null, () =>
            {
                Require(!OutgameTutorialGateUI.IsShowing && !gate.IsOwnedBy(gate),
                    "Dismiss callback ran before release.");
                dismissCount++;
            }), "Same owner could not update its hint.");
            Require(!action.gameObject.activeSelf, "Actionless hint still shows an action button.");
            action.onClick.Invoke();
            Require(actionCount == 1, "Same-owner update retained an old action callback.");
            dismiss.onClick.Invoke();
            dismiss.onClick.Invoke();
            Require(dismissCount == 1, "Dismiss callback must fire exactly once.");

            gate.TryShowHint(gate, "비차단 안내", "이동", () => actionCount++, () => dismissCount++);
            int confirmed = 0;
            gate.ShowMessageGate(gate, null, "기존 강제 안내", () => confirmed++);
            Require(blocker.enabled && blocker.raycastTarget && !raycaster.enabled
                && !action.gameObject.activeSelf && !dismiss.gameObject.activeSelf && message.sizeDelta == size,
                "Forced mode did not restore its original input/layout.");
            Require(!gate.TryShowHint(action, "끼어드는 안내", null, null, null),
                "Other owner interrupted a forced message gate.");
            action.onClick.Invoke();
            dismiss.onClick.Invoke();
            Require(actionCount == 1 && dismissCount == 1, "Hint listeners leaked into forced mode.");
            blocker.GetComponent<Button>().onClick.Invoke();
            blocker.GetComponent<Button>().onClick.Invoke();
            Require(confirmed == 1, "Forced confirmation must still complete exactly once.");
            gate.TryShowHint(gate, "안내", null, null, null);
            gate.ShowBanner(gate, "기존 배너");
            Require(!blocker.raycastTarget && !raycaster.enabled && !dismiss.gameObject.activeSelf
                && message.sizeDelta == size, "Banner inherited hint input/layout.");
            Debug.Log("[NonBlockingGuidanceValidation] PASS: prefab wiring, local raycasts, ownership, mode restoration, clear-before-callback, once-only callbacks.");
        }
        finally
        {
            if (gate != null) gate.ClearForce();
            instanceField.SetValue(null, null);
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    static T Field<T>(OutgameTutorialGateUI gate, string name) where T : class
        => typeof(OutgameTutorialGateUI).GetField(name, PrivateInstance)?.GetValue(gate) as T;

    static void Invoke(OutgameTutorialGateUI gate, string name)
    {
        var method = typeof(OutgameTutorialGateUI).GetMethod(name, PrivateInstance);
        Require(method != null, "Gate initialization method changed: " + name);
        method.Invoke(gate, null);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
