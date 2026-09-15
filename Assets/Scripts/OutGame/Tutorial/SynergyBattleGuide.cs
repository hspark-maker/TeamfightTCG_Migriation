using System.Collections.Generic;

/// <summary>시너지 입문 안내의 덱 준비와 현재 편성 설명.</summary>
public static class SynergyBattleGuide
{
    public static bool IsEditorOpen => DeckEditController.OpenEditor != null;

    public static bool IsDeckReady
    {
        get
        {
            var t_editor = DeckEditController.OpenEditor;
            if (t_editor == null || !t_editor.IsSavedComplete) return false;
            foreach (var t_progress in DeckSynergyEligibility.Resolve(t_editor.WorkingCards))
                if (t_progress.IsActive) return true;
            return false;
        }
    }

    public static bool TryOpenEditor()
    {
        if (IsEditorOpen) return true;
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyDeckTab)) return false;
        if (!TutorialAnchorRegistry.TryGet(EOutgameTutorialAnchor.LobbyDeckTab, out var t_anchor, out _)) return false;
        var t_shell = t_anchor.GetComponentInParent<LobbyTabController>();
        return t_shell != null && t_shell.TrySelectFeature(EOutgameFeature.LobbyDeckTab);
    }

    public static string MessageOf(string _message)
    {
        if (string.IsNullOrEmpty(_message)) return _message;
        if (!_message.Contains("{synergyName}") && !_message.Contains("{synergyCount}")) return _message;
        var t_progress = ResolveExample();
        return _message.Replace("{synergyName}", t_progress != null ? SynergyText.Name(t_progress.Synergy) : "같은 시너지")
            .Replace("{synergyCount}", FirstRequirementOf(t_progress));
    }

    private static string FirstRequirementOf(SynergyProgress _progress)
    {
        var t_tiers = _progress?.Synergy != null ? _progress.Synergy.tiers : null;
        int t_required = int.MaxValue;
        if (t_tiers != null)
            foreach (var t_tier in t_tiers)
                if (t_tier != null && t_tier.requiredCount > 0 && t_tier.requiredCount < t_required)
                    t_required = t_tier.requiredCount;
        return t_required < int.MaxValue ? t_required.ToString() : "필요한 수의";
    }

    private static SynergyProgress ResolveExample()
    {
        var t_editor = DeckEditController.OpenEditor;
        var t_progress = DeckSynergyEligibility.Resolve(t_editor != null ? t_editor.WorkingCards : null);
        if (t_progress.Count > 0) return t_progress[0];

        var t_owned = new List<int>(OwnershipManager.OwnedIds);
        t_owned.Sort();
        t_progress = DeckSynergyEligibility.Resolve(t_owned);
        if (t_progress.Count > 0) return t_progress[0];

        // 아직 성장하지 않은 카드뿐이어도 시너지의 요구 장수를 설명할 수 있어야 한다.
        t_progress = SynergyPreview.Resolve(t_owned);
        return t_progress.Count > 0 ? t_progress[0] : null;
    }
}
