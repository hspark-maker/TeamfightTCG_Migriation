using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using TeamfightTCG.BattleCore;
using UnityEditor;
using UnityEngine;

/// <summary>부활의 HP 복구와 일반 회복 파티클을 구분한다. 실제 전투·세이브는 변경하지 않는다.</summary>
public static class ImmortalHealVfxValidation
{
    [MenuItem("Tools/Battle/Validate Immortal Heal VFX")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Run outside play mode.");
        ExecutionContext.Run(ExecutionContext.Capture(), _ => RunCore(), null);
    }

    static void RunCore()
    {
        SynergyRuleProvider.InstallScoped(new TestProvider());
        var card = new CardInstance(1, 0) { hp = 0, slotIndex = 0, isRevealed = true };
        var field = new BattleFieldState();
        field.Reset(0, null);
        field.SetSlot(0, card);
        BattleEvent[] events;
        using (var capture = BattleEventStream.BeginCapture())
        {
            AttackProcessor.RemoveDead(field);
            events = capture.ToArray();
        }
        Require(card.hp == 5 && card.reviveUsed && field.GetSlot(0) == card, "Half-health revival must retain the slot.");
        BattleEvent heal = default;
        int heals = 0, revives = 0;
        foreach (var entry in events)
        {
            if (entry.Kind == BattleEventKind.Heal) { heal = entry; heals++; }
            if (entry.Kind == BattleEventKind.Revive) revives++;
        }
        Require(heals == 1 && revives == 1 && (heal.Flags & BattleEventFlags.Revival) != 0,
            "Revival must restore HP once and retain its dedicated effect event.");
        Require(BattleReplayStats.Capture(events, null, 0).HealedByOwner[0] == 5, "Existing recovery statistics must be preserved.");
        card.hp = 0;
        Require(!card.ReviveAtHalf(), "Revival must remain once per battle.");
        card.hp = 5;

        var host = new GameObject("ImmortalHealVfxValidation");
        host.SetActive(false);
        var view = host.AddComponent<CardView>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(CardView).GetField(name, flags).SetValue(view, value);
        int Read(string name) => (int)typeof(CardView).GetField(name, flags).GetValue(view);
        try
        {
            Set("boundCard", card);
            Set("shownHp", 5);
            typeof(CardView).GetMethod("BeginHealEffectDeferral", flags).Invoke(view, null);
            BattleEventPresenter.Present(heal, view);
            Require(Read("hpDisplayTarget") == 5 && Read("deferredHealEffectCount") == 0,
                "Revival must update HP without queuing a heal particle after the attack.");
            using (var capture = BattleEventStream.BeginCapture())
            {
                card.Heal(1);
                Set("shownHp", 6);
                BattleEventPresenter.Present(capture.Events[0], view);
                Require((capture.Events[0].Flags & BattleEventFlags.Revival) == 0,
                    "A genuine heal after revival must still have its normal effect.");
            }
            Require(Read("hpDisplayTarget") == 6 && Read("deferredHealEffectCount") == 1,
                "Normal healing must retain HP display and deferred particles.");
            using (var capture = BattleEventStream.BeginCapture())
            {
                card.Heal(1, _showEffect: false);
                BattleEventPresenter.Present(capture.Events[0], view);
                Require(Read("hpPendingHeal") == 1 && Read("deferredHealEffectCount") == 1,
                    "Projectile healing must wait for arrival.");
            }
            Set("shownHp", 7);
            view.PlayHealEffect(1, _consumeDeferred: true);
            Require(Read("hpDisplayTarget") == 7 && Read("hpPendingHeal") == 0 && Read("deferredHealEffectCount") == 2,
                "Projectile arrival must retain its normal heal effect.");
            Debug.Log("[ImmortalHealVfxValidation] PASS: revive HP / revive VFX / no extra heal VFX / once-only / statistics / normal and deferred heals.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class TestProvider : ISynergyRuleProvider
    {
        readonly CardSpec spec = new CardSpec(1, "ImmortalTest", "Immortal test", default, 10,
            CardKeyword.Immortal, 0, 0, 0, 0, 0, "", default, Array.Empty<string>());
        public bool ContainsCard(int cardId) => cardId == 1;
        public CardSpec SpecOf(int cardId) => spec;
        public IReadOnlyList<string> SynergyIdsOf(int cardId) => Array.Empty<string>();
        public IReadOnlyList<SynergyTier> TiersOf(string synergyId) => Array.Empty<SynergyTier>();
    }
}
