using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class ContentProfileValidator : IPreprocessBuildWithReport
{
    const string REGISTRY_PATH = "Assets/SO/CardRegistry.asset";
    const string LIVE_PROFILE_PATH = "Assets/Resources/ContentProfiles/Live.asset";
    const string TEST_PROFILE_PATH = "Assets/Resources/ContentProfiles/Test.asset";

    public int callbackOrder => 0;

    [MenuItem("Tools/Card Battle/Content Profile/Validate")]
    static void ValidateMenu()
    {
        ValidateOrThrow();
        Debug.Log("[ContentProfile] 검증 통과");
    }

    public void OnPreprocessBuild(BuildReport _report) => ValidateOrThrow();

    static void ValidateOrThrow()
    {
        var t_errors = new List<string>();
        CardRegistry t_registry = AssetDatabase.LoadAssetAtPath<CardRegistry>(REGISTRY_PATH);
        ContentProfileConfig t_live = AssetDatabase.LoadAssetAtPath<ContentProfileConfig>(LIVE_PROFILE_PATH);
        ContentProfileConfig t_test = AssetDatabase.LoadAssetAtPath<ContentProfileConfig>(TEST_PROFILE_PATH);

        if (t_registry == null) t_errors.Add($"CardRegistry 없음: {REGISTRY_PATH}");
        if (t_live == null) t_errors.Add($"Live 프로필 없음: {LIVE_PROFILE_PATH}");
        if (t_test == null) t_errors.Add($"Test 프로필 없음: {TEST_PROFILE_PATH}");

        if (t_live != null && (t_live.RunMode != EContentRunMode.Live || t_live.IncludeTestCards || t_live.SaveFolder != "Save"))
            t_errors.Add("Live 프로필은 Live/테스트 제외/Save 조합이어야 함");
        if (t_test != null && (t_test.RunMode != EContentRunMode.Test || !t_test.IncludeTestCards || t_test.SaveFolder != "Save_Test"))
            t_errors.Add("Test 프로필은 Test/테스트 포함/Save_Test 조합이어야 함");

        if (t_registry != null)
        {
            int t_liveCount = 0;
            int t_testCount = 0;
            var t_registered = new HashSet<CardData>();
            foreach (CardData t_card in t_registry.All)
            {
                if (t_card == null) { t_errors.Add("CardRegistry에 null 슬롯 존재"); continue; }
                if (!t_registered.Add(t_card)) t_errors.Add($"CardRegistry 중복 카드: {t_card.name}");
                if (t_card.channel == ECardChannel.Live) t_liveCount++;
                else t_testCount++;
            }

            if (t_liveCount == 0) t_errors.Add("Live 카드가 없음");
            if (t_testCount == 0) t_errors.Add("TestOnly 카드가 없음");
            foreach (CardData t_card in LoadAll<CardData>())
                if (!t_registered.Contains(t_card))
                    t_errors.Add($"CardRegistry 미등록 카드: {t_card.name}");
            ValidateLiveConsumers(t_errors);
        }

        if (t_errors.Count > 0)
            throw new BuildFailedException("[ContentProfile] 검증 실패\n- " + string.Join("\n- ", t_errors));
    }

    static void ValidateLiveConsumers(List<string> _errors)
    {
        foreach (CardPackData t_pack in LoadBuildDependencies<CardPackData>())
            CheckCards(t_pack.Pool, t_pack.name, _errors);

        foreach (AIDeckConfig t_ai in LoadBuildDependencies<AIDeckConfig>())
            if (t_ai.decks != null)
                foreach (AIDeckConfig.DeckEntry t_deck in t_ai.decks)
                    CheckCards(t_deck?.cards, $"{t_ai.name}/{t_deck?.deckName}", _errors);

        foreach (CollectionLayoutConfig t_layout in LoadBuildDependencies<CollectionLayoutConfig>())
            foreach (CollectionRowDef t_row in t_layout.Rows)
                CheckCards(t_row.cards, t_layout.name, _errors);

        foreach (CollectionThemeConfig t_themes in LoadBuildDependencies<CollectionThemeConfig>())
            foreach (CollectionThemeDef t_theme in t_themes.Themes)
                CheckCards(t_theme.cards, $"{t_themes.name}/{t_theme.themeId}", _errors);

        foreach (TutorialScenarioData t_scenario in LoadBuildDependencies<TutorialScenarioData>())
        {
            CheckCards(t_scenario.playerDeck, $"{t_scenario.name}/player", _errors);
            CheckCards(t_scenario.enemyDeck, $"{t_scenario.name}/enemy", _errors);
        }
    }

    static IEnumerable<T> LoadBuildDependencies<T>() where T : UnityEngine.Object
    {
        var t_seen = new HashSet<T>();
        foreach (EditorBuildSettingsScene t_scene in EditorBuildSettings.scenes)
        {
            if (!t_scene.enabled) continue;
            foreach (string t_path in AssetDatabase.GetDependencies(t_scene.path, true))
            {
                T t_asset = AssetDatabase.LoadAssetAtPath<T>(t_path);
                if (t_asset != null && t_seen.Add(t_asset)) yield return t_asset;
            }
        }
    }

    static IEnumerable<T> LoadAll<T>() where T : UnityEngine.Object
    {
        foreach (string t_guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            T t_asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(t_guid));
            if (t_asset != null) yield return t_asset;
        }
    }

    static void CheckCards(IEnumerable<CardData> _cards, string _owner, List<string> _errors)
    {
        if (_cards == null) return;
        foreach (CardData t_card in _cards)
            if (t_card != null && t_card.channel == ECardChannel.TestOnly)
                _errors.Add($"Live 소비 SO '{_owner}'가 TestOnly 카드 '{t_card.name}' 참조");
    }
}
