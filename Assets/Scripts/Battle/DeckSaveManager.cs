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
    // 덱 대표 이미지 키(DeckImageCatalog의 스프라이트 이름). 여기서는 스프라이트를 모르고 문자열만 다룬다.
    static readonly string[] imageKeys = new string[SLOT_COUNT];

    static IReadOnlyList<CardData> _registry;
    static string SavePath => Path.Combine(Application.persistentDataPath, "decks.json");

    public static List<CardData> GetSlot(int _index) => slots[_index];
    public static string GetName(int _index) => string.IsNullOrEmpty(names[_index]) ? $"덱 {_index + 1}" : names[_index];
    public static void SetName(int _index, string _name) => names[_index] = _name;

    // 이미지 키는 표시 경로(DeckImages)가 슬롯 범위 밖까지 훑을 수 있어 이름과 달리 범위를 방어한다.
    public static string GetImageKey(int _index)
        => _index >= 0 && _index < SLOT_COUNT ? (imageKeys[_index] ?? "") : "";

    public static void SetImageKey(int _index, string _key)
    {
        if (_index < 0 || _index >= SLOT_COUNT) return;

        imageKeys[_index] = _key ?? "";
    }

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
        imageKeys[_index] = "";   // 슬롯 재사용 시 새 덱이 새 이미지를 받게
        SaveToFile();
    }

    public static List<CardData> Load(int _index) => slots[_index] ?? new List<CardData>();

    // 카드 전체 목록 등록 (LoadFromFile 전에 반드시 호출)
    public static void SetCardRegistry(IEnumerable<CardData> _cards)
        => _registry = _cards.ToList();

    [System.Serializable]
    // 필드는 추가만(하위호환). 구 세이브에 imageKey가 없으면 빈 값으로 읽히고 표시는 폴백으로 떨어진다.
    class SlotData
    {
        public string slotName;
        public string[] cards;
        public string imageKey;
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
                imageKey = imageKeys[i] ?? "",
            };
        }
        File.WriteAllText(SavePath, JsonUtility.ToJson(t_data));
    }

    // 슬롯 하나만 파일에 반영한다(나머지 슬롯은 디스크 값 보존).
    // SaveToFile은 메모리 6슬롯을 통째로 flush하므로, LoadFromFile을 거치지 않은 씬에서 호출하면
    // 로드 안 된 슬롯이 빈 값으로 영속돼 기존 덱이 사라진다. 부트 미경유 경로는 이 API를 쓸 것.
    public static void SaveSlotToFile(int _index, IEnumerable<CardData> _deck)
    {
        if (_index < 0 || _index >= SLOT_COUNT) return;

        Save(_index, _deck);

        var t_data = ReadFileOrEmpty();
        t_data.slots[_index] = new SlotData
        {
            slotName = names[_index] ?? "",
            cards    = slots[_index]?.Select(c => c != null ? c.name : "").ToArray() ?? new string[0],
            imageKey = imageKeys[_index] ?? "",
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(t_data));
    }

    // 디스크 세이브를 SLOT_COUNT 길이로 정규화해 읽는다(없거나 깨졌으면 빈 슬롯). 겹치는 슬롯은 보존.
    static SaveData ReadFileOrEmpty()
    {
        SaveData t_read = null;
        if (File.Exists(SavePath))
            t_read = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));

        var t_data = new SaveData();
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            var t_src = t_read?.slots != null && i < t_read.slots.Length ? t_read.slots[i] : null;
            t_data.slots[i] = t_src ?? new SlotData { slotName = "", cards = new string[0] };
        }
        return t_data;
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

            names[i]     = t_slot.slotName ?? "";
            imageKeys[i] = t_slot.imageKey ?? "";

            if (t_slot.cards == null || _registry == null) continue;
            slots[i] = t_slot.cards
                // CardRegistry.All에는 ID 보존용 빈 칸(null)이 섞일 수 있다 — c.name 접근 전에 걸러야 NRE가 안 난다.
                .Select(n => _registry.FirstOrDefault(c => c != null && c.name == n))
                .Where(c => c != null)
                .ToList();
        }
    }
}
