using System.Collections.Generic;
using UnityEngine;

// 덱 슬롯이 하나도 없을 때 서는 안전망. 카드 소유는 더 이상 여기서 주지 않는다(정본은 서버다).
// 리테일에서도 설 수 있다 — DeckListController의 삭제에 최소 덱 수 가드가 없어 유저가 덱을 전부 지울 수 있고,
// 그때 이 경로가 빈 목록을 메운다. 이미 소유한 카드로만 덱을 세우므로 SO 목록과 서버 지급이 갈리면 지급을 생략한다.
public static class StarterDeck
{
    const string DECK_NAME = "스타터 덱";

    // 세이브에 덱이 하나도 없을 때만 목록 맨 앞에 스타터덱을 세운다(소유 지급은 없다)
    public static void GrantIfNoDeck(string _starterPackId)
    {
        if (DeckSaveManager.HasAnySavedDeck()) return;

        if (!PackSpec.TryGetPack(_starterPackId, out _))
        {
            Debug.LogWarning("[StarterDeck] 스타터덱 SO 미배선 — 지급 생략(초기화(InitializationRunner) 확인).");
            return;
        }

        var t_cards = TakeDeckCards(_starterPackId);
        if (t_cards.Count != DeckSaveManager.DECK_SIZE)
        {
            Debug.LogWarning($"[StarterDeck] {_starterPackId} pool 유효 카드 {t_cards.Count}장 ≠ DECK_SIZE {DeckSaveManager.DECK_SIZE} — 지급 생략.");
            return;
        }

        var t_missing = new List<int>();
        foreach (var t_cardId in t_cards)
            if (!OwnershipManager.IsOwned(t_cardId)) t_missing.Add(t_cardId);

        if (t_missing.Count > 0)
        {
            Debug.LogWarning($"[StarterDeck] 스타터덱 {t_cards.Count}장 중 미소유 {t_missing.Count}장({string.Join(", ", t_missing)}) — "
                           + "덱을 세우지 않는다(소유의 정본은 서버다. 덱이 0개인 채로 남는다).");
            return;
        }

        if (!DeckSaveManager.TryInsertFront(t_cards, DECK_NAME, DeckImages.PickRandomKey(), out _))
            Debug.LogWarning("[StarterDeck] 덱 삽입 실패 — 지급 생략(DeckSaveManager 로그 확인).");
    }

    // 드로우가 아니라 pool 앞에서부터의 고정 순서 복사(스타터덱은 매 계정 동일)
    static List<int> TakeDeckCards(string _starterPackId)
    {
        var t_cards = new List<int>(DeckSaveManager.DECK_SIZE);
        var t_pool  = PackSpec.ResolveCardIds(_starterPackId, RankManager.CurrentGrade);
        for (int t_i = 0; t_i < t_pool.Count && t_cards.Count < DeckSaveManager.DECK_SIZE; t_i++)
        {
            if (!CardCatalog.Contains(t_pool[t_i]) || t_cards.Contains(t_pool[t_i])) continue;

            t_cards.Add(t_pool[t_i]);
        }
        return t_cards;
    }

}
