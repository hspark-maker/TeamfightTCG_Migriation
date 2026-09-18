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
                    Require(ContentUnlockAuthoring.Data.TryGetContentIntro(content, out var entry) && entry.TryValidate(out _),
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
            clone.contentIntros[0].items[0].icon = null;
            Require(HasIntroError(data, clone), "Missing icon not rejected.");
            clone.contentIntros[0].items[0].icon = ContentUnlockAuthoring.Data.contentIntros[0].items[0].icon;
            var single = clone.contentIntros[0];
            var originalItems = single.items;
            Require(clone.TryGetContentIntro(EContentUnlockIntro.Mission, out var grouped), "Missing grouped intro.");
            single.items = new[] { grouped.items[2], grouped.items[0], grouped.items[1] };
            Require(!HasIntroError(data, clone), "Grouped items must work for non-mission content in authored order.");
            single.items = new[] { grouped.items[0], grouped.items[0] };
            Require(HasIntroError(data, clone), "Duplicate destinations not rejected.");
            single.items = Array.Empty<ContentUnlockIntroItem>();
            Require(HasIntroError(data, clone), "Empty items not rejected.");
            single.items = new[] { grouped.items[0], grouped.items[1], grouped.items[2], originalItems[0] };
            Require(HasIntroError(data, clone), "Items beyond view capacity not rejected.");
            single.items = originalItems;
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
        var presentationInstance = typeof(ContentUnlockPresentation).GetField("s_instance",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        object previousPresentation = presentationInstance.GetValue(null);
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
            var icons = new[] { mission.items[0].icon };
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
            DOTween.Goto(view, 0.6f, false);
            Require(button.interactable, "Confirmation must become available within 0.6 seconds.");
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
                new[] { mission.items[0].icon }, () => confirmations++, () => cancellations++);
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
            presentationInstance.SetValue(null, owner);
            bool CanNavigate() => GuidanceCoordinator.AllowsUserNavigation(EOutgameTutorialAnchor.LobbyCollectionTab);
            bool navigationBeforeIntro = CanNavigate();
            Require(navigationBeforeIntro, "Isolated navigation validation requires no other tutorial navigation lock.");
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
            var reordered = new ContentUnlockIntroDef
            {
                content = EContentUnlockIntro.Roulette,
                description = mission.description,
                items = new[] { mission.items[2], mission.items[0], mission.items[1] },
            };
            Set("m_intro", view);
            Set("m_intros", new List<ContentUnlockIntroDef> { reordered });
            Set("m_introIndex", 0);
            ShowNext();
            Require(!ContentUnlockPresentation.IsPlaying && !CanNavigate(),
                "The visible intro must block tabs even without the presentation runner flag.");
            Require(message.text == reordered.items[0].name + " / " + reordered.items[1].name + " / " + reordered.items[2].name
                && IconsVisible(3), "Non-mission grouped intro must follow authored order.");
            for (int i = 0; i < reordered.items.Length; i++)
            {
                var image = (Image)serialized.FindProperty("_icons").GetArrayElementAtIndex(i).objectReferenceValue;
                var label = (TMPro.TMP_Text)serialized.FindProperty("_iconNames").GetArrayElementAtIndex(i).objectReferenceValue;
                Require(image.sprite == reordered.items[i].icon && label.text == reordered.items[i].name,
                    "Reordering must keep each name and icon together.");
            }
            view.Close();
            Prepare();
            Require(!CanNavigate(), "Lobby tab navigation must be blocked during an intro.");
            Require(message.text == mission.items[0].name + " / " + mission.items[1].name + " / " + mission.items[2].name && IconsVisible(3),
                "Mission introduction must present both mission types and attendance on one panel.");
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            Require(confirmations == 1 && !ContentUnlockIntroView.IsOpen
                && (bool)ownerType.GetField("m_pendingIntro", flags).GetValue(owner),
                "First confirmation must queue the next panel without completing the step.");
            Require(!CanNavigate(), "Closing one panel must not unlock tab navigation before the next intro.");
            ShowNext();
            Require(message.text == roulette.items[0].name && IconsVisible() && !button.interactable,
                "Next content must reopen with its own title and entrance gate.");
            var firstIcon = (Image)serialized.FindProperty("_icons").GetArrayElementAtIndex(0).objectReferenceValue;
            Require(firstIcon.sprite == roulette.items[0].icon, "Next content retained the previous icon.");
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            Require(confirmations == 1 && !ContentUnlockIntroView.IsOpen
                && (bool)ownerType.GetField("m_pendingIntro", flags).GetValue(owner),
                "Second confirmation must queue card enhancement without completing the step.");
            Require(!CanNavigate(), "Tab navigation must remain blocked between all queued intros.");
            ShowNext();
            Require(message.text == enhance.items[0].name && IconsVisible() && !button.interactable
                && firstIcon.sprite == enhance.items[0].icon,
                "Card enhancement must show its own title and icon without a lobby button target.");
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            Require(confirmations == 2 && cancellations == 1 && !ContentUnlockIntroView.IsOpen,
                "Only the last panel may complete the step, exactly once.");
            Require(CanNavigate() == navigationBeforeIntro,
                "Finishing the final intro must restore the previous navigation state.");
            Prepare();
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            owner.SetVisible(false);
            Require(confirmations == 2 && cancellations == 2
                && !(bool)ownerType.GetField("m_pendingIntro", flags).GetValue(owner),
                "Cancellation between panels must clear the remaining queue without completion.");
            Require(CanNavigate() == navigationBeforeIntro,
                "Cancelling the intro queue must restore the previous navigation state.");
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
                    new[] { mission.items[0].icon, mission.items[1].icon },
                    new[] { mission.items[0].name, mission.items[1].name }, new[] { target, secondTarget },
                    () => confirmations++, () => cancellations++,
                    (index, done) => { pairArrivals++; pairDone[index] = done; });
                DOTween.Complete(view, true);
                button.onClick.Invoke();
                button.onClick.Invoke();
                DOTween.Complete(view, true);
            }
            ShowPair();
            Require(pairArrivals == 2 && confirmations == 6 && IconsVisible(2),
                $"Both icons must arrive once without finishing the step early: arrivals={pairArrivals}, confirmations={confirmations}, visible={IconsVisible(2)}, guideIcon={mission.items[1].icon}.");
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
            var thirdIcon = (Image)serialized.FindProperty("_icons").GetArrayElementAtIndex(2).objectReferenceValue;
            var thirdTargetObject = new GameObject("AttendanceTarget", typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(thirdTargetObject, scene);
            var thirdTarget = (RectTransform)thirdTargetObject.transform;
            thirdTarget.SetParent(flightRoot, false);
            thirdTarget.sizeDelta = new Vector2(100f, 100f);
            thirdTarget.anchoredPosition = new Vector2(0f, -450f);
            secondTargetObject.SetActive(true);
            var tripleDone = new Action[3];
            int tripleArrivals = 0;
            void ShowTriple()
            {
                tripleDone = new Action[3];
                view.ShowTogether("일일미션 / 가이드미션 / 출석", mission.description,
                    new[] { mission.items[0].icon, mission.items[1].icon, mission.items[2].icon },
                    new[] { mission.items[0].name, mission.items[1].name, mission.items[2].name },
                    new[] { target, secondTarget, thirdTarget }, () => confirmations++, () => cancellations++,
                    (index, done) => { tripleArrivals++; tripleDone[index] = done; });
                DOTween.Complete(view, true);
                Require(IconsVisible(3), "All three icons must be visible.");
                Require(firstIcon.rectTransform.rect.width * 3f + 120f <= iconRoot.rect.width + 0.1f,
                    "Three icons overflow the authored row.");
                Require(firstIcon.rectTransform.anchoredPosition.x < secondIcon.rectTransform.anchoredPosition.x
                    && secondIcon.rectTransform.anchoredPosition.x < thirdIcon.rectTransform.anchoredPosition.x,
                    "Mission, guide mission and attendance order changed.");
                button.onClick.Invoke();
                button.onClick.Invoke();
                DOTween.Complete(view, true);
            }
            ShowTriple();
            Require(tripleArrivals == 3 && confirmations == 8 && ContentUnlockIntroView.IsOpen,
                "Three arrivals must wait for all button effects.");
            Require(Vector3.Distance(thirdIcon.rectTransform.TransformPoint(thirdIcon.rectTransform.rect.center),
                thirdTarget.TransformPoint(thirdTarget.rect.center)) < 0.1f, "Attendance icon missed its button.");
            tripleDone[0]();
            tripleDone[1]();
            Require(confirmations == 8 && ContentUnlockIntroView.IsOpen, "Attendance effect must finish before completion.");
            tripleDone[2]();
            tripleDone[2]();
            Require(confirmations == 9 && !ContentUnlockIntroView.IsOpen && thirdIcon.transform.parent == iconRoot,
                "Three effects must restore icons and complete exactly once.");
            ShowTriple();
            tripleDone[0]();
            view.Close();
            tripleDone[1]();
            tripleDone[2]();
            Require(confirmations == 9 && cancellations == 6 && thirdIcon.transform.parent == iconRoot,
                "Cancelled attendance arrival must not complete the intro.");
            view.Show("기존 배치 복구", "", icons, () => confirmations++, () => cancellations++);
            DOTween.Complete(view, true);
            Require(IconsVisible(1) && Mathf.Abs(firstIcon.rectTransform.rect.width - 420f) < 0.1f,
                "Single-icon layout must recover its authored size after three icons.");
            view.Close();
            Debug.Log("[ContentUnlockIntroValidation] VIEW PASS: single/pair/triple flights, attendance arrival, all-effects completion, layout restoration, cancellation, inactive targets, scale.");
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
                try
                {
                    if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                    if (dimInstance != null)
                    {
                        ScreenDimValidation.InvokeLifecycle(dimInstance.GetComponent<ScreenDim>(), "OnDestroy");
                        UnityEngine.Object.DestroyImmediate(dimInstance);
                    }
                    EditorSceneManager.ClosePreviewScene(scene);
                }
                finally { presentationInstance.SetValue(null, previousPresentation); }
            }
        }
    }

    [MenuItem("Tools/Tutorial/Validate Pending Unlock Appearance")]
    public static void RunPendingUnlockAppearance()
    {
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var initialized = typeof(ContentUnlockManager).GetField("s_initialized",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(!EditorApplication.isPlaying && !(bool)initialized.GetValue(null)
            && string.IsNullOrEmpty(FirebaseAuthService.Instance.UserId)
            && !OutgameFeatureLock.ForceUnlockAllForDebug, "Requires an uninitialized edit-mode session.");
        var previousProfile = DataSaveManager.Data.Profile;
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject host = null;
        FeatureLockView view = null;
        try
        {
            var slot = new ContentUnlockSaveData { Version = 1 };
            DataSaveManager.Data.Profile = new ProfileSaveData { ContentUnlocks = slot };
            ContentUnlockManager.Initialize(() => false); // 검증 중 저장·규칙 평가를 하지 않는다.
            host = new GameObject("PendingUnlockValidation", typeof(RectTransform), typeof(Image));
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            view = host.AddComponent<FeatureLockView>();
            var badge = new GameObject("LockBadge", typeof(RectTransform));
            badge.transform.SetParent(host.transform, false);
            typeof(FeatureLockView).GetField("m_badge", fields).SetValue(view, badge);
            typeof(FeatureLockView).GetField("m_badgeScale0", fields).SetValue(view, Vector3.one);
            view.Bind(EOutgameFeature.Adventure);
            host.SetActive(true);
            typeof(FeatureLockView).GetMethod("OnDisable", fields).Invoke(view, null);
            typeof(FeatureLockView).GetMethod("OnEnable", fields).Invoke(view, null);
            Require(badge.activeSelf && view.IsLocked, "Adventure starts locked.");

            slot.Unlocked.Add(ContentUnlockManager.ADVENTURE);
            slot.Pending.Add(ContentUnlockManager.ADVENTURE);
            OutgameFeatureLock.NotifyContentChanged();
            Require(!view.IsLocked && badge.activeSelf,
                "Pending adventure intro must keep its lock visible immediately when eligibility changes.");
            view.HoldUnlockPresentation();
            int completed = 0;
            Require(view.PresentUnlock(() => completed++), "Eligible adventure must accept its explicit arrival.");
            OutgameFeatureLock.NotifyContentChanged();
            Require(view.IsPresenting, "A pending refresh must not interrupt the explicit arrival.");
            ((Sequence)typeof(FeatureLockView).GetField("m_unlockFx", fields).GetValue(view)).Complete(true);
            OutgameFeatureLock.NotifyContentChanged();
            Require(completed == 1 && !badge.activeSelf, "Arrival must remain unlocked across refreshes.");
            view.CancelPresentation();
            Require(badge.activeSelf, "Cancelled unconfirmed intro must restore the pending lock.");
            typeof(FeatureLockView).GetMethod("OnDisable", fields).Invoke(view, null);
            typeof(FeatureLockView).GetMethod("OnEnable", fields).Invoke(view, null);
            Require(badge.activeSelf, "Returning to a tab must retain its pending lock.");
            slot.Pending.Clear();
            OutgameFeatureLock.NotifyContentChanged();
            Require(!badge.activeSelf, "Already presented content must stay unlocked.");
            Debug.Log("[ContentUnlockIntroValidation] PENDING PASS: eligibility / intro wait / arrival / refresh / cancel / re-enable / confirmed.");
        }
        finally
        {
            if (view != null) typeof(FeatureLockView).GetMethod("OnDisable", fields).Invoke(view, null);
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            ContentUnlockManager.ResetSession();
            DataSaveManager.Data.Profile = previousProfile;
            EditorSceneManager.ClosePreviewScene(scene);
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
