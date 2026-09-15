using System;
using System.Collections.Generic;

/// <summary>저작된 개념 설명과 이번 카드의 실제 해금 효과를 이어 붙인다.</summary>
public static class UnlockIntroduction
{
    public static bool HasPending(int _card, IReadOnlyList<UnlockIntro> _intros)
    {
        if (OutgameTutorialRunner.Data == null || !OwnershipManager.IsOwned(_card) || _intros == null) return false;
        foreach (UnlockIntro t_intro in _intros)
        {
            if (!IsUnlocked(_card, t_intro)) continue;
            if (!t_intro.IsSynergy) continue;
            if (!OutgameTutorialProgress.IsTriggerDone(EOutgameTutorialTrigger.SynergyIntroduction)) return true;
        }
        return false;
    }

    public static bool TryShow(int _card, IReadOnlyList<UnlockIntro> _intros, Action<bool> _onFinished)
    {
        OutgameTutorialData t_data = OutgameTutorialRunner.Data;
        if (_intros == null || _intros.Count == 0 || !UnlockIntroOverlay.TryGet(out var t_overlay)) return false;
        var t_pages = new List<UnlockIntroPage>();
        var t_marks = new HashSet<EOutgameTutorialTrigger>();
        foreach (UnlockIntro t_intro in _intros)
        {
            bool t_unlocked = IsUnlocked(_card, t_intro);
            var t_key = EOutgameTutorialTrigger.SynergyIntroduction;
            IReadOnlyList<GuideOnboardingPage> t_authored = t_intro.IsSynergy
                ? t_data == null ? Array.Empty<GuideOnboardingPage>() : t_data.synergyIntroduction
                : Array.Empty<GuideOnboardingPage>();
            bool t_first = t_unlocked && t_intro.IsSynergy && !OutgameTutorialProgress.IsTriggerDone(t_key) && !t_marks.Contains(t_key);
            bool t_hasEffect = false;
            foreach (GuideOnboardingPage t_page in t_authored)
            {
                if (t_page == null) continue;
                if (t_page.kind == EGuideOnboardingPage.Concept)
                {
                    if (t_first) t_pages.Add(new UnlockIntroPage(t_page.title, t_page.body));
                }
                else if (t_page.kind == EGuideOnboardingPage.Effect || t_page.kind == EGuideOnboardingPage.Demo)
                {
                    t_hasEffect = true;
                    t_pages.Add(new UnlockIntroPage(t_page.title, t_page.body, new[] { t_intro },
                        playDemo: t_page.kind == EGuideOnboardingPage.Demo));
                }
            }
            if (!t_hasEffect) t_pages.Add(new UnlockIntroPage(t_intro.Name, string.Empty, new[] { t_intro }, playDemo: true));
            if (t_first && t_authored.Count > 0) t_marks.Add(t_key);
        }
        t_overlay.ShowPages(t_pages, _card, _confirmed =>
        {
            if (_confirmed)
                foreach (var t_key in t_marks) OutgameTutorialProgress.MarkTriggerDone(t_key);
            _onFinished?.Invoke(_confirmed);
        });
        return true;
    }

    static bool IsUnlocked(int _card, UnlockIntro _intro)
        => OwnershipManager.IsOwned(_card) && (_intro.IsSynergy
            ? CardGrowthManager.GrowthOf(_card).SynergyUnlocked
            : (CardVisualRules.InfoKeywords(_card) & _intro.Keyword) != 0);
}
