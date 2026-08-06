using System.Collections.Generic;
using UnityEngine;

// 신규 유저 최초 지급 창구(스타터덱 + 카드 소유권)
public static class StarterDeck
{
    const string DECK_NAME = "스타터 덱";

    // 세이브에 덱이 하나도 없을 때만 목록 맨 앞에 스타터덱 지급
    public static void GrantIfNoDeck(CardPackData _starter)
    {
        if (DeckSaveManager.HasAnySavedDeck()) return;

        if (DeckSaveManager.LegacyMigrationPending)
        {
            Debug.LogWarning("[StarterDeck] 레거시 덱 이관 미완료 — 이번 부트는 지급을 보류한다.");
            return;
        }

        if (_starter == null)
        {
            Debug.LogWarning("[StarterDeck] 스타터덱 SO 미배선 — 지급 생략(BootInstaller 확인).");
            return;
        }

        var t_cards = TakeDeckCards(_starter);
        if (t_cards.Count != DeckSaveManager.DECK_SIZE)
        {
            Debug.LogWarning($"[StarterDeck] {_starter.name} pool 유효 카드 {t_cards.Count}장 ≠ DECK_SIZE {DeckSaveManager.DECK_SIZE} — 지급 생략.");
            return;
        }

        OwnershipManager.GrantAll(ToIds(t_cards));

        if (!DeckSaveManager.TryInsertFront(t_cards, DECK_NAME, DeckImages.PickRandomKey(), out _))
            Debug.LogWarning("[StarterDeck] 덱 삽입 실패 — 지급 생략(DeckSaveManager 로그 확인).");
    }

    // 드로우가 아니라 pool 앞에서부터의 고정 순서 복사(스타터덱은 매 계정 동일)
    static List<CardData> TakeDeckCards(CardPackData _starter)
    {
        var t_cards = new List<CardData>(DeckSaveManager.DECK_SIZE);
        var t_pool  = _starter.Pool;
        for (int t_i = 0; t_i < t_pool.Count && t_cards.Count < DeckSaveManager.DECK_SIZE; t_i++)
        {
            if (t_pool[t_i] == null || t_cards.Contains(t_pool[t_i])) continue;

            t_cards.Add(t_pool[t_i]);
        }
        return t_cards;
    }

    static List<int> ToIds(List<CardData> _cards)
    {
        var t_ids = new List<int>(_cards.Count);
        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            t_ids.Add(CardCatalog.IdOf(_cards[t_i]));
        }
        return t_ids;
    }
}
