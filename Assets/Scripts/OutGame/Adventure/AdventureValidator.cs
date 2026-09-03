#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// 모험 저작 결함(챕터·키 안정성·상대 덱·보상) 로그 진단(에디터 수동 실행 전용)
internal static class AdventureValidator
{
    // AdventureConfig의 [ContextMenu]가 유일한 진입점
    public static void Validate(AdventureConfig _config)
    {
        if (_config == null) return;

        var t_chapters = _config.Chapters;
        var t_nodes = _config.Nodes;

        int t_chapterFault = ValidateChapters(t_chapters);

        // 전투 수치의 진실원은 서버 AdventureChapter 표다. 표를 못 읽는 상황(에디터에서 스펙 미적재 등)에
        // 정점마다 "덱 비었음"을 뱉으면 원인이 묻힌다 — 여기서 한 번만 말하고 정점별 덱 검사는 건너뛴다.
        bool t_specReady = AdventureNodeSpec.TryValidateRequired(out string t_specError);
        if (!t_specReady)
            Debug.LogError($"[Adventure] AdventureChapter 서버 표를 읽지 못해 상대 덱을 검증할 수 없다 — {t_specError}");

        int t_unstable = 0;
        int t_emptyDeck = 0;
        int t_noDeckKey = 0;
        int t_noReward = 0;
        int t_dupKey = 0;
        var t_keys = new HashSet<string>();

        // 정점 검증은 챕터를 가로질러 평탄 기준으로 본다 — nodeId 중복은 전역 판정이다
        for (int t_i = 0; t_i < t_nodes.Count; t_i++)
        {
            AdventureNodeDef t_node = t_nodes[t_i];

            if (!t_node.HasStableKey)
            {
                t_unstable++;
                Debug.LogError($"[Adventure] nodeId 미저작 (정점 #{t_i}) — 이 정점과 뒤 정점 전부가 영구 잠금이다.");
            }
            else if (!t_keys.Add(t_node.nodeId))
            {
                t_dupKey++;
                Debug.LogError($"[Adventure] nodeId 중복 '{t_node.nodeId}' (정점 #{t_i}) — 낙인이 한 정점으로 합쳐진다.");
            }

            // 저작이 책임지는 것은 덱 키 하나다 — 카드 목록은 서버 표가 가리키는 AIDeck 행에서 온다.
            if (!t_node.HasAiDeckKey)
            {
                t_noDeckKey++;
                Debug.LogError($"[Adventure] aiDeckId 미저작 (정점 #{t_i} '{t_node.displayName}') — 서버 표를 올릴 수 없다.");
            }

            if (t_specReady &&
                (!AdventureNodeSpec.TryGetBattleSpec(
                     t_node.nodeId, out IReadOnlyList<int> t_enemyDeck, out _, out _) ||
                 CountCards(t_enemyDeck) != DeckSaveManager.DECK_SIZE))
            {
                t_emptyDeck++;
                Debug.LogError($"[Adventure] 상대 덱 비었음 (정점 #{t_i} '{t_node.displayName}') — 전투를 열 수 없다.");
            }

            if (SpecRewardCount(t_node.nodeId) == 0)
            {
                t_noReward++;
                Debug.LogWarning($"[Adventure] 보상 미저작 (정점 #{t_i} '{t_node.displayName}') — 클리어해도 지급이 없다.");
            }
        }

        if (t_specReady && t_chapterFault == 0 && t_unstable == 0 && t_dupKey == 0 &&
            t_emptyDeck == 0 && t_noDeckKey == 0 && t_noReward == 0)
            Debug.Log($"[Adventure] 저작 검증 통과 — 챕터 {t_chapters.Count}개 · 정점 {t_nodes.Count}개, 결함 없음.");
    }

    // 챕터 결함 수(경고 포함)를 돌려준다
    static int ValidateChapters(IReadOnlyList<AdventureChapterDef> _chapters)
    {
        int t_fault = 0;
        var t_keys = new HashSet<string>();

        // 앞 챕터의 요구 등급. 여정이 뒤로 갈수록 낮아지면 순서가 뒤집힌다.
        ERankGrade t_prevGrade = ERankGrade.Bronze;

        if (_chapters.Count == 0)
        {
            Debug.LogError("[Adventure] 챕터 미저작 — 맵에 아무것도 뜨지 않는다.");
            return 1;
        }

        for (int t_i = 0; t_i < _chapters.Count; t_i++)
        {
            AdventureChapterDef t_chapter = _chapters[t_i];

            if (!t_chapter.HasStableKey)
            {
                t_fault++;
                Debug.LogError($"[Adventure] chapterId 미저작 (챕터 #{t_i}) — 완주 보상을 영영 받을 수 없다.");
            }
            else if (!t_keys.Add(t_chapter.chapterId))
            {
                t_fault++;
                Debug.LogError($"[Adventure] chapterId 중복 '{t_chapter.chapterId}' (챕터 #{t_i}) — 수령 낙인이 한 챕터로 합쳐진다.");
            }

            if (t_chapter.NodeCount == 0)
            {
                t_fault++;
                Debug.LogError($"[Adventure] 정점 0개 (챕터 #{t_i} '{t_chapter.title}') — 완주 판정 모수가 없다.");
            }

            if (SpecRewardCount(t_chapter.chapterId) == 0)
            {
                t_fault++;
                Debug.LogWarning($"[Adventure] 완주 보상 미저작 (챕터 #{t_i} '{t_chapter.title}') — 완주해도 지급이 없다.");
            }

            if (t_i == 0 && t_chapter.requiredGrade != ERankGrade.Bronze)
            {
                t_fault++;
                Debug.LogError($"[Adventure] 첫 챕터의 requiredGrade가 {t_chapter.requiredGrade} (챕터 #{t_i} '{t_chapter.title}') — 랭크에 오르기 전 유저는 모험에 아예 못 들어간다.");
            }

            if (t_chapter.requiredGrade < t_prevGrade)
            {
                t_fault++;
                Debug.LogWarning($"[Adventure] requiredGrade 역행 {t_prevGrade} → {t_chapter.requiredGrade} (챕터 #{t_i} '{t_chapter.title}') — 뒤 챕터가 먼저 열려 여정의 순서가 뒤집힌다.");
            }

            t_prevGrade = t_chapter.requiredGrade;

            // 챕터 띠의 보상 슬롯이 2칸이라 3줄부터는 앞칸만 뜬다(지급은 되지만 표시가 잘린다)
            int t_chapterRewards = SpecRewardCount(t_chapter.chapterId);
            if (t_chapterRewards > 2)
            {
                t_fault++;
                Debug.LogWarning($"[Adventure] 완주 보상 {t_chapterRewards}줄 (챕터 #{t_i} '{t_chapter.title}') — 띠 슬롯 2칸을 넘어 뒷줄이 표시되지 않는다.");
            }
        }

        return t_fault;
    }

    static int CountCards(IReadOnlyList<int> _cards)
    {
        if (_cards == null) return 0;

        int t_count = 0;
        for (int t_i = 0; t_i < _cards.Count; t_i++)
            if (_cards[t_i] > 0) t_count++;

        return t_count;
    }

    // 보상의 진실원은 Reward 표다 — SO에는 더 이상 저작값이 없으므로 시트를 직접 읽어 센다
    static int SpecRewardCount(string _ownerKey)
        => AdventureSpec.TryGetRewards(_ownerKey, out List<AlbumRewardDef> t_rewards)
            ? CountRewards(t_rewards)
            : 0;

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
