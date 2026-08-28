using System.Collections.Generic;
using UnityEngine;

// 스타터덱 + 카드 소유권 지급 창구.
// 신규 계정의 정본은 서버 ensureAccount다 — 여기 남은 경로는 튜토리얼 되감기가 덱 슬롯을 비웠을 때만 선다.
// 그때 지급되는 카드는 여전히 SO(poolIds) 기준이라 서버 목록과 갈릴 수 있다(에디터 전용이라 허용).
public static class StarterDeck
{
    const string DECK_NAME = "스타터 덱";

    // 세이브에 덱이 하나도 없을 때만 목록 맨 앞에 스타터덱 지급
    public static void GrantIfNoDeck(CardPackData _starter)
    {
        if (DeckSaveManager.HasAnySavedDeck()) return;

        if (_starter == null)
        {
            Debug.LogWarning("[StarterDeck] 스타터덱 SO 미배선 — 지급 생략(부트 초기화(InitializationRunner) 확인).");
            return;
        }

        var t_cards = TakeDeckCards(_starter);
        if (t_cards.Count != DeckSaveManager.DECK_SIZE)
        {
            Debug.LogWarning($"[StarterDeck] {_starter.name} pool 유효 카드 {t_cards.Count}장 ≠ DECK_SIZE {DeckSaveManager.DECK_SIZE} — 지급 생략.");
            return;
        }

        OwnershipManager.GrantAll(t_cards);

        if (!DeckSaveManager.TryInsertFront(t_cards, DECK_NAME, DeckImages.PickRandomKey(), out _))
            Debug.LogWarning("[StarterDeck] 덱 삽입 실패 — 지급 생략(DeckSaveManager 로그 확인).");
    }

    // 드로우가 아니라 pool 앞에서부터의 고정 순서 복사(스타터덱은 매 계정 동일)
    static List<int> TakeDeckCards(CardPackData _starter)
    {
        var t_cards = new List<int>(DeckSaveManager.DECK_SIZE);
        var t_pool  = _starter.Pool;
        for (int t_i = 0; t_i < t_pool.Count && t_cards.Count < DeckSaveManager.DECK_SIZE; t_i++)
        {
            if (!CardCatalog.Contains(t_pool[t_i]) || t_cards.Contains(t_pool[t_i])) continue;

            t_cards.Add(t_pool[t_i]);
        }
        return t_cards;
    }

}
