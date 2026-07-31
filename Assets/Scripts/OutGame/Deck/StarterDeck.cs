using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 신규 유저 최초 지급 창구. 세이브에 덱이 하나도 없으면 스타터덱을 목록 맨 앞에 넣고 카드 소유권도 함께 준다.
/// 정본은 CardPackData(SO)의 pool을 재사용하되 드로우를 태우지 않는다 — 스타터덱은 매 계정 동일해야 한다.
/// 덱 세이브(DeckSaveManager)가 소유권·팩 SO를 알지 않도록 두 축을 여기서만 엮는다.
/// </summary>
public static class StarterDeck
{
    const string DECK_NAME = "스타터 덱";   // CardPackData에 덱 이름 필드가 없어 여기서 고정한다.

    /// <summary>세이브에 덱이 하나도 없을 때만 목록 맨 앞에 스타터덱을 넣는다. 그 외에는 아무것도 건드리지 않는다.</summary>
    public static void GrantIfNoDeck(CardPackData _starter)
    {
        // "데이터가 없으면"의 판정 기준은 세이브의 덱 유무다. 메모리의 IsSlotValid(6장 완성)로 보면
        // 카드 키가 남아 있는데 레지스트리에서 해석되지 않은 슬롯을 "빈 칸"으로 오인해 덮어쓴다.
        // 덱을 전부 지우면 다음 부트에 다시 받는다 — 의도된 하한선.
        // null 체크보다 이 가드를 앞에 둬야 미배선 경고가 기존 유저에게 매 부트 뜨지 않는다.
        if (DeckSaveManager.HasAnySavedDeck()) return;

        // 구 decks.json이 아직 세이브로 넘어오지 못했다 — 지금 슬롯을 쓰면 그 덱이 영영 묻힌다.
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
            // 불완전 덱은 IsSlotValid가 false라 목록에도 안 뜬다 — 반쪽 지급보다 미지급이 낫다.
            Debug.LogWarning($"[StarterDeck] {_starter.name} pool 유효 카드 {t_cards.Count}장 ≠ DECK_SIZE {DeckSaveManager.DECK_SIZE} — 지급 생략.");
            return;
        }

        // 덱 편집·도감이 소유 필터를 쓰므로 덱만 넣으면 편집 화면에 없는 카드가 편성돼 있는 꼴이 된다.
        OwnershipManager.GrantAll(ToKeys(t_cards));

        // HasAnySavedDeck가 false면 덱이 0개라 삽입 결과는 필연적으로 맨 앞이다 — 좌표를 여기서 알 필요가 없다.
        if (!DeckSaveManager.TryInsertFront(t_cards, DECK_NAME, DeckImages.PickRandomKey(), out _))
            Debug.LogWarning("[StarterDeck] 덱 삽입 실패 — 지급 생략(DeckSaveManager 로그 확인).");
    }

    // pool 앞에서부터 null·중복을 걸러 최대 DECK_SIZE장. 드로우가 아니라 고정 순서 복사다.
    // 중복을 남기면 덱 내 중복 금지(DeckEditController) 규칙을 어긴 덱이 생기고 소유권 장수도 어긋난다.
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

    static List<string> ToKeys(List<CardData> _cards)
    {
        var t_keys = new List<string>(_cards.Count);
        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            t_keys.Add(CardCatalog.KeyOf(_cards[t_i]));
        }
        return t_keys;
    }
}
