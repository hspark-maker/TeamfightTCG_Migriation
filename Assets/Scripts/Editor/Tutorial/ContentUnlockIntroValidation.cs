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
        var ids = new HashSet<int>();
        int forced = 0;
        int intros = 0;
        foreach (var chapter in data.chapters)
        {
            if (!chapter.IsGuided) forced++;
            for (int i = 0; i < chapter.StepCount; i++)
            {
                Require(chapter.TryGetStep(i, out var step) && ids.Add(step.StepId), "Duplicate step ID.");
                if (step.Action != EOutgameTutorialAction.ContentUnlockIntro) continue;
                intros++;
                Require(step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro && !step.LeavesScene,
                    "Unlock intro must wait for its own confirmation without leaving the scene.");
                var sink = new CountingSink();
                var context = new OutgameTutorialStepContext(0, 0, 0, 1, true, sink);
                Require(TutorialStepExecutor.Enter(step, context) == EOutgameTutorialStepResult.Gated
                    && sink.Writes == 0,
                    "Entering an intro must not commit or launch content.");
                foreach (var content in step.ContentIntros)
                    Require(data.TryGetContentIntro(content, out var entry) && entry.icon != null,
                        "Missing intro definition/icon.");
                if (step.ContentIntros[0] == EContentUnlockIntro.Ranked)
                {
                    Require(i > 0 && chapter.TryGetStep(i - 1, out var previous)
                        && previous.Action == EOutgameTutorialAction.EnterFirstRank, "Rank entry must precede its intro.");
                    Require(chapter.TryGetStep(i + 1, out var next)
                        && next.Action == EOutgameTutorialAction.BattleEntry, "Rank intro must preserve battle entry.");
                }
                if (step.ContentIntros[0] == EContentUnlockIntro.Adventure)
                    Require(chapter.TryGetStep(i + 1, out var next)
                        && next.Action == EOutgameTutorialAction.WaitClick, "Adventure button guide must remain a separate step.");
                if (chapter.Trigger == EOutgameTutorialTrigger.ContentUnlocksAvailable)
                    Require(chapter.StepCount == 1 && step.ContentIntros.Count == 2
                        && step.ContentIntros[0] == EContentUnlockIntro.Mission
                        && step.ContentIntros[1] == EContentUnlockIntro.Roulette, "Mission/roulette must be queued in authored order.");
            }
        }
        Require(forced == 4 && intros == 3, "Forced boundary or intro placement changed.");
        Require(ContentUnlockIntroDef.KeyOf(EContentUnlockIntro.Ranked) == null,
            "Rank presentation must not impersonate a content access key.");

        var clone = UnityEngine.Object.Instantiate(data);
        try
        {
            Require(!HasIntroError(clone), "Valid intro authoring rejected.");
            clone.contentIntros[0].icon = null;
            Require(HasIntroError(clone), "Missing icon not rejected.");
            clone.contentIntros[0].icon = data.contentIntros[0].icon;
            clone.contentIntros.Add(clone.contentIntros[0]);
            Require(HasIntroError(clone), "Duplicate definition not rejected.");
            clone.contentIntros.RemoveAt(clone.contentIntros.Count - 1);
            clone.contentIntros.Clear();
            Require(HasIntroError(clone), "Missing definition not rejected.");
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
        Debug.Log("[ContentUnlockIntroValidation] PASS: gating without commit, rank/adventure ordering, sequential intro queue, IDs, invalid authoring.");
    }

    /// <summary>사용자의 열린 씬을 유지한 채 소개 뷰의 확인·취소와 등장 입력을 검사한다.</summary>
    [MenuItem("Tools/Tutorial/Validate Content Unlock View")]
    public static void RunViewBehaviour()
    {
        Require(!EditorApplication.isPlaying, "Run isolated view validation outside play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Assets/Prefabs/UI/OverlayUI/ContentUnlockIntroView.prefab");
            Require(prefab != null, "Intro prefab missing.");
            Require(prefab.GetComponent<Canvas>() != null && prefab.transform.Find("SafeArea/Stage") != null,
                "Onboarding overlay requires its own canvas and SafeArea/Stage.");
            var dim = prefab.transform.Find("PopupDim");
            Require(dim != null && dim.GetComponent<Image>().color.a == 1f
                && dim.GetComponent<Image>().raycastTarget && dim.GetComponent<Button>() == null,
                "Reward-style PopupDim must cover input without confirming the step.");
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var view = instance.GetComponent<ContentUnlockIntroView>();
            instance.SetActive(true);
            var serialized = new SerializedObject(view);
            var button = (Button)serialized.FindProperty("_confirmButton").objectReferenceValue;
            var heading = (TMPro.TMP_Text)serialized.FindProperty("_headingText").objectReferenceValue;
            var message = (TMPro.TMP_Text)serialized.FindProperty("_messageText").objectReferenceValue;
            var data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
                "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
            Require(data.TryGetContentIntro(EContentUnlockIntro.Mission, out var mission)
                && data.TryGetContentIntro(EContentUnlockIntro.Roulette, out _), "Missing grouped definitions.");
            data.TryGetContentIntro(EContentUnlockIntro.Roulette, out var roulette);
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
            Require(instance.GetComponent<CanvasGroup>().blocksRaycasts,
                "Exit must keep the underlying lobby blocked until completion.");
            DOTween.Complete(view, true);
            Require(confirmations == 1 && cancellations == 0 && !ContentUnlockIntroView.IsOpen,
                "Confirmation must complete exactly once.");
            view.Show("모험 오픈 !", "스테이지를 클리어하고 보상을 받으세요.",
                new[] { mission.icon }, () => confirmations++, () => cancellations++);
            Require(!button.interactable, "Reopening must reset entrance input.");
            var secondIcon = (Image)serialized.FindProperty("_icons").GetArrayElementAtIndex(1).objectReferenceValue;
            Require(!secondIcon.gameObject.activeSelf, "Single intro retained second grouped icon.");
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
                Set("m_intros", new List<ContentUnlockIntroDef> { mission, roulette });
                Set("m_introIndex", 0);
                Set("m_visible", true);
                Set("m_playing", true);
                Set("m_sessionVersion", ContentUnlockManager.SessionVersion);
                Set("m_confirmed", (Action)(() => confirmations++));
                Set("m_cancelled", (Action)(() => cancellations++));
                ShowNext();
            }
            Prepare();
            Require(message.text == "미션 오픈 !" && !secondIcon.gameObject.activeSelf,
                "First queued content must have its own panel and one icon.");
            DOTween.Complete(view, true);
            button.onClick.Invoke();
            DOTween.Complete(view, true);
            Require(confirmations == 1 && !ContentUnlockIntroView.IsOpen
                && (bool)ownerType.GetField("m_pendingIntro", flags).GetValue(owner),
                "First confirmation must queue the next panel without completing the step.");
            ShowNext();
            Require(message.text == "룰렛 오픈 !" && !secondIcon.gameObject.activeSelf && !button.interactable,
                "Next content must reopen with its own title and entrance gate.");
            var firstIcon = (Image)serialized.FindProperty("_icons").GetArrayElementAtIndex(0).objectReferenceValue;
            Require(firstIcon.sprite == roulette.icon, "Next content retained the previous icon.");
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
            Debug.Log("[ContentUnlockIntroValidation] VIEW PASS: separate panels, ordered icons, final-only completion, early input, double click, cancel between panels.");
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
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }

    static bool HasIntroError(OutgameTutorialData data)
    {
        foreach (var issue in TutorialValidator.Validate(data))
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
