using System;

/// <summary>순수 입력 정책의 소유권·인계·조작 허용 계약을 검사한다.</summary>
public static class GuidanceInputPolicyValidation
{
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Tutorial/Validate Guidance Input Policy")]
    public static void Run() => UnityEngine.Debug.Log($"[GuidanceInputPolicyValidation] PASS: {Validate()} checks");
#endif

    public static int Validate()
    {
        int checks = 0;
        void Require(bool condition, string reason)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(reason);
        }
        var policy = new GuidanceInputPolicy();
        var owner = new object();
        Require(policy.Mode == EGuidanceInputMode.Free, "Initial state must be free.");
        var first = policy.Acquire(owner);
        Require(policy.Mode == EGuidanceInputMode.Transition, "Acquire must block immediately.");
        foreach (EGuidanceInputAction action in Enum.GetValues(typeof(EGuidanceInputAction)))
        {
            Require(!policy.Allows(action, EOutgameTutorialAnchor.LobbyPlayButton), "Transition leaked " + action);
            Require(policy.Allows(action, EOutgameTutorialAnchor.None, true), "Internal restoration blocked " + action);
        }
        first.Set(EGuidanceInputMode.Target, EOutgameTutorialAnchor.LobbyCollectionTab, EOutgameTutorialCompletion.Click);
        Require(policy.Allows(EGuidanceInputAction.Navigate, EOutgameTutorialAnchor.LobbyCollectionTab), "Target tab blocked.");
        Require(!policy.Allows(EGuidanceInputAction.Navigate, EOutgameTutorialAnchor.LobbyDeckTab), "Unrelated tab leaked.");
        Require(!policy.Allows(EGuidanceInputAction.Activate, EOutgameTutorialAnchor.None), "Settings leaked.");
        Require(!policy.Allows(EGuidanceInputAction.Equip, EOutgameTutorialAnchor.LobbyCollectionTab), "Wrong action on same anchor leaked.");
        var next = policy.Acquire(owner);
        first.Set(EGuidanceInputMode.Free);
        first.Dispose();
        Require(!first.IsCurrent && next.IsCurrent && policy.Mode == EGuidanceInputMode.Transition, "Stale completion released successor.");
        var presentation = policy.Acquire(new object());
        next.Dispose();
        Require(policy.Mode == EGuidanceInputMode.Transition, "One owner released another owner's lock.");
        presentation.Dispose();
        Require(policy.Mode == EGuidanceInputMode.Free, "Completion did not restore input.");
        var guide = policy.Acquire(owner);
        guide.Set(EGuidanceInputMode.Target, EOutgameTutorialAnchor.DeckEditCollectionCard, EOutgameTutorialCompletion.DeckEquip);
        Require(policy.Allows(EGuidanceInputAction.Equip, EOutgameTutorialAnchor.DeckEditCollectionCard), "Equip/drag blocked.");
        Require(!policy.Allows(EGuidanceInputAction.SaveDeck, EOutgameTutorialAnchor.DeckEditCollectionCard), "Equip allowed save.");
        Require(!policy.Allows(EGuidanceInputAction.Navigate, EOutgameTutorialAnchor.DeckEditCollectionCard), "Equip allowed navigation.");
        guide.Set(EGuidanceInputMode.Target, EOutgameTutorialAnchor.DeckEditSaveButton, EOutgameTutorialCompletion.DeckSave);
        Require(policy.Allows(EGuidanceInputAction.SaveDeck, EOutgameTutorialAnchor.DeckEditSaveButton), "Deck save blocked.");
        guide.Set(EGuidanceInputMode.Target, completion: EOutgameTutorialCompletion.CardDetailReturn);
        Require(policy.Allows(EGuidanceInputAction.ReturnCardDetail, EOutgameTutorialAnchor.None), "Detail return blocked.");
        Require(!policy.Allows(EGuidanceInputAction.ReturnAlbum, EOutgameTutorialAnchor.None), "Unrelated album exit leaked.");
        guide.Set(EGuidanceInputMode.Target, completion: EOutgameTutorialCompletion.LobbyReturn);
        Require(policy.Allows(EGuidanceInputAction.ReturnCardDetail, EOutgameTutorialAnchor.None)
            && policy.Allows(EGuidanceInputAction.ReturnAlbum, EOutgameTutorialAnchor.None), "Lobby return blocked.");
        guide.Set(EGuidanceInputMode.Target, completion: EOutgameTutorialCompletion.SynergyDeck);
        Require(policy.Allows(EGuidanceInputAction.Equip, EOutgameTutorialAnchor.DeckEditCollectionCard)
            && policy.Allows(EGuidanceInputAction.SaveDeck, EOutgameTutorialAnchor.DeckEditSaveButton)
            && policy.Allows(EGuidanceInputAction.EditDeck, EOutgameTutorialAnchor.None), "Synergy editing blocked.");
        Require(!policy.Allows(EGuidanceInputAction.Navigate, EOutgameTutorialAnchor.LobbyMatchTab), "Synergy editing allowed departure.");
        guide.Set(EGuidanceInputMode.Transition);
        Require(!policy.Allows(EGuidanceInputAction.ReturnAlbum, EOutgameTutorialAnchor.None), "Saving/restore allowed departure.");
        var resumed = policy.Acquire(owner);
        guide.Dispose();
        Require(resumed.IsCurrent && policy.Mode == EGuidanceInputMode.Transition, "Pause/retry predecessor released restored flow.");
        policy.Clear();
        resumed.Set(EGuidanceInputMode.Target);
        Require(!resumed.IsCurrent && policy.Mode == EGuidanceInputMode.Free, "Old account restored its lock.");
        return checks;
    }
}
