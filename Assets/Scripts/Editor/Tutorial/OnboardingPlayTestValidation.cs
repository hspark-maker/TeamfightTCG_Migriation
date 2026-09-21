#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>온보딩 연습의 계정 격리·샤드 누적·늦은 서버 응답 차단을 메모리에서 검증한다.</summary>
public static class OnboardingPlayTestValidation
{
    const int CARD_ID = 900001;
    const BindingFlags STATIC_FIELDS = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>에셋이나 서버를 변경하지 않고 격리 회귀를 실행한다.</summary>
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !OnboardingPlayTest.IsActive,
            "Stop play mode before validation.");
        List<Action> t_restore = new List<Action>();
        Type t_commands = typeof(DataSaveManager).Assembly.GetType("ServerSaveCommands", true);
        try
        {
            Replace(t_restore, typeof(OnboardingPlayTest), "<IsPreparing>k__BackingField", true);
            ExpectBlocked(() => OnboardingPlayTest.BeginAsync().GetAwaiter().GetResult());
            Set(typeof(OnboardingPlayTest), "<IsPreparing>k__BackingField", false);
            Replace(t_restore, typeof(OnboardingPlayTest), "<IsActive>k__BackingField", true);

            var t_service = new RecordingService();
            Replace(t_restore, t_commands, "s_service", t_service);
            MethodInfo t_read = t_commands.GetMethod("InvokeReadOnlyAsync", STATIC_FIELDS).MakeGenericMethod(typeof(object));
            ExpectBlocked(() => ((UniTask<object>)t_read.Invoke(null, new object[] { "previewValidation", null }))
                .GetAwaiter().GetResult());
            Require(t_service.Calls == 0, "An active preview reached the callable service.");
            Set(typeof(OnboardingPlayTest), "<IsActive>k__BackingField", false);
            UniTask<object> t_late = (UniTask<object>)t_read.Invoke(null, new object[] { "previewValidation", null });
            Set(typeof(OnboardingPlayTest), "<IsActive>k__BackingField", true);
            t_service.Completion.TrySetResult(new object());
            ExpectBlocked(() => t_late.GetAwaiter().GetResult());

            Replace(t_restore, typeof(GrowthSpec), "s_loaded", true);
            Replace(t_restore, typeof(GrowthSpec), "s_hasCardRule", true);
            Replace(t_restore, typeof(GrowthSpec), "s_cardMaxLevel", 4);
            Replace(t_restore, typeof(GrowthSpec), "s_baseEnhanceCost", 10L);
            Replace(t_restore, typeof(GrowthSpec), "s_costGrowthPerLevel", 0L);
            ClearDictionary(t_restore, typeof(GrowthSpec), "s_cardCosts");
            Replace(t_restore, typeof(CardCatalog), "<IsReady>k__BackingField", true);
            ClearDictionary(t_restore, typeof(CardCatalog), "s_specById");
            var t_specs = (Dictionary<int, CardSpec>)Get(typeof(CardCatalog), "s_specById");
            t_specs[CARD_ID] = new CardSpec(CARD_ID, "PlayTestValidation", "PlayTestValidation", default,
                10, CardKeyword.Ranged, 3, 0, 10, 10, 10, "", default, Array.Empty<string>());
            var t_included = (HashSet<int>)Get(typeof(CardCatalog), "s_includedIds");
            if (t_included.Add(CARD_ID)) t_restore.Add(() => t_included.Remove(CARD_ID));
            Replace(t_restore, typeof(CardGrowthManager), "s_initialized", true);
            Replace(t_restore, typeof(CardGrowthManager), "OnGrowthChanged", null);
            ClearDictionary(t_restore, typeof(CardGrowthManager), "s_growth");
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_guidedChapter", -1);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_data", null);
            Replace(t_restore, typeof(CurrencyManager), "OnCurrencyChanged", null);
            Replace(t_restore, typeof(CurrencyManager), "OnCurrencySpent", null);
            ClearArray(t_restore, typeof(CurrencyManager), "s_currencies");
            ClearArray(t_restore, typeof(CurrencyManager), "s_pending");
            CurrencyManager.Adopt(new Dictionary<string, long> { [ECurrencyType.Shard.ToString()] = 100 });

            var t_original = new UserSaveData();
            t_original.CardGrowth.Entries[CARD_ID.ToString()] = new CardGrowthEntry { Level = 1 };
            var t_clone = JsonConvert.DeserializeObject<UserSaveData>(JsonConvert.SerializeObject(t_original));
            Replace(t_restore, typeof(DataSaveManager), "<Data>k__BackingField", t_clone);
            var t_growth = (Dictionary<int, CardGrowthEntry>)Get(typeof(CardGrowthManager), "s_growth");
            t_growth[CARD_ID] = t_clone.CardGrowth.Entries[CARD_ID.ToString()];
            var t_data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
                "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
            Require(t_data != null, "Onboarding asset missing.");
            Set(typeof(OutgameTutorialRunner), "s_data", t_data);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_forcedCount", t_data.FtueChapterCount);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_guidedStep", 0);
            Replace(t_restore, typeof(OutgameTutorialGuide), "s_enhanceCard", CARD_ID);
            Require(OutgameTutorialRunner.TryGetGuidedChapter(EOutgameTutorialTrigger.SynergyGrowthIntroduction,
                out int t_chapter, out _), "Growth introduction missing.");
            Set(typeof(OutgameTutorialRunner), "s_guidedChapter", t_chapter);

            EnhanceResult t_partial = CardGrowthManager.TryEnhanceAsync(CARD_ID, 1).GetAwaiter().GetResult();
            Require(t_partial.Outcome == EEnhanceOutcome.Success && t_partial.Level == 1
                && t_partial.AppliedShards == 1 && CardGrowthManager.ShardProgressOf(CARD_ID) == 1
                && CurrencyManager.Shard == 99, "Partial enhancement skipped progress or charged the wrong amount.");
            Require(GrowthStar.FromLevel(t_partial.Level) < 2, "A partial shard fulfilled the two-star target.");
            Require(OutgameTutorialGuide.NeedsMoreSynergyGrowth, "Partial shards completed growth onboarding.");
            EnhanceResult t_firstStar = CardGrowthManager.TryEnhanceAsync(CARD_ID, 150).GetAwaiter().GetResult();
            Require(t_firstStar.Level == 2 && t_firstStar.AppliedShards == 9 && CurrencyManager.Shard == 90
                && CardGrowthManager.ShardProgressOf(CARD_ID) == 0, "Enhancement crossed the next-star boundary.");
            Require(GrowthStar.FromLevel(t_firstStar.Level) < 2, "One star fulfilled the two-star target.");
            Require(OutgameTutorialGuide.NeedsMoreSynergyGrowth, "One star completed growth onboarding.");
            EnhanceResult t_secondStar = CardGrowthManager.TryEnhanceAsync(CARD_ID, 150).GetAwaiter().GetResult();
            Require(t_secondStar.Level == 3 && t_secondStar.AppliedShards == 10 && CurrencyManager.Shard == 80
                && GrowthStar.FromLevel(t_secondStar.Level) == 2
                && CardGrowthManager.GrowthOf(CARD_ID).UnlockedKeywords == CardKeyword.Ranged,
                "Two-star enhancement did not unlock the authored keyword.");
            Require(!OutgameTutorialGuide.NeedsMoreSynergyGrowth, "Two stars did not satisfy growth onboarding.");
            Require(t_original.CardGrowth.Entries[CARD_ID.ToString()].Level == 1
                && t_original.CardGrowth.Entries[CARD_ID.ToString()].ShardProgress == 0,
                "Preview growth mutated the original save.");
            Require(t_service.Calls == 1, "Fake enhancement called the server.");
            Type t_grants = typeof(DataSaveManager).Assembly.GetType("TutorialGrantsCloud", true);
            Replace(t_restore, t_grants, "<EnhanceCardSpent>k__BackingField", true);
            Replace(t_restore, typeof(OutgameTutorialGuide), "s_freeSpentStep", null);
            Replace(t_restore, typeof(OutgameTutorialGuide), "s_growthAlreadyReached", false);
            t_growth[CARD_ID] = new CardGrowthEntry { Level = 1 };
            CurrencyManager.Adopt(new Dictionary<string, long> { [ECurrencyType.Shard.ToString()] = 0 });
            OutgameTutorialRunner.TryGetGuidedChapter(EOutgameTutorialTrigger.SynergyGrowthIntroduction,
                out _, out var t_growthChapter);
            int t_enhanceIndex = -1;
            for (int t_i = 0; t_i < t_growthChapter.StepCount; t_i++)
                if (t_growthChapter.TryGetStep(t_i, out var t_step) && t_step.StepId == 67) t_enhanceIndex = t_i;
            Require(t_enhanceIndex >= 0, "Free synergy enhancement step missing.");
            Set(typeof(OutgameTutorialRunner), "s_guidedStep", t_enhanceIndex);
            Require(OutgameTutorialGuide.HasFreeCardEnhance(CARD_ID)
                && !OutgameTutorialGuide.HasFreeCardEnhance(CARD_ID + 1), "Free support must target only the guided card.");
            Require(CardGrowthManager.PreviewGrowthAfterEnhance(CARD_ID).Level == 3
                && CardGrowthManager.TryGetEvolutionStep(CARD_ID, out var t_freePreview)
                && t_freePreview.Level == 3 && t_freePreview.Cost == 0,
                "Free introduction preview must show two stars in one enhancement.");
            var t_freeFirst = CardGrowthManager.TryEnhanceAsync(CARD_ID).GetAwaiter().GetResult();
            Require(t_freeFirst.Outcome == EEnhanceOutcome.Success && t_freeFirst.Level == 3
                && t_freeFirst.AppliedShards == 20 && CardGrowthManager.ShardProgressOf(CARD_ID) == 0
                && CurrencyManager.Shard == 0 && !OutgameTutorialGuide.HasFreeCardEnhance(CARD_ID),
                "Earlier tutorial grant consumption or zero balance blocked free synergy growth.");
            t_growth[CARD_ID] = new CardGrowthEntry { Level = 2, ShardProgress = 4 };
            var t_freeSecond = CardGrowthManager.TryEnhanceAsync(CARD_ID).GetAwaiter().GetResult();
            Require(t_freeSecond.Outcome == EEnhanceOutcome.Success && t_freeSecond.Level == 3
                && t_freeSecond.AppliedShards == 6
                && CurrencyManager.Shard == 0 && !OutgameTutorialGuide.HasFreeCardEnhance(CARD_ID),
                "Synergy support must stop at two stars without charging the wallet.");
            ValidateSaveSchema(t_clone, t_growth);
            Debug.Log("[OnboardingPlayTestValidation] PASS: entry rejection, zero-network command guard, late response guard, partial shards, star boundaries, costs, keyword unlock, original save isolation, card-only growth and save schema; unknown legacy fields ignored without regeneration. UI play is not covered.");
        }
        finally
        {
            for (int t_i = t_restore.Count - 1; t_i >= 0; t_i--) t_restore[t_i]();
        }
    }

    static void ValidateSaveSchema(UserSaveData _save, Dictionary<int, CardGrowthEntry> _growth)
    {
        // 위 fixture가 원래 static 값을 finally에서 복원한다. 서버나 스펙 산출물은 건드리지 않는다.
        Require(GrowthSpec.TryValidateRequired(out _), "Retired limit-break rules still block initialization.");
        _growth[CARD_ID] = new CardGrowthEntry { Level = 3, ShardProgress = 4 };
        _save.CardGrowth.Entries[CARD_ID.ToString()] = _growth[CARD_ID];

        CardGrowth t_current = CardGrowthManager.GrowthOf(CARD_ID);
        Require(t_current.HpBonus == 24 && t_current.UnlockedKeywords == CardKeyword.Ranged,
            "Retired growth changed HP or keyword unlock; expected 20 card + 4 partial shard HP only.");
        Require(CardGrowthManager.PreviewGrowthAtLevel(CARD_ID, 4).HpBonus == 30,
            "Enhancement preview included retired growth HP.");
        Require(CardGrowthManager.GrowthAtLevel(CARD_ID, 4).HpBonus == 30,
            "A fixed-level opponent inherited account growth.");
        Type t_aiRow = typeof(CardGrowthManager).Assembly.GetType("AiMatchCardGrowth", true);
        Type t_aiGrowth = typeof(CardGrowthManager).Assembly.GetType("AiMatchGrowth", true);
        foreach (string t_legacy in new[] { string.Empty, ",\"limitBreak\":3" })
        {
            string t_json = "[{\"cardId\":" + CARD_ID + ",\"level\":4,\"hpBonus\":30," +
                "\"evolutionStage\":2,\"unlockedKeywords\":1,\"synergyUnlocked\":true" + t_legacy + "}]";
            object t_rows = JsonConvert.DeserializeObject(t_json, t_aiRow.MakeArrayType());
            var t_ai = (IReadOnlyDictionary<int, CardGrowth>)t_aiGrowth.GetMethod("Read", STATIC_FIELDS)
                .Invoke(null, new object[] { 1, new[] { CARD_ID }, t_rows });
            Require(t_ai[CARD_ID].HpBonus == 30 && t_ai[CARD_ID].UnlockedKeywords == CardKeyword.Ranged,
                "AI growth required retired fields or changed the server's card-only snapshot.");
        }
        // JSON.NET 기본 unknown-field 무시 계약을 확인한다. 런타임 마이그레이션은 만들지 않는다.
        var t_settings = (JsonSerializerSettings)typeof(DataSaveManager)
            .GetProperty("SaveSerializerSettings", STATIC_FIELDS).GetValue(null);
        var t_old = JObject.FromObject(_save, JsonSerializer.Create(t_settings));
        t_old["keywordGrowth"] = new JObject { ["levels"] = new JObject { ["1"] = 10 } };
        var t_oldCard = (JObject)t_old["cardGrowth"]["entries"][CARD_ID.ToString()];
        t_oldCard["snack"] = 99;
        t_oldCard["limitBreak"] = 2;
        var t_copy = t_old.ToObject<UserSaveData>(JsonSerializer.Create(t_settings));
        var t_serialized = JObject.FromObject(t_copy, JsonSerializer.Create(t_settings));
        var t_card = (JObject)t_serialized["cardGrowth"]["entries"][CARD_ID.ToString()];
        Require(t_serialized["keywordGrowth"] == null && t_card["snack"] == null && t_card["limitBreak"] == null,
            "Current save writers regenerated retired growth fields.");
        Require(t_copy.CardGrowth.Entries[CARD_ID.ToString()].Level == 3
            && t_copy.CardGrowth.Entries[CARD_ID.ToString()].ShardProgress == 4,
            "Ignoring unknown fields changed current card growth.");
        Require((int)Get(typeof(DataSaveManager), "SaveSlotCount") == 8 && !Enum.IsDefined(typeof(ESaveSlot), 1 << 4)
            && (int)ESaveSlot.Rank == (1 << 5) && (int)ESaveSlot.Profile == (1 << 9),
            "Save slot removal shifted live slot IDs.");

        Type t_document = typeof(CardGrowthManager).Assembly.GetType("PlayerSaveDocument", true);
        MethodInfo t_map = t_document.GetMethod("ToSlotFieldMap", STATIC_FIELDS);
        try
        {
            t_map.Invoke(null, new object[] { t_copy, (ESaveSlot)(1 << 4), 1L });
            throw new InvalidOperationException("Retired slot generated a metadata-only save write.");
        }
        catch (TargetInvocationException t_error) when (t_error.InnerException is ArgumentOutOfRangeException) { }

    }

    static object Get(Type _type, string _field) => _type.GetField(_field, STATIC_FIELDS).GetValue(null);
    static void Set(Type _type, string _field, object _value) => _type.GetField(_field, STATIC_FIELDS).SetValue(null, _value);

    static void Replace(List<Action> _restore, Type _type, string _field, object _value)
    {
        object t_previous = Get(_type, _field);
        _restore.Add(() => Set(_type, _field, t_previous));
        Set(_type, _field, _value);
    }

    static void ClearDictionary(List<Action> _restore, Type _type, string _field)
    {
        var t_dictionary = (IDictionary)Get(_type, _field);
        var t_previous = new List<DictionaryEntry>();
        foreach (DictionaryEntry t_entry in t_dictionary) t_previous.Add(t_entry);
        _restore.Add(() =>
        {
            t_dictionary.Clear();
            foreach (DictionaryEntry t_entry in t_previous) t_dictionary.Add(t_entry.Key, t_entry.Value);
        });
        t_dictionary.Clear();
    }

    static void ClearArray(List<Action> _restore, Type _type, string _field)
    {
        var t_array = (Array)Get(_type, _field);
        var t_previous = (Array)t_array.Clone();
        _restore.Add(() => Array.Copy(t_previous, t_array, t_array.Length));
        Array.Clear(t_array, 0, t_array.Length);
    }

    static void ExpectBlocked(Action _action)
    {
        try { _action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected the preview guard to reject the operation.");
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }

    sealed class RecordingService : ICallableService
    {
        internal int Calls;
        internal readonly UniTaskCompletionSource<object> Completion = new UniTaskCompletionSource<object>();

        public async UniTask<TResponse> InvokeAsync<TResponse>(string _commandName, object _request,
            int _timeoutMilliseconds = 0) where TResponse : class
        {
            Calls++;
            return (TResponse)await Completion.Task;
        }
    }
}
#endif
