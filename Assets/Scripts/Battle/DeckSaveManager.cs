using System.Collections.Generic;
using System.Linq;

public static class DeckSaveManager
{
    public const int SLOT_COUNT = 6;
    public const int DECK_SIZE  = 6;
    static readonly List<CardData>[] slots = new List<CardData>[SLOT_COUNT];
    static readonly string[] names = new string[SLOT_COUNT];

    public static List<CardData> GetSlot(int _index) => slots[_index];
    public static string GetName(int _index) => string.IsNullOrEmpty(names[_index]) ? $"덱 {_index + 1}" : names[_index];
    public static void SetName(int _index, string _name) => names[_index] = _name;

    public static bool IsSlotValid(int _index)
        => slots[_index] != null && slots[_index].Count == DECK_SIZE && slots[_index].All(d => d != null);

    public static bool HasAnyValidSlot()
    {
        for (int i = 0; i < SLOT_COUNT; i++)
            if (IsSlotValid(i)) return true;
        return false;
    }

    public static void Save(int _index, IEnumerable<CardData> _deck)
        => slots[_index] = new List<CardData>(_deck.Where(d => d != null));

    public static void Delete(int _index)
    {
        slots[_index] = null;
        names[_index] = "";
        SaveToFile();
    }

    public static List<CardData> Load(int _index) => slots[_index] ?? new List<CardData>();

    // 메모리 slots/names를 아웃게임 세이브(outgame_save.json)의 deck 섹션으로 flush 후 저장.
    // 카드→키 변환·복원은 전부 CardCatalog에 위임(사설 목록 주입 폐기).
    public static void SaveToFile()
    {
        var t_deck = DataSaveManager.Data.deck ?? (DataSaveManager.Data.deck = new DeckSaveData());
        t_deck.slots = new DeckSlotSaveData[SLOT_COUNT];

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            t_deck.slots[i] = new DeckSlotSaveData
            {
                name     = names[i] ?? "",
                cardKeys = slots[i]?.Select(c => CardCatalog.KeyOf(c) ?? "").ToArray()
                           ?? new string[0],
            };
        }

        DataSaveManager.Save();
    }

    // deck 섹션에서 slots를 읽어 cardKeys를 CardCatalog로 재수화해 메모리 복원.
    // 섹션 null·슬롯 길이 부족·키 미해석은 모두 안전하게 기본값 처리.
    public static void LoadFromFile()
    {
        var t_deck = DataSaveManager.Data.deck;
        if (t_deck?.slots == null) return;

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (i >= t_deck.slots.Length) break;

            var t_slot = t_deck.slots[i];
            if (t_slot == null) continue;

            names[i] = t_slot.name ?? "";

            if (t_slot.cardKeys == null) continue;
            slots[i] = t_slot.cardKeys
                .Select(k => CardCatalog.Get(k))
                .Where(c => c != null)
                .ToList();
        }
    }
}
