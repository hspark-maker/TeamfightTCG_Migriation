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
        AIDeckConfig t_config = AssetDatabase.LoadAssetAtPath<AIDeckConfig>(AI_DECK_PATH);
        RankConfig t_rank = AssetDatabase.LoadAssetAtPath<RankConfig>(RANK_PATH);
        CardGrowthConfig t_growth = AssetDatabase.LoadAssetAtPath<CardGrowthConfig>(GROWTH_PATH);

        if (t_config == null) { _errors.Add($"AI 덱 설정 없음: {AI_DECK_PATH}"); return; }
        if (t_rank == null) { _errors.Add($"랭크 설정 없음: {RANK_PATH}"); return; }
        if (t_growth == null) { _errors.Add($"카드 성장 설정 없음: {GROWTH_PATH}"); return; }

        ValidateDecks(t_config, t_rank, t_growth, _errors);
        ValidateTierCoverage(t_config, t_rank, _errors);
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
        CollectCurriculumWarnings(t_config, t_rank, t_growth, t_warnings);

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
            if (t_entry.fromTier < 0 || t_entry.fromTier >= _rank.TierCount)
            {
                _errors.Add($"AI 덱 '{t_name}' 시작 티어 {t_entry.fromTier}가 유효 범위를 벗어났습니다.");
                continue;
            }

            int t_aiLevel = _rank.AiCardLevelAt(t_entry.fromTier);
            int t_identityLevel = IdentityLevelOf(t_entry, _growth);
            if (t_aiLevel < t_identityLevel)
                _errors.Add($"AI 덱 '{t_name}'는 Tier {t_entry.fromTier}의 AI Lv{t_aiLevel}에서 완전히 발현되지 않습니다. 필요 Lv{t_identityLevel}: {LockedFeatures(t_entry, t_aiLevel, _growth)}");
        }
    }

    static void ValidateTierCoverage(AIDeckConfig _config, RankConfig _rank, List<string> _errors)
    {
        for (int t_tier = 0; t_tier < _rank.TierCount; t_tier++)
            if (CandidatesAt(_config, t_tier).Count == 0)
                _errors.Add($"AI 덱 후보가 없는 티어: {t_tier}");
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
        if (t_firstSynergyTier < 0 || _config.decks == null) return;

        foreach (AIDeckConfig.DeckEntry t_entry in _config.decks)
        {
            if (t_entry == null || t_entry.fromTier < t_firstSynergyTier || t_entry.fromTier > t_firstSynergyTier + 1) continue;
            int t_synergyCount = SynergyResolver.Resolve(t_entry.cards).Active.Count;
            if (t_synergyCount != 1)
                _warnings.Add($"시너지 입문 구간 덱 '{t_entry.deckName}'의 성립 시너지가 {t_synergyCount}개입니다(권장 1개).");
        }
    }

    static int IdentityLevelOf(AIDeckConfig.DeckEntry _entry, CardGrowthConfig _growth)
    {
        int t_level = 0;
        if (_entry?.cards != null)
            foreach (CardData t_card in _entry.cards)
                if (t_card != null) t_level = Mathf.Max(t_level, t_card.keywordUnlockLevel);

        if (SynergyResolver.Resolve(_entry?.cards).Active.Count > 0)
            t_level = Mathf.Max(t_level, _growth.FirstEvolutionLevel);
        return t_level;
    }

    static List<AIDeckConfig.DeckEntry> CandidatesAt(AIDeckConfig _config, int _tier)
    {
        var t_candidates = new List<AIDeckConfig.DeckEntry>();
        if (_config.decks == null) return t_candidates;
        foreach (AIDeckConfig.DeckEntry t_entry in _config.decks)
            if (t_entry != null && t_entry.cards != null && t_entry.cards.Count == DeckSaveManager.DECK_SIZE
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

    static string LockedFeatures(AIDeckConfig.DeckEntry _entry, int _aiLevel, CardGrowthConfig _growth)
    {
        var t_locked = new List<string>();
        if (_entry?.cards != null)
            foreach (CardData t_card in _entry.cards)
                if (t_card != null && t_card.keywordUnlockLevel > _aiLevel)
                    t_locked.Add($"{t_card.displayName} 키워드(Lv{t_card.keywordUnlockLevel})");
        if (!_growth.SynergyUnlockedAt(_aiLevel) && SynergyResolver.Resolve(_entry?.cards).Active.Count > 0)
            t_locked.Add($"시너지(Lv{_growth.FirstEvolutionLevel})");
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
