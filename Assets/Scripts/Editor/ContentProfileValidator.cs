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

    public void OnPreprocessBuild(BuildReport _report)
    {
        ValidateOrThrow();
        WarnTableDrift(_report);
    }

    /// <summary>빌드에 실릴 카드 SO가 그 빌드가 쓸 표와 다른지 **경고만** 한다(막지 않는다).
    /// 릴리즈 관리 창을 거치지 않는 경로(File > Build Settings, 배치 빌드)에서도 어긋남이 보이게 하는 게 목적이다.
    ///
    /// 모드 판정은 <see cref="ContentProfileConfig"/>의 런타임 규칙과 같아야 한다 — 개발 빌드 = 테스트 프로필.
    /// 에디터 모드(EditorPrefs)는 빌드와 무관하므로 보지 않는다.</summary>
    static void WarnTableDrift(BuildReport _report)
    {
        bool t_dev = (_report.summary.options & BuildOptions.Development) != 0;
        EContentRunMode t_mode = t_dev ? EContentRunMode.Test : EContentRunMode.Live;
        string t_label = ContentRunModeEditor.Label(t_mode);

        List<string> t_drift = ContentRunModeEditor.DiffTable(t_mode, out string t_error);
        if (t_drift == null)
        {
            Debug.LogWarning($"[카드 표 대조] {t_label} 표를 읽지 못해 대조를 건너뛴다 — {t_error}");
            return;
        }
        if (t_drift.Count == 0)
        {
            Debug.Log($"[카드 표 대조] {t_label} 표와 카드 에셋 일치.");
            return;
        }

        Debug.LogWarning($"[카드 표 대조] {t_label} 빌드인데 카드 에셋이 {t_label} 표와 다르다 " +
                         "— 이 빌드에 실리는 값은 표가 아니라 에셋이다.\n" +
                         CardTableTool.DriftSummary(t_drift, 50));
    }

    /// <summary>문제 목록을 던지지 않고 돌려준다(빈 목록 = 통과). 릴리즈 관리 창이 목록으로 띄우는 진입점 —
    /// 빌드 전처리와 **같은 규칙**을 써야 창에서 통과한 것이 빌드에서 막히지 않는다.
    ///
    /// 반환값은 빌드를 막는 에러만이다. <paramref name="_warnings"/>를 주면 검증기가 보고하는
    /// 비차단 경고를 별도로 담아준다.</summary>
    public static List<string> Collect(List<string> _warnings = null)
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
            var t_registered = new HashSet<CardData>();
            foreach (CardData t_card in t_registry.All)
            {
                if (t_card == null) { t_errors.Add("CardRegistry에 null 슬롯 존재"); continue; }
                if (!t_registered.Add(t_card)) t_errors.Add($"CardRegistry 중복 카드: {t_card.name}");
                if (t_card.channel == ECardChannel.Live) t_liveCount++;
            }

            // Live 0장은 빈 게임이라 진짜 오류다. TestOnly 0장은 아니다 —
            // Test 프로필은 IncludeTestCards로 TestOnly 카드를 **덤으로 더** 실을 뿐이라,
            // 0장이면 테스트 빌드가 Live와 같은 카드 목록을 쓰는 정상 상태다(전 카드 출시 = 이 상태).
            if (t_liveCount == 0) t_errors.Add("Live 카드가 없음");
            foreach (CardData t_card in LoadAll<CardData>())
                if (!t_registered.Contains(t_card))
                    t_errors.Add($"CardRegistry 미등록 카드: {t_card.name}");
            ValidateLiveConsumers(t_errors);
        }

        AIDeckBandValidator.CollectIssues(t_errors, _warnings ?? new List<string>());

        return t_errors;
    }

    static void ValidateOrThrow()
    {
        var t_warnings = new List<string>();
        List<string> t_errors = Collect(t_warnings);
        if (t_warnings.Count > 0)
            Debug.LogWarning("[ContentProfile] 경고(빌드는 막지 않는다)\n- " + string.Join("\n- ", t_warnings));
        if (t_errors.Count > 0)
            throw new BuildFailedException("[ContentProfile] 검증 실패\n- " + string.Join("\n- ", t_errors));
    }

    static void ValidateLiveConsumers(List<string> _errors)
    {
        foreach (CardPackData t_pack in LoadBuildDependencies<CardPackData>())
        {
            CheckCards(t_pack.Pool, t_pack.name, _errors);
            foreach (RankPackPool t_rankPool in t_pack.RankPools)
                CheckCards(RankPoolCards(t_rankPool), $"{t_pack.name}/{t_rankPool?.minGrade}", _errors);
        }

        foreach (AIDeckConfig t_ai in LoadBuildDependencies<AIDeckConfig>())
            if (t_ai.decks != null)
                foreach (AIDeckConfig.DeckEntry t_deck in t_ai.decks)
                    CheckCards(t_deck?.cards, $"{t_ai.name}/{t_deck?.deckName}", _errors);

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

    static IEnumerable<CardData> RankPoolCards(RankPackPool _pool)
    {
        if (_pool?.cards == null) yield break;
        foreach (WeightedCard t_weighted in _pool.cards) yield return t_weighted.card;
    }

    static void CheckCards(IEnumerable<CardData> _cards, string _owner, List<string> _errors)
    {
        if (_cards == null) return;
        foreach (CardData t_card in _cards)
            if (t_card != null && t_card.channel == ECardChannel.TestOnly)
                _errors.Add($"Live 소비 SO '{_owner}'가 TestOnly 카드 '{t_card.name}' 참조");
    }
}
