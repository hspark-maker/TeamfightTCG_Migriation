using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 덱 슬롯의 메모리 캐시(영속화 진실원은 DataSaveManager.Data.Deck)
// 압축 불변식 — 유효 덱은 항상 [0 .. DeckCount-1]을 연속 점유하고 그 뒤는 전부 빈 칸이다
public static class DeckSaveManager
{
    public const int SLOT_COUNT = 6;
    public const int DECK_SIZE  = 6;

    static readonly List<int>[] s_slots = new List<int>[SLOT_COUNT];
    static readonly string[] s_names = new string[SLOT_COUNT];
    static readonly string[] s_imageKeys = new string[SLOT_COUNT];

    static bool s_loaded;

    // 덱 변경 통지 — 구성·이름·이미지키를 바꾸는 모든 경로가 여기로 모인다(구독자가 직접 재빌드를 걸 필요 없다)
    public static event Action OnDeckChanged;

    // 첫 무효 슬롯 앞까지의 덱 개수
    public static int DeckCount
    {
        get
        {
            for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
                if (!IsSlotValid(t_i)) return t_i;

            return SLOT_COUNT;
        }
    }

    public static bool IsFull => DeckCount >= SLOT_COUNT;

    public static List<int> GetSlot(int _index) => s_slots[_index];

    // 저장된 덱 이름 원본(빈 문자열 가능)
    public static string GetName(int _index) => s_names[_index] ?? "";
    public static void SetName(int _index, string _name)
    {
        s_names[_index] = _name;
        OnDeckChanged?.Invoke();
    }

    // 표시용 덱 이름(비어 있으면 인덱스 폴백)
    public static string GetDisplayName(int _index)
        => string.IsNullOrEmpty(s_names[_index]) ? $"덱 {_index + 1}" : s_names[_index];

    // 저장된 이름과 겹치지 않는 첫 후보 이름
    public static string SuggestNewDeckName()
    {
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            var t_candidate = $"덱 {t_i + 1}";

            bool t_used = false;
            for (int t_j = 0; t_j < SLOT_COUNT; t_j++)
            {
                if (s_names[t_j] != t_candidate) continue;

                t_used = true;
                break;
            }

            if (!t_used) return t_candidate;
        }

