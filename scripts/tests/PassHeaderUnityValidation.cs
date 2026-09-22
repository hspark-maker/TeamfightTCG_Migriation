#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEditor;

public partial class PassPanel
{
    public static string ValidateHeaderProgress()
    {
        if (PassManager.IsReady || Application.isPlaying) throw new InvalidOperationException("Idle editor only");
        GameObject instance = null;
        int checks = 0;
        Action<bool, string> check = (ok, label) => { if (!ok) throw new Exception(label); checks++; };
        float timeScale = Time.timeScale;
        try
        {
            var snapshot = new PassGetResponse
            {
                Season = new PassSeasonDefinition { SeasonId = "header-preview", DisplayName = "패스", EndAtMs = long.MaxValue },
                Progress = new PassProgress { SeasonId = "header-preview", Exp = 125 },
                Levels = new List<PassLevelDefinition>()
            };
            for (int i = 0; i < 10; i++) snapshot.Levels.Add(new PassLevelDefinition { Level = i + 1, RequiredExp = i * 100 });
            PassManager.Adopt(snapshot);
            PassManager.Adopt(snapshot.Progress);
            instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/PooledUI/PassOverlay.prefab"));
            var panel = instance.GetComponent<PassPanel>();
            Vector3 badgeScale = panel.expLevelBadge.localScale;
            panel.contents.SetActive(true);
            panel.Rebuild();
            check(panel._displayExp == 100 && panel.HasProgressToPlay, "First entry starts in current level");
            check(!panel.expFill.gameObject.activeSelf && panel.expFill.rectTransform.anchorMax.x == 0f, "Zero progress has no fill");
            panel.PlayProgress();
            var first = panel._progressSequence;
            panel.RefreshHeader(); panel.Rebuild();
            check(first == panel._progressSequence, "Periodic refresh must not restart");
            first.Goto(0.08f, false);
            double partial = panel._displayExp;
            check(partial > 100 && partial < 125, "First entry interpolates");
            panel.StopProgress(); panel.Rebuild();
            check(panel._displayExp == partial, "Reentry preserves partial display");
            panel.PlayProgress(); panel._progressSequence.Complete(true);
            check(panel._displayExp == 125 && Mathf.Abs(panel.expFill.rectTransform.anchorMax.x - 0.25f) < 0.001f, "Fill matches actual progress");
            panel.Rebuild(); check(!panel.HasProgressToPlay, "Unchanged reentry has no replay");
            snapshot.Progress.Exp = 200; PassManager.Adopt(snapshot.Progress); panel.Rebuild(); panel.PlayProgress();
            panel._progressSequence.Goto(0.34f, false);
            check(panel._headerBoundaryHeld && panel.expFill.rectTransform.anchorMax.x == 1f, "Boundary holds full fill");
            panel._progressSequence.Goto(0.4f, false);
            check(panel.expLevelBadge.localScale.x > badgeScale.x, "Parent seek advances arrival tween");
            panel._progressSequence.Complete(true);
            check(panel._displayExp == 200 && !panel.expFill.gameObject.activeSelf && panel.expFill.rectTransform.anchorMax.x == 0f, "Exact threshold returns to empty fill");
            check(panel.levelText.text == "3" && panel.expText.text == "0 / 100", "Labels match next level");
            panel.StopProgress();
            check(panel.expLevelBadge.localScale == badgeScale, "Badge settles");
            snapshot.Progress.Exp = 300; PassManager.Adopt(snapshot.Progress); panel.Rebuild(); panel.PlayProgress();
            panel._progressSequence.Goto(0.5f, false);
            check(panel.expLevelBadge.localScale.x > badgeScale.x, "Arrival active before interruption");
            panel.StopProgress();
            check(panel.expLevelBadge.localScale == badgeScale && !panel._headerBoundaryHeld, "Parent cancellation restores arrival and clears hold");
            snapshot.Progress.Exp = 850; PassManager.Adopt(snapshot.Progress); panel.Rebuild(); panel.PlayProgress();
            check(panel._progressSequence.Duration() <= 2.501f, "Multi-level cap");
            panel._progressSequence.Goto(0.1f, false); partial = panel._displayExp;
            snapshot.Progress.Exp = 880; PassManager.Adopt(snapshot.Progress); panel.Rebuild();
            check(panel._displayExp == partial && panel._progressSequence == null, "New target starts at current display");
            Time.timeScale = 0f;
            panel.PlayProgress(); panel._progressSequence.SetUpdate(UpdateType.Manual, true);
            DOTween.ManualUpdate(3f, 3f);
            check(panel._displayExp == 880, "Unscaled progression completes");
            panel.StopProgress();
            snapshot.Progress.Exp = 30; PassManager.Adopt(snapshot.Progress); panel.Rebuild();
            check(panel._displayExp == 30 && !panel.HasProgressToPlay, "Decreased progress snaps");
            snapshot.Season.SeasonId = "next"; snapshot.Progress.Exp = 350; PassManager.Adopt(snapshot.Progress); panel.Rebuild();
            check(panel._displayExp == 300, "Season resets history to current interval");
            panel._progressUser = "different-user"; snapshot.Progress.Exp = 470; PassManager.Adopt(snapshot.Progress); panel.Rebuild();
            check(panel._displayExp == 400, "Account resets history");
            snapshot.Progress.Exp = 900; PassManager.Adopt(snapshot.Progress); panel.Rebuild(); panel.PlayProgress(); panel._progressSequence.Complete(true);
            check(panel.expText.text == "MAX" && panel.expFill.rectTransform.anchorMax.x == 1f, "No repeat ends full MAX");
            snapshot.Repeat = new PassRepeatDefinition { RequiredExp = 100, Reward = new List<ClaimRewardGain> { new ClaimRewardGain { Currency = "Gold", Amount = 1 } } };
            snapshot.Progress.Exp = 1100; PassManager.Adopt(snapshot.Progress); panel.Rebuild(); panel.PlayProgress(); panel._progressSequence.Complete(true);
            check(panel._displayExp == 1100 && panel.expText.text == "0 / 100", "Repeat boundary resets");
            snapshot.Progress.Exp = 100000000; PassManager.Adopt(snapshot.Progress); panel.Rebuild(); panel.PlayProgress();
            check(panel._progressSequence.Duration() <= 2.501f, "Large repeat gain stays bounded");
            panel._progressSequence.Complete(true);
            panel.StopProgress();
            check(panel.expLevelBadge.localScale == badgeScale, "Closing resets badge");
            snapshot.Season = null; PassManager.Adopt(snapshot); panel.Rebuild();
            check(!panel.expFill.gameObject.activeSelf && panel.levelText.text == "", "No season clears header");
            return checks + " header checks passed";
        }
        finally
        {
            Time.timeScale = timeScale;
            if (instance != null) { instance.GetComponent<PassPanel>().StopProgress(); UnityEngine.Object.DestroyImmediate(instance); }
            PassManager.Clear();
        }
    }
}
#endif
