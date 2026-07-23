using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class DeckSaveManager
{
    public const int SLOT_COUNT = 6;
    public const int DECK_SIZE  = 6;
    static readonly List<CardData>[] slots = new List<CardData>[SLOT_COUNT];
    static readonly string[] names = new string[SLOT_COUNT];

    static IReadOnlyList<CardData> _registry;
    static string SavePath => Path.Combine(Application.persistentDataPath, "decks.json");

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

    // 카드 전체 목록 등록 (LoadFromFile 전에 반드시 호출)
    public static void SetCardRegistry(IEnumerable<CardData> _cards)
        => _registry = _cards.ToList();

    [System.Serializable]
    class SlotData
    {
        public string slotName;
        public string[] cards;
    }

    [System.Serializable]
    class SaveData
    {
        public SlotData[] slots = new SlotData[SLOT_COUNT];
    }

    public static void SaveToFile()
    {
        var t_data = new SaveData();
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            t_data.slots[i] = new SlotData
            {
                slotName = names[i] ?? "",
                cards    = slots[i]?.Select(c => c != null ? c.name : "").ToArray()
                           ?? new string[0],
            };
        }
        File.WriteAllText(SavePath, JsonUtility.ToJson(t_data));
    }

    public static void LoadFromFile()
    {
        if (!File.Exists(SavePath)) return;

        var t_data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        if (t_data?.slots == null) return;

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            var t_slot = t_data.slots[i];
            if (t_slot == null) continue;

            names[i] = t_slot.slotName ?? "";

            if (t_slot.cards == null || _registry == null) continue;
            slots[i] = t_slot.cards
                // CardRegistry.All에는 ID 보존용 빈 칸(null)이 섞일 수 있다 — c.name 접근 전에 걸러야 NRE가 안 난다.
                .Select(n => _registry.FirstOrDefault(c => c != null && c.name == n))
                .Where(c => c != null)
                .ToList();
        }
    }
}
