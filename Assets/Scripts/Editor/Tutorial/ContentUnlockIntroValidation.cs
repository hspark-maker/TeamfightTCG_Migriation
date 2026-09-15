#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>세이브와 서버를 변경하지 않는 해금 소개 저작·진행 계약 검사.</summary>
public static class ContentUnlockIntroValidation
{
    [MenuItem("Tools/Tutorial/Validate Content Unlock Intro")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Run isolated authoring validation outside play mode.");
        var data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        Require(data != null, "Tutorial asset missing.");
        Require(ContentUnlockConfig.TryValidate(ContentUnlockAuthoring.Data.contentUnlocks, out var unlockError), unlockError);
        var flowErrors = GuideMissionFlowValidation.Validate(data);
        Require(flowErrors.Count == 0, string.Join("\n", flowErrors));
        Require(ContentUnlockIntroDef.KeyOf(EContentUnlockIntro.CardEnhance) == ContentUnlockManager.CARD_ENHANCE,
            "Card enhancement intro must commit the matching presentation key.");
        var ids = new HashSet<int>();
        int forced = 0;
        int rankIntros = 0;
        foreach (var chapter in data.Chapters)
        {
            if (!chapter.IsGuided) forced++;
            for (int i = 0; i < chapter.StepCount; i++)
            {
                Require(chapter.TryGetStep(i, out var step) && ids.Add(step.StepId), "Duplicate step ID.");
                if (step.Action != EOutgameTutorialAction.ContentUnlockIntro) continue;
                Require(step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro && !step.LeavesScene,
                    "Unlock intro must wait for its own confirmation without leaving the scene.");
                var sink = new CountingSink();
                var context = new OutgameTutorialStepContext(0, 0, 0, 1, true, sink);
                Require(TutorialStepExecutor.Enter(step, context) == EOutgameTutorialStepResult.Gated
                    && sink.Writes == 0,
                    "Entering an intro must not commit or launch content.");
                foreach (var content in step.ContentIntros)
                    Require(ContentUnlockAuthoring.Data.TryGetContentIntro(content, out var entry) && entry.icon != null,
                        "Missing intro definition/icon.");
                if (step.ContentIntros[0] == EContentUnlockIntro.Ranked)
                {
                    rankIntros++;
                    Require(i > 0 && chapter.TryGetStep(i - 1, out var previous)
                        && previous.Action == EOutgameTutorialAction.EnterFirstRank, "Rank entry must precede its intro.");
                    Require(chapter.TryGetStep(i + 1, out var next)
                        && next.Action == EOutgameTutorialAction.BattleEntry, "Rank intro must preserve battle entry.");
                }
            }
        }
        Require(forced == 4 && rankIntros == 1, "Forced boundary or rank intro placement changed.");
        Require(ContentUnlockIntroDef.KeyOf(EContentUnlockIntro.Ranked) == null,
            "Rank presentation must not impersonate a content access key.");

        var clone = UnityEngine.Object.Instantiate(ContentUnlockAuthoring.Data);
        try
        {
            Require(!HasIntroError(data, clone), "Valid intro authoring rejected.");
            clone.contentIntros[0].icon = null;
            Require(HasIntroError(data, clone), "Missing icon not rejected.");
            clone.contentIntros[0].icon = ContentUnlockAuthoring.Data.contentIntros[0].icon;
            clone.contentIntros.Add(clone.contentIntros[0]);
            Require(HasIntroError(data, clone), "Duplicate definition not rejected.");
            clone.contentIntros.RemoveAt(clone.contentIntros.Count - 1);
            clone.contentIntros.Clear();
            Require(HasIntroError(data, clone), "Missing definition not rejected.");
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
        Debug.Log("[ContentUnlockIntroValidation] PASS: gating without commit, rank ordering, mission flows, IDs, invalid authoring.");
    }

    /// <summary>사용자의 열린 씬을 유지한 채 소개 뷰의 확인·취소와 등장 입력을 검사한다.</summary>
    [MenuItem("Tools/Tutorial/Validate Content Unlock View")]
    public static void RunViewBehaviour()
    {
        Require(!EditorApplication.isPlaying, "Run isolated view validation outside play mode.");
        Require(!ScreenDim.IsAvailable, "Isolated view validation requires no registered Full dim.");
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        GameObject dimInstance = null;
        try
        {
            var dimPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Assets/Prefabs/UI/Common/ScreenDim.prefab");
            Require(dimPrefab != null, "Shared dim prefab missing.");
            dimInstance = (GameObject)PrefabUtility.InstantiatePrefab(dimPrefab, scene);
            var dimCanvas = dimInstance.AddComponent<Canvas>();
            dimCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            dimInstance.AddComponent<GraphicRaycaster>();
            var screenDim = dimInstance.GetComponent<ScreenDim>();
            var dimSerialized = new SerializedObject(screenDim);
            dimSerialized.FindProperty("layer").enumValueIndex = (int)EDimLayer.Full;
            dimSerialized.FindProperty("sortingCanvas").objectReferenceValue = dimCanvas;
            dimSerialized.ApplyModifiedPropertiesWithoutUndo();
            ScreenDimValidation.InvokeLifecycle(screenDim, "Awake");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Assets/Prefabs/UI/PooledUI/ContentUnlockIntroView.prefab");
            Require(prefab != null, "Intro prefab missing.");
            Require(prefab.GetComponent<Canvas>() != null && prefab.transform.Find("Contents/SafeArea/Stage") != null,
                "Onboarding overlay requires its own canvas and SafeArea/Stage.");
            var dim = prefab.transform.Find("Contents/PopupDim");
            Require(dim != null && dim.GetComponent<Image>().color.a == 0f
                && dim.GetComponent<Image>().enabled && dim.GetComponent<Image>().raycastTarget
                && dim.GetComponent<Button>() == null,
                "PopupDim must remain a transparent input blocker without confirming the step.");
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var view = instance.GetComponent<ContentUnlockIntroView>();
            instance.SetActive(true);
            var serialized = new SerializedObject(view);
            var button = (Button)serialized.FindProperty("_confirmButton").objectReferenceValue;
            var heading = (TMPro.TMP_Text)serialized.FindProperty("_headingText").objectReferenceValue;
            var message = (TMPro.TMP_Text)serialized.FindProperty("_messageText").objectReferenceValue;
            var data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
                "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
            Require(ContentUnlockAuthoring.Data.TryGetContentIntro(EContentUnlockIntro.Mission, out var mission)
                && ContentUnlockAuthoring.Data.TryGetContentIntro(EContentUnlockIntro.Roulette, out _), "Missing grouped definitions.");
            ContentUnlockAuthoring.Data.TryGetContentIntro(EContentUnlockIntro.Roulette, out var roulette);
            Require(ContentUnlockAuthoring.Data.TryGetContentIntro(EContentUnlockIntro.CardEnhance, out var enhance),
                "Missing card enhancement definition.");
            var icons = new[] { mission.icon };
            int confirmations = 0;
            int cancellations = 0;
            view.Show("미션 오픈 !", mission.description,
                icons, () => confirmations++, () => cancellations++);
            Require(heading != message && heading.text == "신규 컨텐츠" && message.text == "미션 오픈 !",
                "Heading and message must be separate text components.");
            Require(!button.interactable && ContentUnlockIntroView.IsOpen, "Entrance must block confirmation.");
            button.onClick.Invoke();
            Require(confirmations == 0, "Early click completed intro.");
            DOTween.Goto(view, 0.15f, false);
            Require(!button.interactable, "Confirmation enabled halfway through entrance.");
            DOTween.Complete(view, true);
            Require(button.interactable, "Entrance completion did not enable confirmation.");
            button.onClick.Invoke();
            button.onClick.Invoke();
            Require(confirmations == 0, "Confirmation must wait for the overlay exit.");
            Require(instance.transform.Find("Contents").GetComponent<CanvasGroup>().blocksRaycasts,
                "Exit must keep the underlying lobby blocked until completion.");
            DOTween.Complete(view, true);
            Require(confirmations == 1 && cancellations == 0 && !ContentUnlockIntroView.IsOpen,
                "Confirmation must complete exactly once.");
            view.Show("모험 오픈 !", "스테이지를 클리어하고 보상을 받으세요.",
                new[] { mission.icon }, () => confirmations++, () => cancellations++);
            Require(!button.interactable, "Reopening must reset entrance input.");
            bool IconsVisible(int count = 1)
            {
                var slots = serialized.FindProperty("_icons");
                if (slots.arraySize == 0) return false;
                for (int i = 0; i < slots.arraySize; i++)
                {
                    var icon = (Image)slots.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (icon == null || icon.gameObject.activeSelf != (i < count)) return false;
                }
                return true;
            }
            Require(IconsVisible(), "Single intro must show only its first icon.");
            view.Close();
            view.Close();
            Require(confirmations == 1 && cancellations == 1 && !ContentUnlockIntroView.IsOpen,
                "Cancellation must notify once without confirmation.");
            var ownerObject = new GameObject("SequentialIntroValidation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ownerObject, scene);
            var owner = ownerObject.AddComponent<ContentUnlockPresentation>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var ownerType = typeof(ContentUnlockPresentation);
            void Set(string name, object value) => ownerType.GetField(name, flags).SetValue(owner, value);
            void ShowNext() => ownerType.GetMethod("ShowCurrent", flags).Invoke(owner, null);
            void Prepare()
            {
                Set("m_intro", view);
                Set("m_intros", new List<ContentUnlockIntroDef> { mission, roulette, enhance });
                Set("m_introIndex", 0);
                Set("m_visible", true);
                Set("m_playing", true);
                Set("m_sessionVersion", ContentUnlockManager.SessionVersion);
                Set("m_confirmed", (Action)(() => confirmations++));
                Set("m_cancelled", (Action)(() => cancellations++));
                ShowNext();
            }
            Prepare();
            Require(message.text == mission.contentName + " / " + mission.guideMissionName && IconsVisible(2),
                "Mission introduction must present both mission types on one panel.");
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            Require(confirmations == 1 && !ContentUnlockIntroView.IsOpen
                && (bool)ownerType.GetField("m_pendingIntro", flags).GetValue(owner),
                "First confirmation must queue the next panel without completing the step.");
            ShowNext();
            Require(message.text == roulette.contentName && IconsVisible() && !button.interactable,
                "Next content must reopen with its own title and entrance gate.");
            var firstIcon = (Image)serialized.FindProperty("_icons").GetArrayElementAtIndex(0).objectReferenceValue;
            Require(firstIcon.sprite == roulette.icon, "Next content retained the previous icon.");
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            Require(confirmations == 1 && !ContentUnlockIntroView.IsOpen
                && (bool)ownerType.GetField("m_pendingIntro", flags).GetValue(owner),
                "Second confirmation must queue card enhancement without completing the step.");
            ShowNext();
            Require(message.text == enhance.contentName && IconsVisible() && !button.interactable
                && firstIcon.sprite == enhance.icon,
                "Card enhancement must show its own title and icon without a lobby button target.");
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            Require(confirmations == 2 && cancellations == 1 && !ContentUnlockIntroView.IsOpen,
                "Only the last panel may complete the step, exactly once.");
            Prepare();
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            owner.SetVisible(false);
            Require(confirmations == 2 && cancellations == 2
                && !(bool)ownerType.GetField("m_pendingIntro", flags).GetValue(owner),
                "Cancellation between panels must clear the remaining queue without completion.");
            var flightRoot = (RectTransform)serialized.FindProperty("_flightRoot").objectReferenceValue;
            var iconRoot = (RectTransform)serialized.FindProperty("iconRoot").objectReferenceValue;
            var stageGroup = (CanvasGroup)serialized.FindProperty("_contentGroup").objectReferenceValue;
            Require(flightRoot != null && !flightRoot.IsChildOf(stageGroup.transform),
                "Flight root must remain outside the fading stage.");
            Transform iconParent = iconRoot.parent;
            Vector2 iconHome = iconRoot.anchoredPosition;
            Vector3 iconScale = iconRoot.localScale;
            var targetObject = new GameObject("FlightTarget", typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(targetObject, scene);
            var target = (RectTransform)targetObject.transform;
            target.SetParent(flightRoot, false);
            target.sizeDelta = new Vector2(80f, 80f);
            target.anchoredPosition = new Vector2(220f, -340f);
            int arrivals = 0;
            Action arrivalDone = null;
            view.Show("이동 검사", "", icons, () => confirmations++, () => cancellations++, target,
                done => { arrivals++; arrivalDone = done; });
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            Require(arrivals == 0 && confirmations == 2 && firstIcon.transform.parent == flightRoot,
                "Confirmation must start flight without completing or playing the button effect.");
            DOTween.Goto(view, 0.09f);
            Require(stageGroup.alpha < 1f && stageGroup.alpha > 0f
                && firstIcon.GetComponent<CanvasGroup>().alpha == 1f && arrivals == 0,
                "Only the surrounding stage may fade before flight.");
            DOTween.Complete(view, true);
            Require(arrivals == 1 && confirmations == 2 && ContentUnlockIntroView.IsOpen && stageGroup.alpha == 0f,
                "Arrival must wait for the button effect while keeping the input blocker open.");
            Require(Vector3.Distance(firstIcon.rectTransform.TransformPoint(firstIcon.rectTransform.rect.center),
                target.TransformPoint(target.rect.center)) < 0.1f, "Icon missed the destination center.");
            arrivalDone();
            arrivalDone();
            Require(confirmations == 3 && !ContentUnlockIntroView.IsOpen
                && iconRoot.parent == iconParent && iconRoot.anchoredPosition == iconHome && iconRoot.localScale == iconScale,
                "Effect completion must notify once and restore the icon.");
            view.Show("취소 검사", "", icons, () => confirmations++, () => cancellations++, target,
                done => arrivalDone = done);
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            view.Close();
            arrivalDone();
            Require(confirmations == 3 && cancellations == 3 && !ContentUnlockIntroView.IsOpen,
                "A stale effect callback must not complete a cancelled intro.");
            targetObject.SetActive(false);
            view.Show("대상 없음", "", icons, () => confirmations++, () => cancellations++, target,
                done => { arrivals++; done(); });
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            Require(confirmations == 4 && arrivals == 1 && !ContentUnlockIntroView.IsOpen,
                "Inactive destination must finish without playing its effect.");
            targetObject.SetActive(true);
            foreach (float canvasScale in new[] { 0.75f, 1.5f })
            {
                flightRoot.localScale = Vector3.one * canvasScale;
                view.Show("배율 검사", "", icons, () => confirmations++, () => cancellations++, target,
                    done => arrivalDone = done);
                DOTween.Complete(view, true);
                button.onClick.Invoke();
                DOTween.Complete(view, true);
                Require(Vector3.Distance(firstIcon.rectTransform.TransformPoint(firstIcon.rectTransform.rect.center),
                    target.TransformPoint(target.rect.center)) < 0.1f, "Scaled flight root missed the target.");
                arrivalDone();
            }
            flightRoot.localScale = Vector3.one;
            view.Show("이동 중 취소", "", icons, () => confirmations++, () => cancellations++, target);
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            view.Close();
            Require(confirmations == 6 && cancellations == 4 && iconRoot.parent == iconParent
                && iconRoot.anchoredPosition == iconHome && iconRoot.localScale == iconScale,
                "Mid-flight cancellation must restore the icon without success.");
            var secondIcon = (Image)serialized.FindProperty("_icons").GetArrayElementAtIndex(1).objectReferenceValue;
            var secondTargetObject = new GameObject("GuideMissionTarget", typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(secondTargetObject, scene);
            var secondTarget = (RectTransform)secondTargetObject.transform;
            secondTarget.SetParent(flightRoot, false);
            secondTarget.sizeDelta = new Vector2(100f, 100f);
            secondTarget.anchoredPosition = new Vector2(-220f, -340f);
            var pairDone = new Action[2];
            int pairArrivals = 0;
            void ShowPair()
            {
                pairDone = new Action[2];
                view.ShowTogether("일일미션 / 가이드 미션 오픈 !", mission.description,
                    new[] { mission.icon, mission.guideMissionIcon },
                    new[] { mission.contentName, mission.guideMissionName }, new[] { target, secondTarget },
                    () => confirmations++, () => cancellations++,
                    (index, done) => { pairArrivals++; pairDone[index] = done; });
                DOTween.Complete(view, true);
                button.onClick.Invoke();
                button.onClick.Invoke();
                DOTween.Complete(view, true);
            }
            ShowPair();
            Require(pairArrivals == 2 && confirmations == 6 && IconsVisible(2),
                $"Both icons must arrive once without finishing the step early: arrivals={pairArrivals}, confirmations={confirmations}, visible={IconsVisible(2)}, guideIcon={mission.guideMissionIcon}.");
            Require(Vector3.Distance(firstIcon.rectTransform.TransformPoint(firstIcon.rectTransform.rect.center),
                target.TransformPoint(target.rect.center)) < 0.1f
                && Vector3.Distance(secondIcon.rectTransform.TransformPoint(secondIcon.rectTransform.rect.center),
                secondTarget.TransformPoint(secondTarget.rect.center)) < 0.1f,
                "Each icon must reach its own mission button.");
            pairDone[1]();
            pairDone[1]();
            Require(confirmations == 6 && ContentUnlockIntroView.IsOpen,
                "One completed effect must not finish the pair or release input.");
            pairDone[0]();
            Require(confirmations == 7 && !ContentUnlockIntroView.IsOpen
                && firstIcon.transform.parent == iconRoot && secondIcon.transform.parent == iconRoot,
                "Both effects must finish before restoring the two icons and completing once.");
            ShowPair();
            pairDone[0]();
            view.Close();
            pairDone[1]();
            Require(confirmations == 7 && cancellations == 5, "Cancelled pair accepted a stale effect callback.");
            secondTargetObject.SetActive(false);
            ShowPair();
            Require(pairDone[1] == null && pairDone[0] != null, "Hidden guide button must skip only its own effect.");
            pairDone[0]();
            Require(confirmations == 8 && !ContentUnlockIntroView.IsOpen, "Remaining mission effect did not finish.");
            Debug.Log("[ContentUnlockIntroValidation] VIEW PASS: single/pair flights, separate destinations, all-effects completion, queue, cancellation, restoration, inactive targets, scale.");
        }
        finally
        {
            try
            {
                if (instance != null)
                {
                    var view = instance.GetComponent<ContentUnlockIntroView>();
                    view.Close();
                    DOTween.Kill(view);
                }
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                if (dimInstance != null)
                {
                    ScreenDimValidation.InvokeLifecycle(dimInstance.GetComponent<ScreenDim>(), "OnDestroy");
                    UnityEngine.Object.DestroyImmediate(dimInstance);
                }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }

    static bool HasIntroError(OutgameTutorialData data, ContentUnlockData unlocks)
    {
        foreach (var issue in TutorialValidator.Validate(data, unlocks))
            if (issue.Level == ETutorialIssueLevel.Error && issue.Rule.StartsWith("해금 소개")) return true;
        return false;
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class CountingSink : ITutorialProgressSink
    {
        public int Writes { get; private set; }
        public void Commit(int chapter, int step) => Writes++;
        public void Complete() => Writes++;
    }
}
#endif