        return "덱 1";
    }

    // 덱 대표 이미지 키(범위 밖 조회는 빈 문자열)
    public static string GetImageKey(int _index)
        => _index >= 0 && _index < SLOT_COUNT ? (s_imageKeys[_index] ?? "") : "";

    public static void SetImageKey(int _index, string _key)
    {
        if (_index < 0 || _index >= SLOT_COUNT) return;

        s_imageKeys[_index] = _key ?? "";
        OnDeckChanged?.Invoke();
    }

    public static bool IsSlotValid(int _index)
        => s_slots[_index] != null && s_slots[_index].Count == DECK_SIZE && s_slots[_index].All(CardCatalog.Contains);

    public static bool HasAnyValidSlot()
    {
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            if (IsSlotValid(t_i)) return true;

        return false;
    }

    // 카드 목록을 슬롯용 덱으로 정규화(null·중복 제거 후 정확히 DECK_SIZE장)
    public static bool TryBuildDeck(IEnumerable<int> _source, out List<int> _deck)
    {
        _deck = new List<int>(DECK_SIZE);

        if (_source == null) return false;

        foreach (int t_card in _source)
        {
            if (_deck.Count >= DECK_SIZE) break;
            if (!CardCatalog.Contains(t_card) || _deck.Contains(t_card)) continue;

            _deck.Add(t_card);
        }

        return _deck.Count == DECK_SIZE;
    }

    // 같은 카드 구성의 저장 슬롯 찾기(편성 순서 무관)
    public static bool TryFindSlot(IEnumerable<int> _source, out int _index)
    {
        _index = -1;

        if (!TryBuildDeck(_source, out List<int> t_deck)) return false;

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            if (!IsSlotValid(t_i)) continue;

            var t_slot = s_slots[t_i];

            bool t_same = true;
            for (int t_j = 0; t_j < t_deck.Count && t_same; t_j++)
                t_same = t_slot.Contains(t_deck[t_j]);

            if (!t_same) continue;

            _index = t_i;
            return true;
        }

        return false;
    }

    // 신규 덱을 목록 맨 앞에 삽입(이름·이미지키까지 한 번에 저장)
    public static bool TryInsertFront(IEnumerable<int> _deck, string _name, string _imageKey, out int _index)
    {
        _index = -1;

        if (!CanReorder()) return false;
        if (IsFull) return false;

        var t_cards = _deck != null ? _deck.Where(CardCatalog.Contains).ToList() : new List<int>();
        if (t_cards.Count != DECK_SIZE) return false;

        int t_count = DeckCount;
        for (int t_i = t_count - 1; t_i >= 0; t_i--)
            CopySlot(t_i, t_i + 1);

        ClearSlot(0);
        s_slots[0]     = t_cards;
        s_names[0]     = _name ?? "";
        s_imageKeys[0] = _imageKey ?? "";

        SaveAll();
        _index = 0;
        OnDeckChanged?.Invoke();
        return true;
    }

    // 덱 삭제 후 뒤 덱을 앞으로 당겨 구멍 제거
    public static bool TryDeleteAt(int _index)
    {
        if (!CanReorder()) return false;

        if (_index < 0 || _index >= SLOT_COUNT || !IsSlotValid(_index)) return false;

        int t_count = DeckCount;
        if (_index >= t_count) return false;

        for (int t_i = _index; t_i < t_count - 1; t_i++)
            CopySlot(t_i + 1, t_i);

        ClearSlot(t_count - 1);

        SaveAll();
        OnDeckChanged?.Invoke();
        return true;
    }

    public static List<int> Load(int _index) => s_slots[_index] ?? new List<int>();

    // 세이브에 덱이 하나라도 들어 있는지(메모리 아님) — 스타터덱 재지급 방지 판정용
    public static bool HasAnySavedDeck()
    {
        var t_slots = NormalizedSlots();
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            if (t_slots[t_i].CardIds.Count > 0) return true;

        return false;
    }

    // 세이브의 덱 노드를 메모리로 복원(DataSaveManager.Load 이후 호출)
    public static void LoadFromSave()
    {
        s_loaded = true;

        var t_slots = NormalizedSlots();

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            var t_slot = t_slots[t_i];

            s_names[t_i]     = t_slot.Name;
            s_imageKeys[t_i] = t_slot.ImageKey;

            s_slots[t_i] = t_slot.CardIds
                .Where(CardCatalog.Contains)
                .ToList();
        }

        if (Compact()) SaveAll();

        // 초기화 전에 그려진 UI는 빈 덱으로 굳는다 — 로드 완료도 변경으로 통지해야 따라온다.
        // 초기화 한복판이라 구독자 예외를 여기서 흘리면 스타터 덱 지급·튜토 되감기가 통째로 스킵된다.
        try { OnDeckChanged?.Invoke(); }
        catch (Exception t_exception) { Debug.LogException(t_exception); }
    }

    // 대상 슬롯만 세이브에 반영(나머지 슬롯은 저장된 값 보존)
    public static void SaveSlot(int _index, IEnumerable<int> _deck)
    {
        if (_index < 0 || _index >= SLOT_COUNT) return;

        Save(_index, _deck);
        WriteSlot(NormalizedSlots(), _index);
        DataSaveManager.Save();
        OnDeckChanged?.Invoke();
    }

    static bool CanReorder()
    {
        if (s_loaded && CardCatalog.IsReady) return true;

        Debug.LogWarning("[DeckSaveManager] LoadFromSave 미경유 또는 카드 레지스트리 미주입 — 순서 변경 거부(메모리·세이브 어긋남 방지). 초기화 프리팹이 있는 씬에서 실행할 것.");
        return false;
    }

    static void Save(int _index, IEnumerable<int> _deck)
        => s_slots[_index] = new List<int>(_deck.Where(CardCatalog.Contains));

    static void SaveAll()
    {
        if (!s_loaded)
        {
            Debug.LogWarning("[DeckSaveManager] LoadFromSave 미경유 — 전량 저장 거부(세이브 덱 전체 소실 방지). 초기화 프리팹이 있는 씬에서 실행할 것.");
            return;
        }

        var t_slots = NormalizedSlots();
        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
            WriteSlot(t_slots, t_i);

        DataSaveManager.Save();
    }

    // 원본을 지우지 않는 순수 복사(연쇄 이동에서 다음 이동의 원본이 살아 있어야 한다)
    static void CopySlot(int _from, int _to)
    {
        s_slots[_to]     = s_slots[_from];
        s_names[_to]     = s_names[_from];
        s_imageKeys[_to] = s_imageKeys[_from];
    }

    static void ClearSlot(int _index)
    {
        s_slots[_index]     = null;
        s_names[_index]     = "";
        s_imageKeys[_index] = "";
    }

    static bool Compact()
    {
        var t_saved = NormalizedSlots();

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            if (t_saved[t_i].CardIds.Count != DECK_SIZE || IsSlotValid(t_i)) continue;

            Debug.LogWarning($"[DeckSaveManager] 슬롯 {t_i}의 카드 번호를 레지스트리에서 해석하지 못했다 — 압축·저장 보류(세이브 원본 보존). 카드 번호 변경·삭제를 확인할 것.");
            return false;
        }

        int  t_write   = 0;
        int  t_dropped = 0;
        bool t_changed = false;

        for (int t_read = 0; t_read < SLOT_COUNT; t_read++)
        {
            if (!IsSlotValid(t_read))
            {
                if (t_saved[t_read].CardIds.Count > 0) t_dropped++;
                continue;
            }

            if (t_read != t_write)
            {
                CopySlot(t_read, t_write);
                t_changed = true;
            }
            t_write++;
        }

        for (int t_i = t_write; t_i < SLOT_COUNT; t_i++)
        {
            if (t_saved[t_i].CardIds.Count > 0 || !string.IsNullOrEmpty(s_names[t_i]) || !string.IsNullOrEmpty(s_imageKeys[t_i]))
                t_changed = true;

            ClearSlot(t_i);
        }

        if (t_dropped > 0)
            Debug.LogWarning($"[DeckSaveManager] 유효 덱 {DECK_SIZE}장을 이루지 못한 슬롯 {t_dropped}개를 압축에서 제외했다.");

        return t_changed;
    }

    // 세이브 인스턴스를 제자리에서 정규화해 반환하므로 읽기·쓰기가 같은 목록을 본다
    static List<DeckSlotSaveData> NormalizedSlots()
    {
        var t_deck = DataSaveManager.Data.Deck;
        if (t_deck == null)
        {
            t_deck = new DeckSaveData();
            DataSaveManager.Data.Deck = t_deck;
        }

        if (t_deck.Slots == null) t_deck.Slots = new List<DeckSlotSaveData>(SLOT_COUNT);

        while (t_deck.Slots.Count > SLOT_COUNT) t_deck.Slots.RemoveAt(t_deck.Slots.Count - 1);
        while (t_deck.Slots.Count < SLOT_COUNT) t_deck.Slots.Add(null);

        for (int t_i = 0; t_i < SLOT_COUNT; t_i++)
        {
            var t_slot = t_deck.Slots[t_i];
            if (t_slot == null) t_deck.Slots[t_i] = t_slot = new DeckSlotSaveData();

            if (t_slot.Name == null)    t_slot.Name    = "";
            if (t_slot.CardIds == null) t_slot.CardIds = new List<int>();
            if (t_slot.ImageKey == null) t_slot.ImageKey = "";
        }

        return t_deck.Slots;
    }

    static void WriteSlot(List<DeckSlotSaveData> _slots, int _index)
    {
        var t_dst = _slots[_index];

        t_dst.Name     = s_names[_index] ?? "";
        t_dst.CardIds  = s_slots[_index]?.Where(CardCatalog.Contains).ToList()
                         ?? new List<int>();
        t_dst.ImageKey = s_imageKeys[_index] ?? "";
    }
}
