using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class AIDeckBandValidator
{
    const string AI_DECK_PATH = "Assets/SO/AIDeckConfig.asset";
    const string RANK_PATH = "Assets/SO/Rank/RankConfig.asset";
    const string GROWTH_PATH = "Assets/SO/CardGrowth/CardGrowthConfig.asset";

    public static void CollectErrors(List<string> _errors)
    {
        if (!TryLoad(out AIDeckConfig t_config, out RankConfig t_rank, out CardGrowthConfig t_growth, _errors)) return;

        ValidateDecks(t_config, t_rank, t_growth, _errors);
        ValidateTierCoverage(t_config, t_rank, _errors);
    }

    /// <summary>빌드를 막지는 않지만 저작자가 봐야 하는 어긋남을 로그로 남긴다(빌드 전처리에서 호출).</summary>
    public static void WarnBands()
    {
        if (!TryLoad(out AIDeckConfig t_config, out RankConfig t_rank, out CardGrowthConfig t_growth, null)) return;

        var t_warnings = new List<string>();
        CollectWarnings(t_config, t_rank, t_growth, t_warnings);
        if (t_warnings.Count == 0) return;

        Debug.LogWarning("[AI 덱] 저작 경고\n- " + string.Join("\n- ", t_warnings));
    }

    static bool TryLoad(out AIDeckConfig _config, out RankConfig _rank, out CardGrowthConfig _growth, List<string> _errors)
    {
        _config = AssetDatabase.LoadAssetAtPath<AIDeckConfig>(AI_DECK_PATH);
        _rank = AssetDatabase.LoadAssetAtPath<RankConfig>(RANK_PATH);
        _growth = AssetDatabase.LoadAssetAtPath<CardGrowthConfig>(GROWTH_PATH);

        if (_config == null) { _errors?.Add($"AI 덱 설정 없음: {AI_DECK_PATH}"); return false; }
        if (_rank == null) { _errors?.Add($"랭크 설정 없음: {RANK_PATH}"); return false; }
        if (_growth == null) { _errors?.Add($"카드 성장 설정 없음: {GROWTH_PATH}"); return false; }
        return true;
    }

    [MenuItem("Tools/Card Battle/AI 덱 티어 분포")]
    static void PrintReport()
    {
        AIDeckConfig t_config = AssetDatabase.LoadAssetAtPath<AIDeckConfig>(AI_DECK_PATH);
        RankConfig t_rank = AssetDatabase.LoadAssetAtPath<RankConfig>(RANK_PATH);
        CardGrowthConfig t_growth = AssetDatabase.LoadAssetAtPath<CardGrowthConfig>(GROWTH_PATH);
        if (t_config == null || t_rank == null || t_growth == null)
        {
            Debug.LogError("[AI 덱] AIDeckConfig/RankConfig/CardGrowthConfig 에셋을 찾지 못했습니다.");
            return;
        }

        var t_errors = new List<string>();
        CollectErrors(t_errors);
        var t_warnings = new List<string>();
        CollectWarnings(t_config, t_rank, t_growth, t_warnings);

        var t_report = new StringBuilder("[AI 덱 티어 분포]\n");
        for (int t_tier = 0; t_tier < t_rank.TierCount; t_tier++)
        {
            int t_level = t_rank.AiCardLevelAt(t_tier);
            List<AIDeckConfig.DeckEntry> t_candidates = CandidatesAt(t_config, t_tier);
            int t_totalWeight = TotalWeight(t_candidates);
            t_report.Append($"Tier {t_tier} / AI Lv{t_level}: ");

            if (t_candidates.Count == 0)
            {
                t_report.Append("후보 없음\n");
                continue;
            }

            for (int t_i = 0; t_i < t_candidates.Count; t_i++)
            {
                AIDeckConfig.DeckEntry t_entry = t_candidates[t_i];
                float t_probability = 100f * t_entry.WeightOrOne / t_totalWeight;
                if (t_i > 0) t_report.Append(" | ");
                t_report.Append($"{t_entry.deckName} {t_probability:0.#}% ");
                AppendFeatures(t_report, t_entry, t_level, t_growth);
            }
            t_report.AppendLine();
        }

        if (t_warnings.Count > 0)
            t_report.Append("경고:\n- ").Append(string.Join("\n- ", t_warnings)).AppendLine();
        if (t_errors.Count > 0)
            t_report.Append("에러:\n- ").Append(string.Join("\n- ", t_errors)).AppendLine();

        if (t_errors.Count > 0) Debug.LogError(t_report.ToString());
        else if (t_warnings.Count > 0) Debug.LogWarning(t_report.ToString());
        else Debug.Log(t_report.ToString());
    }

    [MenuItem("Tools/Card Battle/AI 덱 시작 티어 자동 제안 적용")]
    static void ApplySuggestedStartTiers()
    {
        AIDeckConfig t_config = AssetDatabase.LoadAssetAtPath<AIDeckConfig>(AI_DECK_PATH);
        RankConfig t_rank = AssetDatabase.LoadAssetAtPath<RankConfig>(RANK_PATH);
        CardGrowthConfig t_growth = AssetDatabase.LoadAssetAtPath<CardGrowthConfig>(GROWTH_PATH);
        if (t_config == null || t_rank == null || t_growth == null || t_config.decks == null) return;
        if (!EditorUtility.DisplayDialog("AI 덱 시작 티어 자동 제안", "모든 덱의 fromTier를 기능이 완전히 열리는 최초 티어로 변경합니다.", "적용", "취소")) return;

        Undo.RecordObject(t_config, "AI 덱 시작 티어 자동 제안");
        foreach (AIDeckConfig.DeckEntry t_entry in t_config.decks)
        {
            if (t_entry == null) continue;
            int t_identityLevel = IdentityLevelOf(t_entry, t_growth);
            for (int t_tier = 0; t_tier < t_rank.TierCount; t_tier++)
            {
                if (t_rank.AiCardLevelAt(t_tier) < t_identityLevel) continue;
                t_entry.fromTier = t_tier;
                break;
            }
        }
        EditorUtility.SetDirty(t_config);
        AssetDatabase.SaveAssets();
        PrintReport();
    }

    static void ValidateDecks(AIDeckConfig _config, RankConfig _rank, CardGrowthConfig _growth, List<string> _errors)
    {
        if (_config.decks == null) { _errors.Add("AI 덱 목록이 null입니다."); return; }

        foreach (AIDeckConfig.DeckEntry t_entry in _config.decks)
        {
            string t_name = t_entry?.deckName ?? "<null>";
            int t_count = t_entry?.cards?.Count ?? 0;
            if (t_count != DeckSaveManager.DECK_SIZE)
                _errors.Add($"AI 덱 '{t_name}' 카드 수 {t_count} ≠ {DeckSaveManager.DECK_SIZE}");
            if (t_entry == null) continue;
            if (t_entry.cards != null && t_entry.cards.Exists(_card => _card == null))
                _errors.Add($"AI 덱 '{t_name}'에 null 카드가 있습니다.");
            if (t_entry.fromTier < 0 || t_entry.fromTier >= _rank.TierCount)
            {
                _errors.Add($"AI 덱 '{t_name}' 시작 티어 {t_entry.fromTier}가 유효 범위를 벗어났습니다.");
                continue;
            }

            int t_aiLevel = _rank.AiCardLevelAt(t_entry.fromTier);

            int t_keywordLevel = KeywordLevelOf(t_entry);
            if (t_aiLevel < t_keywordLevel)
                _errors.Add($"AI 덱 '{t_name}'는 Tier {t_entry.fromTier}의 AI Lv{t_aiLevel}에서 완전히 발현되지 않습니다. 필요 Lv{t_keywordLevel}: {LockedKeywords(t_entry, t_aiLevel)}");

            // 시너지는 시작 티어 한 점이 아니라 배치 구간 전체로 본다. 구간 내내 잠긴 덱은 시너지 없는 덱으로
            // 일관되게 굴러가므로 발현 실패가 아니다(저티어 커리큘럼의 정상 상태 — 대신 경고로 짚는다).
            // 막아야 하는 건 구간 도중에 열려서 같은 덱이 티어에 따라 다른 게임이 되는 경우다.
            if (!HasSynergy(t_entry) || _growth.SynergyUnlockedAt(t_aiLevel)) continue;
            if (!_growth.SynergyUnlockedAt(BandMaxAiLevel(_rank, t_entry))) continue;

            _errors.Add($"AI 덱 '{t_name}'는 배치 구간 Tier {t_entry.fromTier}~{BandLastTier(_rank, t_entry)} 도중에 " +
                        $"시너지(Lv{_growth.FirstEvolutionLevel})가 열립니다 — 같은 덱이 티어에 따라 다른 게임이 됩니다. " +
                        $"성립 시너지: {SynergyNames(t_entry)}");
        }
    }

    static void ValidateTierCoverage(AIDeckConfig _config, RankConfig _rank, List<string> _errors)
    {
        for (int t_tier = 0; t_tier < _rank.TierCount; t_tier++)
            if (CandidatesAt(_config, t_tier).Count == 0)
                _errors.Add($"AI 덱 후보가 없는 티어: {t_tier}");
    }

    static void CollectWarnings(AIDeckConfig _config, RankConfig _rank, CardGrowthConfig _growth, List<string> _warnings)
    {
        CollectCurriculumWarnings(_config, _rank, _growth, _warnings);
        CollectDormantSynergyWarnings(_config, _rank, _growth, _warnings);
    }

    static void CollectCurriculumWarnings(AIDeckConfig _config, RankConfig _rank, CardGrowthConfig _growth, List<string> _warnings)
    {
        int t_firstSynergyTier = -1;
        for (int t_tier = 0; t_tier < _rank.TierCount; t_tier++)
        {
            if (!_growth.SynergyUnlockedAt(_rank.AiCardLevelAt(t_tier))) continue;
            t_firstSynergyTier = t_tier;
            break;
        }
        if (t_firstSynergyTier < 0) return;

        var t_warned = new HashSet<AIDeckConfig.DeckEntry>();
        for (int t_tier = t_firstSynergyTier; t_tier <= t_firstSynergyTier + 1 && t_tier < _rank.TierCount; t_tier++)
        {
            foreach (AIDeckConfig.DeckEntry t_entry in CandidatesAt(_config, t_tier))
            {
                int t_synergyCount = SynergyResolver.Resolve(t_entry.cards).Active.Count;
                if (t_synergyCount == 1 || !t_warned.Add(t_entry)) continue;
                _warnings.Add($"시너지 입문 구간 덱 '{t_entry.deckName}'의 성립 시너지가 {t_synergyCount}개입니다(권장 1개).");
            }
        }
    }

    /// <summary>구간 내내 잠겨 빌드는 통과하지만 저작 의도와 어긋날 수 있는 시너지를 짚는다.
    /// 카드 시너지 태그가 바뀌면 손대지 않은 덱에서도 성립 여부가 달라지므로 이 경고가 유일한 단서다.</summary>
    static void CollectDormantSynergyWarnings(AIDeckConfig _config, RankConfig _rank, CardGrowthConfig _growth, List<string> _warnings)
    {
        if (_config.decks == null) return;

        int t_spreadUp = Mathf.Max(0, _rank.aiLevelSpreadUp);
        foreach (AIDeckConfig.DeckEntry t_entry in _config.decks)
        {
            if (t_entry == null || t_entry.fromTier < 0 || t_entry.fromTier >= _rank.TierCount) continue;
            if (!HasSynergy(t_entry)) continue;

            int t_bandMax = BandMaxAiLevel(_rank, t_entry);
            if (_growth.SynergyUnlockedAt(t_bandMax)) continue;

            string t_band = $"Tier {t_entry.fromTier}~{BandLastTier(_rank, t_entry)}";
            if (t_spreadUp > 0 && _growth.SynergyUnlockedAt(t_bandMax + t_spreadUp))
                _warnings.Add($"덱 '{t_entry.deckName}'는 기준 레벨(Lv{t_bandMax})로는 시너지가 잠기지만 상향 편차 +{t_spreadUp}로 " +
                              $"일부 카드가 해금선 Lv{_growth.FirstEvolutionLevel}에 닿습니다 — {t_band}에서 조건부로 터질 수 있습니다. " +
                              $"성립 시너지: {SynergyNames(t_entry)}");
            else
                _warnings.Add($"덱 '{t_entry.deckName}'는 시너지 {SynergyNames(t_entry)}가 성립하지만 {t_band} 내내 잠겨 발현되지 않습니다 " +
                              "— 의도한 구성이 아니면 카드 시너지 태그를 확인하세요.");
        }
    }

    static int IdentityLevelOf(AIDeckConfig.DeckEntry _entry, CardGrowthConfig _growth)
    {
        int t_level = KeywordLevelOf(_entry);
        if (HasSynergy(_entry))
            t_level = Mathf.Max(t_level, _growth.FirstEvolutionLevel);
        return t_level;
    }

    // 덱 카드가 요구하는 최대 키워드 해금 레벨
    static int KeywordLevelOf(AIDeckConfig.DeckEntry _entry)
    {
        int t_level = 0;
        if (_entry?.cards == null) return t_level;

        foreach (CardData t_card in _entry.cards)
            if (t_card != null) t_level = Mathf.Max(t_level, t_card.keywordUnlockLevel);
        return t_level;
    }

    static bool HasSynergy(AIDeckConfig.DeckEntry _entry) => SynergyResolver.Resolve(_entry?.cards).Active.Count > 0;

    // 덱이 놓이는 마지막 티어(toTier 미저작이면 최고 티어)
    static int BandLastTier(RankConfig _rank, AIDeckConfig.DeckEntry _entry)
        => Mathf.Clamp(_entry.ToTierOrMax, _entry.fromTier, Mathf.Max(0, _rank.TierCount - 1));

    // 배치 구간에서 AI가 갖는 가장 높은 기준 레벨
    static int BandMaxAiLevel(RankConfig _rank, AIDeckConfig.DeckEntry _entry)
    {
        int t_max = 0;
        for (int t_tier = _entry.fromTier; t_tier <= BandLastTier(_rank, _entry); t_tier++)
            t_max = Mathf.Max(t_max, _rank.AiCardLevelAt(t_tier));
        return t_max;
    }

    static List<AIDeckConfig.DeckEntry> CandidatesAt(AIDeckConfig _config, int _tier)
    {
        var t_candidates = new List<AIDeckConfig.DeckEntry>();
        if (_config.decks == null) return t_candidates;
        foreach (AIDeckConfig.DeckEntry t_entry in _config.decks)
            if (t_entry != null && t_entry.cards != null && t_entry.cards.Count == DeckSaveManager.DECK_SIZE
                && !t_entry.cards.Exists(_card => _card == null)
                && t_entry.fromTier <= _tier && _tier <= t_entry.ToTierOrMax)
                t_candidates.Add(t_entry);
        return t_candidates;
    }

    static int TotalWeight(List<AIDeckConfig.DeckEntry> _entries)
    {
        int t_total = 0;
        foreach (AIDeckConfig.DeckEntry t_entry in _entries) t_total += t_entry.WeightOrOne;
        return t_total;
    }

    static string SynergyNames(AIDeckConfig.DeckEntry _entry)
    {
        var t_names = new List<string>();
        foreach (ActiveSynergy t_active in SynergyResolver.Resolve(_entry?.cards).Active)
            t_names.Add(t_active.Synergy != null ? t_active.Synergy.displayName : "null");
        return string.Join(", ", t_names);
    }

    static string LockedKeywords(AIDeckConfig.DeckEntry _entry, int _aiLevel)
    {
        var t_locked = new List<string>();
        if (_entry?.cards == null) return string.Empty;

        foreach (CardData t_card in _entry.cards)
            if (t_card != null && t_card.keywordUnlockLevel > _aiLevel)
                t_locked.Add($"{t_card.displayName} 키워드(Lv{t_card.keywordUnlockLevel})");
        return string.Join(", ", t_locked);
    }

    static void AppendFeatures(StringBuilder _builder, AIDeckConfig.DeckEntry _entry, int _aiLevel, CardGrowthConfig _growth)
    {
        CardKeyword t_keywords = CardKeyword.None;
        if (_entry.cards != null)
            foreach (CardData t_card in _entry.cards)
                if (t_card != null && _aiLevel >= t_card.keywordUnlockLevel) t_keywords |= t_card.keywords;

        _builder.Append($"[키워드:{t_keywords}");
        if (_growth.SynergyUnlockedAt(_aiLevel))
        {
            IReadOnlyList<ActiveSynergy> t_active = SynergyResolver.Resolve(_entry.cards).Active;
            _builder.Append("/시너지:");
            for (int t_i = 0; t_i < t_active.Count; t_i++)
            {
                if (t_i > 0) _builder.Append(',');
                SynergyData t_synergy = t_active[t_i].Synergy;
                _builder.Append(t_synergy != null ? t_synergy.displayName : "null");
            }
            if (t_active.Count == 0) _builder.Append("없음");
        }
        else _builder.Append("/시너지:잠김");
        _builder.Append(']');
    }
}
