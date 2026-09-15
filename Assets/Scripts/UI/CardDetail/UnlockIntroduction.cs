using System;
using System.Collections.Generic;

/// <summary>해금 효과 재생 중 최초 시너지 설명을 튜토리얼 배너로 안내한다.</summary>
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
        string t_message = HasPending(_card, _intros) ? t_data.synergyIntroductionMessage : null;
        bool t_first = !string.IsNullOrEmpty(t_message);
        t_overlay.Show(_intros, _card, _confirmed =>
        {
            if (_confirmed && t_first)
                OutgameTutorialProgress.MarkTriggerDone(EOutgameTutorialTrigger.SynergyIntroduction);
            _onFinished?.Invoke(_confirmed);
        }, t_message);
        return true;
    }

    static bool IsUnlocked(int _card, UnlockIntro _intro)
        => OwnershipManager.IsOwned(_card) && (_intro.IsSynergy
            ? CardGrowthManager.GrowthOf(_card).SynergyUnlocked
            : (CardVisualRules.InfoKeywords(_card) & _intro.Keyword) != 0);
}
