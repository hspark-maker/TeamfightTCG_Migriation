using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public sealed class ContentProfileValidator : IPreprocessBuildWithReport
{
    const string SYNERGY_REGISTRY_PATH = "Assets/SO/SynergyRegistry.asset";
    const string LIVE_PROFILE_PATH = "Assets/Resources/ContentProfiles/Live.asset";
    const string TEST_PROFILE_PATH = "Assets/Resources/ContentProfiles/Test.asset";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport _report)
    {
        EContentRunMode t_mode = BuildMode(_report);

        // 어느 창에서 빌드하든 여기는 지난다. 개발 빌드 여부를 산출물로 확인하려면 APK 를 깔아 봐야 해서
        // 매번 추측이 붙던 자리 — 빌드 시작 시점에 한 줄로 못 박는다.
        bool t_development = (_report.summary.options & BuildOptions.Development) != 0;
        Debug.Log($"[빌드] mode={t_mode} development={t_development} target={_report.summary.platform} " +
                  $"app={PlayerSettings.bundleVersion} tableGen={ContentVersion.Major} options={_report.summary.options}");

        ValidateOrThrow(t_mode);
    }

    static EContentRunMode BuildMode(BuildReport _report)
        => (_report.summary.options & BuildOptions.Development) != 0
            ? EContentRunMode.Test
            : EContentRunMode.Live;

    /// <summary>빌드에 실릴 카드 SO가 그 빌드가 쓸 표와 다른지 **경고만** 한다(막지 않는다).
    /// 릴리즈 관리 창을 거치지 않는 경로(File > Build Settings, 배치 빌드)에서도 어긋남이 보이게 하는 게 목적이다.
    ///
    /// 모드 판정은 <see cref="ContentProfileConfig"/>의 런타임 규칙과 같아야 한다 — 개발 빌드 = 테스트 프로필.
    /// 에디터 모드(EditorPrefs)는 빌드와 무관하므로 보지 않는다.</summary>
    /// <summary>문제 목록을 던지지 않고 돌려준다(빈 목록 = 통과). 릴리즈 관리 창이 목록으로 띄우는 진입점 —
    /// 빌드 전처리와 **같은 규칙**을 써야 창에서 통과한 것이 빌드에서 막히지 않는다.
    ///
    /// 반환값은 빌드를 막는 에러만이다. <paramref name="_warnings"/>를 주면 검증기가 보고하는
    /// 비차단 경고를 별도로 담아준다.</summary>
    public static List<string> Collect(List<string> _warnings = null, EContentRunMode? _mode = null)
    {
        // 시트를 새로 생성한 직후에도 검사가 낡은 스냅샷을 보지 않게 매번 다시 읽는다.
        // SpecSource의 자동 리셋은 플레이 진입 훅뿐이라 에디터 세션에서는 안 돈다.
        SpecSource.Reload();

        var t_errors = new List<string>();
        // 테이블 세대 상수가 C#·content-version.json·서버 TS 세 곳에서 같은지 본다.
        // 앱 빌드 버전은 여기서 보지 않는다 — 테이블 세대와 묶여 있지 않다.
        if (!ContentVersionConsistency.TryValidate(out string t_versionError))
            t_errors.Add(t_versionError);

        ContentProfileConfig t_live = AssetDatabase.LoadAssetAtPath<ContentProfileConfig>(LIVE_PROFILE_PATH);
        ContentProfileConfig t_test = AssetDatabase.LoadAssetAtPath<ContentProfileConfig>(TEST_PROFILE_PATH);

        if (t_live == null) t_errors.Add($"Live 프로필 없음: {LIVE_PROFILE_PATH}");
        if (t_test == null) t_errors.Add($"Test 프로필 없음: {TEST_PROFILE_PATH}");

        if (t_live != null && (t_live.RunMode != EContentRunMode.Live || t_live.IncludeTestCards || t_live.SaveFolder != "Save"))
            t_errors.Add("Live 프로필은 Live/테스트 제외/Save 조합이어야 함");
        if (t_test != null && (t_test.RunMode != EContentRunMode.Test || !t_test.IncludeTestCards || t_test.SaveFolder != "Save_Test"))
            t_errors.Add("Test 프로필은 Test/테스트 포함/Save_Test 조합이어야 함");

        {
            int t_liveCount = 0;
            var t_liveIds = new HashSet<int>();
            try
            {
                foreach (CardSpec t_spec in SpecSource.LoadCards().Values)
                    if (t_spec.Channel == ECardChannel.Live) t_liveIds.Add(t_spec.Id);
            }
            catch (Exception t_exception)
            {
                t_errors.Add($"Live 카드 표 검증 실패: {t_exception.Message}");
            }
            t_liveCount = t_liveIds.Count;

            // Live 0장은 빈 게임이라 진짜 오류다. TestOnly 0장은 아니다 —
            // Test 프로필은 IncludeTestCards로 TestOnly 카드를 **덤으로 더** 실을 뿐이라,
            // 0장이면 테스트 빌드가 Live와 같은 카드 목록을 쓰는 정상 상태다(전 카드 출시 = 이 상태).
            if (t_liveCount == 0) t_errors.Add("Live 카드가 없음");
            ValidateLiveConsumers(t_liveIds, t_errors, _warnings);
        }

        ValidateCardArtAddresses(t_errors, _warnings, _mode);
        ValidateSynergyRegistry(t_errors, _warnings, _mode);

        return t_errors;
    }

    static void ValidateSynergyRegistry(
        List<string> _errors,
        List<string> _warnings,
        EContentRunMode? _mode)
    {
        SynergyRegistry t_registry = AssetDatabase.LoadAssetAtPath<SynergyRegistry>(SYNERGY_REGISTRY_PATH);
        if (t_registry == null)
        {
            _errors.Add($"SynergyRegistry 없음: {SYNERGY_REGISTRY_PATH}");
            return;
        }

        try { t_registry.ValidateOrThrow(); }
        catch (Exception t_exception)
        {
            _errors.Add(t_exception.Message);
            return;
        }

        // 카드 표는 Card 하나라 모드별로 두 번 돌 이유가 없다(Card_Test 표 폐기).
        try
        {
            foreach (CardSpec t_spec in SpecSource.LoadCards().Values)
                foreach (string t_name in t_spec.SynergyNames)
                    t_registry.Require(t_name);
        }
        catch (Exception t_exception)
        {
            _errors.Add($"카드 시너지 검증 실패: {t_exception.Message}");
        }
    }

    static void ValidateCardArtAddresses(
        List<string> _errors,
        List<string> _warnings,
        EContentRunMode? _mode)
    {
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null)
        {
            _errors.Add("Addressables Settings 없음");
            return;
        }

        var t_addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (AddressableAssetGroup t_group in t_settings.groups)
        {
            if (t_group == null) continue;
            foreach (AddressableAssetEntry t_entry in t_group.entries)
                if (t_entry != null && t_entry.labels.Contains("Cards"))
                {
                    if (!t_addresses.Add(t_entry.address)) _errors.Add($"Cards 주소 중복: {t_entry.address}");
                    string t_path = AssetDatabase.GUIDToAssetPath(t_entry.guid);
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(t_path) == null)
                        _errors.Add($"Cards 주소가 Sprite 에셋이 아님: {t_entry.address} ({t_path})");
                }
        }

        var t_expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (EContentRunMode t_mode in new[] { EContentRunMode.Live, EContentRunMode.Test })
        {
            List<string> t_issues = !_mode.HasValue || _mode.Value == t_mode ? _errors : _warnings;
            Dictionary<int, CardSpec> t_specs;
            try { t_specs = SpecSource.LoadCards(); }
            catch (Exception t_exception)
            {
                t_issues?.Add($"카드 아트 주소 검증용 {t_mode} 표 로드 실패: {t_exception.Message}");
                continue;
            }

            foreach (CardSpec t_spec in t_specs.Values)
            {
                string t_missingAddress = null;
                for (int t_stage = 0; t_stage <= CardSpec.MaxEvolutionStage; t_stage++)
                {
                    string t_address = CardArtCache.AddressOf(t_spec, t_stage);
                    t_expected.Add(t_address);
                    bool t_exists = t_addresses.Contains(t_address);
                    if (t_stage == 0 && !t_exists)
                        t_issues?.Add($"기본 카드 아트 주소 없음: {t_address}");
                    else if (!t_exists && t_missingAddress == null)
                        t_missingAddress = t_address;
                    else if (t_exists && t_missingAddress != null)
                    {
                        t_issues?.Add($"카드 아트 단계 중간 누락: {t_missingAddress}");
                        t_missingAddress = null;
                    }
                }
            }
        }

        if (t_expected.Count == 0) return;
        foreach (string t_address in t_addresses)
            if (!t_expected.Contains(t_address)) _errors.Add($"규칙 밖 Cards 주소: {t_address}");
    }

    static void ValidateOrThrow(EContentRunMode _mode)
    {
        var t_warnings = new List<string>();
        List<string> t_errors = Collect(t_warnings, _mode);
        if (t_warnings.Count > 0)
            Debug.LogWarning("[ContentProfile] 경고(빌드는 막지 않는다)\n- " + string.Join("\n- ", t_warnings));
        if (t_errors.Count > 0)
            throw new BuildFailedException("[ContentProfile] 검증 실패\n- " + string.Join("\n- ", t_errors));
    }

    static void ValidateLiveConsumers(HashSet<int> _liveIds, List<string> _errors, List<string> _warnings)
    {
        if (!RankGradeSpec.TryValidateRequired(out string t_rankGradeError))
            _errors.Add(t_rankGradeError);

        // 드롭은 만족하는 가장 높은 minGrade 한 줄만 적용된다 — 한 등급만 물으면 하위 등급 전용 줄이 검사에서 샌다.
        IReadOnlyList<CardPack> t_packs = SpecSource.Manager?.CardPack?.All;
        if (t_packs != null)
            foreach (CardPack t_pack in t_packs)
            {
                if (t_pack == null) continue;
                foreach (ERankGrade t_grade in System.Enum.GetValues(typeof(ERankGrade)))
                    CheckCards(PackSpec.ResolveCardIds(t_pack.packId, t_grade), $"{t_pack.packId}/{t_grade}", _liveIds, _errors);
            }

        ValidateAIDeckSpec(_liveIds, _errors, _warnings);

        foreach (TutorialScenarioData t_scenario in LoadBuildDependencies<TutorialScenarioData>())
        {
            CheckCards(t_scenario.PlayerDeckIds, $"{t_scenario.name}/player", _liveIds, _errors);
            CheckCards(t_scenario.EnemyDeckIds, $"{t_scenario.name}/enemy", _liveIds, _errors);
        }
    }

    /// <summary>AIDeck 서버 표는 런타임의 유일한 AI 덱 진실원이다. 비거나 불완전하면 빌드를 막는다.</summary>
    static void ValidateAIDeckSpec(HashSet<int> _liveIds, List<string> _errors, List<string> _warnings)
    {
        IReadOnlyList<AIDeck> t_decks = SpecSource.Manager?.AIDeck?.All;
        if (t_decks == null || t_decks.Count == 0)
        {
            _errors.Add("AIDeck 서버 표가 비어 있다.");
            return;
        }

        int t_tierCount = ResolveRankTierCount(_warnings);
        var t_byId = new Dictionary<string, AIDeck>(StringComparer.Ordinal);
        var t_invalid = new HashSet<string>(StringComparer.Ordinal);
        foreach (AIDeck t_deck in t_decks)
        {
            if (t_deck == null || string.IsNullOrEmpty(t_deck.deckId))
            {
                _errors.Add("AIDeck에 빈 deckId가 있음");
                continue;
            }
            if (t_byId.ContainsKey(t_deck.deckId))
            {
                _errors.Add($"AIDeck deckId 중복: {t_deck.deckId}");
                t_invalid.Add(t_deck.deckId);
                continue;
            }
            t_byId.Add(t_deck.deckId, t_deck);

            // 레벨은 한쪽만 채우면 런타임이 미저작으로 보고 조용히 바닥으로 떨어진다 — 반쪽 저작을 여기서 잡는다.
            bool t_hasFrom = t_deck.fromLevel > 0;
            bool t_hasTo = t_deck.toLevel > 0;
            if (t_hasFrom != t_hasTo)
                _errors.Add($"{t_deck.deckId} 레벨 범위 반쪽 저작: {t_deck.fromLevel}~{t_deck.toLevel} " +
                            "(둘 다 채우거나 둘 다 0이어야 한다)");
            else if (t_hasFrom && (t_deck.toLevel < t_deck.fromLevel ||
                                   t_deck.toLevel > GrowthSpec.CardMaxLevelCeiling))
                _errors.Add($"{t_deck.deckId} 레벨 범위 오류: {t_deck.fromLevel}~{t_deck.toLevel} " +
                            $"(유효 범위 {CardGrowth.BaseLevel}~{GrowthSpec.CardMaxLevelCeiling})");

            foreach (int t_cardId in DeckCards(t_deck))
                if (t_cardId <= 0 || !_liveIds.Contains(t_cardId))
                {
                    _errors.Add($"{t_deck.deckId}가 TestOnly/미존재 카드 ID '{t_cardId}' 참조");
                    t_invalid.Add(t_deck.deckId);
                }
        }

        for (int t_tier = 0; t_tier < t_tierCount; t_tier++)
        {
            bool t_covered = false;
            foreach (AIDeck t_deck in t_byId.Values)
            {
                int t_toTier = t_deck.toTier == 0 ? int.MaxValue : t_deck.toTier;
                if (!t_invalid.Contains(t_deck.deckId) && t_deck.fromTier <= t_tier && t_tier <= t_toTier)
                {
                    t_covered = true;
                    break;
                }
            }
            if (!t_covered) _errors.Add($"AI 덱 티어 커버리지 누락: {t_tier}");
        }
    }

    /// <summary>덱 한 줄의 카드 칸을 저작 순서대로 편다. 칸 수가 <see cref="DeckSaveManager.DECK_SIZE"/>와
    /// 어긋나면 시트 스키마가 낡은 것이라 에러로 잡는다.</summary>
    static IEnumerable<int> DeckCards(AIDeck _deck)
    {
        var t_cards = new[] { _deck.card1, _deck.card2, _deck.card3, _deck.card4, _deck.card5, _deck.card6 };
        if (t_cards.Length != DeckSaveManager.DECK_SIZE)
            throw new BuildFailedException(
                $"[ContentProfile] AIDeck 시트의 카드 칸 {t_cards.Length}개가 덱 크기 {DeckSaveManager.DECK_SIZE}와 다르다.");
        return t_cards;
    }

    /// <summary>AI 덱의 fromTier/toTier 축은 <see cref="RankManager.TierIndex"/>다. 0은 축을 못 찾았다는
    /// 뜻이고, 그때는 커버리지 검사를 건너뛴다 — 모르는 축으로 판정해 엉뚱한 빌드 실패를 내는 것보다 낫다.
    /// 구간 자체의 상한은 재지 않는다: 모험 전용 덱처럼 랭크 축 밖에 두려고 일부러 높은 티어를 쓰는 행이 있다.</summary>
    static int ResolveRankTierCount(List<string> _warnings)
    {
        int t_gradeCount = SpecSource.Manager?.RankGrade?.All?.Count ?? 0;
        int t_count = t_gradeCount * RankConfig.DivisionsPerGrade;

        if (t_count <= 0)
            _warnings?.Add("RankGrade 서버 표가 비어 있어 AI 덱 티어 커버리지 검사를 건너뛴다.");
        return t_count;
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

    static void CheckCards(IEnumerable<int> _cards, string _owner, HashSet<int> _liveIds, List<string> _errors)
    {
        if (_cards == null) return;
        foreach (int t_cardId in _cards)
            if (t_cardId > 0 && !_liveIds.Contains(t_cardId))
                _errors.Add($"Live 소비 SO '{_owner}'가 TestOnly/미존재 카드 ID '{t_cardId}' 참조");
    }
}
