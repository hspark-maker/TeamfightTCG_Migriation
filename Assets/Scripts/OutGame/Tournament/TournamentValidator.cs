#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// 토너먼트 저작 결함(챕터·키 안정성·상대 덱·보상) 로그 진단(에디터 수동 실행 전용)
internal static class TournamentValidator
{
    // TournamentConfig의 [ContextMenu]가 유일한 진입점
    public static void Validate(TournamentConfig _config)
    {
        if (_config == null) return;

        var t_chapters = _config.Chapters;
        var t_nodes = _config.Nodes;

        int t_chapterFault = ValidateChapters(t_chapters);

        int t_unstable = 0;
        int t_emptyDeck = 0;
        int t_noReward = 0;
        int t_dupKey = 0;
        var t_keys = new HashSet<string>();

        // 정점 검증은 챕터를 가로질러 평탄 기준으로 본다 — nodeId 중복은 전역 판정이다
        for (int t_i = 0; t_i < t_nodes.Count; t_i++)
        {
            TournamentNodeDef t_node = t_nodes[t_i];

            if (!t_node.HasStableKey)
            {
                t_unstable++;
                Debug.LogError($"[Tournament] nodeId 미저작 (정점 #{t_i}) — 이 정점과 뒤 정점 전부가 영구 잠금이다.");
            }
            else if (!t_keys.Add(t_node.nodeId))
            {
                t_dupKey++;
                Debug.LogError($"[Tournament] nodeId 중복 '{t_node.nodeId}' (정점 #{t_i}) — 낙인이 한 정점으로 합쳐진다.");
            }

            if (CountCards(t_node.enemyDeck) == 0)
            {
                t_emptyDeck++;
                Debug.LogError($"[Tournament] 상대 덱 비었음 (정점 #{t_i} '{t_node.displayName}') — 전투를 열 수 없다.");
            }

            if (CountRewards(t_node.rewards) == 0)
            {
                t_noReward++;
                Debug.LogWarning($"[Tournament] 보상 미저작 (정점 #{t_i} '{t_node.displayName}') — 클리어해도 지급이 없다.");
            }
        }

        if (t_chapterFault == 0 && t_unstable == 0 && t_dupKey == 0 && t_emptyDeck == 0 && t_noReward == 0)
            Debug.Log($"[Tournament] 저작 검증 통과 — 챕터 {t_chapters.Count}개 · 정점 {t_nodes.Count}개, 결함 없음.");
    }

    // 챕터 결함 수(경고 포함)를 돌려준다
    static int ValidateChapters(IReadOnlyList<TournamentChapterDef> _chapters)
    {
        int t_fault = 0;
        var t_keys = new HashSet<string>();

        if (_chapters.Count == 0)
        {
            Debug.LogError("[Tournament] 챕터 미저작 — 맵에 아무것도 뜨지 않는다.");
            return 1;
        }

        for (int t_i = 0; t_i < _chapters.Count; t_i++)
        {
            TournamentChapterDef t_chapter = _chapters[t_i];

            if (!t_chapter.HasStableKey)
            {
                t_fault++;
                Debug.LogError($"[Tournament] chapterId 미저작 (챕터 #{t_i}) — 완주 보상을 영영 받을 수 없다.");
            }
            else if (!t_keys.Add(t_chapter.chapterId))
            {
                t_fault++;
                Debug.LogError($"[Tournament] chapterId 중복 '{t_chapter.chapterId}' (챕터 #{t_i}) — 수령 낙인이 한 챕터로 합쳐진다.");
            }

            if (t_chapter.NodeCount == 0)
            {
                t_fault++;
                Debug.LogError($"[Tournament] 정점 0개 (챕터 #{t_i} '{t_chapter.title}') — 완주 판정 모수가 없다.");
            }

            // 레거시 합성 챕터는 저작물이 아니다 — 이관 전에는 완주 보상이 없는 게 정상이라 세지 않는다
            if (!IsLegacy(t_chapter) && CountRewards(t_chapter.completionRewards) == 0)
            {
                t_fault++;
                Debug.LogWarning($"[Tournament] 완주 보상 미저작 (챕터 #{t_i} '{t_chapter.title}') — 완주해도 지급이 없다.");
            }
        }

        return t_fault;
    }

    static bool IsLegacy(TournamentChapterDef _chapter)
        => string.Equals(_chapter.chapterId, TournamentConfig.LEGACY_CHAPTER_ID, System.StringComparison.Ordinal);

    static int CountCards(List<CardData> _cards)
    {
        if (_cards == null) return 0;

        int t_count = 0;
        for (int t_i = 0; t_i < _cards.Count; t_i++)
            if (_cards[t_i] != null) t_count++;

        return t_count;
    }

    // 액수 0 이하는 지급도 표시도 되지 않으므로 보상으로 세지 않는다
    static int CountRewards(List<AlbumRewardDef> _rewards)
    {
        if (_rewards == null) return 0;

        int t_count = 0;
        for (int t_i = 0; t_i < _rewards.Count; t_i++)
            if (_rewards[t_i].amount > 0) t_count++;

        return t_count;
    }
}
#endif
