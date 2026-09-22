using System;
using System.Collections.Generic;

public enum EGuidanceInputMode { Free, Target, Transition }
public enum EGuidanceInputAction { Activate, Navigate, Equip, SaveDeck, EditDeck, ReturnCardDetail, ReturnAlbum }

/// <summary>안내 실행별 입력 소유권과 허용 조작을 관리한다.</summary>
public sealed class GuidanceInputPolicy
{
    readonly Dictionary<object, Lease> m_owners = new Dictionary<object, Lease>();

    public EGuidanceInputMode Mode
    {
        get
        {
            var mode = EGuidanceInputMode.Free;
            foreach (var lease in m_owners.Values)
                if (lease.Mode > mode) mode = lease.Mode;
            return mode;
        }
    }

    public Lease Acquire(object owner)
    {
        var lease = new Lease(this, owner);
        m_owners[owner] = lease;
        return lease;
    }

    public bool Allows(EGuidanceInputAction action, EOutgameTutorialAnchor anchor, bool internalNavigation = false)
    {
        if (internalNavigation) return true;
        foreach (var lease in m_owners.Values)
            if (!lease.Allows(action, anchor)) return false;
        return true;
    }

    public void Clear() => m_owners.Clear();

    public sealed class Lease : IDisposable
    {
        readonly GuidanceInputPolicy m_policy;
        readonly object m_owner;
        EOutgameTutorialAnchor m_anchor;
        EOutgameTutorialCompletion m_completion;
        public EGuidanceInputMode Mode { get; private set; } = EGuidanceInputMode.Transition;
        public bool IsCurrent => m_policy.m_owners.TryGetValue(m_owner, out var current) && ReferenceEquals(current, this);

        internal Lease(GuidanceInputPolicy policy, object owner) { m_policy = policy; m_owner = owner; }

        public void Set(EGuidanceInputMode mode, EOutgameTutorialAnchor anchor = EOutgameTutorialAnchor.None,
            EOutgameTutorialCompletion completion = default)
        {
            if (!IsCurrent) return;
            Mode = mode;
            m_anchor = anchor;
            m_completion = completion;
        }

        public void Dispose()
        {
            if (IsCurrent) m_policy.m_owners.Remove(m_owner);
        }

        internal bool Allows(EGuidanceInputAction action, EOutgameTutorialAnchor anchor)
        {
            if (Mode == EGuidanceInputMode.Free) return true;
            if (Mode == EGuidanceInputMode.Transition) return false;
            if (action == EGuidanceInputAction.ReturnCardDetail)
                return m_completion == EOutgameTutorialCompletion.CardDetailReturn || m_completion == EOutgameTutorialCompletion.LobbyReturn;
            if (action == EGuidanceInputAction.ReturnAlbum) return m_completion == EOutgameTutorialCompletion.LobbyReturn;
            if (m_completion == EOutgameTutorialCompletion.SynergyDeck)
                return action == EGuidanceInputAction.Equip || action == EGuidanceInputAction.SaveDeck
                    || action == EGuidanceInputAction.EditDeck;
            if (anchor == EOutgameTutorialAnchor.None || anchor != m_anchor) return false;
            if (action == EGuidanceInputAction.Navigate) return m_completion == EOutgameTutorialCompletion.Click;
            if (action == EGuidanceInputAction.EditDeck) return m_completion == EOutgameTutorialCompletion.Click;
            if (action == EGuidanceInputAction.Equip) return m_completion == EOutgameTutorialCompletion.DeckEquip;
            if (action == EGuidanceInputAction.SaveDeck) return m_completion == EOutgameTutorialCompletion.DeckSave;
            return m_completion == EOutgameTutorialCompletion.Click || m_completion == EOutgameTutorialCompletion.Purchase
                || m_completion == EOutgameTutorialCompletion.Enhance || m_completion == EOutgameTutorialCompletion.KeywordEnhance
                || m_completion == EOutgameTutorialCompletion.DeckEquip || m_completion == EOutgameTutorialCompletion.DeckSave;
        }
    }
}
